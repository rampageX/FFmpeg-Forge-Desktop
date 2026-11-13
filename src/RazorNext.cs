using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FFmpegForge
{
    /// 2.2.0 — Built-in RazorNext engine. RazorNext is Visual Enhancer's name for RelayVSR
    /// (Xijun Wang, Xin Li, Zirui Lang, Suhang Yao, Haoran Li, Zhibo Chen — Apache-2.0): collaborative streaming 4×
    /// real-world video super-resolution (sparse Wan keyframes + a compact Flash decoder for the frames in between).
    /// FFmpeg Forge installs the official RelayVSR code (github.com/kopperx/RelayVSR) and its two checkpoints
    /// (huggingface.co/kopper/RelayVSR) in its own folder and runs them with Python / PyTorch — no Visual Enhancer needed.
    /// Videos AND pictures: a picture is a one-frame stream.
    public static class RazorBuiltin
    {
        public static string Dir = "";
        // pinned revisions = the ones Visual Enhancer's RazorNext is built from (THIRD-PARTY-NOTICES)
        public const string RepoRev = "e856773350f53036d825404c9ea9228e6dffadca";
        public const string RepoZip = "https://github.com/kopperx/RelayVSR/archive/" + RepoRev + ".zip";
        public const string HfRepo = "kopper/RelayVSR";
        public const string HfRev = "d43dc5545f9a3bd1c7730b68ebff4ab1f9199bd1";
        public static readonly string[] HfFiles = { "wan.safetensors", "flash.safetensors" };
        public static readonly int[] Scales = { 1, 2, 3, 4 };     // native ×4; ×1 / ×2 / ×3 = ×4 result resized down

        public static string DefaultInstallDir
        {
            get
            {
                string app = Path.Combine(AppPaths.AppDir, "RazorNext");
                try
                {
                    string probeDir = Directory.Exists(app) ? app : AppPaths.AppDir;
                    string t = Path.Combine(probeDir, ".ffmpegforge-write-test"); File.WriteAllText(t, "x"); File.Delete(t);
                    return app;
                }
                catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFmpegForge", "RazorNext"); }
            }
        }

        public static string Root { get { return string.IsNullOrEmpty(Dir) ? DefaultInstallDir : Dir; } }
        public static string AppDir { get { return Path.Combine(Root, "app"); } }
        public static string Weights { get { return Path.Combine(AppDir, "weights"); } }

        public static string Python
        {
            get
            {
                foreach (var p in new[] { Path.Combine(Root, "venv", "Scripts", "python.exe"), Path.Combine(Root, "venv", "bin", "python") })
                    try { if (File.Exists(p)) return p; } catch { }
                return null;
            }
        }

        public static bool Ready
        {
            get
            {
                try
                {
                    return Python != null && File.Exists(Path.Combine(AppDir, "inference.py")) &&
                           Directory.Exists(Path.Combine(AppDir, "refvsr_inference")) &&
                           HfFiles.All(f => File.Exists(Path.Combine(Weights, f)));
                }
                catch { return false; }
            }
        }

        public static string Status()
        {
            if (Ready) return "Ready: RazorNext model (RelayVSR) installed in " + Root;
            if (Python != null) return "RazorNext is partly installed in " + Root + ": click \"Install RazorNext model\" again to finish.";
            return "RazorNext model not installed yet: click \"Install RazorNext model\" (about 15 GB with PyTorch). No Visual Enhancer needed.";
        }

        /// Writes the streaming helper (embedded in FFmpegForge.exe) into the RelayVSR folder.
        public static void SyncHelper()
        {
            try
            {
                string want;
                using (var st = typeof(RazorBuiltin).Assembly.GetManifestResourceStream("forge_razornext.py"))
                using (var r = new StreamReader(st, Encoding.UTF8)) want = r.ReadToEnd().Replace("\r\n", "\n");
                string path = Path.Combine(AppDir, "forge_razornext.py");
                if (!File.Exists(path) || File.ReadAllText(path) != want) File.WriteAllText(path, want, new UTF8Encoding(false));
            }
            catch { }
        }

        public static string WriteInstaller(string root)
        {
            Directory.CreateDirectory(root);
            string bat = Path.Combine(root, "install_razornext.bat");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("title FFmpeg Forge - RazorNext (RelayVSR) installation");
            sb.AppendLine("setlocal");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo  RazorNext for FFmpeg Forge = RelayVSR (Apache-2.0)");
            sb.AppendLine("echo  Folder: %CD%");
            sb.AppendLine("echo  Downloads: uv + Python 3.12, PyTorch 2.10 CUDA 12.8, RelayVSR code (GitHub),");
            sb.AppendLine("echo  RelayVSR checkpoints wan + flash (Hugging Face kopper/RelayVSR).");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("if not exist uv\\uv.exe (");
            sb.AppendLine("  echo [1/6] Downloading uv...");
            sb.AppendLine("  powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '" + SeedEngine.UvZip + "' -OutFile uv.zip; Expand-Archive -Force uv.zip uv; Remove-Item uv.zip\" || goto :fail");
            sb.AppendLine(")");
            sb.AppendLine("echo [2/6] Python 3.12 environment...");
            sb.AppendLine("set UV_PYTHON_INSTALL_DIR=%CD%\\python");
            sb.AppendLine("set UV_CACHE_DIR=%CD%\\cache");
            sb.AppendLine("set UV_LINK_MODE=copy");          // exFAT / FAT / network drives have no hard links: copy without the uv warning
            sb.AppendLine("if not exist venv\\Scripts\\python.exe uv\\uv.exe venv --python 3.12 --seed venv || goto :fail");
            sb.AppendLine("echo [3/6] PyTorch 2.10 with CUDA 12.8 (large download)...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe \"torch==2.10.*\" --index-url " + SeedEngine.TorchIndex + " || goto :fail");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe \"diffusers==0.37.1\" \"accelerate>=1.0,<2\" \"einops>=0.8\" \"av>=17,<19\" \"safetensors>=0.6\" numpy pillow huggingface_hub || goto :fail");
            sb.AppendLine("echo [4/6] Triton for Windows (optional: faster attention kernels)...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe \"triton-windows>=3.6,<3.7\" || echo     Triton not installed: RazorNext uses exact PyTorch attention instead (slower, same result).");
            sb.AppendLine("echo [5/6] RelayVSR code (github.com/kopperx/RelayVSR)...");
            sb.AppendLine("if not exist app\\inference.py powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '" + RepoZip + "' -OutFile repo.zip; if (Test-Path repo_tmp) { Remove-Item -Recurse -Force repo_tmp }; Expand-Archive -Force repo.zip repo_tmp; Remove-Item repo.zip; if (Test-Path app) { Remove-Item -Recurse -Force app }; Move-Item (Get-ChildItem repo_tmp)[0].FullName app; Remove-Item -Recurse -Force repo_tmp\" || goto :fail");
            sb.AppendLine("echo [6/6] RazorNext / RelayVSR checkpoints from Hugging Face (several GB)...");
            var files = string.Join(",", HfFiles.Select(f => "'" + f + "'").ToArray());
            sb.AppendLine("venv\\Scripts\\python.exe -c \"from huggingface_hub import hf_hub_download; [print('ok', hf_hub_download('" + HfRepo + "', f, revision='" + HfRev + "', local_dir=r'app\\weights')) for f in [" + files + "]]\" || goto :fail");
            sb.AppendLine("venv\\Scripts\\python.exe -c \"import torch;print('PyTorch',torch.__version__,'CUDA',torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else '(CPU only: extremely slow)')\"");
            sb.AppendLine("echo.");
            sb.AppendLine("echo RazorNext is installed. You can close this window and go back to FFmpeg Forge.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 0");
            sb.AppendLine(":fail");
            sb.AppendLine("echo.");
            sb.AppendLine("echo Installation failed. Check your internet connection and run install_razornext.bat again.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 1");
            File.WriteAllText(bat, sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            return bat;
        }
    }
}
