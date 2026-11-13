# FFmpeg Forge - VisionNext (VOSR 2.0) streaming helper.
# Runs inside the official VOSR folder (github.com/cswry/VOSR, Apache-2.0). Reads raw RGB24 frames on stdin and
# writes the upscaled RGB24 frames on stdout, so FFmpeg Forge streams the video straight from the decoder to the
# final encoder: no picture files on disk, one single (final) encode, and the exact per-frame result of the
# official one-step inference (bicubic pre-resize, VOSR 2.0 one-step DiT, tiled latent inference, wavelet colour fix).
#   python -u forge_visionnext.py -w W -h H -u 1|2 -tile 256|512 -device cuda|cpu
import argparse, json, math, os, sys, time, threading, queue, importlib

def log(msg):
    sys.stderr.write("ForgeVN " + msg + "\n"); sys.stderr.flush()

def main():
    ap = argparse.ArgumentParser(add_help=False)
    ap.add_argument("-w", type=int, required=True)
    ap.add_argument("-h", type=int, required=True)
    ap.add_argument("-u", type=int, default=2)
    ap.add_argument("-tile", type=int, default=512)
    ap.add_argument("-device", default="cuda")
    ap.add_argument("-precision", default="bf16")     # bf16 (fast, tensor cores) or fp32 (reference)
    ap.add_argument("-frames", type=int, default=0)    # frames per GPU pass (0 = automatic)
    a = ap.parse_args()

    real_out = sys.stdout.buffer                   # frames go here; the VOSR code's prints are discarded
    sys.stdout = open(os.devnull, "w")
    # The VOSR code wraps some layers in torch.compile, which needs Triton: Triton does not exist for Windows
    # (TritonMissing error). Without it the model runs in normal (eager) mode: same result, no compilation step.
    import importlib.util, warnings
    warnings.filterwarnings("ignore")
    if importlib.util.find_spec("triton") is None:
        os.environ["TORCHDYNAMO_DISABLE"] = "1"
    here = os.path.dirname(os.path.abspath(__file__))
    os.chdir(here)
    sys.path.insert(0, here)
    import numpy as np
    import torch
    try:
        import torch._dynamo
        torch._dynamo.config.suppress_errors = True      # a broken Triton falls back to eager mode instead of failing
        if os.environ.get("TORCHDYNAMO_DISABLE") == "1": torch._dynamo.config.disable = True
    except Exception:
        pass
    import torch.nn.functional as F
    try:
        from diffusers.utils import logging as dlog
        dlog.set_verbosity_error()
    except Exception:
        pass
    from PIL import Image
    from torchvision import transforms
    from safetensors.torch import load_file
    inf = importlib.import_module("inference_vosr_onestep")

    want = a.device.lower()
    if want == "cuda" and not torch.cuda.is_available():
        log("no CUDA GPU available: running on the CPU (very slow)")
        want = "cpu"
    device = want
    if device == "cuda":
        torch.backends.cudnn.benchmark = True
        torch.backends.cuda.matmul.allow_tf32 = True
        torch.backends.cudnn.allow_tf32 = True
    else:
        torch.set_num_threads(max(1, os.cpu_count() or 1))

    # ---- configuration exactly as inference_vosr_onestep.py builds it (args.json + CLI defaults) ----
    ckpt = os.path.join("preset", "ckpts", "VOSR2")
    with open(os.path.join(ckpt, "args.json"), "r") as f:
        cfg = json.load(f)
    args = argparse.Namespace(**cfg)
    for k, v in dict(checkpoint=ckpt, upscale=a.u, process_size=512, align_method="wavelet", tile_size=a.tile,
                     tile_overlap=32, vae_tile_size=None, vae_tile_overlap=None, posterior_mode=True, infer_steps=1,
                     seed=42, force_rerun=True, config="").items():
        setattr(args, k, v)
    if getattr(args, "auxiliary_time_cond", None) is None:
        args.auxiliary_time_cond = True
    args.auxiliary_time_cond = bool(args.auxiliary_time_cond)
    if a.w * a.u * a.h * a.u > 6000000:              # very large outputs (×4 / ×8): tiled VAE from the start
        args.vae_tile_size, args.vae_tile_overlap = 1024, 128
    torch.manual_seed(args.seed); np.random.seed(args.seed)
    if device == "cuda": torch.cuda.manual_seed_all(args.seed)

    t_load = time.time()
    from models.qwenimage_vae2d import AutoencoderKLQwenImage2D
    vae = AutoencoderKLQwenImage2D.from_pretrained(getattr(args, "ae_path", inf.QWEN_AE_PATH))
    vae.to(device).eval()
    venc = inf.load_dinov2(args, device)
    base_channel = 16
    model = inf.LightningDiT(
        input_size=args.resolution // 8, patch_size=args.patch_size, in_channels=2 * base_channel, out_channels=base_channel,
        hidden_size=args.dim, depth=args.depth, num_heads=args.num_heads, mlp_ratio=args.mlp_ratio, z_dims=args.enc_dim,
        encdim_ratio=args.encdim_ratio, auxiliary_time_cond=args.auxiliary_time_cond, use_qknorm=args.use_qknorm,
        use_swiglu=args.use_swiglu, use_rope=args.use_rope, use_rmsnorm=args.use_rmsnorm, wo_shift=args.wo_shift,
        num_fused_layers=len(args.layer_dinov2b_list))
    weight = None
    for d in (os.path.join(ckpt, "clean_weights"), os.path.join(ckpt, "checkpoints"), ckpt):
        for name in ("ema_model.safetensors", "model.safetensors"):
            if os.path.isfile(os.path.join(d, name)): weight = os.path.join(d, name); break
        if weight: break
    model.load_state_dict(load_file(weight), strict=False)
    model.to(device).eval()
    model.forward = model.forward_flexible
    vosr = inf.VOSR(time_dist=args.time_dist, cfg_ratio=args.cfg_ratio, cfg_scale=args.cfg_scale, interp_type=args.interp_type,
                    accelerator=inf._inference_accelerator_stub(), t_start=args.t_start, t_end=args.t_end, args=args)
    for m_ in (model, venc, vae):
        for p_ in m_.parameters(): p_.requires_grad_(False)
    # bf16 on the GPU: tensor cores + flash attention (the DiT and DINOv2 use scaled_dot_product_attention).
    # Norms, RoPE, tile blending and the colour fix stay in fp32.
    bf16 = device == "cuda" and a.precision == "bf16" and torch.cuda.is_bf16_supported()
    if bf16:
        pass                                       # weights stay fp32: autocast casts them once per pass (cheap)
    def ac():
        return torch.autocast("cuda", dtype=torch.bfloat16) if bf16 else torch.autocast("cpu", enabled=False)
    name = (torch.cuda.get_device_name(0) if device == "cuda" else "CPU")
    log("device %s | %s | %s | %dx%d -> %dx%d | tile %d | model loaded in %.0f s" % (device, name, "bf16" if bf16 else "fp32", a.w, a.h, a.w * a.u, a.h * a.u, a.tile, time.time() - t_load))

    # ---- fast tiled inference: same tiles, Gaussian blending and steps as inf.tiled_latent_inference, but all the
    # tiles of a frame go through DINOv2 and the DiT in batches (one big GPU pass instead of one small pass per tile).
    # The noise is the same for every frame (seed 42, like each frame run on its own): no noise flicker in videos.
    AE = 8
    state = {"chunk": 64}
    noise_cache = {}
    def noise_like(x):
        k = tuple(x.shape)
        z = noise_cache.get(k)
        if z is None:
            g = torch.Generator(device="cpu").manual_seed(args.seed)
            z = torch.randn(k, generator=g, dtype=torch.float32).to(x.device)
            noise_cache.clear(); noise_cache[k] = z
        return z
    # every crop / input below holds B frames (B = frames processed together); "chunk" counts samples per pass
    def venc_batch(crops):                         # list of Bx3xhxw -> list (per crop) of feature lists (B each)
        out = [None] * len(crops)
        B = crops[0].shape[0]
        groups = {}
        for i, c in enumerate(crops): groups.setdefault(tuple(c.shape[2:]), []).append(i)
        for _, idx in groups.items():
            step = max(1, state["chunk"] // B)
            for j in range(0, len(idx), step):
                part = idx[j:j + step]
                with ac():
                    f = inf.get_venc_features(venc, torch.cat([crops[i] for i in part], 0), args)
                for k, i in enumerate(part):
                    out[i] = [l_[k * B:(k + 1) * B] for l_ in f]
        return out
    def dit_batch(inps, feats, t_cur, t_nxt):     # list of BxCxhxw -> list of BxCxhxw (fp32)
        res = []
        B = inps[0].shape[0]
        i = 0
        while i < len(inps):
            n_ = min(max(1, state["chunk"] // B), len(inps) - i)
            try:
                x = torch.cat(inps[i:i + n_], 0)
                fz = None if feats[0] is None else [torch.cat([feats[i + k][l_] for k in range(n_)], 0) for l_ in range(len(feats[0]))]
                with ac():
                    u = model(x, t_cur.expand(n_ * B), t_nxt.expand(n_ * B), fz)
                res.extend(u.float().split(B, 0)); i += n_
            except RuntimeError as e:
                if "out of memory" not in str(e).lower() or n_ * B <= B: raise
                if device == "cuda": torch.cuda.empty_cache()
                state["chunk"] = max(1, (n_ * B) // 2)
                log("GPU memory full: %d tiles per pass" % max(1, state["chunk"] // B))
        return res
    def upscale(lq):
        # sizes that are not a multiple of 8: reflect-pad for the VAE, crop the result back (no-op otherwise)
        H0, W0 = lq.shape[2], lq.shape[3]
        ph, pw = (-H0) % AE, (-W0) % AE
        if ph or pw:
            return upscale(F.pad(lq, (0, pw, 0, ph), mode="reflect"))[:, :, :H0, :W0]
        with ac():
            lq_latent, mean, std = inf.encode_dispatch(vae, lq, args, device)
        lq_latent = lq_latent.float()
        _, lc, lh, lw = lq_latent.shape
        ps = getattr(args, "patch_size", 2)
        n_steps = args.infer_steps
        t_seq = torch.linspace(1., 0., n_steps + 1, device=device)
        z = noise_like(lq_latent[:1]).expand_as(lq_latent).contiguous()     # same noise for every frame
        whole = args.tile_size <= 0
        if not whole:
            lt = max((args.tile_size // AE // ps) * ps, ps)
            lo = max(args.tile_overlap // AE, lt // 8)
            lt = min(lt, min(lh, lw))
            if lt < max(lh, lw): lt = max(ps, lt - lt % ps)   # big tiles (2048 / 4096) on a smaller frame: keep the tile patch-aligned
            lo = min(lo, lt - 1)
            whole = lh <= lt and lw <= lt
        if whole:
            with ac():
                fea = inf.get_venc_features(venc, lq, args)
            for i in range(n_steps):
                t_cur, t_nxt = t_seq[i], t_seq[i + 1]
                u = dit_batch([torch.cat([lq_latent, z], 1)], [fea], t_cur, t_nxt)[0]
                z = z - (t_cur - t_nxt) * u
        else:
            pos = [(hi, wi) for hi in inf._make_tile_grid(lh, lt, lo) for wi in inf._make_tile_grid(lw, lt, lo)]
            gw = inf._gaussian_weights(lt, lt, lc, device)
            crops = [lq[:, :, hi * AE:min((hi + lt) * AE, lq.shape[2]), wi * AE:min((wi + lt) * AE, lq.shape[3])] for hi, wi in pos]
            feats = venc_batch(crops)
            for i in range(n_steps):
                t_cur, t_nxt = t_seq[i], t_seq[i + 1]
                inps = [torch.cat([lq_latent[:, :, hi:hi + lt, wi:wi + lt], z[:, :, hi:hi + lt, wi:wi + lt]], 1) for hi, wi in pos]
                us = dit_batch(inps, feats, t_cur, t_nxt)
                u_acc = torch.zeros_like(lq_latent); w_acc = torch.zeros_like(lq_latent)
                for (hi, wi), u in zip(pos, us):
                    u_acc[:, :, hi:hi + lt, wi:wi + lt] += u * gw
                    w_acc[:, :, hi:hi + lt, wi:wi + lt] += gw
                z = z - (t_cur - t_nxt) * (u_acc / w_acc)
        with ac():
            sr = inf.decode_dispatch(vae, z, args, mean, std, None)
        return sr.float()

    # wavelet colour fix of the official script (cv2.GaussianBlur sigma 5, float32 -> 41-tap kernel, reflect-101)
    sig = 5.0; ks = int(round(sig * 4 * 2 + 1)) | 1
    g = torch.arange(ks, dtype=torch.float32) - ks // 2
    g = torch.exp(-(g * g) / (2 * sig * sig)); g = g / g.sum()
    def blur(x):                                   # x: 1x3xHxW float32
        p = ks // 2
        x = F.pad(x, (p, p, 0, 0), mode="reflect"); x = F.conv2d(x, g.view(1, 1, 1, ks).repeat(3, 1, 1, 1).to(x.device), groups=3)
        x = F.pad(x, (0, 0, p, p), mode="reflect"); x = F.conv2d(x, g.view(1, 1, ks, 1).repeat(3, 1, 1, 1).to(x.device), groups=3)
        return x
    def color_fix(sr_u8, src_u8):                  # HxWx3 uint8 arrays -> HxWx3 uint8
        t = torch.from_numpy(sr_u8).to(device).permute(2, 0, 1).unsqueeze(0).float() / 255.0
        s = torch.from_numpy(src_u8).to(device).permute(2, 0, 1).unsqueeze(0).float() / 255.0
        r = (blur(s) + (t - blur(t))).clamp(0, 1) * 255.0
        return r.squeeze(0).permute(1, 2, 0).to(torch.uint8).cpu().numpy()   # astype(uint8) truncates, like the original

    W, H, U = a.w, a.h, a.u
    fsize = W * H * 3
    # frames per GPU pass: 2 on big GPUs (better use of the GPU between the VAE, DINOv2 and DiT stages); OOM -> 1
    vram_gb = torch.cuda.get_device_properties(0).total_memory / 2 ** 30 if device == "cuda" else 0
    FR = {"n": max(1, a.frames) if a.frames > 0 else (2 if vram_gb >= 20 and W * U * H * U <= 4200000 else 1)}

    inq, outq = queue.Queue(maxsize=6), queue.Queue(maxsize=6)
    stdin = sys.stdin.buffer
    def reader():                                  # decoding + the official bicubic pre-resize run on the CPU in parallel
        try:
            while True:
                buf = stdin.read(fsize)
                if not buf or len(buf) < fsize: break
                raw = Image.frombytes("RGB", (W, H), buf)
                img = raw.resize((W * U, H * U), Image.BICUBIC) if U != 1 else raw
                inq.put(np.asarray(img))
        finally:
            inq.put(None)
    def writer():
        while True:
            item = outq.get()
            if item is None: break
            real_out.write(item.numpy().tobytes())
        real_out.flush()
    threading.Thread(target=reader, daemon=True).start()
    tw = threading.Thread(target=writer, daemon=True); tw.start()

    def run_batch(imgs):                           # list of HxWx3 uint8 (pre-resized) -> B x H x W x 3 uint8 (CPU)
        x8 = torch.from_numpy(np.stack(imgs)).to(device)                    # B H W 3
        lq = x8.permute(0, 3, 1, 2).float() / 255.0 * 2.0 - 1.0              # = ToTensor() * 2 - 1
        while True:
            try:
                sr = upscale(lq); break
            except RuntimeError as e:
                # big outputs (×4 / ×8): the VAE encode / decode switches to tiles (official tiled_vae) instead of failing
                if "out of memory" not in str(e).lower() or (args.vae_tile_size or 0) == 256: raise
                if device == "cuda": torch.cuda.empty_cache()
                args.vae_tile_size = 1024 if not args.vae_tile_size else max(256, args.vae_tile_size // 2)
                args.vae_tile_overlap = max(64, args.vae_tile_size // 8)
                log("GPU memory full: VAE tiles of %d px" % args.vae_tile_size)
        sr8 = (sr.float() * 0.5 + 0.5).mul(255).to(torch.uint8)            # = ToPILImage() (truncation), on the GPU
        t = sr8.float() / 255.0
        src = x8.permute(0, 3, 1, 2).float() / 255.0
        r = (blur(src) + (t - blur(t))).clamp(0, 1) * 255.0                 # wavelet colour fix, on the GPU
        return r.to(torch.uint8).permute(0, 2, 3, 1).cpu()

    n = 0; t0 = time.time(); done = False
    log("frames per pass: %d" % FR["n"])
    with torch.no_grad():
        while not done:
            imgs = []
            while len(imgs) < FR["n"]:
                it = inq.get()
                if it is None: done = True; break
                imgs.append(it)
            if not imgs: break
            try:
                outs = run_batch(imgs)
            except RuntimeError as e:
                if "out of memory" not in str(e).lower() or len(imgs) == 1: raise
                if device == "cuda": torch.cuda.empty_cache()
                FR["n"] = 1
                log("GPU memory full: 1 frame per pass")
                outs = torch.cat([run_batch([im]) for im in imgs], 0)
            for k in range(outs.shape[0]):
                outq.put(outs[k])
            n0 = n; n += len(imgs)
            if n0 == 0 or n // 10 != n0 // 10:
                log("frame %d | %.2f fps" % (n, n / max(1e-3, time.time() - t0)))
    outq.put(None); tw.join()
    log("done | %d frames | %.2f fps" % (n, n / max(1e-3, time.time() - t0)))

if __name__ == "__main__":
    main()
