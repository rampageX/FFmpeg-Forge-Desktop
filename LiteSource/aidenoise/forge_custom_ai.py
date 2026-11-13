# FFmpeg Forge - "Python / ONNX AI": runs a model imported by the user on raw RGB24 frames (stdin -> stdout).
#   python -u forge_custom_ai.py -w W -h H -model FILE [-device cuda|cpu] [-precision fp16|fp32] [-tile 0] [-keep 0|1] [-probe]
# Supported model files:
#   .onnx                                  ONNX Runtime (CUDA if available, else CPU). NCHW or NHWC, RGB, values 0..1.
#   .pth .pt .ckpt .safetensors .bin       PyTorch image models, loaded by spandrel (ESRGAN, SwinIR, HAT, DAT, SPAN, SCUNet,
#                                          NAFNet, Restormer, Compact, OmniSR, ... any architecture spandrel knows).
#   .py                                    your own Python script:
#        SCALE = 2                         (optional) output scale
#        def load(device): return model    (option A) a torch.nn.Module: NCHW float RGB 0..1 in, NCHW out (tiled for you)
#        def process(img): return img      (option B) HxWx3 uint8 RGB numpy in, HxWx3 numpy out (uint8, or float 0..1)
# -probe: loads the model, runs it once on a small picture and prints "FORGE_SCALE <s>" (output / input size).
import argparse, importlib.util, math, os, sys, threading, queue, time

def log(msg):
    sys.stderr.write("ForgeAI " + msg + "\n"); sys.stderr.flush()

def main():
    ap = argparse.ArgumentParser(add_help=False)
    ap.add_argument("-w", type=int, default=64)
    ap.add_argument("-h", type=int, default=64)
    ap.add_argument("-model", required=True)
    ap.add_argument("-device", default="cuda")
    ap.add_argument("-precision", default="fp16")
    ap.add_argument("-tile", type=int, default=0)
    ap.add_argument("-keep", type=int, default=0)
    ap.add_argument("-probe", action="store_true")
    ap.add_argument("-ow", type=int, default=0)       # exact output size (the model result is resampled to it)
    ap.add_argument("-oh", type=int, default=0)
    a = ap.parse_args()

    real_out = sys.stdout.buffer
    sys.stdout = open(os.devnull, "w")             # whatever the model code prints never reaches the frame stream
    import warnings; warnings.filterwarnings("ignore")
    import numpy as np
    import torch
    import torch.nn.functional as F

    path = a.model
    ext = os.path.splitext(path)[1].lower()
    cuda = a.device == "cuda" and torch.cuda.is_available()
    dev = "cuda" if cuda else "cpu"
    if a.device == "cuda" and not cuda: log("no CUDA GPU available: running on the CPU (slow)")
    if cuda:
        torch.backends.cudnn.benchmark = True
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
    else:
        torch.set_num_threads(max(1, os.cpu_count() or 1))

    fixed = None            # fixed model input size (h, w) for ONNX models with static dimensions
    kind = ""
    run_t = None            # callable: float tensor B,3,h,w (0..1, on dev) -> tensor B,3,h*s,w*s
    run_np = None           # callable: uint8 HxWx3 -> numpy HxW'x3 (whole frame, no tiling)
    mem_px = 0.5e6          # pixels per pass (tiling budget)

    if ext == ".onnx":
        try: import onnxruntime as ort
        except ImportError: log("onnxruntime is not installed: click \"Install AI runtime\" in FFmpeg Forge"); sys.exit(3)
        if hasattr(ort, "preload_dlls"):
            try: ort.preload_dlls()                # CUDA / cuDNN DLLs from the PyTorch install
            except Exception: pass
        av = ort.get_available_providers()
        prov = [p for p in (["CUDAExecutionProvider"] if cuda else []) + (["DmlExecutionProvider"] if a.device == "cuda" else []) + ["CPUExecutionProvider"] if p in av]
        so = ort.SessionOptions(); so.graph_optimization_level = ort.GraphOptimizationLevel.ORT_ENABLE_ALL
        sess = ort.InferenceSession(path, sess_options=so, providers=prov)
        used = sess.get_providers()[0]
        inp = sess.get_inputs()[0]; outn = sess.get_outputs()[0].name
        shp = list(inp.shape)
        nhwc = len(shp) == 4 and (shp[3] == 3 or shp[3] == 1) and shp[1] not in (1, 3)
        half = "float16" in inp.type
        hh, ww = (shp[1], shp[2]) if nhwc else (shp[2], shp[3])
        if isinstance(hh, int) and isinstance(ww, int) and hh > 0 and ww > 0: fixed = (hh, ww)
        bfix = shp[0] if isinstance(shp[0], int) and shp[0] > 0 else None
        def onnx_run(x):
            outs = []
            for i in range(x.shape[0]) if bfix == 1 else [None]:
                xi = x if i is None else x[i:i + 1]
                xn = xi.permute(0, 2, 3, 1) if nhwc else xi
                xn = xn.detach().to("cpu", torch.float16 if half else torch.float32).contiguous().numpy()
                y = sess.run([outn], {inp.name: xn})[0]
                y = torch.from_numpy(np.ascontiguousarray(y)).float()
                if nhwc: y = y.permute(0, 3, 1, 2)
                outs.append(y)
            return torch.cat(outs, 0).to(x.device)
        run_t = onnx_run
        kind = "ONNX (%s%s)" % (used.replace("ExecutionProvider", ""), ", fp16" if half else "")
        mem_px = 1.0e6 if used != "CPUExecutionProvider" else 0.4e6
    elif ext == ".py":
        spec = importlib.util.spec_from_file_location("forge_user_model", path)
        mod = importlib.util.module_from_spec(spec)
        sys.path.insert(0, os.path.dirname(os.path.abspath(path)))
        spec.loader.exec_module(mod)
        if hasattr(mod, "setup"): mod.setup(dev)
        if hasattr(mod, "load"):
            net = mod.load(dev)
            if hasattr(net, "eval"): net = net.eval()
            dtype = torch.float16 if (cuda and a.precision == "fp16" and getattr(mod, "HALF", True)) else torch.float32
            try: net = net.to(dev, dtype=dtype)
            except Exception: dtype = torch.float32
            def py_run(x):
                with torch.inference_mode():
                    return net(x.to(dtype)).float()
            run_t = py_run
            kind = "Python script (torch module, %s)" % ("fp16" if dtype == torch.float16 else "fp32")
            mem_px = getattr(mod, "PIXELS_PER_PASS", 0.5e6)
        elif hasattr(mod, "process"):
            def np_run(img):
                y = mod.process(img)
                y = np.asarray(y)
                if y.dtype != np.uint8: y = (np.clip(y.astype(np.float32), 0, 1) * 255.0 + 0.5).astype(np.uint8)
                if y.ndim == 2: y = np.stack([y] * 3, -1)
                return np.ascontiguousarray(y[:, :, :3])
            run_np = np_run
            kind = "Python script (process)"
        else:
            log("the script must define load(device) or process(img)"); sys.exit(3)
    else:
        try: import spandrel
        except ImportError: log("spandrel is not installed: click \"Install AI runtime\" in FFmpeg Forge"); sys.exit(3)
        try:
            import spandrel_extra_arches; spandrel_extra_arches.install()
        except Exception:
            pass
        desc = spandrel.ModelLoader().load_from_file(path)
        net = desc.model.eval()
        for p in net.parameters(): p.requires_grad_(False)
        if cuda and a.precision == "fp16":
            dtype = torch.float16 if desc.supports_half else (torch.bfloat16 if desc.supports_bfloat16 and torch.cuda.is_bf16_supported() else torch.float32)
        else:
            dtype = torch.float32
        net = net.to(dev, dtype=dtype)
        if desc.input_channels != 3: log("this model expects %d input channels (only RGB models are supported)" % desc.input_channels); sys.exit(3)
        def sp_run(x):
            with torch.inference_mode():
                return net(x.to(dtype)).float()
        run_t = sp_run
        kind = "%s ×%d (PyTorch, %s)" % (desc.architecture.name, desc.scale, {torch.float16: "fp16", torch.bfloat16: "bf16"}.get(dtype, "fp32"))
        light = desc.architecture.name in ("SPAN", "Compact", "SRVGG (RealESRGAN Compact)", "RealCUGAN", "ESRGAN", "RealPLKSR", "PLKSR", "SuperUltraCompact")
        mem_px = 1.0e6 if light else 0.25e6

    if cuda:
        vram = torch.cuda.get_device_properties(0).total_memory / 2 ** 30
        mem_px = mem_px * max(0.25, vram / 12.0)

    # ---- scale of the model (output / input) from a small test picture ----
    def probe_scale():
        if run_np is not None:
            t = np.zeros((32, 32, 3), np.uint8); t[8:24, 8:24] = 200
            y = run_np(t); return y.shape[0] / 32.0
        ph, pw = fixed if fixed else (64, 64)
        y = run_t(torch.rand(1, 3, ph, pw, device=dev))
        return y.shape[2] / float(ph)
    s = probe_scale()
    S = int(round(s)) if abs(s - round(s)) < 0.02 else s
    if a.probe:
        real_out.write(("FORGE_SCALE %s\n" % S).encode()); real_out.flush()
        return

    W, H = a.w, a.h
    OW, OH = (W, H) if a.keep else (int(round(W * S)), int(round(H * S)))
    if a.ow > 0 and a.oh > 0: OW, OH = a.ow, a.oh
    fsize = W * H * 3

    # ---- tiled processing (fixed-size tiles for static ONNX models), blended with linear ramps ----
    state = {"tile": a.tile if a.tile > 0 else (0 if W * H <= mem_px else int(math.sqrt(mem_px)))}
    if fixed: state["tile"] = -1
    OV = 16
    def run_frame(x):                              # x: 1,3,H,W float 0..1 -> 1,3,OH',OW'
        th, tw = (fixed if fixed else ((state["tile"], state["tile"]) if state["tile"] > 0 else (H, W)))
        if not fixed and th >= H and tw >= W:
            return run_t(x)
        th, tw = min(th, H) if not fixed else th, min(tw, W) if not fixed else tw
        pad_h, pad_w = max(0, th - H), max(0, tw - W)
        xp = F.pad(x, (0, pad_w, 0, pad_h), mode="replicate") if (pad_h or pad_w) else x
        Hp, Wp = xp.shape[2], xp.shape[3]
        def starts(n, t):
            if n <= t: return [0]
            st = list(range(0, n - t, max(1, t - OV))); st.append(n - t); return st
        sc = S
        out = None; wsum = None
        for y0 in starts(Hp, th):
            for x0 in starts(Wp, tw):
                o = run_t(xp[:, :, y0:y0 + th, x0:x0 + tw])
                if out is None:
                    out = torch.zeros((1, o.shape[1], int(round(Hp * sc)), int(round(Wp * sc))), device=o.device)
                    wsum = torch.zeros((1, 1, out.shape[2], out.shape[3]), device=o.device)
                oh, ow = o.shape[2], o.shape[3]
                ry = torch.ones(oh, device=o.device); rx = torch.ones(ow, device=o.device)
                ov = int(OV * sc)
                if ov > 1:
                    ramp = torch.linspace(0.05, 1.0, ov, device=o.device)
                    if y0 > 0: ry[:ov] = ramp
                    if y0 + th < Hp: ry[-ov:] = ramp.flip(0)
                    if x0 > 0: rx[:ov] = ramp
                    if x0 + tw < Wp: rx[-ov:] = ramp.flip(0)
                wgt = (ry[:, None] * rx[None, :])[None, None]
                Y0, X0 = int(round(y0 * sc)), int(round(x0 * sc))
                out[:, :, Y0:Y0 + oh, X0:X0 + ow] += o * wgt
                wsum[:, :, Y0:Y0 + oh, X0:X0 + ow] += wgt
        out = out / wsum.clamp_min(1e-4)
        return out[:, :, :int(round(H * sc)), :int(round(W * sc))]

    def process(buf):
        if run_np is not None:
            img = np.frombuffer(buf, np.uint8).reshape(H, W, 3)
            y = run_np(img)
            if y.shape[0] != OH or y.shape[1] != OW:
                t = torch.from_numpy(y).permute(2, 0, 1)[None].float() / 255.0
                t = F.interpolate(t, size=(OH, OW), mode="bicubic", align_corners=False, antialias=True)
                y = (t[0].permute(1, 2, 0).clamp(0, 1) * 255.0 + 0.5).to(torch.uint8).numpy()
            return y.tobytes()
        x = torch.from_numpy(np.frombuffer(bytearray(buf), np.uint8).reshape(H, W, 3)).to(dev).permute(2, 0, 1)[None].float() / 255.0
        while True:
            try:
                y = run_frame(x); break
            except RuntimeError as e:
                if "out of memory" not in str(e).lower() or fixed: raise
                if cuda: torch.cuda.empty_cache()
                t = state["tile"] if state["tile"] > 0 else max(H, W)
                if t <= 96: raise
                state["tile"] = max(96, t // 2)
                log("GPU memory full: tiles of %d px" % state["tile"])
        y = y[:, :3]
        if y.shape[2] != OH or y.shape[3] != OW:
            y = F.interpolate(y, size=(OH, OW), mode="bicubic", align_corners=False, antialias=True)
        return (y[0].permute(1, 2, 0).clamp(0, 1) * 255.0 + 0.5).to(torch.uint8).cpu().numpy().tobytes()

    log("device %s | %s | %s | %dx%d -> %dx%d%s" % (dev, torch.cuda.get_device_name(0) if cuda else "CPU", kind, W, H, OW, OH,
        " | tiles %dx%d" % fixed if fixed else (" | tiles %d" % state["tile"] if state["tile"] > 0 else "")))

    inq, outq = queue.Queue(maxsize=6), queue.Queue(maxsize=6)
    stdin = sys.stdin.buffer
    def reader():
        try:
            while True:
                b = stdin.read(fsize)
                if not b or len(b) < fsize: break
                inq.put(b)
        finally:
            inq.put(None)
    def writer():
        while True:
            it = outq.get()
            if it is None: break
            real_out.write(it)
        real_out.flush()
    threading.Thread(target=reader, daemon=True).start()
    tw_ = threading.Thread(target=writer, daemon=True); tw_.start()
    n = 0; t0 = time.time()
    with torch.inference_mode():
        while True:
            b = inq.get()
            if b is None: break
            outq.put(process(b))
            n += 1
            if n == 1 or n % 25 == 0: log("frame %d | %.2f fps" % (n, n / max(1e-3, time.time() - t0)))
    outq.put(None); tw_.join()
    log("done | %d frames | %.2f fps" % (n, n / max(1e-3, time.time() - t0)))

if __name__ == "__main__":
    main()
