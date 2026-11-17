# Upstream 2.4.0 filmstrip preview integration

- Source: `Aaronstanley78/FFmpeg-Forge-Desktop` official release `2.4.0` (`FFmpegForge-2.4.0-Portable.rar`, published 2026-11-17). The Git tag does **not** carry the new preview sources; the release RAR does.
- Source recovered in an isolated Windows GitHub Actions inspection run: https://github.com/rampageX/FFmpeg-Forge-Desktop/actions/runs/44196344218
- UI: migrate only the `PreviewWindow`, `PreviewHeader`, and `FilmStripView` implementations from the archive's 659-line `Preview.cs` into `src/Preview.cs`.
- Preserve the fork's existing `PreviewFrames` export/preview branches, HDR10/HLG Hable SDR display correction, source metadata probing, protection against duplicate tonemapping, and conditional zscale/tonemap capability guard.
- Bind current conversion `Job` to `PreviewWindow.Job` to drive REC / DONE / STOPPED, frame count, timecode, encode progress, and ETA.
- Replace upstream film-edge branding with `FFMPEG FORGE LITE`.
- Add Windows CI smoke tests for film rendering (slider and side-by-side), frame history and new-job reset, progress/REC/DONE, alongside preexisting HDR and UI regressions.
- Validated build: https://github.com/rampageX/FFmpeg-Forge-Desktop/actions/runs/44196808644
- This is an **unreleased** development-branch integration. The branch carries no downloaded upstream RAR or duplicated source tree; only the implemented UI and this provenance note.
