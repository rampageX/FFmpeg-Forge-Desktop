# FFmpeg Forge Lite — Windows x64 build, dark UI and upstream sync

## Why LiteSource exists

The upstream GitHub `main` directory still contains v1.1.3a C# files even though its official v2.3.0 release archive contains the **real v2.3.0 `Source/`**. To avoid silently downgrading the application, this fork keeps a verified copy of the official 2.3.0 source in `LiteSource/` and builds **from that directory**.

Our Lite-specific changes are confined to `LiteSource/src/{Controls,MainForm,Filters,RipForm,DarkControls}.cs` plus the build scripts. This gives us an actual Git history for UI modifications instead of brittle runtime regex editing.

## Build the Windows portable ZIP

1. Open **Actions → Build FFmpeg Forge Lite → Run workflow**.
2. Use upstream tag `2.3.0`. Leave **publish = false** for a test build.
3. After a successful Windows build and UI smoke test, download the **FFmpegForge-Lite-portable** workflow artifact.
4. Set **publish = true** only after checking the changes yourself. The `v<version>-rampage.2` release is opt-in, and an existing tag will never be overwritten.

`scripts/Build-Portable.ps1` rebuilds the maintained C# source, checks assembly version, removes unused header/drop artwork resources, and verifies that the final ZIP includes no `FFmpeg/`, `bin/`, `Source/`, `ffmpeg.exe`, `ffprobe.exe` or `ffplay.exe`. It downloads the official archive **only** for the runtime auxiliary files (ForgeRTX, dovi_tool and licenses). It keeps the original portable settings marker.

Point **FFmpeg location…** to your own `ffmpeg.exe`. Place `ffprobe.exe` alongside it for input metadata and progress reporting.

## UI changes in rampage.2

- Empty-list background artwork removed; drag/drop hint, file list and shortcuts are preserved.
- Native scrollbars use `DarkMode_Explorer` on **handle creation**, including dynamically added controls. No scrollbar is manually repainted during mouse scrolling.
- WinForms `ComboBox` controls use owner-drawn dark list items and an arrow/chrome overlay drawn after native paint. Drop-down popup scrollbars receive the same native theme. No continuous painting timer.
- Settings sections use narrowly scoped composited rendering and buffered ancestors, avoiding deeply nested `WS_EX_COMPOSITED` regions.
- Expand/collapse uses one synchronous layout batch per option group, without an animation timer or repeated independent reflow.

`scripts/Test-Ui.ps1` performs a Windows STA constructor/toggle smoke test. On-screen flicker, drag scroll, DPI scaling and popup visuals still require an interactive Windows test.

## Merge newer upstream changes

**Upstream GitHub code:** run **Actions → Sync upstream main**. That executes a normal Git fetch/merge into fork `main` without force-push. Conflicts stop the job. It does not overwrite `LiteSource/`.

**Upstream release sources:** official releases currently carry newer code than GitHub `main`. When a new release appears, extract its `Source/`, compare it against `LiteSource/` and port the new changes **without** replacing our modified Controls/MainForm/Filters/RipForm classes wholesale. Only after this review should `LiteSource/src/Program.cs` and the workflow's `upstream_tag` be updated together. A mismatch fails the build instead of publishing a misleading version.

Never auto-upgrade or delete any existing release. Workflow builds leave the previous release intact.
