# FFmpeg Forge Lite 2.3.5

Portable Windows converter based on the **official FFmpeg Forge 2.3.0
release source**, with fixes for dark WinForms controls, responsive
DVD/Blu-ray ripper layout, responsive settings, HDR10/HLG colour correction and 35 mm filmstrip preview with Pause/Live and review. The 2.3.5 update adopts the Graphite Aurora palette from upstream 3.0.1 without importing new processing features.

**Download:** [FFMpegForgeLite.v2.3.5.zip](https://github.com/rampageX/FFmpeg-Forge-Desktop/releases/download/v2.3.5/FFMpegForgeLite.v2.3.5.zip)

Extract the ZIP, run **FFmpegForge.exe**, then choose your own **FFmpeg
location...** (ffmpeg.exe, with ffprobe.exe next to it). No FFmpeg binaries
are bundled.

## Repository layout

- src/ — current and authoritative 2.3.5 C# source
- shaders/, aidenoise/, qtgmc/ — shaders and embedded processing scripts
- native/rtxhelper/ — optional NVIDIA RTX helper source
- scripts/ — packaging and Windows UI regression tests
- docs/ — images and developer documentation
- archive/upstream/v2.3.0/ — original RAR backup manifest (owner-accessible draft Release)
- archive/legacy/ — pointers to old Git history
- dist/ — ignored local build output

Run **build.bat** on Windows with the .NET Framework C# compiler:
dist/FFmpegForge.exe is generated without any EXE in the repository root.

[Development / upstream update guidance](docs/DEVELOPMENT.md)

Original project: https://github.com/Aaronstanley78/FFmpeg-Forge-Desktop
