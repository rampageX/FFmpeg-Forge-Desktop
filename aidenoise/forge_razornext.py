# FFmpeg Forge - RazorNext (RelayVSR) streaming helper.
# RazorNext is Visual Enhancer's name for RelayVSR (Xijun Wang, Xin Li, Zirui Lang, Suhang Yao, Haoran Li, Zhibo Chen,
# github.com/kopperx/RelayVSR, Apache-2.0): collaborative streaming 4x real-world video super-resolution
# (sparse Wan keyframes + a compact Flash conditional decoder for the frames in between).
# Runs inside the official RelayVSR folder. Reads raw RGB24 frames on stdin and writes the upscaled RGB24 frames on
# stdout, so FFmpeg Forge streams the video straight from the decoder to the final encoder (no picture files, one
# single encode). Pictures are a one-frame stream. The model is native 4x: x1 / x2 / x3 are the 4x result brought
# back down with an anti-aliased resize (x1 = same size, AI restoration only).
#   python -u forge_razornext.py -w W -h H -u 1|2|3|4 -device cuda|cpu [-window 16] [-precision bf16|fp16|fp32]
import argparse, os, sys, time, threading, queue, types


def log(msg):
    sys.stderr.write("ForgeRN " + msg + "\n"); sys.stderr.flush()


def stub_triton():
    """Triton is not part of PyTorch on Windows. The RelayVSR modules import it at load time, so a tiny stand-in
    lets them load; the Triton kernels are then replaced by exact PyTorch (SDPA) versions below."""
    class _Any(types.ModuleType):
        def __getattr__(self, name):
            if name.startswith("__"): raise AttributeError(name)
            return object
    tri = types.ModuleType("triton")
    tri.jit = lambda fn=None, **k: fn if fn is not None else (lambda f: f)
    tri.cdiv = lambda a, b: -(-a // b)
    tri.next_power_of_2 = lambda n: 1 << max(0, int(n - 1).bit_length())
    tl = _Any("triton.language")
    tri.language = tl
    sys.modules["triton"] = tri
    sys.modules["triton.language"] = tl


def main():
    ap = argparse.ArgumentParser(add_help=False)
    ap.add_argument("-w", type=int, required=True)
    ap.add_argument("-h", type=int, required=True)
    ap.add_argument("-u", type=int, default=4)
    ap.add_argument("-device", default="cuda")
    ap.add_argument("-window", type=int, default=16)       # a Wan keyframe every (window - 1) frames
    ap.add_argument("-precision", default="bf16")
    a = ap.parse_args()
    if a.u not in (1, 2, 3, 4):
        log("unsupported upscale factor %d (RazorNext: x1, x2, x3 or x4)" % a.u); sys.exit(2)

    real_out = sys.stdout.buffer                    # frames go here; prints of the model code are discarded
    sys.stdout = open(os.devnull, "w")
    import warnings
    warnings.filterwarnings("ignore")
    here = os.path.dirname(os.path.abspath(__file__))
    os.chdir(here)
    sys.path.insert(0, here)

    try:
        import triton  # noqa: F401  (triton-windows on Windows, triton on Linux)
        has_triton = True
    except Exception:
        has_triton = False

    import numpy as np
    import torch
    import torch.nn.functional as F
    try:
        from diffusers.utils import logging as dlog
        dlog.set_verbosity_error()
    except Exception:
        pass

    want = a.device.lower()
    if want == "cuda" and not torch.cuda.is_available():
        log("no CUDA GPU available: running on the CPU (extremely slow)")
        want = "cpu"
    device = torch.device(want)
    if device.type == "cuda":
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
        if a.precision == "fp32": dtype = torch.float32
        elif a.precision == "fp16" or not torch.cuda.is_bf16_supported(): dtype = torch.float16
        else: dtype = torch.bfloat16
    else:
        torch.set_num_threads(max(1, os.cpu_count() or 1))
        dtype = torch.float32

    # the stand-in exists only while the RelayVSR modules load: PyTorch itself never sees it
    if not has_triton:
        stub_triton()
    import refvsr_inference.models.wan.transformer as wan_tf
    import refvsr_inference.models.flash.model as flash_model
    from refvsr_inference.checkpoints import load_checkpoints
    from refvsr_inference.models.wan.sampling import WanVSRPipeline
    from refvsr_inference.runtime.streaming import CollaborativeFrameStream, InputFrame
    if not has_triton:
        for k in ("triton.language", "triton"): sys.modules.pop(k, None)

    # ---- exact PyTorch replacements for the two Triton kernels (same masks, same softmax scale) ----
    def spatial_sdpa(query, key, value, spec):
        """Q=[B,N,H,D], K/V=[B,M,H,D]; the boundary-preserving local window of SpatialAttentionSpec.mask_mod,
        processed in bands of query rows that only gather the key rows they can see."""
        b, n, heads, dim = query.shape
        hh, ww_ = spec.height, spec.width
        span = hh * ww_
        wh = min(spec.window_height, hh)
        key_frames = spec.frames if spec.key_frames is None else spec.key_frames
        mask_mod = spec.mask_mod()
        dev = query.device
        out = torch.empty_like(query)
        band = max(1, 2048 // max(1, ww_))
        for f in range(spec.frames):
            qt = f + spec.query_frame_offset
            kfs = [kf for kf in range(key_frames)
                   if kf + spec.key_frame_offset <= qt and kf + spec.key_frame_offset > qt - spec.temporal_window]
            for y0 in range(0, hh, band):
                y1 = min(hh, y0 + band)
                top0 = min(max(y0 - wh // 2, 0), hh - wh)
                top1 = min(max(y1 - 1 - wh // 2, 0), hh - wh) + wh
                qidx = torch.arange(f * span + y0 * ww_, f * span + y1 * ww_, device=dev)
                kidx = torch.cat([torch.arange(kf * span + top0 * ww_, kf * span + top1 * ww_, device=dev) for kf in kfs])
                m = mask_mod(0, 0, qidx[:, None], kidx[None, :])
                o = F.scaled_dot_product_attention(
                    query[:, qidx].transpose(1, 2), key[:, kidx].transpose(1, 2), value[:, kidx].transpose(1, 2),
                    attn_mask=m[None, None])
                out[:, qidx] = o.transpose(1, 2).to(out.dtype)
        return out

    state = {"triton": has_triton}
    orig_spatial = wan_tf.spatial_attention
    orig_local = flash_model.local_attention

    def spatial_attention(query, key, value, spec):
        if state["triton"]:
            try:
                return orig_spatial(query, key, value, spec)
            except Exception as e:
                if "out of memory" in str(e).lower(): raise
                state["triton"] = False
                log("Triton kernels unavailable (%s): exact PyTorch attention is used instead" % type(e).__name__)
        return spatial_sdpa(query, key, value, spec)

    def local_attention(*args, **kw):
        if state["triton"]:
            try:
                return orig_local(*args, **kw)
            except Exception as e:
                if "out of memory" in str(e).lower(): raise
                state["triton"] = False
                log("Triton kernels unavailable (%s): exact PyTorch attention is used instead" % type(e).__name__)
        kw["backend"] = "sdpa"
        return orig_local(*args, **kw)

    wan_tf.spatial_attention = spatial_attention
    flash_model.local_attention = local_attention

    # ---- model, exactly as the official inference.py builds it ----
    t_load = time.time()
    weights = os.path.join(here, "weights")
    wan, flash = load_checkpoints(os.path.join(weights, "wan.safetensors"), os.path.join(weights, "flash.safetensors"),
                                  device=device, dtype=dtype)
    seed = 42
    sampler = WanVSRPipeline(
        scheduler_config={"num_train_timesteps": 1000, "shift": 5.0},
        sampling={"execution_mode": "streaming", "num_inference_steps": 1, "timesteps": [1000], "guidance_scale": 1.0,
                  "seed": seed, "kv_cache_window_size": wan.temporal_context_frames})
    sampler.prepare_streaming(model=wan, prompt_embeds=wan.prompt_embeds, device=device, compute_dtype=dtype)
    stream = CollaborativeFrameStream(wan_model=wan, flash_model=flash, wan_pipeline=sampler,
                                      prompt_embeds=wan.prompt_embeds, device=device, compute_dtype=dtype,
                                      window_size=max(2, a.window), seed=seed)
    W, H, U = a.w, a.h, a.u
    OW, OH = W * U, H * U
    name = torch.cuda.get_device_name(0) if device.type == "cuda" else "CPU"
    log("device %s | %s | %s | %dx%d -> %dx%d (native x4%s) | keyframe every %d frames | %s | model loaded in %.0f s" % (
        device.type, name, str(dtype).replace("torch.", ""), W, H, OW, OH, "" if U == 4 else ", resized to x%d" % U,
        max(1, a.window - 1), "Triton" if has_triton else "PyTorch attention", time.time() - t_load))

    fsize = W * H * 3
    inq, outq = queue.Queue(maxsize=24), queue.Queue(maxsize=8)
    stdin = sys.stdin.buffer

    def reader():
        try:
            while True:
                buf = stdin.read(fsize)
                if not buf or len(buf) < fsize: break
                inq.put(buf)
        finally:
            inq.put(None)

    def writer():
        while True:
            item = outq.get()
            if item is None: break
            real_out.write(item)
        real_out.flush()

    threading.Thread(target=reader, daemon=True).start()
    tw = threading.Thread(target=writer, daemon=True); tw.start()

    def packets():
        i = 0
        while True:
            buf = inq.get()
            if buf is None: return
            rgb = torch.frombuffer(bytearray(buf), dtype=torch.uint8).view(H, W, 3).permute(2, 0, 1).contiguous()
            yield InputFrame(i, rgb, source_time=i / 25.0)
            i += 1

    def finish(x):                                  # 1x3x4Hx4W in [-1, 1] -> OHxOWx3 uint8 bytes
        x = x[:, :, :H * 4, :W * 4].float()
        if U in (1, 2):
            x = F.interpolate(x, size=(OH, OW), mode="area")
        elif U == 3:
            x = F.interpolate(x, size=(OH, OW), mode="bicubic", align_corners=False, antialias=True)
        x = x[0].add(1).mul_(127.5).round_().clamp_(0, 255).to(torch.uint8)
        return x.permute(1, 2, 0).contiguous().cpu().numpy().tobytes()

    n = 0; t0 = time.time()
    with torch.no_grad():
        try:
            for result in stream.frames(packets()):
                outq.put(finish(result.rgb))
                n += 1
                if n == 1 or n % 10 == 0:
                    log("frame %d | %.2f fps" % (n, n / max(1e-3, time.time() - t0)))
        except ValueError as e:
            if "empty stream" not in str(e).lower(): raise
    outq.put(None); tw.join()
    log("done | %d frames | %.2f fps" % (n, n / max(1e-3, time.time() - t0)))


if __name__ == "__main__":
    main()
