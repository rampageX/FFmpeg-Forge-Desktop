using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace FFmpegForge
{
    /// Deinterlacing (Video category). QTGMC = QTempGaussMC from vsjetpack (MIT) on VapourSynth (LGPL-2.1),
    /// run by forge_qtgmc.py in its own Python environment:
    ///   python forge_qtgmc.py (BestSource → QTGMC → YUV4MPEG2) → ffmpeg (lossless FFV1 intermediate + source audio) → normal export
    /// Without the QTGMC environment, FFmpeg's bwdif deinterlacer is used instead (and the log says so).
    public static class Qtgmc
    {
        public static readonly string[] Packages =
        {
            "vapoursynth==81", "vsjetpack==2.2.6", "jetpytools==3.2.0",
            "vapoursynth-mvutensils==9", "vapoursynth-znedi3==3.3", "vapoursynth-zsmooth==0.20.0",
            "vapoursynth-resize2==0.5.1", "vapoursynth-akarin==1.5.0", "vapoursynth-vszip==24.0.0", "vapoursynth-bestsource==23"
        };
        const string Marker = "qtgmc_ok.txt";

        public static string Root
        {
            get
            {
                if (AppPaths.Portable) return Path.Combine(AppPaths.AppDir, "QTGMC");
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FFmpegForge", "QTGMC");
            }
        }

        public static string Python
        {
            get
            {
                string r = Root;
                string w = Path.Combine(r, "venv", "Scripts", "python.exe");
                if (File.Exists(w)) return w;
                string l = Path.Combine(r, "venv", "bin", "python");
                return File.Exists(l) ? l : null;
            }
        }

        public static bool Ready { get { return Python != null && File.Exists(Path.Combine(Root, Marker)); } }

        public static string Script { get { return Path.Combine(Root, "forge_qtgmc.py"); } }

        static string Res(string name)
        {
            using (var s = typeof(Qtgmc).Assembly.GetManifestResourceStream(name))
            using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
        }

        public static void SyncScript()
        {
            try
            {
                Directory.CreateDirectory(Root);
                string want = Res("forge_qtgmc.py").Replace("\r\n", "\n");
                if (!File.Exists(Script) || File.ReadAllText(Script) != want) File.WriteAllText(Script, want, new UTF8Encoding(false));
            }
            catch { }
        }

        public static string WriteInstaller(string root)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "forge_qtgmc.py"), Res("forge_qtgmc.py").Replace("\r\n", "\n"), new UTF8Encoding(false));
            try { File.Delete(Path.Combine(root, Marker)); } catch { }
            string bat = Path.Combine(root, "install_qtgmc.bat");
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("title FFmpeg Forge - QTGMC installation");
            sb.AppendLine("setlocal");
            sb.AppendLine("cd /d \"%~dp0\"");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo  QTGMC deinterlacing for FFmpeg Forge");
            sb.AppendLine("echo  VapourSynth R81 + vsjetpack QTempGaussMC and its plugins");
            sb.AppendLine("echo  Folder: %CD%");
            sb.AppendLine("echo  Downloads: uv + Python 3.12 and about 150 MB of packages.");
            sb.AppendLine("echo ============================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("if not exist uv\\uv.exe (");
            sb.AppendLine("  echo [1/4] Downloading uv...");
            sb.AppendLine("  powershell -NoProfile -ExecutionPolicy Bypass -Command \"$ProgressPreference='SilentlyContinue'; Invoke-WebRequest -UseBasicParsing '" + AiDenoise.UvZip + "' -OutFile uv.zip; Expand-Archive -Force uv.zip uv; Remove-Item uv.zip\" || goto :fail");
            sb.AppendLine(")");
            sb.AppendLine("echo [2/4] Python 3.12 environment...");
            sb.AppendLine("set UV_PYTHON_INSTALL_DIR=%CD%\\python");
            sb.AppendLine("set UV_CACHE_DIR=%CD%\\cache");
            sb.AppendLine("set UV_LINK_MODE=copy");          // exFAT / FAT / network drives have no hard links: copy without the uv warning
            sb.AppendLine("if not exist venv\\Scripts\\python.exe uv\\uv.exe venv --python 3.12 --seed venv || goto :fail");
            sb.AppendLine("echo [3/4] VapourSynth, vsjetpack (QTGMC) and plugins...");
            sb.AppendLine("uv\\uv.exe pip install --python venv\\Scripts\\python.exe " + string.Join(" ", Packages) + " || goto :fail");
            sb.AppendLine("echo [4/4] Checking QTGMC...");
            sb.AppendLine("venv\\Scripts\\python.exe forge_qtgmc.py -src x -probe > " + Marker + " || goto :fail");
            sb.AppendLine("type " + Marker);
            sb.AppendLine("echo.");
            sb.AppendLine("echo QTGMC is installed. You can close this window and go back to FFmpeg Forge.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 0");
            sb.AppendLine(":fail");
            sb.AppendLine("if exist " + Marker + " del " + Marker);
            sb.AppendLine("echo.");
            sb.AppendLine("echo Installation failed. Check your internet connection and run install_qtgmc.bat again.");
            sb.AppendLine("pause");
            sb.AppendLine("exit /b 1");
            File.WriteAllText(bat, sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
            return bat;
        }

        /// Deinterlacing applies to this job: the option is on, the output is a video file or a sequence,
        /// and the source is interlaced (ffprobe field order) or the field order is forced.
        public static bool Wanted(Job job, ConvSettings s)
        {
            if (s.Deint <= 0 || s.Format.Kind != MediaKind.Video || s.KeepVideo) return false;
            if (job.Kind != MediaKind.Video || !job.HasVideo || job.Width <= 0) return false;
            return job.IsInterlaced || s.DeintOrder > 0;
        }

        public static string OrderText(Job job, ConvSettings s)
        {
            if (s.DeintOrder == 1) return "top field first (forced)";
            if (s.DeintOrder == 2) return "bottom field first (forced)";
            string f = job.FieldOrder ?? "";
            if (f == "tt" || f == "tb") return "top field first";
            if (f == "bb" || f == "bt") return "bottom field first";
            return "auto";
        }

        /// Frame rate of the intermediate snapped to the standard rate it comes from (0 = keep the probed one).
        public static double ExactRate(Job j)
        {
            if (j.FpsNum <= 0 || j.FpsDen <= 0) return 0;
            double r = j.FpsNum / (double)j.FpsDen;
            double[] std = { 24000.0 / 1001, 24, 25, 30000.0 / 1001, 30, 48, 50, 60000.0 / 1001, 60, 100, 120000.0 / 1001, 120 };
            foreach (var x in std) if (Math.Abs(r - x) / x < 0.001) return x;
            return 0;
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        /// bwdif filter used when QTGMC is not installed (same rate / double rate, same field order).
        public static string Bwdif(Job job, ConvSettings s)
        {
            string parity = s.DeintOrder == 1 ? "tff" : s.DeintOrder == 2 ? "bff" : "auto";
            if (s.DeintOrder == 0)
            {
                string f = job.FieldOrder ?? "";
                if (f == "tt" || f == "tb") parity = "tff"; else if (f == "bb" || f == "bt") parity = "bff";
            }
            return "bwdif=mode=" + (s.Deint == 2 ? "send_frame" : "send_field") + ":parity=" + parity + ":deint=all";
        }

        /// Stage: source → QTGMC (or bwdif) → FFV1 lossless progressive intermediate (trim applied, audio copied).
        public static AiDenoise.Stage Build(Job job, ConvSettings s, bool useQtgmc, out string error)
        {
            error = null;
            int w = job.Width, h = job.Height;
            if (w < 16 || h < 16) { error = "Could not read the picture size (ffprobe)."; return null; }
            if (useQtgmc) { if (!Ready) { error = "QTGMC is not installed."; return null; } SyncScript(); }
            var st = new AiDenoise.Stage { Picture = false };
            st.TempDir = Path.Combine(Path.GetTempPath(), "FFmpegForge-Deinterlace-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            st.Intermediate = Path.Combine(st.TempDir, "deinterlaced.mkv");

            double ss = s.TrimStart > 0 ? s.TrimStart : 0, to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            dur = Math.Max(0, dur - ss);
            st.Seconds = dur;

            var p = new Rtx.DriverPlan { W = w, H = h, OW = w, OH = h, Expected = dur, NoHelper = true };
            if (useQtgmc)
            {
                string order = s.DeintOrder == 1 ? "tff" : s.DeintOrder == 2 ? "bff" : "auto";
                if (s.DeintOrder == 0)
                {
                    string f = job.FieldOrder ?? "";
                    if (f == "tt" || f == "tb") order = "tff"; else if (f == "bb" || f == "bt") order = "bff";
                }
                p.DecExe = Python; p.DecName = "QTGMC";
                p.DecArgs = "-u " + Q(Script) + " -src " + Q(job.Path) + " -mode " + (s.Deint == 2 ? "same" : "bob") + " -order " + order +
                            (ss > 0 ? " -ss " + Formats.Inv(ss) : "") + (to > ss && to > 0 ? " -to " + Formats.Inv(to) : "");
                p.DecEnv = new Dictionary<string, string> { { "PYTHONIOENCODING", "utf-8" }, { "__CWD", Root } };
            }
            else
            {
                p.DecName = "bwdif";
                p.DecArgs = "-hide_banner -nostdin -loglevel error " + trim + "-i " + Q(job.Path) + " -map 0:v:0 -an -sn -dn -vf " + Q(Bwdif(job, s)) +
                            " -strict -1 -f yuv4mpegpipe -";
            }
            string m = h >= 720 ? "bt709" : "smpte170m", prim = h >= 720 ? "bt709" : (h == 576 ? "bt470bg" : "smpte170m");
            p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats " +
                        "-thread_queue_size 64 -f yuv4mpegpipe -i - " + trim + "-i " + Q(job.Path) +
                        " -map 0:v:0 -map 1:a? -c:v ffv1 -level 3 -g 1 -slices 16 -slicecrc 0 -field_order progressive" +
                        " -color_primaries " + prim + " -color_trc " + m + " -colorspace " + m +
                        (job.IsDsdAudio || trim.Length > 0 ? " -c:a flac" : " -c:a copy") + " -shortest " + Q(st.Intermediate);
            st.Plan = p;
            return st;
        }
    }
}
