# FFmpeg Forge Lite v2.3.4

## Accepted baseline
- Windows-tested `forge/upstream240-film-preview`, commit `747112cc27892c7c6309b58e3320e99884361af6`.
- Filmstrip source extracted from upstream 2.4.0 Portable RAR, not the outdated Git tag (see `docs/UPSTREAM-2.4.0-FILMSTRIP.md`).
- Prior HDR10/HLG display-only colour correction retained.

## New in v2.3.4
- Animated 35 mm film preview with sprockets, frame numbering, vignette and encoding progress.
- Pause/P freezes preview while encoding continues. PageUp/PageDown or ◀/▶ review the latest 16 sampled original/final pairs. Live resumes.
- Space still switches slider/side-by-side; Left/Right adjust split.
- HDR10/HLG, SDR and TrueHDR preview corrections remain intact without changing actual encoding.
- Standard UI, RTX/AI/Scale and DVD RIP features remain.
- One Release attachment: `FFMpegForgeLite.v2.3.4.zip`; GitHub's two auto source archives; no separate Source.zip or SHA file.
- No bundled FFmpeg/FFprobe/FFplay; older releases untouched.

## Verification
Windows CI checks 2.3.4.0 assembly, package manifest/digest, HDR display preview, filmstrip rendering, HOLD/Live, bounded before/after review history, and WinForms/Scale/DVD RIP regressions.
