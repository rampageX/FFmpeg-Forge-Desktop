using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FFmpegForge
{
    /// Settings of the "AI Upscale" section (VisionNext or, since 2.2.0, RazorNext).
    public class VisionSettings
    {
        public bool Enabled;
        public string Model = "visionnext";     // "visionnext" (VOSR 2.0) or "razornext" (RelayVSR, 2.2.0)
        public string MemBudget = "auto";       // RazorNext with Visual Enhancer: --memory-budget auto|unlimited|12|8|6
        public bool Razor { get { return Model == "razornext"; } }
        public string Name { get { return Razor ? "RazorNext" : "VisionNext"; } }
        public int Scale = 2;        // 1 or 2
        public int Tile = 512;       // 256 or 512
        public int Engine;           // 0 = built-in VOSR 2.0 (no Visual Enhancer), 1 = Visual Enhancer (VE_CLI.exe)
        public bool UseGpu = true;   // built-in engine: GPU (CUDA) or CPU
    }

    /// Built-in VisionNext engine: the model behind VisionNext is VOSR 2.0 (Rongyuan Wu, Lingchen Sun et al., CVPR 2026,
    /// Apache-2.0). FFmpeg Forge installs the official VOSR code (github.com/cswry/VOSR) and the VOSR 2.0 weights
    /// (huggingface.co/CSWRY/VOSR) in its own folder and runs them with Python / PyTorch — no Visual Enhancer needed.
    public static class VisionBuiltin
    {
        public static string Dir = "";
        public const string RepoZip = "https://github.com/cswry/VOSR/archive/refs/heads/main.zip";
        public const string HfRepo = "CSWRY/VOSR";
        public static readonly string[] HfFiles = {
            "VOSR2/args.json", "VOSR2/checkpoints/ema_model.safetensors",
            "Qwen-Image-vae-2d/config.json", "Qwen-Image-vae-2d/diffusion_pytorch_model.safetensors",
            "torch_cache/checkpoints/dinov2_vitl14_pretrain.pth" };

        public static string DefaultInstallDir
        {
            get
            {
                // in FFmpeg Forge's own folder when it is writable, otherwise in the user's local app data
                string app = Path.Combine(AppPaths.AppDir, "VisionNext");
                try
                {
                    string probeDir = Directory.Exists(app) ? app : AppPaths.AppDir;
                    string t = Path.Combine(probeDir, ".ffmpegforge-write-test"); File.WriteAllText(t, "x"); File.Delete(t);
                    return app;
                }
                catch { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFmpegForge", "VisionNext"); }
            }
        }

        public static string Root { get { return string.IsNullOrEmpty(Dir) ? DefaultInstallDir : Dir; } }
        public static string AppDir { get { return Path.Combine(Root, "app"); } }
        public static string Ckpts { get { return Path.Combine(AppDir, "preset", "ckpts"); } }

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
                    return Python != null && File.Exists(Path.Combine(AppDir, "inference_vosr_onestep.py")) &&
                           HfFiles.All(f => File.Exists(Path.Combine(Ckpts, f.Replace('/', Path.DirectorySeparatorChar))));
                }
                catch { return false; }
            }
        }

        public static string Status()
        {
            if (Ready) return "Ready: VisionNext model (VOSR 2.0) installed in " + Root;
            if (Python != null) return "VisionNext is partly installed in " + Root + ": click \"Install VisionNext model\" again to finish.";
            return "VisionNext model not installed yet: click \"Install VisionNext model\" (about 10 GB with PyTorch). No Visual Enhancer needed.";
        }

        /// Writes the streaming helper (embedded in FFmpegForge.exe) into the VOSR folder.
        public static void SyncHelper()
        {
            try
            {
                string want;
                using (var st = typeof(VisionBuiltin).Assembly.GetManifestResourceStream("forge_visionnext.py"))
                using (var r = new StreamReader(st, Encoding.UTF8)) want = r.ReadToEnd().Replace("\r\n", "\n");
                string path = Path.Combine(AppDir, "forge_visionnext.py");
                if (!File.Exists(path) || File.ReadAllText(path) != want) File.WriteAllText(path, want, new UTF8Encoding(false));
            }
            catch { }
        }

        public static string WriteInstaller(string root)
        {
            Directory.CreateDirectory(root);
            string bat = Path.Combine(root, "install_visionnext.bat");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("title FFmpeg Forge - VisionNext (VOSR 2.0) installation");
            sb.AppendLine("setlocal");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo  VisionNext for FFmpeg Forge = VOSR 2.0 (CVPR 2026, Apache-2.0)");
            sb.AppendLine("echo  Folder: %CD%");
            sb.AppendLine("echo  Downloads: uv + Python 3.12, PyTorch CUDA 12.8, VOSR code (GitHub),");
            sb.AppendLine("echo  VOSR 2.0 weights + Qwen-Image 2D VAE + DINOv2-L (Hugging Face CSWRY/VOSR).");
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
            sb.AppendLine("echo [3/6] PyTorch with CUDA 12.8 (large download)...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe torch torchvision --index-url " + SeedEngine.TorchIndex + " || goto :fail");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe \"diffusers==0.35.0\" \"fairscale==0.4.13\" einops timm safetensors pillow numpy tqdm huggingface_hub accelerate || goto :fail");
            sb.AppendLine("echo [4/6] VOSR code (github.com/cswry/VOSR)...");
            sb.AppendLine("if not exist app\\inference_vosr_onestep.py powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '" + RepoZip + "' -OutFile repo.zip; if (Test-Path repo_tmp) { Remove-Item -Recurse -Force repo_tmp }; Expand-Archive -Force repo.zip repo_tmp; Remove-Item repo.zip; if (Test-Path app) { Remove-Item -Recurse -Force app }; Move-Item (Get-ChildItem repo_tmp)[0].FullName app; Remove-Item -Recurse -Force repo_tmp\" || goto :fail");
            sb.AppendLine("echo [5/6] VisionNext / VOSR 2.0 model files from Hugging Face (several GB)...");
            var files = string.Join(",", HfFiles.Select(f => "'" + f + "'").ToArray());
            sb.AppendLine("venv\\Scripts\\python.exe -c \"from huggingface_hub import hf_hub_download; [print('ok', hf_hub_download('" + HfRepo + "', f, local_dir=r'app\\preset\\ckpts')) for f in [" + files + "]]\" || goto :fail");
            sb.AppendLine("echo [6/6] DINOv2 code (torch.hub)...");
            sb.AppendLine("cd app");
            sb.AppendLine("..\\venv\\Scripts\\python.exe -c \"import torch; torch.hub.set_dir('preset/ckpts/torch_cache'); m = torch.hub.load('facebookresearch/dinov2', 'dinov2_vitl14', trust_repo=True); print('DINOv2 ready')\" || goto :fail");
            sb.AppendLine("cd ..");
            sb.AppendLine("venv\\Scripts\\python.exe -c \"import torch;print('PyTorch',torch.__version__,'CUDA',torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else '(CPU only: very slow)')\"");
            sb.AppendLine("echo.");
            sb.AppendLine("echo VisionNext is installed. You can close this window and go back to FFmpeg Forge.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 0");
            sb.AppendLine(":fail");
            sb.AppendLine("echo.");
            sb.AppendLine("echo Installation failed. Check your internet connection and run install_visionnext.bat again.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 1");
            File.WriteAllText(bat, sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            return bat;
        }
    }

    /// AI upscaling with the VisionNext model (VOSR 2.0, ONNX Runtime DirectML) of the user's own
    /// Visual Enhancer installation, through VE_CLI.exe:
    ///   VE_CLI.exe upscale INPUT -o OUTPUT --engine visionnext --model visionnext --scale 1|2 --tile 256|512 --format png
    /// Videos are streamed: ffmpeg decoder (trim, rotation, Denoiser, filters) -> VE_CLI on small batches of frames
    ///   -> final ffmpeg encoder (format, codec, audio), all running at the same time: one single encode.
    /// Nothing from Visual Enhancer or the model is shipped with FFmpeg Forge.
    public static class VisionEngine
    {
        public static string Dir = "";
        public static readonly string[] DefaultDirs = { @"F:\Nouveau dossier", @"C:\Program Files\Visual Enhancer", @"C:\Visual Enhancer" };
        public static readonly string[] Models = { "VisionNext", "RazorNext" };
        public static readonly string[] MemBudgets = { "auto", "unlimited", "12", "8", "6" };
        public static readonly int[] Scales = { 1, 2 };
        public static readonly int[] Tiles = { 256, 512 };

        /// Folder that holds VE_CLI.exe (the folder itself or one level below).
        public static string Resolve(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return null;
            try
            {
                if (File.Exists(Path.Combine(dir, "VE_CLI.exe"))) return dir;
                if (Directory.Exists(dir))
                    foreach (var sub in Directory.GetDirectories(dir))
                        if (File.Exists(Path.Combine(sub, "VE_CLI.exe"))) return sub;
            }
            catch { }
            return null;
        }

        public static string Root { get { return Resolve(Dir); } }
        public static string ExePath { get { string r = Root; return r == null ? null : Path.Combine(r, "VE_CLI.exe"); } }
        public static string AppPath { get { string r = Root; if (r == null) return null; string a = Path.Combine(r, "Visual Enhancer.exe"); return File.Exists(a) ? a : null; } }

        static string RuntimeDir(string root)
        {
            string env = Environment.GetEnvironmentVariable("VE_VISIONNEXT_RUNTIME_DIRECTORY");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(root, "bin", "runtime", "visionnext");
        }

        static string ModelDir(string root)
        {
            string env = Environment.GetEnvironmentVariable("VE_VISIONNEXT_MODEL_DIRECTORY");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(RuntimeDir(root), "models");
        }

        public static bool RuntimeInstalled { get { string r = Root; return r != null && File.Exists(Path.Combine(RuntimeDir(r), "neuroframe_engine_visionnext.dll")); } }

        /// RazorNext (2.2.0): Visual Enhancer keeps it next to VisionNext (bin\runtime\razornext). When that folder cannot be
        /// found the check is left to VE_CLI itself (it downloads missing models automatically).
        public static bool? RazorRuntimeInstalled
        {
            get
            {
                string r = Root;
                if (r == null) return false;
                try
                {
                    string env = Environment.GetEnvironmentVariable("VE_RAZORNEXT_RUNTIME_DIRECTORY");
                    string d = !string.IsNullOrEmpty(env) ? env : Path.Combine(r, "bin", "runtime", "razornext");
                    if (Directory.Exists(d)) return true;
                    return Directory.Exists(Path.Combine(r, "bin", "runtime")) ? (bool?)null : false;
                }
                catch { return null; }
            }
        }

        public static string RazorStatus()
        {
            if (string.IsNullOrEmpty(Dir)) return "Choose your Visual Enhancer folder (the one with VE_CLI.exe).";
            string r = Root;
            if (r == null) return "VE_CLI.exe not found in " + Dir;
            var m = RazorRuntimeInstalled;
            return "Ready: " + Path.Combine(r, "VE_CLI.exe") +
                   (m == true ? "\nRazorNext runtime found." : "\nIf the RazorNext model is missing, Visual Enhancer downloads it on the first export (or: Visual Enhancer → Models → RazorNext → Download).");
        }

        /// true / false, or null when it cannot be told (Visual Enhancer may keep the model elsewhere).
        public static bool? ModelInstalled
        {
            get
            {
                string r = Root;
                if (r == null) return false;
                try
                {
                    string md = ModelDir(r);
                    if (!Directory.Exists(md)) return null;
                    return Directory.GetFiles(md, "*.onnx", SearchOption.AllDirectories).Length > 0 ||
                           Directory.GetFiles(md, "*.data", SearchOption.AllDirectories).Length > 0;
                }
                catch { return null; }
            }
        }

        public static bool Ready { get { return ExePath != null && RuntimeInstalled; } }

        public static string Status()
        {
            if (string.IsNullOrEmpty(Dir)) return "Choose your Visual Enhancer folder (the one with VE_CLI.exe).";
            string r = Root;
            if (r == null) return "VE_CLI.exe not found in " + Dir;
            if (!RuntimeInstalled) return "Visual Enhancer found, but its VisionNext runtime (bin\\runtime\\visionnext) is missing: update Visual Enhancer.";
            var m = ModelInstalled;
            return "Ready: " + Path.Combine(r, "VE_CLI.exe") +
                   (m == false ? "\nThe VisionNext model is not installed yet: open Visual Enhancer → Models → VisionNext → Download (≈ 2.8 GB)." :
                    m == true ? "\nVisionNext model installed." : "");
        }

        // ---------------- plan ----------------
        public class Plan
        {
            public string TempDir, PreArgs, Exe, WorkDir, VeArgs, VeIn, VeOut, EncArgs, Output;
            public bool Video, Builtin, Streamed;     // Streamed: Visual Enhancer video, decoder -> VE_CLI (small batches) -> encoder running at the same time
            public string VeTemplate;                 // VE_CLI arguments with %IN% / %OUT% for the streamed batches
            public int Batch;
            public Rtx.DriverPlan Driver;     // built-in engine: decoder -> VOSR 2.0 helper -> final encoder, streamed (no picture files)
            public double Expected;
            public int InW, InH, OutW, OutH;
            public string Name = "VisionNext";        // VisionNext or RazorNext (2.2.0)
            public string Text() { if (Driver != null) return Driver.Text(); if (Streamed) return "ffmpeg " + PreArgs + "  |  \"" + Exe + "\" " + VeTemplate + "  (batches of " + Batch + " frames)  |  ffmpeg " + EncArgs; return "ffmpeg " + PreArgs + "  &&  \"" + Exe + "\" " + VeArgs + "  &&  ffmpeg " + EncArgs; }
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        public static Plan Build(Job job, ConvSettings s, string outPath, out string error)
        {
            error = null;
            var v = s.Vision;
            var fmt = s.Format;
            bool builtin = v.Engine == 0;
            bool razor = v.Razor;
            string nm = v.Name;
            string exe = builtin ? (razor ? RazorBuiltin.Python : VisionBuiltin.Python) : ExePath;
            if (builtin && razor && !RazorBuiltin.Ready) { error = "The RazorNext model is not installed: in the AI Upscale section (Model: RazorNext) click \"Install RazorNext model\" (no Visual Enhancer needed)."; return null; }
            if (builtin && !razor && !VisionBuiltin.Ready) { error = "The VisionNext model is not installed: in the AI Upscale section (Model: VisionNext) click \"Install VisionNext model\" (no Visual Enhancer needed)."; return null; }
            if (!builtin && exe == null) { error = "Upscale " + nm + " with Visual Enhancer: choose the folder that contains VE_CLI.exe (AI Upscale → Visual Enhancer folder…)." + (string.IsNullOrEmpty(Dir) ? "" : " Current folder: " + Dir); return null; }
            if (!builtin && !razor && !RuntimeInstalled) { error = "The VisionNext runtime of Visual Enhancer is missing (bin\\runtime\\visionnext): update Visual Enhancer."; return null; }
            if (job.Kind == MediaKind.Audio || fmt.Kind == MediaKind.Audio) { error = "Upscale " + nm + " needs a video or a picture."; return null; }
            bool image = fmt.Kind == MediaKind.Image;
            bool srcVideo = job.Kind == MediaKind.Video || CommandBuilder.IsAnimatedImageSource(job);
            if (image && srcVideo) { error = "Upscale " + nm + " + Image output works on pictures. For a video, choose the Video category."; return null; }
            if (s.Seed != null && s.Seed.Enabled) { error = "Tick only one AI upscaler: SeedVR2 or Upscale " + nm + "."; return null; }
            if (s.ScaleActive) { error = "Upscale " + nm + " is an upscaler too: untick \"Scale\" / OptiScaler."; return null; }
            if (s.RtxActive) { error = "Untick the NVIDIA RTX AI options (VSR / TrueHDR / DLSS) to use Upscale " + nm + "."; return null; }
            if (!string.IsNullOrWhiteSpace(s.FilterComplex)) { error = "The advanced filtergraph cannot be combined with Upscale " + nm + ". Use the filter chain instead."; return null; }
            string vkey = fmt.VCodec;
            if (!image)
            {
                if (s.VCodec != "auto") vkey = s.VCodec;
                if (vkey == "copy") { error = "Upscale " + nm + " needs a real video codec (not stream copy)."; return null; }
            }
            int w = job.Width, h = job.Height;
            if (w < 16 || h < 16) { error = "Could not read the picture size (ffprobe). Re-add the file."; return null; }
            if (s.Rotate == 1 || s.Rotate == 3) { int t = w; w = h; h = t; }
            int scale;
            if (razor)
            {
                // RelayVSR is native ×4. Built-in: ×1 / ×2 / ×3 / ×4. Visual Enhancer's RazorNext: ×1 / ×2 / ×4 / ×8.
                scale = v.Scale == 1 || v.Scale == 2 || v.Scale == 3 || v.Scale == 4 || v.Scale == 8 ? v.Scale : 4;
                if (builtin && scale == 8) { error = "The built-in RazorNext engine upscales ×1, ×2, ×3 or ×4 (native ×4). For ×8 choose Engine → Visual Enhancer."; return null; }
                if (!builtin && scale == 3) { error = "Visual Enhancer's RazorNext upscales ×1, ×2, ×4 or ×8. For ×3 choose Engine → Built-in."; return null; }
            }
            else
            {
                scale = v.Scale == 1 || v.Scale == 3 || v.Scale == 4 || v.Scale == 8 ? v.Scale : 2;
                if (scale > 2 && !builtin) { error = "Visual Enhancer's VisionNext only upscales ×1 or ×2. For ×" + scale + " choose Engine → Built-in (no Visual Enhancer)."; return null; }
            }
            int tile = v.Tile == 256 ? 256 : 512;
            string mem = VisionEngine.MemBudgets.Contains(v.MemBudget ?? "") ? v.MemBudget : "auto";
            if (!image && vkey != null && vkey.Contains("nvenc") && (w * scale > (vkey.Contains("264") ? 4096 : 8192) || h * scale > (vkey.Contains("264") ? 4096 : 8192)))
            { error = "The " + nm + " output (" + (w * scale) + "×" + (h * scale) + ") is too large for " + vkey + " (max " + (vkey.Contains("264") ? "4096" : "8192") + " px): choose H.265 / libx264 / libx265 or a smaller upscale factor."; return null; }
            if (w * scale > 16384 || h * scale > 16384) { error = "The " + nm + " output would be larger than 16384 pixels: use a smaller upscale factor."; return null; }

            var p = new Plan { Exe = exe, Builtin = builtin, Name = nm, WorkDir = builtin ? (razor ? RazorBuiltin.AppDir : VisionBuiltin.AppDir) : Path.GetDirectoryName(exe), Output = outPath, InW = w, InH = h, OutW = w * scale, OutH = h * scale, Video = !image };
            p.TempDir = Path.Combine(Path.GetTempPath(), "FFmpegForge-" + nm + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));

            // 1) pre-pass: trim, rotation, Denoiser, user filters -> PNG picture(s)
            int fn = job.FpsNum > 0 ? job.FpsNum : 30, fd = job.FpsDen > 0 ? job.FpsDen : 1;
            if (s.Fps > 0) { fn = (int)Math.Round(s.Fps * 1000); fd = 1000; }
            string rate = fn + "/" + fd;
            double ss = s.TrimStart > 0 ? s.TrimStart : 0, to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = image ? "" : (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            dur = Math.Max(0, dur - ss);
            bool still = !image && !srcVideo;      // picture -> video clip
            if (still) { dur = s.StillDuration > 0 ? s.StillDuration : 5; trim = "-loop 1 -t " + Formats.Inv(dur) + " "; }
            p.Expected = image ? 0 : dur;
            var pre = new List<string>();
            CommandBuilder.AddRotate(pre, s.Rotate);
            string dn = Denoise.Filter(s, job, image);
            if (dn != null) pre.Add(dn);
            string userV = s.UserVideoChain();
            if (userV.Length > 0) pre.Add(userV);
            pre.Add("format=rgb24");
            string streamDec = null;
            if (image)
            {
                p.VeIn = Path.Combine(p.TempDir, "input.png");
                p.VeOut = Path.Combine(p.TempDir, razor ? "razornext.png" : "visionnext.png");
                p.PreArgs = "-hide_banner -nostdin -y -loglevel error -i " + Q(job.Path) + " -map 0:v:0 -frames:v 1 -vf " + Q(string.Join(",", pre.ToArray())) + " " + Q(p.VeIn);
                streamDec = "-hide_banner -nostdin -loglevel error -i " + Q(job.Path) + " -map 0:v:0 -frames:v 1 -vf " + Q(string.Join(",", pre.ToArray())) + " -f rawvideo -";
            }
            else
            {
                p.VeIn = Path.Combine(p.TempDir, "frames");
                p.VeOut = Path.Combine(p.TempDir, "upscaled");
                streamDec = "-hide_banner -nostdin -loglevel error " + trim + "-i " + Q(job.Path) + " -map 0:v:0 -an -sn -dn -vf " + Q(string.Join(",", pre.ToArray())) + " -r " + rate + " -f rawvideo -";
                p.PreArgs = "-hide_banner -nostdin -y -loglevel error -progress pipe:1 -nostats " + trim + "-i " + Q(job.Path) + " -map 0:v:0 -an -sn -dn -vf " + Q(string.Join(",", pre.ToArray())) +
                            " -r " + rate + " -start_number 1 " + Q(Path.Combine(p.VeIn, "%08d.png"));
            }

            // 2) VE_CLI upscale with VisionNext / RazorNext
            string veModel = razor ? "--engine razornext --scale " + scale + " --memory-budget " + mem + " --format png"
                                   : "--engine visionnext --model " + (string.IsNullOrEmpty(v.Model) ? "visionnext" : v.Model) + " --scale " + scale + " --tile " + tile + " --format png";
            if (builtin && razor)
            {
                // RelayVSR streaming helper (always used through MakeDriver below): raw frames in, raw frames out
                p.VeArgs = "-u forge_razornext.py -w " + w + " -h " + h + " -u " + scale;
            }
            else if (builtin)
            {
                // official VOSR one-step inference: a picture or a folder in, PNG pictures out (same names)
                string outDir = image ? Path.Combine(p.TempDir, "out") : p.VeOut;
                p.VeArgs = "-u inference_vosr_onestep.py -c preset/ckpts/VOSR2 -i " + Q(p.VeIn) + " -o " + Q(outDir) +
                           " -u " + scale + " --tile_size " + tile + " --tile_overlap 32 --align_method wavelet";
            }
            else
            p.VeArgs = "upscale " + Q(p.VeIn) + " -o " + Q(p.VeOut) + " " + veModel;

            // 3) final encode with FFmpeg Forge's format / codec / audio settings
            var vf = new List<string>();
            bool even = !image && CommandBuilder.NeedsEven(vkey);
            string sc = CommandBuilder.ScaleFilter(s, even);
            if (sc != null) vf.Add(sc);
            if (image)
            {
                string ia = CommandBuilder.ImageArgs(vkey ?? "auto", s.ImgQuality) ?? "";
                p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -i " + Q(p.VeOut) + " " +
                            (vf.Count > 0 ? "-vf " + Q(string.Join(",", vf.ToArray())) + " " : "") + "-frames:v 1 -update 1 " + ia +
                            (string.IsNullOrWhiteSpace(s.CustomArgs) ? "" : " " + s.CustomArgs.Trim()) + " -strict -2 " + Q(outPath);
                if (builtin) MakeDriver(p, streamDec, p.EncArgs.Replace("-i " + Q(p.VeOut), RawIn(p) + "-i -"), v, scale, tile, 0);
                return p;
            }
            int outShort = Math.Min(p.OutW, p.OutH);
            vf.Add("scale=out_color_matrix=" + (outShort >= 720 ? "bt709" : "smpte170m") + ":out_range=tv");
            if (even) vf.Add("scale=trunc(iw/2)*2:trunc(ih/2)*2");
            string va = CommandBuilder.VideoArgs(vkey, s, fmt.Ext) + (outShort >= 720 ? " -color_primaries bt709 -color_trc bt709 -colorspace bt709" : "");
            string akey = s.ACodec == "auto" ? fmt.ACodec : s.ACodec;
            if (s.ChLayout == "copy" && akey != "none") akey = "copy";
            string audio;
            if (akey == "none" || (job.Probed && !job.HasAudio) || still) audio = "-an";
            else
            {
                string userA = s.UserAudioChain();
                if (akey == "copy" && userA.Length > 0) { error = "Audio filters cannot be used with audio stream copy."; return null; }
                s.SourceChannels = job.AudioChannels; s.SourceIsVideo = true; s.SourceDuration = dur; s.SourceDsd = job.IsDsdAudio; s.DsdTargetRate = 48000;
                string af = akey != "copy" ? CommandBuilder.AudioChain(s, userA) : null;
                if (akey != "copy")
                {
                    string lerr = ChannelLayouts.Check(s.ChLayout, akey);
                    if (lerr != null && s.ACodec == "auto") { string alt = ChannelLayouts.AutoCodec(fmt.Ext); if (alt != null) { akey = alt; lerr = null; } }
                    if (lerr != null) { error = lerr; return null; }
                }
                audio = "-map 1:a:0? " + (af != null ? "-af " + Q(af) + " " : "") + CommandBuilder.AudioFormatArgs(s, akey != "copy") + " " +
                        CommandBuilder.AudioArgs(akey, s.AKbps) + (akey != "copy" ? " " + ChannelLayouts.ExtraArgs(s.ChLayout, akey) : "");
            }
            p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats " +
                        "-framerate " + rate + " -thread_queue_size 64 -start_number 1 -i " + Q(Path.Combine(p.TempDir, "final", "%08d.png")) + " " +
                        (still ? "" : trim + "-i " + Q(job.Path) + " ") + "-map 0:v:0 " + audio +
                        " -vf " + Q(string.Join(",", vf.ToArray())) + " " + va +
                        " -sn " + (still ? "" : "-shortest ") + (fmt.Extra ?? "") + (fmt.Dynamic && fmt.Muxer != null ? " -f " + fmt.Muxer : "") +
                        (string.IsNullOrWhiteSpace(s.CustomArgs) ? "" : " " + s.CustomArgs.Trim()) + " -strict -2 " + Q(outPath);
            if (!builtin)
            {
                // Visual Enhancer: the video is streamed too. VE_CLI only reads picture files, so FFmpeg Forge feeds it small
                // batches of frames while the final encoder is already running: one single encode, no full PNG sequence on disk.
                p.Streamed = true;
                p.Batch = (int)Math.Max(8, Math.Min(96, 450e6 / ((double)p.OutW * p.OutH * 3)));
                p.VeTemplate = "upscale \"%IN%\" -o \"%OUT%\" " + veModel;
                p.PreArgs = streamDec.Replace(" -f rawvideo -", " -f image2pipe -c:v png -compression_level 1 -");
                p.EncArgs = p.EncArgs.Replace("-framerate " + rate + " -thread_queue_size 64 -start_number 1 -i " + Q(Path.Combine(p.TempDir, "final", "%08d.png")),
                                              "-f image2pipe -framerate " + rate + " -thread_queue_size 64 -c:v png -i -");
            }
            if (builtin)
                MakeDriver(p, streamDec, p.EncArgs.Replace("-framerate " + rate + " -thread_queue_size 64 -start_number 1 -i " + Q(Path.Combine(p.TempDir, "final", "%08d.png")),
                                                           RawIn(p) + "-framerate " + rate + " -i -"), v, scale, tile, dur);
            return p;
        }

        static string RawIn(Plan p) { return "-f rawvideo -pix_fmt rgb24 -s " + p.OutW + "x" + p.OutH + " "; }

        /// Built-in engine: ffmpeg decoder -> forge_visionnext.py (VOSR 2.0) -> final ffmpeg encoder, all streamed:
        /// no picture files and a single encode, with the same per-frame result as the official inference script.
        static void MakeDriver(Plan p, string dec, string enc, VisionSettings v, int scale, int tile, double seconds)
        {
            if (v.Razor)
            {
                // RazorNext (RelayVSR): decoder -> forge_razornext.py (temporal streaming 4× VSR) -> final encoder
                RazorBuiltin.SyncHelper();
                p.Driver = new Rtx.DriverPlan
                {
                    W = p.InW, H = p.InH, OW = p.OutW, OH = p.OutH, Expected = seconds, Helper = RazorBuiltin.Python, HelperName = "ForgeRN",
                    DecArgs = dec, EncArgs = enc,
                    HelperArgs = "-u " + Q(Path.Combine(RazorBuiltin.AppDir, "forge_razornext.py")) + " -w " + p.InW + " -h " + p.InH + " -u " + scale +
                                 " -device " + (v.UseGpu ? "cuda" : "cpu")
                };
                return;
            }
            VisionBuiltin.SyncHelper();
            p.Driver = new Rtx.DriverPlan
            {
                W = p.InW, H = p.InH, OW = p.OutW, OH = p.OutH, Expected = seconds, Helper = VisionBuiltin.Python, HelperName = "ForgeVN",
                DecArgs = dec, EncArgs = enc,
                HelperArgs = "-u " + Q(Path.Combine(VisionBuiltin.AppDir, "forge_visionnext.py")) + " -w " + p.InW + " -h " + p.InH + " -u " + scale +
                             " -tile " + tile + " -device " + (v.UseGpu ? "cuda" : "cpu")
            };
        }

        // ---------------- run ----------------
        static readonly Regex Pct = new Regex(@"(\d{1,3}(?:[.,]\d+)?)\s*%", RegexOptions.Compiled);

        /// Pre-pass 10 %, VisionNext 80 %, final encode 10 %.
        public static int Run(Plan plan, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            if (plan.Streamed) return RunStream(plan, onProgress, onLog, cancelled, out lastErrors);
            try
            {
                Directory.CreateDirectory(plan.TempDir);
                if (plan.Video) { Directory.CreateDirectory(plan.VeIn); Directory.CreateDirectory(plan.VeOut); }
                try { string od = Path.GetDirectoryName(plan.Output); if (!string.IsNullOrEmpty(od)) Directory.CreateDirectory(od); } catch { }

                onLog(plan.Name + " 1/3 — preparing the " + (plan.Video ? "frames" : "picture") + " (FFmpeg)");
                int code = Runner.Run(Runner.FFmpeg, plan.PreArgs, plan.Expected, x => onProgress(0.10 * x), onLog, cancelled, out lastErrors);
                if (code != 0) { Cleanup(plan); return code; }
                int total = plan.Video ? Directory.GetFiles(plan.VeIn, "*.png").Length : 1;
                if (total == 0) { lastErrors = "No frame was extracted from the video."; Cleanup(plan); return 3; }

                onLog(plan.Name + " 2/3 — AI upscale " + plan.InW + "×" + plan.InH + " → " + plan.OutW + "×" + plan.OutH + (plan.Video ? " (" + total + " frames)" : "") + (plan.Builtin ? (plan.Name == "RazorNext" ? " (built-in RelayVSR)" : " (built-in VOSR 2.0)") : " (Visual Enhancer)"));
                code = RunVe(plan, total, x => onProgress(0.10 + 0.80 * x), onLog, cancelled, out lastErrors);
                if (code != 0) { Cleanup(plan); return code; }

                if (plan.Video)
                {
                    // VE_CLI may rename its outputs: order them by name and number them again for FFmpeg
                    var outs = Directory.GetFiles(plan.VeOut, "*.png", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                    if (outs.Count == 0) { lastErrors = plan.Name + " did not write any upscaled frame (see the log above)."; Cleanup(plan); return 3; }
                    if (outs.Count != total) onLog("⚠ " + plan.Name + " wrote " + outs.Count + " frames for " + total + " source frames.");
                    string fin = Path.Combine(plan.TempDir, "final");
                    Directory.CreateDirectory(fin);
                    for (int i = 0; i < outs.Count; i++) File.Move(outs[i], Path.Combine(fin, (i + 1).ToString("00000000") + ".png"));
                    try { Directory.Delete(plan.VeIn, true); } catch { }
                }
                else if (!File.Exists(plan.VeOut))
                {
                    // VE_CLI may have added a suffix to the file name
                    var any = Directory.GetFiles(plan.TempDir, "*.png", SearchOption.AllDirectories).Where(f => !f.Equals(plan.VeIn, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (any.Count == 0) { lastErrors = plan.Name + " finished but did not write " + plan.VeOut; Cleanup(plan); return 3; }
                    File.Move(any[0], plan.VeOut);
                }

                onLog(plan.Name + " 3/3 — encoding the final file (FFmpeg)");
                code = Runner.Run(Runner.FFmpeg, plan.EncArgs, plan.Expected, x => onProgress(0.90 + 0.10 * x), onLog, cancelled, out lastErrors);
                Cleanup(plan);
                if (code == 0) onProgress(1.0);
                return code;
            }
            catch (Exception ex) { lastErrors = ex.Message; Cleanup(plan); return -2; }
        }

        static int RunVe(Plan plan, int total, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            var errs = new List<string>();
            var psi = new ProcessStartInfo(plan.Exe, plan.VeArgs)
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = plan.WorkDir,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            if (plan.Builtin)
            {
                psi.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                psi.EnvironmentVariables["PYTHONUTF8"] = "1";
                psi.EnvironmentVariables["HF_HUB_OFFLINE"] = "1";
                psi.EnvironmentVariables["INFER_NO_SKIP"] = "1";
            }
            long lastLog = -10000;
            var sw = Stopwatch.StartNew();
            Action<string> line = l =>
            {
                if (string.IsNullOrWhiteSpace(l)) return;
                l = l.Trim();
                var m = Pct.Match(l);
                if (m.Success)
                {
                    double pc;
                    if (!plan.Video && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out pc) && pc >= 0 && pc <= 100)
                        onProgress(pc / 100.0);
                    if (sw.ElapsedMilliseconds - lastLog < 4000 && !l.Contains("100%")) return;      // progress lines: one every 4 s
                    lastLog = sw.ElapsedMilliseconds;
                }
                lock (errs) { errs.Add(l); if (errs.Count > 40) errs.RemoveAt(0); }
                onLog(l);
            };
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (o, e) => line(e.Data);
                p.ErrorDataReceived += (o, e) => line(e.Data);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                long lastCount = 0;
                while (!p.WaitForExit(500))
                {
                    if (cancelled()) { KillTree(p); lastErrors = "Cancelled"; return -1; }
                    if (plan.Video && sw.ElapsedMilliseconds - lastCount > 1500)
                    {
                        lastCount = sw.ElapsedMilliseconds;
                        try { onProgress(Math.Min(1.0, (double)Directory.GetFiles(plan.VeOut, "*.png", SearchOption.AllDirectories).Length / total)); } catch { }
                    }
                }
                p.WaitForExit();
                lock (errs) lastErrors = string.Join(Environment.NewLine, errs.ToArray());
                return p.ExitCode == -1 ? 255 : p.ExitCode;
            }
        }

        // ---------------- Visual Enhancer, streamed ----------------
        static byte[] ReadExact(Stream st, int n)
        {
            var b = new byte[n]; int o = 0;
            while (o < n) { int r = st.Read(b, o, n - o); if (r <= 0) return null; o += r; }
            return b;
        }

        /// One PNG picture from an image2pipe stream (signature + chunks up to IEND), or null at the end.
        static byte[] ReadPng(Stream st)
        {
            var sig = ReadExact(st, 8);
            if (sig == null) return null;
            var ms = new MemoryStream(); ms.Write(sig, 0, 8);
            while (true)
            {
                var hdr = ReadExact(st, 8);
                if (hdr == null) return null;
                ms.Write(hdr, 0, 8);
                int len = (hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3];
                if (len < 0 || len > 512 * 1024 * 1024) return null;
                var body = ReadExact(st, len + 4);
                if (body == null) return null;
                ms.Write(body, 0, body.Length);
                if (hdr[4] == 'I' && hdr[5] == 'E' && hdr[6] == 'N' && hdr[7] == 'D') return ms.ToArray();
            }
        }

        static Process StartFfmpeg(string args, bool stdin)
        {
            var p = new Process { StartInfo = new ProcessStartInfo(Runner.FFmpeg, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = stdin, RedirectStandardOutput = true,
                RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8
            } };
            p.Start();
            return p;
        }

        /// decoder (PNG frames on a pipe) -> VE_CLI on batches of frames -> final encoder (PNG frames on a pipe), all running at the same time.
        static int RunStream(Plan plan, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            lastErrors = null;
            var errs = new List<string>();
            Action<string, string> addErr = (tag, l) =>
            {
                if (string.IsNullOrWhiteSpace(l)) return;
                lock (errs) { errs.Add(tag + l); if (errs.Count > 40) errs.RemoveAt(0); }
                onLog(tag + l);
            };
            Process dec = null, enc = null;
            var batches = new System.Collections.Concurrent.BlockingCollection<List<byte[]>>(2);
            bool failed = false;
            try
            {
                Directory.CreateDirectory(plan.TempDir);
                try { string od = Path.GetDirectoryName(plan.Output); if (!string.IsNullOrEmpty(od)) Directory.CreateDirectory(od); } catch { }
                onLog(plan.Name + " (Visual Enhancer) — streamed: decoding, AI upscale (batches of " + plan.Batch + " frames) and final encoding run at the same time");

                enc = StartFfmpeg(plan.EncArgs, true);
                long frames = 0;
                double encDone = 0;
                enc.OutputDataReceived += (o, e) =>
                {
                    if (e.Data == null) return;
                    string l = e.Data; long us;
                    if (plan.Expected > 0 && (l.StartsWith("out_time_us=") || l.StartsWith("out_time_ms=")) && long.TryParse(l.Substring(12), out us) && us > 0)
                    { encDone = Math.Min(1.0, us / 1e6 / plan.Expected); onProgress(encDone); }
                };
                enc.ErrorDataReceived += (o, e) => addErr("", e.Data);
                enc.BeginOutputReadLine(); enc.BeginErrorReadLine();

                dec = StartFfmpeg(plan.PreArgs, false);
                dec.ErrorDataReceived += (o, e) => addErr("[decoder] ", e.Data);
                dec.BeginErrorReadLine();

                var d1 = dec;
                var reader = new System.Threading.Thread(() =>
                {
                    try
                    {
                        var st = new BufferedStream(d1.StandardOutput.BaseStream, 1 << 20);
                        var cur = new List<byte[]>();
                        while (true)
                        {
                            var png = ReadPng(st);
                            if (png == null) break;
                            cur.Add(png);
                            if (cur.Count >= plan.Batch) { batches.Add(cur); cur = new List<byte[]>(); }
                        }
                        if (cur.Count > 0) batches.Add(cur);
                    }
                    catch { }
                    finally { batches.CompleteAdding(); }
                }) { IsBackground = true };
                reader.Start();

                var encIn = enc.StandardInput.BaseStream;
                int n = 0;
                foreach (var batch in batches.GetConsumingEnumerable())
                {
                    if (cancelled()) { lastErrors = "Cancelled"; Cleanup(plan); return -1; }
                    n++;
                    string bin = Path.Combine(plan.TempDir, "in" + n), bout = Path.Combine(plan.TempDir, "out" + n);
                    Directory.CreateDirectory(bin); Directory.CreateDirectory(bout);
                    for (int i = 0; i < batch.Count; i++) File.WriteAllBytes(Path.Combine(bin, (frames + i + 1).ToString("00000000") + ".png"), batch[i]);
                    plan.VeArgs = plan.VeTemplate.Replace("%IN%", bin).Replace("%OUT%", bout);
                    plan.VeOut = bout;
                    if (n == 1) onLog(plan.Name + " — " + plan.InW + "×" + plan.InH + " → " + plan.OutW + "×" + plan.OutH + " (Visual Enhancer, streamed)");
                    string verr;
                    int code = RunVe(plan, batch.Count, x => { }, l => { if (n == 1 || !Pct.IsMatch(l)) onLog(l); }, cancelled, out verr);
                    if (code != 0) { lock (errs) errs.Add(verr); failed = true; lastErrors = verr; break; }
                    var outs = Directory.GetFiles(bout, "*.png", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                    if (outs.Count == 0) { lastErrors = plan.Name + " did not write any upscaled frame (see the log above)."; failed = true; break; }
                    if (outs.Count != batch.Count) onLog("⚠ " + plan.Name + " wrote " + outs.Count + " frames for " + batch.Count + " source frames.");
                    foreach (var f in outs) { var b = File.ReadAllBytes(f); encIn.Write(b, 0, b.Length); }
                    encIn.Flush();
                    frames += batch.Count;
                    try { Directory.Delete(bin, true); Directory.Delete(bout, true); } catch { }
                    if (plan.Expected <= 0) onProgress(0.5);
                }
                if (failed)
                {
                    foreach (var pr in new[] { dec, enc }) { try { if (!pr.HasExited) pr.Kill(); } catch { } }
                    Cleanup(plan);
                    return 3;
                }
                try { encIn.Close(); } catch { }
                while (!enc.WaitForExit(200))
                    if (cancelled()) { try { enc.Kill(); } catch { } lastErrors = "Cancelled"; Cleanup(plan); return -1; }
                enc.WaitForExit();
                lock (errs) lastErrors = string.Join(Environment.NewLine, errs.ToArray());
                if (frames == 0) { lastErrors = "No frame was decoded from the video." + Environment.NewLine + lastErrors; Cleanup(plan); return 3; }
                onLog(plan.Name + " — " + frames + " frames upscaled and encoded");
                Cleanup(plan);
                if (enc.ExitCode == 0) onProgress(1.0);
                return enc.ExitCode;
            }
            catch (Exception ex)
            {
                foreach (var pr in new[] { dec, enc }) { try { if (pr != null && !pr.HasExited) pr.Kill(); } catch { } }
                lastErrors = ex.Message; Cleanup(plan); return -2;
            }
            finally
            {
                if (cancelled()) foreach (var pr in new[] { dec, enc }) { try { if (pr != null && !pr.HasExited) pr.Kill(); } catch { } }
            }
        }

        static void KillTree(Process p)
        {
            try
            {
                if (Path.DirectorySeparatorChar == '\\')
                {
                    var k = Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true });
                    if (k != null) k.WaitForExit(5000);
                }
            }
            catch { }
            try { if (!p.HasExited) p.Kill(); } catch { }
            try { p.WaitForExit(5000); } catch { }
        }

        static void Cleanup(Plan plan)
        {
            try { if (plan.TempDir != null && Directory.Exists(plan.TempDir)) Directory.Delete(plan.TempDir, true); } catch { }
        }
    }
}
