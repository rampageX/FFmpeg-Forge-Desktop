#!/usr/bin/env python3
"""One-time, auditable Git working-tree normalization for release 2.3.1."""
from pathlib import Path
import shutil

repo = Path.cwd()
old = repo / "LiteSource"
assert (old / "src" / "Program.cs").exists(), "verified source missing"
assert (repo / "FFmpegForge" / "src" / "Program.cs").exists(), "legacy tree missing"

# Preserve original upstream user documentation in the baseline archive.
archive = repo / "archive" / "upstream" / "v2.3.0"
archive.mkdir(parents=True, exist_ok=True)
shutil.copy2(old / "README.txt", archive / "README-original.txt")

for directory, target in (
    ("src", "src"), ("shaders", "shaders"),
    ("aidenoise", "aidenoise"), ("qtgmc", "qtgmc"),
    ("rtxhelper", "native/rtxhelper")
):
    origin = old / directory
    destination = repo / target
    assert origin.is_dir(), str(origin)
    assert not destination.exists(), str(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.move(str(origin), str(destination))

# Version is real assembly, UI, diagnostic and informational version, not
# merely a Git release tag.
def replace(rel, old_text, new_text, count):
    file = repo / rel
    text = file.read_text(encoding="utf-8-sig")
    assert text.count(old_text) == count, (rel, old_text, text.count(old_text))
    file.write_text(text.replace(old_text, new_text), encoding="utf-8-sig" if rel.endswith("Program.cs") else "utf-8")

replace("src/Program.cs", "2.3.0.0", "2.3.1.0", 2)
replace("src/Program.cs", "2.3.0", "2.3.1", 2)
replace("src/MainForm.cs", 'FFmpeg Forge 2.3.0 — Universal', 'FFmpeg Forge 2.3.1 — Universal', 1)
replace("src/Controls.cs", 'Version = "2.3.0"', 'Version = "2.3.1"', 2)
replace("src/RipForm.cs", '2.3.0 — "Rip DVD / Blu-ray"', '2.3.1 — "Rip DVD / Blu-ray"', 1)
# The original archive has bundled-FFmpeg instructions, inaccurate for Lite.
p=repo/"src"/"Program.cs"
t=p.read_text(encoding="utf-8-sig")
t=t.replace("its files (FFmpeg in bin\\, settings…)", "its settings and tools")
p.write_text(t, encoding="utf-8-sig")

# Remove old source tree and old root binaries. Historical commits keep them.
shutil.rmtree(repo / "FFmpegForge")
shutil.rmtree(old)
for name in ("FFmpegForge.exe", "ForgeRTX.exe", "LISEZMOI.txt", "LITE-BUILD.md"):
    p=repo/name
    if p.exists(): p.unlink()

(repo / "build.bat").write_bytes((
    '@echo off\r\nsetlocal\r\n'
    'REM Portable 2.3.1: source in src\\, generated executables only in dist\\.\r\n'
    'cd /d "%~dp0"\r\n'
    'if not exist "dist" mkdir "dist"\r\n'
    'set "CSC=%WINDIR%\\Microsoft.NET\\Framework64\\v4.0.30319\\csc.exe"\r\n'
    'if not exist "%CSC%" set "CSC=%WINDIR%\\Microsoft.NET\\Framework\\v4.0.30319\\csc.exe"\r\n'
    'if not exist "%CSC%" (echo ERROR: .NET C# compiler not found & exit /b 1)\r\n'
    '"%CSC%" /nologo /target:winexe /optimize+ /win32icon:src\\app.ico '
    '/out:dist\\FFmpegForge.exe '
    '/resource:shaders\\FSR.glsl,FSR.glsl '
    '/resource:shaders\\NIS.glsl,NIS.glsl '
    '/resource:shaders\\SGSR.glsl,SGSR.glsl '
    '/resource:shaders\\Anime4K.glsl,Anime4K.glsl '
    '/resource:shaders\\OptiScaler.glsl,OptiScaler.glsl '
    '/resource:shaders\\FSR2.glsl,FSR2.glsl '
    '/resource:src\\hrir_ircam.bin,hrir_ircam.bin '
    '/resource:aidenoise\\forge_ai_denoise.py,forge_ai_denoise.py '
    '/resource:aidenoise\\network_scunet.py,network_scunet.py '
    '/resource:aidenoise\\LICENSE-SCUNet.txt,LICENSE-SCUNet.txt '
    '/resource:aidenoise\\forge_visionnext.py,forge_visionnext.py '
    '/resource:aidenoise\\forge_custom_ai.py,forge_custom_ai.py '
    '/resource:aidenoise\\forge_razornext.py,forge_razornext.py '
    '/resource:qtgmc\\forge_qtgmc.py,forge_qtgmc.py '
    '/r:System.Windows.Forms.dll /r:System.Drawing.dll src\\*.cs\r\n'
    'if errorlevel 1 (echo Build FAILED & exit /b 1)\r\n'
    'echo OK: dist\\FFmpegForge.exe\r\nexit /b 0\r\n'
).encode("cp1252"))

(repo / ".gitignore").write_text("""# Generated binaries, never commit at the repository root
dist/
bin/
obj/
.vs/
*.exe
*.dll
*.pdb
*.log
*.tmp
*.user
__pycache__/
*.pyc
Thumbs.db
.DS_Store
""")
(repo / ".gitattributes").write_text("archive/upstream/v2.3.0/*.rar filter=lfs diff=lfs merge=lfs -text\n")

(archive / "README.md").write_text("""# Original FFmpeg Forge 2.3.0 release baseline

Immutable upstream artifact: FFmpegForge-2.3.0-Portable.rar (209,148,735 bytes).

Original download: https://github.com/Aaronstanley78/FFmpeg-Forge-Desktop/releases/tag/2.3.0

SHA256: c63f278ffafadba93374ac0c5153b802b4c5e086b0515af7ad2d95da9b25dcc9

The RAR is tracked here using Git LFS (run git lfs pull to retrieve it).
It is not embedded in the Lite runtime ZIP or stored as a giant ordinary Git blob.
The original archive includes the full official 2.3.0 Source/ tree.
""")
(repo / "archive" / "legacy").mkdir(parents=True, exist_ok=True)
(repo / "archive" / "legacy" / "README.md").write_text("""# Old source: retained in Git history

The obsolete v1.1.3a FFmpegForge/ source and the old binaries at
repository root were removed from the current tree, not from history.
Recover them from commit d2ba7a4e18382d3e2d373fd692d6d4b4931b3d22.
The maintained code is now in src/.
""")
(repo / "README.md").write_text("""# FFmpeg Forge Lite 2.3.1

Portable Windows converter based on the **official FFmpeg Forge 2.3.0
release source**, with fixes for dark WinForms controls, responsive
DVD/Blu-ray ripper layout and slow/flashy options expansion.

**Download:** [FFMpegForgeLite.zip](../../releases/latest/download/FFMpegForgeLite.zip)

Extract the ZIP, run **FFmpegForge.exe**, then choose your own **FFmpeg
location...** (ffmpeg.exe, with ffprobe.exe next to it). No FFmpeg binaries
are bundled.

## Repository layout

- src/ — current and authoritative 2.3.1 C# source
- shaders/, aidenoise/, qtgmc/ — shaders and embedded processing scripts
- native/rtxhelper/ — optional NVIDIA RTX helper source
- scripts/ — packaging and Windows UI regression tests
- docs/ — images and developer documentation
- archive/upstream/v2.3.0/ — original 209 MB RAR (Git LFS)
- archive/legacy/ — pointers to old Git history
- dist/ — ignored local build output

Run **build.bat** on Windows with the .NET Framework C# compiler:
dist/FFmpegForge.exe is generated without any EXE in the repository root.

[Development / upstream update guidance](docs/DEVELOPMENT.md)

Original project: https://github.com/Aaronstanley78/FFmpeg-Forge-Desktop
""")
(repo / "docs" / "DEVELOPMENT.md").write_text("""# Development and upstream updates

Source of truth: src/ (Lite 2.3.1). The upstream GitHub main branch was
older than its v2.3.0 RAR Source/, so never replace src/ with upstream
GitHub main without an explicit version review.

Windows build: build.bat → dist/FFmpegForge.exe.
Release build: scripts/Build-Portable.ps1 → dist/FFMpegForgeLite.zip.
UI regression: scripts/Test-Ui.ps1 runs on Windows with STA.

Upstream baseline: archive/upstream/v2.3.0/FFmpegForge-2.3.0-Portable.rar
is stored with Git LFS and checked against its SHA256 manifest.

To merge upstream changes:
1. Fetch the latest official upstream release archive.
2. Compare its Source/src/ against the maintained src/ in a scratch branch.
3. Merge features selectively; retain Lite's DarkControls/DarkNumeric and
   DVD RIP layout fixes.
4. Update assembly versions and release notes, then build and test.
5. Do not force push, delete old releases, or regenerate binaries in root.

A separate workflow fetches upstream main metadata for review without
automatically restoring obsolete files.
""")
# Keep existing CI test content while changing its expected filename.
test = repo / "scripts" / "Test-Ui.ps1"
t=test.read_text(encoding="utf-8")
assert "'*-Portable.zip'" in t
test.write_text(t.replace("'*-"+"Portable.zip'","'FFMpegForgeLite.zip'"),encoding="utf-8")
print("REORGANIZED: source at src/, old root binaries gone; archive manifest and LFS path ready.")
