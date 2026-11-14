# FFmpeg Forge Lite v2.3.3 — HDR preview color correction

## User acceptance

The project owner confirmed the repaired live preview on Windows on 2026-11-14, after testing the `forge/hdr-preview-tonemap` development build. This release uses that tested source revision, plus version and release metadata changes only.

## Changes from v2.3.2

- Live before/after preview: convert HDR10 (PQ) and HLG source frames through an SDR display-only chain based on the Film Grain Studio approach: source primaries/transfer/matrix/range → linear → Hable tone mapping → BT.709 → JPEG.
- Extend FFprobe's source metadata capture to color primaries, colorspace matrix, and limited/full range so preview transformations do not blindly assume transfer and gamut.
- Apply correction to ordinary FFmpeg exports and relevant AI/RTX preview branches; keep SDR inputs unchanged.
- Avoid double tone mapping when the user's filter chain already converts HDR to SDR.
- Recognize HDR output generated via SDR-to-HDR and RTX TrueHDR paths when constructing display previews.
- Skip HDR preview with an informative log if the selected FFmpeg lacks `zscale` or `tonemap`, without changing the actual export.
- Retain v2.3.2's independent Scale window, compatibility guide, custom AI controls, dark UI and persisted main/Scale/RIP window geometry.

## Integrity and verification

- Canonical source tree: `src/`. Assembly, file and informational versions: **2.3.3**.
- One downloadable release asset: `FFMpegForgeLite.zip`. GitHub generates its own two source archives. No manual Source ZIP or checksum attachment.
- Build script verifies upstream pristine 2.3.0 RAR helper baseline, assembly version, portable ZIP content and SHA-256.
- Windows CI tests generated HDR10/HLG/SDR preview commands, no redundant tone mapping, AI route and UI behavior, three-window geometry, and DVD RIP layout.
- The production processing pipeline and HDR video metadata remain unchanged; the new conversion is only for the SDR JPEG comparison window.
- Historical releases are untouched.
