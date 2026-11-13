# FFmpeg Forge - QTGMC deinterlacing (VapourSynth + vsjetpack QTempGaussMC)
# Reads the video with BestSource, deinterlaces it with QTGMC and writes YUV4MPEG2 frames to stdout.
#   python forge_qtgmc.py -src <file> -mode bob|same -order auto|tff|bff [-ss sec] [-to sec]
import sys, os, argparse

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("-src", required=True)
    ap.add_argument("-mode", default="bob")
    ap.add_argument("-order", default="auto")
    ap.add_argument("-ss", type=float, default=0.0)
    ap.add_argument("-to", type=float, default=-1.0)
    ap.add_argument("-threads", type=int, default=0)
    ap.add_argument("-probe", action="store_true")
    a = ap.parse_args()

    import vapoursynth as vs
    core = vs.core
    if a.threads > 0:
        core.num_threads = a.threads
    if a.probe:
        from vsdeinterlace import QTempGaussMC  # noqa: F401
        for ns in ("bs", "mv", "znedi3", "zsmooth", "resize2", "akarin", "vszip"):
            if not hasattr(core, ns) and not (ns == "mv" and hasattr(core, "mvu")):
                print("FORGE_QTGMC_MISSING " + ns, flush=True)
                return 2
        print("FORGE_QTGMC_OK " + str(vs.__version__), flush=True)
        return 0

    from vsdeinterlace import QTempGaussMC
    try:
        clip = core.bs.VideoSource(a.src, cachemode=0)   # index in memory, no cache file next to the video
    except Exception:
        clip = core.bs.VideoSource(a.src)

    order = a.order
    if order == "auto":
        fb = 0
        try:
            fb = int(clip.get_frame(min(10, clip.num_frames - 1)).props.get("_FieldBased", 0))
        except Exception:
            fb = 0
        order = "bff" if fb == 1 else "tff"
    tff = order != "bff"

    fps = clip.fps.numerator / clip.fps.denominator if clip.fps.denominator else 25.0
    first = int(round(max(0.0, a.ss) * fps))
    last = clip.num_frames if a.to <= 0 else min(clip.num_frames, int(round(a.to * fps)))
    if first > 0 or last < clip.num_frames:
        clip = clip[first:max(first + 1, last)]

    if a.mode == "same":
        out = QTempGaussMC(motion_blur_fps_divisor=2).deinterlace(clip, tff=tff)
    else:
        out = QTempGaussMC().bob(clip, tff=tff)
    out = core.std.SetFieldBased(out, 0)

    sys.stderr.write("ForgeQTGMC: %dx%d %s, %s field first, %s -> %d frames at %.3f fps\n" % (
        out.width, out.height, out.format.name, "top" if tff else "bottom",
        "double rate" if a.mode != "same" else "same rate", out.num_frames, out.fps.numerator / max(1, out.fps.denominator)))
    sys.stderr.flush()
    try:
        out.output(sys.stdout.buffer, y4m=True)
    except (BrokenPipeError, OSError):
        return 0
    sys.stdout.buffer.flush()
    return 0

if __name__ == "__main__":
    sys.exit(main())
