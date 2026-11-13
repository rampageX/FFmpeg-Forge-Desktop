using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FFmpegForge
{
    /// "Python / ONNX AI": models imported by the user (.onnx, PyTorch .pth/.pt/.ckpt/.safetensors, or a .py script)
    /// run on every frame of the export by forge_custom_ai.py, inside the AI Denoiser's Python environment
    /// (PyTorch CUDA + ONNX Runtime GPU + spandrel):
    ///   ffmpeg (trim, decode → RGB) → forge_custom_ai.py (model, tiles) → ffmpeg (lossless intermediate) → normal export
    public static class CustomAi
    {
        public static readonly string[] Extensions = { ".onnx", ".pth", ".pt", ".ckpt", ".safetensors", ".bin", ".py" };

        /// Model library: models\custom next to FFmpegForge.exe (or %LOCALAPPDATA%\FFmpegForge\models\custom if read-only).
        public static string LibraryDir
        {
            get
            {
                string d = Path.Combine(AppPaths.AppDir, "models", "custom");
                try
                {
                    Directory.CreateDirectory(d);
                    string t = Path.Combine(d, ".write-test");
                    File.WriteAllText(t, ""); File.Delete(t);
                    return d;
                }
                catch
                {
                    d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFmpegForge", "models", "custom");
                    try { Directory.CreateDirectory(d); } catch { }
                    return d;
                }
            }
        }

        public static List<string> Models()
        {
            var l = new List<string>();
            try
            {
                foreach (var f in Directory.GetFiles(LibraryDir))
                    if (Extensions.Contains(Path.GetExtension(f).ToLowerInvariant())) l.Add(Path.GetFileName(f));
            }
            catch { }
            l.Sort(StringComparer.OrdinalIgnoreCase);
            return l;
        }

        public static string PathOf(string name) { return string.IsNullOrEmpty(name) ? null : Path.Combine(LibraryDir, name); }

        public static string Kind(string name)
        {
            string e = (Path.GetExtension(name ?? "") ?? "").ToLowerInvariant();
            if (e == ".onnx") return "ONNX";
            if (e == ".py") return "Python script";
            return "PyTorch";
        }

        /// Copies a model into the library (with the external-data files of big ONNX models). Returns the library name.
        public static string Import(string src)
        {
            string dir = LibraryDir, name = Path.GetFileName(src);
            File.Copy(src, Path.Combine(dir, name), true);
            if (Path.GetExtension(src).ToLowerInvariant() == ".onnx")
            {
                string sd = Path.GetDirectoryName(src), stem = Path.GetFileNameWithoutExtension(src);
                foreach (var f in Directory.GetFiles(sd))
                {
                    string fn = Path.GetFileName(f);
                    if (fn.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                    if (fn.StartsWith(stem, StringComparison.OrdinalIgnoreCase) && (fn.EndsWith(".data", StringComparison.OrdinalIgnoreCase) || fn.EndsWith(".onnx_data", StringComparison.OrdinalIgnoreCase) || fn.EndsWith(".weights", StringComparison.OrdinalIgnoreCase)))
                        File.Copy(f, Path.Combine(dir, fn), true);
                }
            }
            probeCache.Clear();
            return name;
        }

        public static void Remove(string name)
        {
            string p = PathOf(name);
            if (p != null && File.Exists(p)) File.Delete(p);
            probeCache.Clear();
        }

        /// Writes an example Python model into the library.
        public static string WriteTemplate()
        {
            string p = Path.Combine(LibraryDir, "example_script.py");
            var sb = new StringBuilder();
            sb.AppendLine("# FFmpeg Forge - Python / ONNX AI: example model script");
            sb.AppendLine("# Option A: load(device) returns a torch.nn.Module (NCHW float RGB 0..1 -> NCHW). FFmpeg Forge tiles it on the GPU.");
            sb.AppendLine("# Option B: process(img) gets one frame (HxWx3 uint8 RGB numpy) and returns a frame (uint8, or float 0..1).");
            sb.AppendLine("# The output can be bigger (upscalers): FFmpeg Forge detects the scale automatically.");
            sb.AppendLine("import torch");
            sb.AppendLine("import torch.nn.functional as F");
            sb.AppendLine("");
            sb.AppendLine("class Sharpen(torch.nn.Module):");
            sb.AppendLine("    def forward(self, x):");
            sb.AppendLine("        blur = F.avg_pool2d(F.pad(x, (1, 1, 1, 1), mode='replicate'), 3, stride=1)");
            sb.AppendLine("        return (x + 0.6 * (x - blur)).clamp(0, 1)");
            sb.AppendLine("");
            sb.AppendLine("def load(device):");
            sb.AppendLine("    # e.g. build your network and load its weights here:");
            sb.AppendLine("    #   net = MyNet(); net.load_state_dict(torch.load('my_weights.pth', map_location='cpu')); return net");
            sb.AppendLine("    return Sharpen()");
            File.WriteAllText(p, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
            return Path.GetFileName(p);
        }

        // ---------------- runtime ----------------
        static bool SitePackage(string pkg)
        {
            try
            {
                string root = AiDenoise.Root;
                if (Directory.Exists(Path.Combine(root, "venv", "Lib", "site-packages", pkg))) return true;
                string lib = Path.Combine(root, "venv", "lib");
                if (Directory.Exists(lib))
                    foreach (var py in Directory.GetDirectories(lib))
                        if (Directory.Exists(Path.Combine(py, "site-packages", pkg))) return true;
            }
            catch { }
            return false;
        }

        public static bool HasOnnx { get { return SitePackage("onnxruntime"); } }

        /// Python + PyTorch (the AI Denoiser environment) and the loader the chosen model needs.
        public static string Missing(string name)
        {
            if (AiDenoise.Python == null) return "The AI runtime is not installed: click \"Install AI runtime…\".";
            string k = Kind(name);
            if (k == "ONNX" && !HasOnnx) return "ONNX Runtime is missing: click \"Install AI runtime…\".";
            if (k == "PyTorch" && !AiDenoise.HasSpandrel) return "The PyTorch model loader (spandrel) is missing: click \"Install AI runtime…\".";
            return null;
        }

        public static string Script { get { return Path.Combine(AiDenoise.Root, "forge_custom_ai.py"); } }

        static readonly Dictionary<string, double> probeCache = new Dictionary<string, double>();

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        /// Loads the model once on a small picture to learn its scale (output / input size). 0 = failure.
        public static double Probe(string name, bool gpu, out string error)
        {
            error = null;
            string path = PathOf(name);
            if (path == null || !File.Exists(path)) { error = "Model not found: " + name; return 0; }
            string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + gpu;
            double cached;
            if (probeCache.TryGetValue(key, out cached)) return cached;
            AiDenoise.SyncScripts();
            var psi = new ProcessStartInfo(AiDenoise.Python, "-u " + Q(Script) + " -probe -model " + Q(path) + " -device " + (gpu ? "cuda" : "cpu"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(path)
            };
            psi.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            try
            {
                using (var p = Process.Start(psi))
                {
                    var errT = p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(600000)) { try { p.Kill(); } catch { } error = "The model took too long to load."; return 0; }
                    string err = errT.Result;
                    foreach (var line in o.Split('\n'))
                        if (line.StartsWith("FORGE_SCALE "))
                        {
                            double s;
                            if (double.TryParse(line.Substring(12).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out s) && s > 0 && s <= 16)
                            { probeCache[key] = s; return s; }
                        }
                    var lines = err.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
                    error = "The model could not be loaded" + (lines.Count > 0 ? ": " + string.Join(" | ", lines.Skip(Math.Max(0, lines.Count - 3)).ToArray()) : ".");
                    return 0;
                }
            }
            catch (Exception ex) { error = ex.Message; return 0; }
        }

        /// Stage: source (or the Denoiser's result) → user model → lossless intermediate at the model's output size.
        public static AiDenoise.Stage Build(Job job, ConvSettings s, bool pictureOutput, out string error)
        {
            return BuildModel(job, s, pictureOutput, s.CaiModel, s.CaiGpu, s.CaiFp32, s.CaiTile, s.CaiKeep ? 1.0 : 0, "custom_ai", out error);
        }

        /// Any model (library name or full path) → lossless intermediate. factor: 0 = the model's own scale,
        /// otherwise the exact output factor (the model result is resampled to it, e.g. a ×4 model for ×2 / ×3).
        public static AiDenoise.Stage BuildModel(Job job, ConvSettings s, bool pictureOutput, string model, bool gpu, bool fp32, int tile, double factor, string tag, out string error)
        {
            error = Missing(model);
            if (error != null) return null;
            if (!AiDenoise.Ready) { error = "The AI runtime is not complete: click \"Install AI runtime…\" (Python / ONNX AI section)."; return null; }
            double sc = Probe(model, gpu, out error);
            if (sc <= 0) return null;
            int w = job.Width, h = job.Height;
            double f = factor > 0 ? factor : sc;
            int ow = (int)Math.Round(w * f), oh = (int)Math.Round(h * f);
            if (ow > 16384 || oh > 16384) { error = "The AI output would be " + ow + "×" + oh + " (more than 16384 px): choose a smaller factor."; return null; }
            var probe = s.Clone(); probe.DnModel = "scunet";
            var st = AiDenoise.Build(job, probe, pictureOutput, out error);
            if (st == null) return null;
            var p = st.Plan;
            p.OW = ow; p.OH = oh; p.HelperName = "ForgeAI";
            string from = "-f rawvideo -pix_fmt rgb24 -s " + w + "x" + h + " ";
            int i = p.EncArgs.IndexOf(from, StringComparison.Ordinal);
            if (i < 0) { error = "Internal error (encoder input)."; return null; }
            p.EncArgs = p.EncArgs.Substring(0, i) + "-f rawvideo -pix_fmt rgb24 -s " + ow + "x" + oh + " " + p.EncArgs.Substring(i + from.Length);
            p.HelperArgs = "-u " + Q(Script) + " -w " + w + " -h " + h + " -model " + Q(PathOf(model)) + " -device " + (gpu ? "cuda" : "cpu") +
                           " -precision " + (fp32 ? "fp32" : "fp16") + " -tile " + Math.Max(0, tile) + " -ow " + ow + " -oh " + oh;
            string old = st.Intermediate;
            st.Intermediate = Path.Combine(st.TempDir, tag + (st.Picture ? ".png" : ".mkv"));
            p.EncArgs = p.EncArgs.Replace(Q(old), Q(st.Intermediate));
            return st;
        }
    }
}
