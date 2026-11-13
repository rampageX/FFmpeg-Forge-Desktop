# FFmpeg Forge Lite — build and upstream sync

This fork has two important upstream source versions: **GitHub `main` still contains v1.1.3a sources**, while the official **v2.3.0 Portable RAR includes the actual v2.3.0 `Source/`**. Compiling the GitHub root `FFmpegForge/build.bat` directly would regress the application. The Lite workflow deliberately downloads the official release archive and checks the embedded source version before building.

## Build a Windows portable ZIP

1. Open **Actions → Build FFmpeg Forge Lite → Run workflow**.
2. Enter the official upstream release tag, e.g. `2.3.0`. Leave **publish = false** for a test build.
3. Download the **FFmpegForge-Lite-portable** workflow artifact after the build succeeds.
4. To publish a version, run the workflow again with **publish = true**. The release is `v<upstream-version>-rampage.1`. Existing release tags are never overwritten.

The source patch disables only the decorative header-art draw, retaining the application's title, logo, version badge, status, and rest of the UI. The build packages `FFmpegForge.exe`, `ForgeRTX.exe`, `dovi_tool.exe`, licenses, portable marker and available model directory. The package has no `FFmpeg/`, `bin/`, `Source/` or `ffmpeg.exe`, `ffprobe.exe`, `ffplay.exe`.

On first use, select **FFmpeg location...** to point to your own `ffmpeg.exe`. Keep `ffprobe.exe` in the same folder if you need input metadata, duration and progress.

The build fails safely if the upstream source version or the banner drawing code changes. That is deliberate: it prevents silently shipping an unchanged banner or an older executable.

## Merge later upstream GitHub changes

Open **Actions → Sync upstream main → Run workflow**. This performs a normal `git fetch` + `git merge` onto your fork's `main` branch, then pushes without force. A conflict stops the job; no repository history is overwritten.

The Lite patch is stored separately in `scripts/Build-Portable.ps1`, rather than modifying old upstream source files, to reduce merge conflicts. When upstream publishes a new release, supply its release tag to the build workflow. The official RAR must include matching source; otherwise the build will stop.

Build/release workflows are manual by default; merging upstream does **not** automatically publish a GitHub Release.
