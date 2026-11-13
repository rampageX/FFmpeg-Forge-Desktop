# FFmpeg Forge - AI denoiser helper (SCUNet "real" model + temporal stabilisation)
# Reads raw RGB24 frames on stdin, writes denoised RGB24 frames on stdout.
#   python -u forge_ai_denoise.py -w WIDTH -h HEIGHT [-model scunet_color_real_psnr.pth] [-tile 0=auto] [-temporal 1] [-device auto|cuda|cpu]
# SCUNet: Kai Zhang et al., "Practical Blind Image Denoising via Swin-Conv-UNet and Data Synthesis" (Apache-2.0).
#
# Speed: whole frames (or big batched tiles) sized to the GPU memory, several frames per forward pass when they
# are small, fp16, cuDNN autotuning, cached attention bias / masks, and reading / GPU work / writing overlapped
# in three threads. Out-of-memory errors shrink the work size automatically instead of failing.
import argparse, os, sys, time, threading, queue

def log(msg):
    sys.stderr.write("ForgeAI " + msg + "\n"); sys.stderr.flush()

def main():
    ap = argparse.ArgumentParser(add_help=False)
    ap.add_argument("-w", type=int, required=True)
    ap.add_argument("-h", type=int, required=True)
    ap.add_argument("-model", default=None)
    ap.add_argument("-tile", type=int, default=0)
    ap.add_argument("-temporal", type=int, default=1)
    ap.add_argument("-device", default="auto")
    ap.add_argument("-graph", type=int, default=1)      # CUDA Graph replay of the whole network (whole-frame mode)
    a = ap.parse_args()

    real_out = sys.stdout.buffer        # frames go here; anything the libraries print is discarded
    sys.stdout = open(os.devnull, "w")
    here = os.path.dirname(os.path.abspath(__file__))
    sys.path.insert(0, here)
    import numpy as np
    import torch
    import torch.nn.functional as F
    import network_scunet
    from network_scunet import SCUNet

    # ---- cache what SCUNet rebuilds on every call (relative position bias, shifted-window masks) ----
    def rel_cached(self):
        c = getattr(self, "_rel_cache", None)
        p = self.relative_position_params
        if c is None or c.dtype != p.dtype or c.device != p.device:
            ws = self.window_size
            cord = torch.tensor(np.array([[i, j] for i in range(ws) for j in range(ws)]), device=p.device)
            rel = cord[:, None, :] - cord[None, :, :] + ws - 1
            c = p[:, rel[:, :, 0].long(), rel[:, :, 1].long()]
            self._rel_cache = c
        return c
    orig_mask = network_scunet.WMSA.generate_mask
    def mask_cached(self, h, w, p, shift):
        cache = self.__dict__.setdefault("_mask_cache", {})
        key = (h, w, p, shift, str(self.relative_position_params.device))
        m = cache.get(key)
        if m is None:
            m = orig_mask(self, h, w, p, shift)
            if len(cache) > 8: cache.clear()
            cache[key] = m
        return m
    network_scunet.WMSA.relative_embedding = rel_cached
    network_scunet.WMSA.generate_mask = mask_cached

    # ---- fused window attention: one scaled_dot_product_attention call (memory-efficient GPU kernel) instead of
    # einsum -> + bias -> mask -> softmax -> einsum. Same maths; the relative-position bias and the shifted-window
    # mask are merged once into a single additive bias per window layout and cached.
    from einops import rearrange
    def bias_cached(self, hw, ww, b, dtype):
        cache = self.__dict__.setdefault("_bias_cache", {})
        key = (hw, ww, b, dtype)
        bias = cache.get(key)
        if bias is None:
            rel = self.relative_embedding().to(dtype)                       # h p q
            if self.type != "W":
                m = self.generate_mask(hw, ww, self.window_size, shift=self.window_size // 2)[0, 0]   # nw p q (bool)
                bias = rel.unsqueeze(0).expand(m.shape[0], -1, -1, -1).clone()                    # nw h p q
                bias.masked_fill_(m.unsqueeze(1), float("-inf"))
                bias = bias.repeat(b, 1, 1, 1)                                                      # (b nw) h p q
            else:
                bias = rel.unsqueeze(0)                                                             # 1 h p q
            if len(cache) > 6: cache.clear()
            cache[key] = bias
        return bias
    def wmsa_forward(self, x):
        ws = self.window_size
        if self.type != "W": x = torch.roll(x, shifts=(-(ws // 2), -(ws // 2)), dims=(1, 2))
        b, H_, W_, c = x.shape
        hw, ww = H_ // ws, W_ // ws
        x = x.view(b, hw, ws, ww, ws, c).permute(0, 1, 3, 2, 4, 5).reshape(b * hw * ww, ws * ws, c)
        qkv = self.embedding_layer(x).view(b * hw * ww, ws * ws, 3 * self.n_heads, self.head_dim).transpose(1, 2)
        q, k, v = qkv.split(self.n_heads, dim=1)                                  # (b nw) h np c
        o = F.scaled_dot_product_attention(q, k, v, attn_mask=bias_cached(self, hw, ww, b, q.dtype), scale=self.scale)
        o = self.linear(o.transpose(1, 2).reshape(b * hw * ww, ws * ws, self.n_heads * self.head_dim))
        o = o.view(b, hw, ww, ws, ws, -1).permute(0, 1, 3, 2, 4, 5).reshape(b, H_, W_, -1)
        if self.type != "W": o = torch.roll(o, shifts=(ws // 2, ws // 2), dims=(1, 2))
        return o
    if hasattr(F, "scaled_dot_product_attention"):
        network_scunet.WMSA.forward = wmsa_forward

    dev = a.device
    if dev == "auto":
        dev = "cuda" if torch.cuda.is_available() else "cpu"
    cuda = dev == "cuda"
    if cuda:
        torch.backends.cudnn.benchmark = True
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
    else:
        torch.set_num_threads(max(1, os.cpu_count() or 1))

    model_path = a.model or os.path.join(here, "scunet_color_real_psnr.pth")
    arch, mem_factor = "SCUNet", 0.30
    if os.path.basename(model_path).lower().startswith("scunet"):
        net = SCUNet(in_nc=3, config=[4, 4, 4, 4, 4, 4, 4], dim=64)
        net.load_state_dict(torch.load(model_path, map_location="cpu", weights_only=True), strict=True)
        net.eval()
        for p in net.parameters():
            p.requires_grad_(False)
        net = net.to(dev)
        dtype = torch.float16 if cuda else torch.float32
        if cuda:
            net = net.half()
    else:
        # restoration models (HAT, DAT, SPAN… ×1 / ×2 / ×4) loaded by spandrel (MIT, chaiNNer). They clean the picture
        # while upscaling; the result is brought back to the source size with an antialiased bicubic (supersampling),
        # so the export keeps its resolution and gets the model's denoising / restoration at full quality.
        import spandrel
        import torch.nn as nn
        desc = spandrel.ModelLoader().load_from_file(model_path)
        arch, sc = desc.architecture.name, int(desc.scale)
        sr = desc.model.eval()
        for p in sr.parameters():
            p.requires_grad_(False)
        if cuda:
            dtype = torch.float16 if desc.supports_half else (torch.bfloat16 if desc.supports_bfloat16 and torch.cuda.is_bf16_supported() else torch.float32)
        else:
            dtype = torch.float32
        sr = sr.to(dev, dtype=dtype)
        class Restore(nn.Module):
            def __init__(self, m, scale):
                super().__init__(); self.m = m; self.scale = scale
            def forward(self, x):
                y = self.m(x)
                if self.scale == 1: return y
                y = F.interpolate(y.float(), size=(x.shape[2], x.shape[3]), mode="bicubic", align_corners=False, antialias=True)
                return y.to(x.dtype)
        net = Restore(sr, sc).eval()
        # transformers (HAT, DAT…) need far more memory per pixel than light CNNs (SPAN, Compact…)
        mem_factor = 0.30 if arch in ("SPAN", "SRVGG (RealESRGAN Compact)", "Compact", "RealCUGAN") else 0.03
        arch = "%s ×%d %s" % (arch, sc, {torch.float16: "fp16", torch.bfloat16: "bf16"}.get(dtype, "fp32"))

    W, H = a.w, a.h
    fsize = W * H * 3
    ov = 32
    # work size (pixels per forward pass) from the GPU memory; shrinks by itself on out-of-memory
    if cuda:
        vram = torch.cuda.get_device_properties(0).total_memory / 2 ** 30
        budget = int(max(0.06, min(12.0, vram * mem_factor)) * 1e6)
        name = torch.cuda.get_device_name(0) + " (%.0f GB)" % vram
    else:
        budget = int(0.5e6)
        name = "CPU (slow, no CUDA GPU found)"
    if a.tile > 0:
        budget = a.tile * a.tile
    state = {"budget": budget}

    ramp_cache = {}
    def weights(th, tw, top, bottom, left, right):
        key = (th, tw, top, bottom, left, right)
        wgt = ramp_cache.get(key)
        if wgt is None:
            def ramp(n, first, last):
                r = torch.ones(n, device=dev, dtype=dtype)
                if n > 2 * ov:
                    s = torch.linspace(0.05, 1.0, ov, device=dev, dtype=dtype)
                    if not first: r[:ov] = s
                    if not last: r[-ov:] = s.flip(0)
                return r
            wgt = ramp(th, top, bottom)[:, None] * ramp(tw, left, right)[None, :]
            ramp_cache[key] = wgt
        return wgt

    def forward_frames(x):                  # x: Bx3xHxW in [0,1] -> same
        b = x.shape[0]
        if b * H * W <= state["budget"]:
            return net(x)
        # tiles: as big as the budget allows, all the same size, processed several at a time
        side = int(state["budget"] ** 0.5)
        th = max(128, min(H, side)); tw = max(128, min(W, side))
        th -= th % 64 if th < H else 0; tw -= tw % 64 if tw < W else 0
        th = max(128, min(H, th)); tw = max(128, min(W, tw))
        def starts(n, t):
            if n <= t: return [0]
            st = list(range(0, n - t, t - ov)); st.append(n - t); return st
        coords = [(y0, x0) for y0 in starts(H, th) for x0 in starts(W, tw)]
        per = max(1, state["budget"] // (th * tw))
        out = torch.zeros_like(x)
        wsum = torch.zeros((1, 1, H, W), device=x.device, dtype=x.dtype)
        for i in range(0, len(coords), per):
            chunk = coords[i:i + per]
            batch = torch.cat([x[:, :, y0:y0 + th, x0:x0 + tw] for (y0, x0) in chunk], 0)
            o = net(batch)
            for k, (y0, x0) in enumerate(chunk):
                wgt = weights(th, tw, y0 == 0, y0 + th >= H, x0 == 0, x0 + tw >= W)
                out[:, :, y0:y0 + th, x0:x0 + tw] += o[k * b:(k + 1) * b] * wgt
                wsum[:, :, y0:y0 + th, x0:x0 + tw] += wgt
        return out / wsum.clamp_min(1e-4)

    def run(x):
        while True:
            try:
                return forward_frames(x)
            except RuntimeError as e:          # torch.cuda.OutOfMemoryError is a RuntimeError
                if "out of memory" not in str(e).lower() or state["budget"] <= 128 * 128:
                    raise
                if cuda: torch.cuda.empty_cache()
                state["budget"] = max(128 * 128, state["budget"] // 2)
                log("GPU memory full: smaller work size (%d px)" % state["budget"])

    frames_per_pass = max(1, min(8, state["budget"] // (W * H)))

    # ---- CUDA Graph: SCUNet launches thousands of small GPU operations per pass; on Windows the Python / driver
    # overhead of launching them one by one costs more than the maths. The whole network is captured once and
    # replayed with a single call per batch (same kernels, same result).
    gs = None
    if cuda and a.graph and W * H <= state["budget"]:
      B = frames_per_pass
      while gs is None and B >= 1:
        try:
            with torch.inference_mode():
                s_in = torch.zeros((B, 3, H, W), device=dev, dtype=dtype)
                side = torch.cuda.Stream(); side.wait_stream(torch.cuda.current_stream())
                with torch.cuda.stream(side):
                    for _ in range(3): net(s_in)
                torch.cuda.current_stream().wait_stream(side)
                torch.cuda.synchronize()
                g = torch.cuda.CUDAGraph()
                with torch.cuda.graph(g):
                    s_out = net(s_in)
                torch.cuda.synchronize()
            gs = (g, s_in, s_out, B)
            frames_per_pass = B
        except Exception as e:
            gs = None; g = s_in = s_out = None
            try: torch.cuda.synchronize(); torch.cuda.empty_cache()
            except Exception: pass
            if "out of memory" in str(e).lower() and B > 1:
                B //= 2; continue
            log("CUDA Graph not available (%s): normal mode" % str(e).splitlines()[0][:120])
            break
    plain_run = run
    def run(x):
        if gs is None: return plain_run(x)
        g, s_in, s_out, B = gs
        k = x.shape[0]
        if k < B: x = torch.cat([x, x[-1:].expand(B - k, -1, -1, -1)], 0)
        s_in.copy_(x)
        g.replay()
        return s_out[:k].clone()

    log("device " + dev + " | " + name + " | " + arch + " | " + str(W) + "x" + str(H) +
        (" | whole frames x%d per pass" % frames_per_pass if W * H <= state["budget"] else " | tiles") +
        (" | CUDA Graph" if gs is not None else "") + (" | fused attention" if arch == "SCUNet" else ""))

    # ---- reader / writer threads: decoding, GPU work and encoding overlap ----
    inq, outq = queue.Queue(maxsize=max(8, 2 * frames_per_pass)), queue.Queue(maxsize=max(8, 2 * frames_per_pass))
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
    tr = threading.Thread(target=reader, daemon=True); tr.start()
    tw_ = threading.Thread(target=writer, daemon=True); tw_.start()

    prev = None
    n = 0
    t0 = time.time()
    done = False
    with torch.inference_mode():
        while not done:
            bufs = []
            while len(bufs) < frames_per_pass:
                buf = inq.get()
                if buf is None: done = True; break
                bufs.append(buf)
                if gs is None and inq.empty() and bufs: break   # do not wait for a full batch when frames come slowly
            if not bufs: break
            arr = np.frombuffer(bytearray(b"".join(bufs)), dtype=np.uint8).reshape(len(bufs), H, W, 3)
            t = torch.from_numpy(arr)
            if cuda: t = t.to(dev)
            x = t.permute(0, 3, 1, 2).to(dtype) / 255.0
            y = run(x).clamp_(0, 1)
            outs = []
            for k in range(y.shape[0]):
                yk = y[k:k + 1]
                if a.temporal and prev is not None:
                    # Neat Video-style temporal stabilisation: in still areas the new frame is blended with the
                    # previous result (no flicker); where the picture moves, the new frame is kept as is.
                    d = (yk - prev).abs().mean(1, keepdim=True)
                    d = F.avg_pool2d(d, 9, stride=1, padding=4)
                    wgt = 0.55 * torch.exp(-(d / 0.025) ** 2)
                    yk = yk + wgt * (prev - yk)
                prev = yk
                outs.append(yk)
            o = (torch.cat(outs, 0).permute(0, 2, 3, 1) * 255.0 + 0.5).clamp_(0, 255).to(torch.uint8).cpu().numpy()
            for k in range(o.shape[0]):
                outq.put(o[k].tobytes())
            n += len(bufs)
            if n <= len(bufs) or n % 25 < len(bufs):
                log("frame %d | %.2f fps" % (n, n / max(1e-3, time.time() - t0)))
    outq.put(None)
    tw_.join()
    log("done | %d frames | %.2f fps" % (n, n / max(1e-3, time.time() - t0)))

if __name__ == "__main__":
    main()
