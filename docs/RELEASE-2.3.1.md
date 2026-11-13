# FFmpeg Forge Lite v2.3.1

Release date: 2026-11-13

Based on the complete official FFmpeg Forge v2.3.0 release source (not the old v1.1.3a GitHub source tree).

- Reorganized the canonical, fully tested C# source under `src/`. Generated binaries now go to the ignored `dist/` directory instead of the Git root.
- Removed decorative artwork from the application header and empty file list.
- Darkened WinForms scrollbars, ComboBox borders/arrows, numeric spinners and DVD RIP progress bar using controlled paint timing.
- Eliminated unnecessary option expansion animation and reduced white flashing through batched layout and limited compositing.
- Reworked DVD RIP dropdown widths, layout and scrolling for different window sizes.
- Lightweight Windows portable download: **FFMpegForgeLite.zip**, without bundled ffmpeg.exe, ffprobe.exe or ffplay.exe.
- Original official FFmpegForge-2.3.0-Portable.rar (209,148,735 bytes) backed up separately as a SHA256-verified draft GitHub Release asset; its manifest lives under `archive/upstream/v2.3.0/`.

CI checks: assembly version `2.3.1.0`, 407 main-window controls including 52 dark dropdowns and 19 dark number inputs, 16 dynamic option transitions, 980×720 / 1180×860 / 1525×953 DVD RIP synthetic layouts, ZIP manifest and upstream baseline SHA256. Screen-level visual regression was already confirmed by the user on the preceding Lite test build.

The only published binary attachment should be `FFMpegForgeLite.zip`; GitHub automatically provides the two source archives. Historical Releases remain unchanged.
