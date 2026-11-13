using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FFmpegForge
{
    /// User-visible texts of the compression assistant, in one place (ready for a future translation system).
    public static class AdvisorText
    {
        public const string Title = "Video optimization recommended";
        public const string TitleEfficient = "Already efficiently compressed";
        public const string Message = "This video could be compressed further while keeping excellent visual quality.";
        public const string Apply = "Apply recommended settings";
        public const string Measure = "Measure size and time";
        public const string Measuring = "Measuring…";
        public const string Ignore = "Ignore";
        public const string Preset = "✦  Optimized compression";
        public const string Indicative = "indicative estimate";
        public const string MeasuredNote = "measured on 3 samples";
        public const string NoTime = "measure to estimate the time";
        public const string Suggest = "Suggest compression optimizations automatically";
        public const string NotGuaranteed = "Estimates, not guarantees: the final size depends on the content (constant-quality encoding).";
    }

    /// One recommendation for one source file.
    public class Advice
    {
        public bool Recommend;          // a worthwhile size reduction is expected
        public string Headline, Reason; // why (or why not)
        public string VKey, VLabel;     // video codec key (Formats.VideoCodecs) and readable name
        public string Ext;              // container
        public string ACodec = "copy"; public int AKbps; public string ALabel;
        public bool KeepTracks;         // several audio tracks / subtitles: MKV + "Keep all tracks"
        public int QualityIdx = 1, PresetIdx = 3;
        public long SrcBytes, EstLow, EstHigh;
        public bool Measured;
        public double TimeLow, TimeHigh;  // seconds, 0 = unknown
        public string Alt;              // optional faster / other alternative
        public double SrcBpp, TargetBpp;
        public bool Measuring;
    }

    /// Compression assistant: decides from the probe data whether re-encoding is worth it, which settings to use,
    /// and estimates the result. No fixed numbers: everything comes from the source bitrate, the frame size and rate,
    /// the codec efficiency, local speed history and (on demand) real test encodes of three samples.
    public static class Compression
    {
        // Bits per pixel per frame that x265 "Very high" (CRF 22, preset medium) typically produces on 1080p30 live
        // action, from clean / static content (low) to grainy / high-motion content (high). Other sizes / rates / codecs
        // are derived from it. It only gives the *indicative* range; "Measure" replaces it with real test encodes.
        const double RefBppLow = 0.030, RefBppHigh = 0.15;

        static readonly HashSet<string> Inefficient = new HashSet<string> {
            "mpeg2video", "mpeg1video", "mpeg4", "msmpeg4v3", "msmpeg4v2", "wmv1", "wmv2", "wmv3", "vc1", "mjpeg", "prores", "dnxhd",
            "rawvideo", "ffv1", "huffyuv", "utvideo", "v210", "dvvideo", "cinepak", "flv1", "h263", "theora", "vp8", "qtrle", "png", "magicyuv", "cfhd" };
        static readonly HashSet<string> Modern = new HashSet<string> { "hevc", "av1", "vp9", "vvc" };

        static double CodecFactor(string vkey)
        {
            switch (vkey)
            {
                case "h265": return 1.0;
                case "nvh265": return 1.25;     // GPU encoders need a bit more bitrate for the same quality
                case "av1": return 0.85;
                case "vp9": return 1.05;
                case "h264": return 1.55;
                default: return 1.3;
            }
        }

        public static double Fps(Job j)
        {
            double f = j.FpsNum > 0 && j.FpsDen > 0 ? (double)j.FpsNum / j.FpsDen : 0;
            return f > 1 && f < 300 ? f : 30;
        }

        /// Video bitrate (bits/s): from the stream, else total - audio, else file size / duration - audio.
        public static long VideoBps(Job j)
        {
            if (j.VideoBitrate > 0) return j.VideoBitrate;
            long audio = Math.Max(0, j.AudioBitrate);
            if (j.TotalBitrate > 0) return Math.Max(0, j.TotalBitrate - audio);
            if (j.Size > 0 && j.Duration > 0) return Math.Max(0, (long)(j.Size * 8 / j.Duration) - audio);
            return 0;
        }

        // ---------------- encoder availability ----------------
        static bool? nvenc;
        /// true when hevc_nvenc really works (FFmpeg build + NVIDIA GPU + driver): one 1-frame test, cached.
        public static bool NvencHevc
        {
            get
            {
                if (nvenc.HasValue) return nvenc.Value;
                nvenc = false;
                if (string.IsNullOrEmpty(Runner.FFmpeg) || !FormatCatalog.HasEncoder("hevc_nvenc") || FormatCatalog.Encoders.Count == 0) return false;
                try
                {
                    var psi = new ProcessStartInfo(Runner.FFmpeg, "-hide_banner -nostdin -loglevel error -f lavfi -i color=c=black:s=256x256:r=25 -frames:v 2 -c:v hevc_nvenc -f null -")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                    using (var p = Process.Start(psi))
                    {
                        p.StandardError.ReadToEnd();
                        if (p.WaitForExit(15000)) nvenc = p.ExitCode == 0; else { try { p.Kill(); } catch { } }
                    }
                }
                catch { }
                return nvenc.Value;
            }
        }

        static bool Enc(string name) { return FormatCatalog.Encoders.Count == 0 ? name != "hevc_nvenc" : FormatCatalog.HasEncoder(name); }

        // ---------------- local speed history ----------------
        /// Seconds of encoding per second of video, learned from previous conversions (key = codec|preset|size class).
        public static Dictionary<string, double> History = new Dictionary<string, double>();
        static string SizeClass(int w, int h) { long px = (long)w * h; return px <= 500000 ? "sd" : px <= 1100000 ? "720" : px <= 2400000 ? "1080" : px <= 4500000 ? "1440" : px <= 9000000 ? "2160" : "8k"; }
        public static string HistoryKey(string vkey, int preset, int w, int h) { return vkey + "|" + preset + "|" + SizeClass(w, h); }

        public static void Learn(string vkey, int preset, int w, int h, double mediaSeconds, double wallSeconds)
        {
            if (mediaSeconds < 2 || wallSeconds <= 0 || string.IsNullOrEmpty(vkey)) return;
            string k = HistoryKey(vkey, preset, w, h);
            double r = wallSeconds / mediaSeconds, old;
            History[k] = History.TryGetValue(k, out old) ? old * 0.6 + r * 0.4 : r;
        }

        // ---------------- analysis ----------------
        public static Advice Analyze(Job j)
        {
            var a = new Advice { SrcBytes = j.Size };
            if (!j.Probed || !j.HasVideo || j.Kind != MediaKind.Video) { a.Reason = "Not a video (or the analysis failed)."; return a; }
            if (j.Width < 16 || j.Height < 16 || j.Duration <= 0 || j.Size <= 0) { a.Reason = "The analysis could not read the size, duration or bitrate of this file."; return a; }

            string src = (j.VideoCodec ?? "").ToLowerInvariant();
            double fps = Fps(j), px = (double)j.Width * j.Height, dur = j.Duration;
            long vbps = VideoBps(j);
            a.SrcBpp = vbps > 0 ? vbps / (px * fps) : 0;

            // ---- target settings ----
            string ext = (Path.GetExtension(j.Path) ?? "").TrimStart('.').ToLowerInvariant();
            string outExt = ext == "mkv" ? "mkv" : ext == "webm" ? "webm" : "mp4";
            bool extra = j.AudioStreams > 1 || j.SubtitleStreams > 0;
            if (extra && outExt != "mkv") outExt = "mkv";
            a.Ext = outExt; a.KeepTracks = extra;

            if (outExt == "webm") { a.VKey = Enc("libsvtav1") ? "av1" : "vp9"; }
            else
            {
                bool x265 = Enc("libx265");
                bool gpu = Enc("hevc_nvenc") && NvencHevc;
                a.VKey = x265 ? (gpu && dur > 600 ? "nvh265" : "h265") : (gpu ? "nvh265" : "h264");
                if (a.VKey == "h265" && gpu) a.Alt = "Faster alternative: H.265 NVIDIA NVENC (GPU), about 20–30 % larger files.";
            }
            a.VLabel = a.VKey == "h265" ? "H.265 / HEVC (x265)" : a.VKey == "nvh265" ? "H.265 / HEVC (NVIDIA NVENC)" : a.VKey == "av1" ? "AV1 (SVT-AV1)" : a.VKey == "vp9" ? "VP9" : "H.264 (x264)";

            // ---- audio: keep it when it is already efficient and fits the container ----
            string ac = (j.AudioCodec ?? "").ToLowerInvariant();
            long abps = Math.Max(0, j.AudioBitrate);
            int ch = Math.Max(1, j.AudioChannels);
            long perStream = j.AudioStreams > 0 ? abps / j.AudioStreams : abps;
            bool efficientAudio = (ac == "aac" || ac == "opus" || ac == "mp3" || ac == "vorbis" || ac == "ac3" || ac == "eac3") &&
                                  (perStream == 0 || perStream <= (ch > 2 ? 768000 : 330000));
            bool fits = outExt == "mkv" || (outExt == "mp4" && ac != "vorbis") || (outExt == "webm" && (ac == "opus" || ac == "vorbis"));
            long audioOut;
            if (!j.HasAudio) { a.ACodec = "none"; a.ALabel = "no audio"; audioOut = 0; }
            else if (efficientAudio && fits) { a.ACodec = "copy"; a.ALabel = "kept as is (" + ac.ToUpperInvariant() + ", no re-encoding)"; audioOut = abps > 0 ? abps : 128000L * Math.Max(1, j.AudioStreams); }
            else
            {
                bool opus = outExt != "mp4";
                a.ACodec = opus ? "opus" : "aac";
                a.AKbps = ch > 2 ? (opus ? 320 : 384) : (opus ? 160 : 192);
                a.ALabel = (opus ? "Opus " : "AAC ") + a.AKbps + " kb/s" + (ch > 2 ? " (" + ch + " channels)" : "");
                audioOut = a.AKbps * 1000L * Math.Max(1, j.AudioStreams);
            }

            // ---- indicative size range from the expected density of the target encoder ----
            double sizeF = Math.Pow(px / 2073600.0, -0.20), fpsF = Math.Pow(fps / 30.0, -0.25), cf = CodecFactor(a.VKey);
            double lowBpp = RefBppLow * sizeF * fpsF * cf, highBpp = RefBppHigh * sizeF * fpsF * cf;
            if (j.Is10Bit) { lowBpp *= 1.05; highBpp *= 1.05; }
            a.TargetBpp = highBpp;
            double srcVideoBytes = vbps > 0 ? vbps * dur / 8 : Math.Max(0, j.Size - abps * dur / 8);
            double vLow = Math.Min(srcVideoBytes, lowBpp * px * fps * dur / 8), vHigh = Math.Min(srcVideoBytes, highBpp * px * fps * dur / 8);
            double aBytes = audioOut * dur / 8;
            a.EstLow = (long)(vLow + aBytes); a.EstHigh = (long)Math.Min(j.Size * 0.95, vHigh + aBytes);
            if (a.EstLow > a.EstHigh) a.EstLow = (long)(a.EstHigh * 0.8);

            // ---- time from local history (no history = unknown, never invented) ----
            double r;
            if (History.TryGetValue(HistoryKey(a.VKey, a.PresetIdx, j.Width, j.Height), out r)) { a.TimeLow = r * dur * 0.8; a.TimeHigh = r * dur * 1.25; }

            // ---- decision ----
            if (j.IsHdr) { a.Reason = "HDR video (" + j.ColorTransfer + "): the optimized preset is 8-bit SDR, so it is not suggested for HDR sources."; return a; }
            if (dur < 5) { a.Reason = "Very short video: the possible saving is too small to be worth a re-encode."; return a; }
            if (vbps <= 0) { a.Reason = "The video bitrate could not be determined."; return a; }
            bool modern = Modern.Contains(src), old = Inefficient.Contains(src);
            double expectedHigh = vHigh + aBytes;
            double saving = 1 - expectedHigh / j.Size;          // pessimistic saving (high end of the estimate)
            if (modern && a.SrcBpp < highBpp * 2.0)
            {
                a.Reason = Pretty(src) + " at " + Mbps(vbps) + " is already efficiently compressed: re-encoding would save little and cost some quality.";
                a.Headline = AdvisorText.TitleEfficient; return a;
            }
            if (saving < (old ? 0.25 : 0.33))
            {
                a.Reason = "The video already uses about " + Mbps(vbps) + " (" + a.SrcBpp.ToString("0.000", CultureInfo.InvariantCulture) + " bits per pixel): a re-encode would probably save less than a third of the size.";
                a.Headline = AdvisorText.TitleEfficient; return a;
            }
            a.Recommend = true;
            a.Headline = AdvisorText.Title;
            a.Reason = (old ? Pretty(src) + " (an old or editing codec) at " + Mbps(vbps) : Pretty(src) + " at " + Mbps(vbps)) +
                       " (" + a.SrcBpp.ToString("0.000", CultureInfo.InvariantCulture) + " bits per pixel, a " + a.VLabel.Split(' ')[0] + " encode typically needs " +
                       (lowBpp).ToString("0.000", CultureInfo.InvariantCulture) + "–" + highBpp.ToString("0.000", CultureInfo.InvariantCulture) + ").";
            return a;
        }

        public static string Pretty(string c)
        {
            switch ((c ?? "").ToLowerInvariant())
            {
                case "h264": return "H.264"; case "hevc": return "H.265/HEVC"; case "av1": return "AV1"; case "vp9": return "VP9"; case "vp8": return "VP8";
                case "mpeg2video": return "MPEG-2"; case "mpeg1video": return "MPEG-1"; case "mpeg4": return "MPEG-4 Part 2"; case "mjpeg": return "Motion JPEG";
                case "prores": return "ProRes"; case "dnxhd": return "DNxHD/DNxHR"; case "rawvideo": return "uncompressed video"; case "ffv1": return "FFV1";
                case "wmv1": case "wmv2": case "wmv3": return "WMV"; case "vc1": return "VC-1"; case "dvvideo": return "DV";
                default: return (c ?? "?").ToUpperInvariant();
            }
        }

        /// Uses speed history learned after the analysis (another conversion or a measurement) when the advice has no time yet.
        public static void RefreshTime(Job j, Advice a)
        {
            if (a == null || a.Measured || a.VKey == null || j.Duration <= 0) return;
            double r;
            if (History.TryGetValue(HistoryKey(a.VKey, a.PresetIdx, j.Width, j.Height), out r)) { a.TimeLow = r * j.Duration * 0.8; a.TimeHigh = r * j.Duration * 1.25; }
        }

        public static string Mbps(long bps) { return (bps / 1e6).ToString(bps < 10e6 ? "0.0" : "0", CultureInfo.InvariantCulture) + " Mb/s"; }

        // ---------------- settings that the advice applies ----------------
        public static ConvSettings TargetSettings(Advice a)
        {
            return new ConvSettings { VCodec = a.VKey, QualityIdx = a.QualityIdx, PresetIdx = a.PresetIdx, VBitrate = 0, NvCrf = 0 };
        }

        // ---------------- measurement: real test encodes of 3 samples ----------------
        /// Encodes three short samples (20 %, 50 %, 80 % of the video) with the recommended video settings and
        /// extrapolates the size and the time. Runs on a background thread; returns false on failure.
        public static bool Measure(Job j, Advice a, Func<bool> cancelled, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(Runner.FFmpeg)) { error = "FFmpeg not found."; return false; }
            double dur = j.Duration;
            double seg = dur < 15 ? Math.Max(1, Math.Min(dur, 10)) : 4;
            var starts = dur < 15 ? new[] { 0.0 } : new[] { dur * 0.2, dur * 0.5, dur * 0.8 - seg };
            var s = TargetSettings(a);
            string va = CommandBuilder.VideoArgs(a.VKey, s, a.Ext);
            var rates = new List<double>(); double wall = 0, media = 0;
            string tmp = Path.Combine(Path.GetTempPath(), "FFmpegForge-measure-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".mkv");
            try
            {
                foreach (var st in starts)
                {
                    if (cancelled()) { error = "Cancelled"; return false; }
                    string args = "-hide_banner -nostdin -y -loglevel error -ss " + Formats.Inv(Math.Max(0, st)) + " -i \"" + j.Path + "\" -t " + Formats.Inv(seg) +
                                  " -map 0:v:0 -an -sn -dn " + va + " -f matroska \"" + tmp + "\"";
                    var sw = Stopwatch.StartNew();
                    var psi = new ProcessStartInfo(Runner.FFmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                    string err;
                    using (var p = Process.Start(psi))
                    {
                        try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }
                        var et = p.StandardError.ReadToEndAsync();
                        while (!p.WaitForExit(200)) if (cancelled()) { try { p.Kill(); } catch { } error = "Cancelled"; return false; }
                        err = et.Result;
                        if (p.ExitCode != 0) { error = string.IsNullOrWhiteSpace(err) ? "test encode failed (code " + p.ExitCode + ")" : err.Trim().Split('\n').Last(); return false; }
                    }
                    sw.Stop();
                    long bytes = new FileInfo(tmp).Length;
                    var probe = new Job { Path = tmp }; Runner.Probe(probe);
                    double got = probe.Duration > 0.2 ? Math.Min(seg, probe.Duration) : seg;
                    rates.Add(bytes / got);
                    wall += sw.Elapsed.TotalSeconds; media += got;
                }
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            if (rates.Count == 0) { error = "no sample"; return false; }
            double audio = 0;
            if (a.ACodec == "copy") audio = (j.AudioBitrate > 0 ? j.AudioBitrate : 128000L * Math.Max(1, j.AudioStreams)) / 8.0;
            else if (a.ACodec != "none") audio = a.AKbps * 1000.0 * Math.Max(1, j.AudioStreams) / 8.0;
            a.EstLow = (long)((rates.Min() * 0.9 + audio) * j.Duration);
            a.EstHigh = (long)((rates.Max() * 1.1 + audio) * j.Duration);
            a.Measured = true;
            double r = wall / media;     // includes FFmpeg start-up: slightly pessimistic, never optimistic
            a.TimeLow = r * j.Duration * 0.85; a.TimeHigh = r * j.Duration * 1.2;
            Learn(a.VKey, a.PresetIdx, j.Width, j.Height, media, wall);
            // the measurement can overturn the indicative decision
            if (a.Recommend && a.EstHigh > j.Size * 0.85) { a.Recommend = false; a.Headline = AdvisorText.TitleEfficient; a.Reason = "The test encodes show that this video would shrink by less than 15 %: keeping it as is is better."; }
            return true;
        }

        public static string Size(long b)
        {
            if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
            return Math.Max(1, (long)Math.Round(b / (double)(1 << 20))) + " MB";
        }

        public static string Time(double s)
        {
            if (s < 90) return Math.Max(10, (int)(Math.Round(s / 10) * 10)) + " s";
            if (s < 5400) return (int)Math.Round(s / 60) + " min";
            return (s / 3600).ToString("0.0", CultureInfo.InvariantCulture) + " h";
        }

        /// "200 – 300 MB · 65 – 75 % smaller"
        public static string SizeText(Advice a)
        {
            if (a.EstHigh <= 0) return "";
            int pHigh = (int)Math.Round(100 * (1 - a.EstLow / (double)a.SrcBytes)), pLow = (int)Math.Round(100 * (1 - a.EstHigh / (double)a.SrcBytes));
            return "≈ " + Size(a.EstLow) + " – " + Size(a.EstHigh) + "  ·  about " + Math.Max(0, pLow) + "–" + Math.Max(0, pHigh) + " % smaller";
        }

        public static string TimeText(Advice a)
        {
            if (a.TimeHigh <= 0) return AdvisorText.NoTime;
            string lo = Time(a.TimeLow), hi = Time(a.TimeHigh);
            return "≈ " + (lo == hi ? lo : lo + " – " + hi) + (a.Measured ? "" : " (from your previous conversions)");
        }
    }

    /// Cover / thumbnail added after the conversion, without re-encoding the video:
    ///   MP4 / M4V: JPEG attached picture (cover art: Windows Explorer, iTunes / Apple, VLC, Plex…)
    ///   MKV: cover.jpg attachment (Matroska cover convention; shown by some players, Explorer needs an add-on)
    /// Other containers have no reliable cover support: skipped.
    public static class Cover
    {
        public static bool Supported(string ext) { return ext == "mp4" || ext == "m4v" || ext == "mkv"; }   // MOV (QuickTime mode) silently drops cover art

        static int Run(string args, out string err)
        {
            var psi = new ProcessStartInfo(Runner.FFmpeg, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            using (var p = Process.Start(psi))
            {
                var t = p.StandardError.ReadToEndAsync();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                err = t.Result.Trim();
                if (err.Length > 0) err = err.Split('\n').Last().Trim();
                return p.ExitCode;
            }
        }

        static int CountStreams(string file, string type)
        {
            try
            {
                var psi = new ProcessStartInfo(Runner.FFprobe, "-v error -show_entries stream=codec_type -of csv=p=0 \"" + file + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
                    return o.Split('\n').Count(l => l.Trim().Split(',')[0] == type);   // side data (Dolby Vision, HDR) adds fields after a comma
                }
            }
            catch { return 0; }
        }

        static bool HasCover(string file)
        {
            try
            {
                var psi = new ProcessStartInfo(Runner.FFprobe, "-v error -show_entries stream_disposition=attached_pic -of csv=p=0 \"" + file + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi)) { string o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return o.Split('\n').Any(l => l.Trim().Split(',')[0] == "1"); }
            }
            catch { return true; }      // cannot check: do not block
        }

        public static bool Apply(string output, ConvSettings s, out string error)
        {
            error = null;
            string ext = Path.GetExtension(output).TrimStart('.').ToLowerInvariant();
            if (!Supported(ext)) { error = "unsupported container"; return false; }
            string dir = Path.Combine(Path.GetTempPath(), "FFmpegForge-cover-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            string jpg = Path.Combine(dir, "cover.jpg"), tmp = Path.Combine(dir, "with_cover." + ext);
            try
            {
                string src;
                if (s.CoverMode == 2)
                {
                    if (string.IsNullOrEmpty(s.CoverPath) || !File.Exists(s.CoverPath)) { error = "the cover image was not found"; return false; }
                    src = "-i \"" + s.CoverPath + "\"";
                }
                else src = "-ss " + Formats.Inv(Math.Max(0, s.CoverAt)) + " -i \"" + output + "\"";      // a frame of the converted video
                // JPEG, at most 1920 px wide, full-range colour
                if (Run("-hide_banner -nostdin -y -loglevel error " + src + " -map 0:v:0 -frames:v 1 -vf \"scale='min(1920,iw)':-2:flags=lanczos,format=yuvj420p\" -q:v 2 \"" + jpg + "\"", out error) != 0 || !File.Exists(jpg))
                {
                    if (s.CoverMode == 1 && Run("-hide_banner -nostdin -y -loglevel error -i \"" + output + "\" -map 0:v:0 -frames:v 1 -vf \"scale='min(1920,iw)':-2:flags=lanczos,format=yuvj420p\" -q:v 2 \"" + jpg + "\"", out error) == 0 && File.Exists(jpg)) { }
                    else { error = error ?? "could not make the picture"; return false; }
                }
                string args;
                if (ext == "mkv")
                {
                    int att = CountStreams(output, "attachment");
                    args = "-hide_banner -nostdin -y -loglevel error -i \"" + output + "\" -map 0 -c copy -strict unofficial -attach \"" + jpg + "\" -metadata:s:t:" + att + " mimetype=image/jpeg -metadata:s:t:" + att + " filename=cover.jpg \"" + tmp + "\"";
                }
                else
                {
                    int nv = CountStreams(output, "video");
                    args = "-hide_banner -nostdin -y -loglevel error -i \"" + output + "\" -i \"" + jpg + "\" -map 0 -map 1 -c copy -strict unofficial -disposition:v:" + nv + " attached_pic " +
                           (ext == "mov" ? "" : "-movflags +faststart ") + "\"" + tmp + "\"";
                }
                if (Run(args, out error) != 0 || !File.Exists(tmp) || new FileInfo(tmp).Length < new FileInfo(output).Length / 2) { if (error == null || error.Length == 0) error = "remux failed"; return false; }
                if (!HasCover(tmp)) { error = "the container did not keep the picture"; return false; }
                File.Copy(tmp, output, true);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
