using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FFmpegForge
{
    /// AI Denoiser (one checkbox): SCUNet "real" blind denoising network (Kai Zhang et al., Apache-2.0) run by
    /// a small Python / PyTorch helper, plus Neat Video-style temporal stabilisation.
    ///   ffmpeg (trim, decode → RGB) → forge_ai_denoise.py (GPU) → ffmpeg (lossless UT Video / PNG intermediate, ~10× faster than FFV1)
    /// The intermediate then goes through the normal export (Scale, RTX, SeedVR2, VisionNext, codecs, audio…).
    /// When the AI engine is not installed, FFmpeg Forge's FFmpeg denoiser (auto noise profile) is used instead.
    public static class AiDenoise
    {
        public static string Dir = "";
        public const string ModelFile = "scunet_color_real_psnr.pth";
        public const string ModelUrl = "https://github.com/cszn/KAIR/releases/download/v1.0/scunet_color_real_psnr.pth";
        public const string UvZip = "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip";
        public const string TorchIndex = "https://download.pytorch.org/whl/cu128";

        public static string DefaultInstallDir
        {
            get
            {
                if (AppPaths.Portable) return Path.Combine(AppPaths.AppDir, "AI-Denoiser");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFmpegForge", "AI-Denoiser");
            }
        }

        public static string Root { get { return string.IsNullOrEmpty(Dir) ? DefaultInstallDir : Dir; } }

        public class ModelInfo
        {
            public string Key, Label, Arch, Note;
            public ModelInfo(string k, string l, string a, string n) { Key = k; Label = l; Arch = a; Note = n; }
        }

        /// Denoiser IA models. "scunet" is built in; the others are .pth files (models\ folder) run through spandrel.
        public static readonly ModelInfo[] Models =
        {
            new ModelInfo("scunet", "SCUNet (built-in)", "SCUNet", "real camera noise and film grain, with temporal stabilisation"),
            new ModelInfo("4xNomos8kHAT-L_otf.pth", "Nomos8k HAT-L (best, slowest)", "HAT-L", "strong restoration with the finest detail (noise, blur, JPEG) — very heavy"),
            new ModelInfo("4xNomosUniDAT_otf.pth", "NomosUni DAT (strong denoise)", "DAT", "strong denoising and compression clean-up, natural look — heavy"),
            new ModelInfo("4xPurePhoto-Span.pth", "PurePhoto SPAN (fast)", "SPAN", "light clean-up for good-quality sources — very fast"),
        };

        public static ModelInfo FindModel(string key)
        {
            foreach (var m in Models) if (m.Key == key) return m;
            return Models[0];
        }

        /// Folders searched for the model files: AI-Denoiser\models, then models\ next to FFmpegForge.exe.
        public static string[] ModelDirs { get { return new[] { Path.Combine(Root, "models"), Path.Combine(AppPaths.AppDir, "models") }; } }

        public static string ModelPath(string key)
        {
            if (key == null || key == "scunet") return Path.Combine(Root, ModelFile);
            foreach (var d in ModelDirs)
                try { string p = Path.Combine(d, key); if (File.Exists(p)) return p; } catch { }
            return null;
        }

        /// spandrel (+ torchvision) in the AI Denoiser's Python environment.
        public static bool HasSpandrel
        {
            get
            {
                try
                {
                    if (Directory.Exists(Path.Combine(Root, "venv", "Lib", "site-packages", "spandrel"))) return true;
                    string lib = Path.Combine(Root, "venv", "lib");
                    if (Directory.Exists(lib))
                        foreach (var py in Directory.GetDirectories(lib))
                            if (Directory.Exists(Path.Combine(py, "site-packages", "spandrel"))) return true;
                }
                catch { }
                return false;
            }
        }

        public static bool ReadyFor(ConvSettings s)
        {
            if (!Ready) return false;
            if (s == null || s.DnModel == null || s.DnModel == "scunet") return true;
            return HasSpandrel && ModelPath(s.DnModel) != null;
        }

        public static string StatusFor(string key)
        {
            var m = FindModel(key);
            if (!Ready) return "AI Denoiser not installed: FFmpeg Forge's classic denoiser is used until you install it (tick again to install).";
            if (m.Key == "scunet") return "AI model ready (SCUNet) · runs on your GPU before the other effects.";
            if (ModelPath(m.Key) == null) return "Model file missing: put " + m.Key + " in " + ModelDirs[1] + " (or " + ModelDirs[0] + ").";
            if (!HasSpandrel) return "This model needs the spandrel loader: click \"Install model support\" (one time).";
            return "Ready: " + m.Label + " · " + m.Note + ".";
        }
        public static string Script { get { return Path.Combine(Root, "forge_ai_denoise.py"); } }

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
                    var m = new FileInfo(Path.Combine(Root, ModelFile));
                    return Python != null && File.Exists(Script) && File.Exists(Path.Combine(Root, "network_scunet.py")) && m.Exists && m.Length > 60000000;
                }
                catch { return false; }
            }
        }

        static string Res(string name)
        {
            using (var s = typeof(AiDenoise).Assembly.GetManifestResourceStream(name))
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        /// Writes the helper scripts (embedded in FFmpegForge.exe) and the installation script.
        public static string WriteInstaller(string root)
        {
            Directory.CreateDirectory(root);
            foreach (var f in new[] { "forge_ai_denoise.py", "network_scunet.py", "LICENSE-SCUNet.txt", "forge_custom_ai.py" })
                File.WriteAllText(Path.Combine(root, f), Res(f).Replace("\r\n", "\n"), new UTF8Encoding(false));
            string bat = Path.Combine(root, "install_ai_denoiser.bat");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("title FFmpeg Forge - AI Denoiser installation");
            sb.AppendLine("setlocal");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo  AI Denoiser for FFmpeg Forge (SCUNet, Apache-2.0)");
            sb.AppendLine("echo  Folder: %CD%");
            sb.AppendLine("echo  Downloads: uv + Python 3.12, PyTorch CUDA 12.8 (about 3 GB), SCUNet model (72 MB).");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("if not exist uv\\uv.exe (");
            sb.AppendLine("  echo [1/4] Downloading uv...");
            sb.AppendLine("  powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '" + UvZip + "' -OutFile uv.zip; Expand-Archive -Force uv.zip uv; Remove-Item uv.zip\" || goto :fail");
            sb.AppendLine(")");
            sb.AppendLine("echo [2/4] Python 3.12 environment...");
            sb.AppendLine("set UV_PYTHON_INSTALL_DIR=%CD%\\python");
            sb.AppendLine("set UV_CACHE_DIR=%CD%\\cache");
            sb.AppendLine("set UV_LINK_MODE=copy");          // exFAT / FAT / network drives have no hard links: copy without the uv warning
            sb.AppendLine("if not exist venv\\Scripts\\python.exe uv\\uv.exe venv --python 3.12 --seed venv || goto :fail");
            sb.AppendLine("echo [3/4] PyTorch with CUDA 12.8 (large download)...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe torch torchvision --index-url " + TorchIndex + " || goto :fail");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe numpy einops timm || goto :fail");
            sb.AppendLine("echo Model loader (spandrel, for HAT / DAT / SPAN models)...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe spandrel safetensors onnx onnxruntime-gpu || goto :fail");
            sb.AppendLine("if not exist models mkdir models");
            sb.AppendLine("echo [4/4] SCUNet model...");
            sb.AppendLine("if not exist " + ModelFile + " powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '" + ModelUrl + "' -OutFile " + ModelFile + "\" || goto :fail");
            sb.AppendLine("venv\\Scripts\\python.exe -c \"import torch;print('PyTorch',torch.__version__,'CUDA',torch.cuda.is_available(), torch.cuda.get_device_name(0) if torch.cuda.is_available() else '(CPU only: slow)')\"");
            sb.AppendLine("echo.");
            sb.AppendLine("echo The AI Denoiser is installed. You can close this window and go back to FFmpeg Forge.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 0");
            sb.AppendLine(":fail");
            sb.AppendLine("echo.");
            sb.AppendLine("echo Installation failed. Check your internet connection and run install_ai_denoiser.bat again.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 1");
            File.WriteAllText(bat, sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            return bat;
        }

        /// One-time installation of spandrel (+ torchvision) into an existing AI Denoiser environment.
        public static string WriteModelSupportInstaller(string root)
        {
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(Path.Combine(root, "models"));
            string bat = Path.Combine(root, "install_model_support.bat");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("title FFmpeg Forge - AI runtime (PyTorch, spandrel, ONNX Runtime)");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine("set UV_PYTHON_INSTALL_DIR=%CD%\\python");
            sb.AppendLine("set UV_CACHE_DIR=%CD%\\cache");
            sb.AppendLine("set UV_LINK_MODE=copy");          // exFAT / FAT / network drives have no hard links: copy without the uv warning
            sb.AppendLine("echo Installing the AI runtime: PyTorch CUDA + torchvision, spandrel (MIT) and ONNX Runtime GPU...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe torch torchvision --index-url " + TorchIndex + " || goto :fail");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe spandrel safetensors onnx onnxruntime-gpu || goto :fail");
            sb.AppendLine("venv\\Scripts\\python.exe -c \"import torch, spandrel, onnxruntime as o; print('PyTorch', torch.__version__, '| CUDA', torch.cuda.is_available(), '| spandrel', spandrel.__version__, '| ONNX Runtime', o.__version__, o.get_available_providers())\"");
            sb.AppendLine("echo.");
            sb.AppendLine("echo Done. You can close this window and go back to FFmpeg Forge.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 0");
            sb.AppendLine(":fail");
            sb.AppendLine("echo Installation failed. Check your internet connection and try again.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 1");
            File.WriteAllText(bat, sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            return bat;
        }

        /// Keeps the helper scripts of an existing installation up to date with this FFmpeg Forge version.
        public static void SyncScripts()
        {
            foreach (var f in new[] { "forge_ai_denoise.py", "network_scunet.py", "forge_custom_ai.py" })
            {
                try
                {
                    string want = Res(f).Replace("\r\n", "\n"), path = Path.Combine(Root, f);
                    if (!File.Exists(path) || File.ReadAllText(path) != want) File.WriteAllText(path, want, new UTF8Encoding(false));
                }
                catch { }
            }
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        public class Stage
        {
            public Rtx.DriverPlan Plan;
            public string Intermediate, TempDir;
            public bool Picture;
            public double Seconds;
        }

        /// Stage 1: source → AI denoiser → lossless intermediate (trim applied, audio copied for videos).
        public static Stage Build(Job job, ConvSettings s, bool pictureOutput, out string error)
        {
            error = null;
            if (!ReadyFor(s)) { error = StatusFor(s.DnModel); return null; }
            SyncScripts();
            int w = job.Width, h = job.Height;
            if (w < 16 || h < 16) { error = "Could not read the picture size (ffprobe)."; return null; }
            bool srcVideo = job.Kind == MediaKind.Video || CommandBuilder.IsAnimatedImageSource(job);
            bool picture = pictureOutput || !srcVideo;
            var st = new Stage { Picture = picture };
            st.TempDir = Path.Combine(Path.GetTempPath(), "FFmpegForge-AIDenoise-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            st.Intermediate = Path.Combine(st.TempDir, picture ? "denoised.png" : "denoised.mkv");

            int fn = job.FpsNum > 0 ? job.FpsNum : 30, fd = job.FpsDen > 0 ? job.FpsDen : 1;
            if (s.Fps > 0 && !picture) { fn = (int)Math.Round(s.Fps * 1000); fd = 1000; }
            string rate = fn + "/" + fd;
            double ss = s.TrimStart > 0 ? s.TrimStart : 0, to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = picture ? "" : (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            dur = Math.Max(0, dur - ss);
            st.Seconds = picture ? 0 : dur;
            string m709 = h >= 720 ? "bt709" : "smpte170m";

            var p = new Rtx.DriverPlan { W = w, H = h, OW = w, OH = h, Expected = st.Seconds, Helper = Python, HelperName = "ForgeAI" };
            p.DecArgs = "-hide_banner -nostdin -loglevel error " + trim + "-i " + Q(job.Path) + " -map 0:v:0 -an -sn -dn" +
                        (picture ? " -frames:v 1" : " -r " + rate) + " -vf " + Q("scale=" + w + ":" + h + ",format=rgb24") + " -f rawvideo -";
            p.HelperArgs = "-u " + Q(Script) + " -w " + w + " -h " + h + " -tile 0 -temporal " + (picture ? 0 : 1) + " -model " + Q(ModelPath(s.DnModel));
            if (picture)
                p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -f rawvideo -pix_fmt rgb24 -s " + w + "x" + h + " -i - -frames:v 1 -update 1 " + Q(st.Intermediate);
            else
                p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats " +
                            "-f rawvideo -pix_fmt rgb24 -s " + w + "x" + h + " -framerate " + rate + " -i - " + trim + "-i " + Q(job.Path) +
                            " -map 0:v:0 -map 1:a? -vf " + Q("scale=out_color_matrix=" + m709 + ":out_range=tv,format=yuv444p") +
                            " -c:v utvideo -pred median -color_primaries " + (h >= 720 ? "bt709" : "smpte170m") + " -color_trc " + (h >= 720 ? "bt709" : "smpte170m") +
                            " -colorspace " + m709 + (job.IsDsdAudio
                                ? " -af " + Q("aresample=176400,lowpass=f=40000,lowpass=f=40000,volume=2.0,alimiter=limit=0.98:attack=1:release=50:level=disabled") + " -c:a flac"
                                : " -c:a copy") + " -shortest " + Q(st.Intermediate);
            st.Plan = p;
            return st;
        }

        public static void Cleanup(Stage st)
        {
            try { if (st != null && st.TempDir != null && Directory.Exists(st.TempDir)) Directory.Delete(st.TempDir, true); } catch { }
        }
    }
}
