# Development and upstream updates

Source of truth: src/ (Lite 2.3.1). The upstream GitHub main branch was
older than its v2.3.0 RAR Source/, so never replace src/ with upstream
GitHub main without an explicit version review.

Windows build: build.bat → dist/FFmpegForge.exe.
Release build: scripts/Build-Portable.ps1 → dist/FFMpegForgeLite.zip.
UI regression: scripts/Test-Ui.ps1 runs on Windows with STA.

Upstream baseline: archive/upstream/v2.3.0/README.md contains the manifest for the original RAR, which is backed up in an owner-accessible draft Release attachment. The 209 MB binary is deliberately not part of Git history; verify SHA256 against the archive manifest.

To merge upstream changes:
1. Fetch the latest official upstream release archive.
2. Compare its Source/src/ against the maintained src/ in a scratch branch.
3. Merge features selectively; retain Lite's DarkControls/DarkNumeric and
   DVD RIP layout fixes.
4. Update assembly versions and release notes, then build and test.
5. Do not force push, delete old releases, or regenerate binaries in root.

A separate workflow fetches upstream main metadata for review without
automatically restoring obsolete files.
