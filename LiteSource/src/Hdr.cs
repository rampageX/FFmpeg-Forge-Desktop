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
    /// SDR → HDR (2.1.6). Inverse tone mapping done by FFmpeg:
    ///   zscale: SDR → BT.2020 PQ with SDR white at 203 nits (ITU-R BT.2408 reference white)
    ///   lutyuv: highlight expansion on the PQ luma (linear below 60 % of SDR white, smooth rise up to the chosen peak)
    /// Modes: 1 = HDR10 (PQ + static metadata), 2 = Dolby Vision profile 8.1 (HDR10 base layer + dynamic L1 metadata
    /// measured on every frame, RPU made by dovi_tool and injected after the encode).
    public static class Hdr
    {
        public const int Off = 0, Hdr10 = 1, DolbyVision81 = 2;
        public static readonly int[] Peaks = { 600, 1000, 1500, 2000, 4000 };

        static string Inv(double d) { return d.ToString("0.##########", CultureInfo.InvariantCulture); }

        const double M1 = 0.1593017578125, M2 = 78.84375, C1 = 0.8359375, C2 = 18.8515625, C3 = 18.6875;

        /// lutyuv expression (16-bit full-range PQ luma in, 16-bit PQ luma out).
        static string Expansion(int peak)
        {
            double k = 0.6, p = Math.Max(1.0, peak / 203.0);
            return "st(0,pow(val/65535," + Inv(1 / M2) + "));" +
                   "st(1,10000*pow(max(ld(0)-" + Inv(C1) + ",0)/(" + Inv(C2) + "-" + Inv(C3) + "*ld(0))," + Inv(1 / M1) + ")/203);" +
                   "st(2,if(gt(ld(1)," + Inv(k) + "),pow((min(ld(1),1)-" + Inv(k) + ")/" + Inv(1 - k) + ",2),0));" +
                   "st(3,ld(1)*(1+" + Inv(p - 1) + "*ld(2))*203/10000);" +
                   "st(4,pow(ld(3)," + Inv(M1) + "));" +
                   "65535*pow((" + Inv(C1) + "+" + Inv(C2) + "*ld(4))/(1+" + Inv(C3) + "*ld(4))," + Inv(M2) + ")";
        }

        /// Filter chain SDR → PQ BT.2020. image: output RGB 16-bit (PNG) or YUV 4:4:4 10-bit (AVIF); video: yuv420p10le.
        public static string Chain(Job job, int peak, string outFmt)
        {
            bool still = job.Kind == MediaKind.Image;
            string pix = (job.PixFmt ?? "").ToLowerInvariant();
            bool rgbSrc = pix.Length == 0 ? still : (pix.StartsWith("rgb") || pix.StartsWith("bgr") || pix.StartsWith("gbr") || pix.StartsWith("pal") || pix.StartsWith("gray") || pix.StartsWith("argb") || pix.StartsWith("abgr") || pix.StartsWith("ya"));
            bool full = pix.StartsWith("yuvj") || (still && !rgbSrc);
            int h = job.Height;
            string sd = h > 0 && h < 720 ? (h == 576 ? "bt470bg" : "170m") : "bt709";
            string inTags = still
                ? "tin=iec61966-2-1:pin=bt709" + (rgbSrc ? "" : ":min=" + (h >= 720 ? "bt709" : "170m")) + ":rin=" + (full || rgbSrc ? "pc" : "tv")
                : "tin=bt709:pin=" + (sd == "bt709" ? "bt709" : sd == "bt470bg" ? "bt470bg" : "170m") + ":min=" + (sd == "bt709" ? "bt709" : sd == "bt470bg" ? "bt470bg" : "170m") + ":rin=" + (full ? "pc" : "tv");
            string e = Expansion(peak);
            // expansion on the PQ luma only (Y'): chroma untouched, so hues are kept exactly (a per-channel curve shifts them)
            string c = "zscale=" + inTags + ":t=smpte2084:p=bt2020:m=bt2020nc:r=pc:npl=203,format=yuv444p16le,lutyuv=y='" + e + "'";
            if (outFmt == "rgb48") return c + ",zscale=r=pc,format=rgb48be";
            if (outFmt == "yuv444") return c + ",format=yuv444p10le";
            return c + ",zscale=r=tv,format=yuv420p10le";
        }

        public static string Tags(bool full) { return "-color_primaries bt2020 -color_trc smpte2084 -colorspace bt2020nc -color_range " + (full ? "pc" : "tv"); }

        /// Codecs able to carry 10-bit HDR.
        public static bool CodecOk(string k)
        {
            return k == "h265" || k == "nvh265" || k == "av1" || k == "nvav1" || k == "vp9" || (k != null && k.StartsWith("prores")) || k == "dnxhr_hqx" || k == "dnxhr_444" || k == "ffv1"
                || k == "enc:hevc_nvenc" || k == "enc:libx265" || k == "enc:libsvtav1";
        }

        /// Encoder arguments for the HDR10 signal (10-bit, BT.2020 PQ tags, static metadata).
        public static string VideoArgs(string va, int peak, bool dv)
        {
            bool nv = va.Contains("_nvenc");
            string p10 = nv ? "p010le" : "yuv420p10le";
            if (va.Contains("-pix_fmt yuv420p ") || va.EndsWith("-pix_fmt yuv420p")) va = Regex.Replace(va, @"-pix_fmt yuv420p(\s|$)", "-pix_fmt " + p10 + "$1");
            else if (!va.Contains("-pix_fmt")) va += " -pix_fmt " + p10;
            va += " " + Tags(false);
            int fall = Math.Min(400, peak / 2);
            if (va.Contains("libx265"))
                va += " -x265-params hdr10=1:repeat-headers=1:hdr10-opt=1:master-display=G(8500,39850)B(6550,2300)R(35400,14600)WP(15635,16450)L(" + (peak * 10000) + ",50):max-cll=" + peak + "," + fall +
                      (dv ? ":vbv-bufsize=40000:vbv-maxrate=40000" : "");
            else if (va.Contains("libsvtav1"))
                va += " -svtav1-params mastering-display=G(0.265,0.690)B(0.150,0.060)R(0.680,0.320)WP(0.3127,0.3290)L(" + peak + ",0.005):content-light=" + peak + "," + fall;
            if (dv) va += " -fps_mode cfr";
            return va;
        }

        /// Why SDR → HDR cannot run with these settings (null = OK).
        public static string Check(Job job, ConvSettings s)
        {
            if (s.SdrHdr <= 0) return null;
            if (job != null && job.IsHdr) return "This file is already HDR (" + job.ColorTransfer + "): SDR → HDR is for SDR videos and pictures. Set SDR → HDR to Off.";
            if (s.RtxActive) return "SDR → HDR and NVIDIA RTX AI are two different HDR engines (RTX TrueHDR is NVIDIA's own SDR → HDR): use only one of them.";
            if (s.ScaleActive) return "SDR → HDR cannot be combined with Scale / OptiScaler in the same export: upscale first, then convert the result to HDR.";
            if (s.Vision != null && s.Vision.Enabled) return "SDR → HDR cannot be combined with AI Upscale (" + s.Vision.Name + ") in the same export: upscale first, then convert the result to HDR.";
            if (s.Seed != null && s.Seed.Enabled) return "SDR → HDR cannot be combined with SeedVR2 in the same export: restore first, then convert the result to HDR.";
            if (s.Format.Kind == MediaKind.Video)
            {
                if (s.SeqExport > 0) return "PNG/JPG SEQUENCE is SDR: untick it to export an HDR video.";
                if (AudioTracks.Active(s)) return "SDR → HDR re-encodes the video: it cannot be combined with the AUDIO TRACKS options (Keep the video as is / added tracks).";
                if (s.SdrHdr == DolbyVision81)
                {
                    string e = (s.Format.Ext ?? "").ToLowerInvariant();
                    if (e != "mp4" && e != "m4v" && e != "mov" && e != "mkv") return "Dolby Vision is written in MP4, M4V, MOV or MKV: choose one of these formats.";
                    if (DoviTool == null) return "Dolby Vision needs dovi_tool.exe (MIT licence), shipped next to FFmpegForge.exe: re-extract the FFmpeg Forge archive.";
                }
            }
            else if (s.Format.Kind == MediaKind.Image)
            {
                string k = s.Format.VCodec;
                if (k != "png" && k != "avif") return "HDR pictures are written as PNG (16-bit, cICP HDR tag) or AVIF (10-bit): choose one of these formats.";
                if (s.ExtractMode != 0) return "SDR → HDR for pictures exports one frame: set \"From a video\" to a single frame.";
            }
            return null;
        }

        // ------------------------------------------------------------------ Dolby Vision 8.1
        public static string DoviTool
        {
            get
            {
                string app = AppDomain.CurrentDomain.BaseDirectory;
                foreach (var n in new[] { "dovi_tool.exe", "dovi_tool" })
                    foreach (var d in new[] { app, Path.Combine(app, "bin"), Path.Combine(app, "tools") })
                    { string p = Path.Combine(d, n); if (File.Exists(p)) return p; }
                if (!string.IsNullOrEmpty(Runner.FFmpeg))
                    foreach (var n in new[] { "dovi_tool.exe", "dovi_tool" })
                    { string p = Path.Combine(Path.GetDirectoryName(Runner.FFmpeg), n); if (File.Exists(p)) return p; }
                return null;
            }
        }

        static int Exec(string exe, string args, Func<bool> cancelled, out string output, int timeoutMs = 0, string cwd = null)
        {
            var sb = new StringBuilder();
            var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = cwd ?? "", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                var sw = Stopwatch.StartNew();
                while (!p.WaitForExit(200))
                {
                    if (cancelled()) { try { p.Kill(); } catch { } output = "Cancelled"; return -1; }
                    if (timeoutMs > 0 && sw.ElapsedMilliseconds > timeoutMs) { try { p.Kill(); } catch { } output = "timeout"; return -2; }
                }
                p.WaitForExit();
                lock (sb) output = sb.ToString();
                return p.ExitCode;
            }
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }
        static int Pq12(double y10) { return (int)Math.Round(Math.Max(0, Math.Min(1, (y10 - 64) / 876.0)) * 4095); }
        static double Nits(int pq12)
        {
            double e = pq12 / 4095.0, p = Math.Pow(e, 1 / M2);
            return 10000 * Math.Pow(Math.Max(p - C1, 0) / (C2 - C3 * p), 1 / M1);
        }

        /// Turns the HDR10 file written by the export into Dolby Vision profile 8.1 (in place).
        public static bool MakeDolbyVision(string outPath, int peak, Action<string> log, Action<double> progress, Func<bool> cancelled, out string error)
        {
            error = null;
            string dovi = DoviTool;
            if (dovi == null) { error = "dovi_tool.exe not found next to FFmpegForge.exe."; return false; }
            string tmp = Path.Combine(Path.GetTempPath(), "FFmpegForge-DV-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(tmp);
            try
            {
                string stats = Path.Combine(tmp, "stats.txt"), hevc = Path.Combine(tmp, "base.hevc"), inj = Path.Combine(tmp, "dv.hevc"),
                       json = Path.Combine(tmp, "dv.json"), rpu = Path.Combine(tmp, "rpu.bin"), outTmp = Path.Combine(tmp, "dv" + Path.GetExtension(outPath));
                string o;
                // 1. measure every frame of the HDR10 render (PQ luma: min / average / max)
                log("Dolby Vision 8.1: measuring every frame (dynamic L1 metadata)…");
                if (Exec(Runner.FFmpeg, "-hide_banner -nostdin -loglevel error -i " + Q(outPath) + " -map 0:v:0 -vf \"signalstats,metadata=print:file=stats.txt\" -f null -", cancelled, out o, 0, tmp) != 0 || !File.Exists(stats))
                { error = "frame measurement failed: " + Last(o); return false; }
                progress(0.4);
                var frames = new List<double[]>();
                double[] cur = null;
                foreach (var line in File.ReadAllLines(stats))
                {
                    if (line.StartsWith("frame:")) { if (cur != null) frames.Add(cur); cur = new double[3]; continue; }
                    if (cur == null) continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string k = line.Substring(0, eq); double v;
                    if (!double.TryParse(line.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) continue;
                    if (k == "lavfi.signalstats.YMIN") cur[0] = v; else if (k == "lavfi.signalstats.YAVG") cur[1] = v; else if (k == "lavfi.signalstats.YMAX") cur[2] = v;
                }
                if (cur != null) frames.Add(cur);
                if (frames.Count == 0) { error = "no frame measured"; return false; }

                // 2. shots: consecutive frames with similar brightness share one L1 block (stable tone mapping, no pumping)
                var shots = new List<int[]>();       // start, duration, min, max, avg
                int sStart = 0; double sMin = 4095, sMax = 0, sSum = 0; int sN = 0;
                Action close = () => { if (sN > 0) shots.Add(new[] { sStart, sN, (int)sMin, (int)Math.Max(sMax, sSum / sN + 1), (int)Math.Round(sSum / sN) }); };
                for (int i = 0; i < frames.Count; i++)
                {
                    int mn = Pq12(frames[i][0]), av = Pq12(frames[i][1]), mx = Math.Max(Pq12(frames[i][2]), av + 1);
                    if (sN > 0 && (Math.Abs(av - sSum / sN) > 160 || Math.Abs(mx - sMax) > 320 || sN >= 1800))
                    { close(); sStart = i; sMin = 4095; sMax = 0; sSum = 0; sN = 0; }
                    sMin = Math.Min(sMin, mn); sMax = Math.Max(sMax, mx); sSum += av; sN++;
                }
                close();
                int maxPq = shots.Max(x => x[3]);
                int maxCll = (int)Math.Round(Math.Min(10000, Nits(maxPq)));
                int maxFall = (int)Math.Round(Math.Min(maxCll, frames.Max(f => Nits(Pq12(f[1])))));
                var sb = new StringBuilder();
                sb.Append("{\"cm_version\":\"V40\",\"profile\":\"8.1\",\"length\":").Append(frames.Count);
                sb.Append(",\"level6\":{\"max_display_mastering_luminance\":").Append(peak).Append(",\"min_display_mastering_luminance\":1");
                sb.Append(",\"max_content_light_level\":").Append(Math.Max(1, maxCll)).Append(",\"max_frame_average_light_level\":").Append(Math.Max(1, maxFall)).Append('}');
                sb.Append(",\"default_metadata_blocks\":[],\"shots\":[");
                for (int i = 0; i < shots.Count; i++)
                {
                    var x = shots[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"start\":").Append(x[0]).Append(",\"duration\":").Append(x[1]).Append(",\"metadata_blocks\":[{\"Level1\":{\"min_pq\":").Append(x[2])
                      .Append(",\"max_pq\":").Append(Math.Min(4095, x[3])).Append(",\"avg_pq\":").Append(Math.Min(4095, x[4])).Append("}}]}");
                }
                sb.Append("]}");
                File.WriteAllText(json, sb.ToString(), new UTF8Encoding(false));
                log("Dolby Vision 8.1: " + frames.Count + " frames, " + shots.Count + " shot(s), MaxCLL " + maxCll + " nits, MaxFALL " + maxFall + " nits");

                // 3. RPU (dovi_tool generate), 4. base layer in Annex B, 5. RPU injection
                if (Exec(dovi, "generate -j " + Q(json) + " -o " + Q(rpu), cancelled, out o) != 0 || !File.Exists(rpu)) { error = "dovi_tool generate: " + Last(o); return false; }
                progress(0.55);
                if (Exec(Runner.FFmpeg, "-hide_banner -nostdin -loglevel error -y -i " + Q(outPath) + " -map 0:v:0 -c copy -bsf:v hevc_mp4toannexb -f hevc " + Q(hevc), cancelled, out o) != 0) { error = "HEVC extraction: " + Last(o); return false; }
                progress(0.65);
                if (Exec(dovi, "inject-rpu -i " + Q(hevc) + " --rpu-in " + Q(rpu) + " -o " + Q(inj), cancelled, out o) != 0 || !File.Exists(inj)) { error = "dovi_tool inject-rpu: " + Last(o); return false; }
                progress(0.8);

                // 6. remux: DV video + every other stream of the export, Dolby Vision configuration record (dvcC)
                string rate = "25";
                if (Exec(Runner.FFprobe, "-v error -select_streams v:0 -show_entries stream=r_frame_rate -of default=nw=1:nk=1 " + Q(outPath), cancelled, out o) == 0 && o.Trim().Length > 0) rate = o.Trim().Split('\n')[0].Trim();
                string ext = Path.GetExtension(outPath).ToLowerInvariant();
                string vOnly = Path.Combine(tmp, "dv_video.mp4");
                // a) Annex B → MP4 (timestamps from the frame rate, Dolby Vision configuration record from the RPU)
                if (Exec(Runner.FFmpeg, "-hide_banner -nostdin -loglevel error -y -fflags +genpts -framerate " + rate + " -i " + Q(inj) +
                         " -map 0:v:0 -c copy -bsf:v dovi_rpu=compression=none -strict unofficial -tag:v hvc1 " + Q(vOnly), cancelled, out o) != 0 || !File.Exists(vOnly))
                { error = "Dolby Vision remux: " + Last(o); return false; }
                // b) Dolby Vision video + every other stream of the export (audio, subtitles, chapters, metadata)
                string mux = "-hide_banner -nostdin -loglevel error -y -i " + Q(vOnly) + " -i " + Q(outPath) +
                             " -map 0:v:0 -map 1 -map -1:v:0 -map_metadata 1 -map_chapters 1 -c copy -strict unofficial" +
                             (ext == ".mkv" ? "" : " -tag:v hvc1 -movflags +faststart") + " " + Q(outTmp);
                if (Exec(Runner.FFmpeg, mux, cancelled, out o) != 0 || !File.Exists(outTmp)) { error = "Dolby Vision remux: " + Last(o); return false; }
                // check: Dolby Vision configuration record, profile 8, BL compatibility 1
                Exec(Runner.FFprobe, "-v error -select_streams v:0 -show_entries stream_side_data=dv_profile,dv_bl_signal_compatibility_id -of default=nw=1 " + Q(outTmp), cancelled, out o);
                if (!o.Contains("dv_profile=8") || !o.Contains("dv_bl_signal_compatibility_id=1")) { error = "the Dolby Vision configuration record was not written (" + Last(o) + ")"; return false; }
                File.Copy(outTmp, outPath, true);
                progress(1);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        static string Last(string o)
        {
            var l = (o ?? "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("at ") && !Regex.IsMatch(x, @"^\d+: ")).ToList();
            return l.Count == 0 ? "unknown error" : string.Join(" | ", l.Skip(Math.Max(0, l.Count - 2)).ToArray());
        }
    }
}
