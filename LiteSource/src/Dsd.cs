using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FFmpegForge
{
    /// DSD export (.dsf Sony DSF / .dff Philips DSDIFF). FFmpeg can read DSD but cannot write it, so:
    ///   ffmpeg (trim, channels, volume, filters, oversampling to 2.8224 / 5.6448 MHz, 32-bit float)
    ///   -> 1-bit 7th-order delta-sigma modulator (FFmpeg Forge) -> DSF / DFF writer (FFmpeg Forge)
    /// 0 dBFS PCM is written at 50 % modulation (the SACD reference level).
    public static class DsdEngine
    {
        public static bool IsDsd(string key) { return key != null && (key.StartsWith("dsf") || key.StartsWith("dff")); }
        public static string Ext(string key) { return key.StartsWith("dff") ? "dff" : "dsf"; }
        public static int Rate(string key) { return key.EndsWith("128") ? 5644800 : 2822400; }

        // 7th-order noise-transfer function: zeros at DC, maximally flat high-pass poles, max |NTF| = 1.5 (Lee's rule)
        // In-band (20 kHz) SNR ≈ 114 dB at DSD64 with a full-scale sine.
        static readonly double[] B = { 1.0, -7.0, 21.0, -35.0, 35.0, -21.0, 7.0, -1.0 };
        static readonly double[] A = { 1.0, -6.190634424301064, 16.466669948915044, -24.39229562441837, 21.729169663738936, -11.639290082760443, 3.470829144755111, -0.44444444444444425 };

        // ---------------- channel layouts ----------------
        static Dictionary<string, string[]> ffLayouts;
        /// FFmpeg's standard layouts (name -> channel names), read once from "ffmpeg -layouts".
        static Dictionary<string, string[]> FfLayouts
        {
            get
            {
                if (ffLayouts != null) return ffLayouts;
                var d = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var psi = new ProcessStartInfo(Runner.FFmpeg, "-hide_banner -layouts") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(10000);
                        bool std = false;
                        foreach (var raw in o.Split('\n'))
                        {
                            string l = raw.Trim();
                            if (l.StartsWith("Standard channel layouts")) { std = true; continue; }
                            if (!std || l.Length == 0 || l.StartsWith("NAME")) continue;
                            var parts = l.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length == 2) d[parts[0]] = parts[1].Split('+');
                        }
                    }
                }
                catch { }
                if (d.Count == 0)
                    foreach (var kv in new[] { "mono=FC", "stereo=FL+FR", "2.1=FL+FR+LFE", "3.0=FL+FR+FC", "quad=FL+FR+BL+BR", "5.0=FL+FR+FC+BL+BR", "5.1=FL+FR+FC+LFE+BL+BR",
                                               "6.1=FL+FR+FC+LFE+BC+SL+SR", "7.1=FL+FR+FC+LFE+BL+BR+SL+SR", "5.1.2=FL+FR+FC+LFE+BL+BR+TFL+TFR", "5.1.4=FL+FR+FC+LFE+BL+BR+TFL+TFR+TBL+TBR",
                                               "7.1.2=FL+FR+FC+LFE+BL+BR+SL+SR+TFL+TFR", "7.1.4=FL+FR+FC+LFE+BL+BR+SL+SR+TFL+TFR+TBL+TBR",
                                               "22.2=FL+FR+FC+LFE+BL+BR+FLC+FRC+BC+SL+SR+TC+TFL+TFC+TFR+TBL+TBC+TBR+LFE2+TSL+TSR+BFC+BFL+BFR" })
                    { var x = kv.Split('='); d[x[0]] = x[1].Split('+'); }
                ffLayouts = d;
                return d;
            }
        }

        /// Channel names of the DSD output and the aformat layout to force (null = keep what the chain makes).
        static string[] ChannelNames(Job job, ConvSettings s, out string aformat)
        {
            aformat = null;
            string[] names;
            if (s.ChLayout == "binaural") { aformat = "stereo"; return new[] { "FL", "FR" }; }
            var cl = string.IsNullOrEmpty(s.ChLayout) || s.ChLayout == "copy" ? null : ChannelLayouts.Get(s.ChLayout);
            if (cl != null && cl.Count > 0)
            {
                if (cl.Std != null && FfLayouts.TryGetValue(cl.Std, out names)) { aformat = cl.Std; return names; }
                if (cl.Custom != null && cl.Custom.StartsWith("pan="))
                {
                    string head = cl.Custom.Substring(4); int bar = head.IndexOf('|'); if (bar > 0) head = head.Substring(0, bar);
                    return head.Split('+');
                }
                return Enumerable.Range(0, cl.Count).Select(i => "C" + i.ToString("000")).ToArray();
            }
            // "Original": the source layout
            int n = job.AudioChannels > 0 ? job.AudioChannels : 2;
            string sl = job.AudioLayout;
            if (!string.IsNullOrEmpty(sl) && FfLayouts.TryGetValue(sl, out names) && names.Length == n) { aformat = sl; return names; }
            if (n == 1) { aformat = "mono"; return new[] { "FC" }; }
            if (n == 2) { aformat = "stereo"; return new[] { "FL", "FR" }; }
            return Enumerable.Range(0, n).Select(i => "C" + i.ToString("000")).ToArray();
        }

        /// DSF channel type (1..7) for these channels, 0 when the Sony DSF format cannot hold them.
        static int DsfType(string[] n)
        {
            string j = string.Join("+", n).Replace("SL", "BL").Replace("SR", "BR");
            switch (j)
            {
                case "FC": return 1;
                case "FL+FR": return 2;
                case "FL+FR+FC": return 3;
                case "FL+FR+BL+BR": return 4;
                case "FL+FR+FC+LFE": return 5;
                case "FL+FR+FC+BL+BR": return 6;
                case "FL+FR+FC+LFE+BL+BR": return 7;
            }
            return 0;
        }

        /// DSDIFF channel IDs: the standard ones where they exist, "Cnnn" (channel number) for the others.
        static string[] DffIds(string[] n)
        {
            var ids = new string[n.Length];
            var used = new HashSet<string>();
            bool st = n.Length == 2;
            for (int i = 0; i < n.Length; i++)
            {
                string id = null;
                switch (n[i])
                {
                    case "FL": id = st ? "SLFT" : "MLFT"; break;
                    case "FR": id = st ? "SRGT" : "MRGT"; break;
                    case "FC": id = "C   "; break;
                    case "LFE": id = "LFE "; break;
                    case "BL": case "SL": id = "LS  "; break;
                    case "BR": case "SR": id = "RS  "; break;
                }
                if (id == null || used.Contains(id)) id = "C" + i.ToString("000");
                used.Add(id); ids[i] = id;
            }
            return ids;
        }

        /// DSF holds up to 5.1 (its norm): other layouts are written as DFF (DSDIFF), which holds any number of channels.
        public static string ResolveKey(Job job, ConvSettings s, string key)
        {
            if (!key.StartsWith("dsf")) return key;
            string af;
            return DsfType(ChannelNames(job, s, out af)) == 0 ? "dff" + key.Substring(3) : key;
        }

        public class Plan
        {
            public string DecArgs, Output, Key;
            public int Rate, Channels, DsfType;
            public string[] DffIds, Names;
            public string Requested;
            public double Expected;
            public bool Dff;
            public string Text() { return DecArgs + "  |  FFmpeg Forge DSD modulator → " + (Dff ? "DFF" : "DSF") + " (" + (Rate == 5644800 ? "DSD128" : "DSD64") + ")"; }
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        static bool? soxr;
        static bool HasSoxr
        {
            get
            {
                if (soxr != null) return soxr.Value;
                soxr = false;
                try
                {
                    var psi = new ProcessStartInfo(Runner.FFmpeg, "-hide_banner -buildconf") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var p = Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                        p.WaitForExit(10000);
                        soxr = o.Contains("libsoxr");
                    }
                }
                catch { }
                return soxr.Value;
            }
        }

        public static Plan Build(Job job, ConvSettings s, string key, string outPath, out string error)
        {
            error = null;
            if (job.Probed && !job.HasAudio) { error = "This file has no audio track to convert to DSD."; return null; }
            string requested = key;
            key = ResolveKey(job, s, key);
            int rate = Rate(key);
            string aformat;
            var names = ChannelNames(job, s, out aformat);
            int ch = names.Length;
            if (ch > 64) { error = "Too many channels for DSD (" + ch + ")."; return null; }

            double ss = s.TrimStart > 0 ? s.TrimStart : 0, to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            dur = Math.Max(0, dur - ss);

            s.SourceChannels = job.AudioChannels; s.SourceIsVideo = job.Kind == MediaKind.Video; s.SourceDuration = dur;
            s.SourceDsd = job.IsDsdAudio; s.DsdTargetRate = 176400;
            var af = new List<string>();
            string chain = CommandBuilder.AudioChain(s, s.UserAudioChain());
            if (chain != null) af.Add(chain);
            if (aformat != null) af.Add("aformat=channel_layouts=" + aformat);
            else if (ch > 2)   // custom channel orders (7.1.6, 9.1.x…): FFmpeg's resampler needs a plain N-channel layout
                af.Add("channelmap=map=" + string.Join("|", Enumerable.Range(0, ch).Select(i => i.ToString()).ToArray()) + ":channel_layout=" + ch + " channels");
            af.Add(HasSoxr ? "aresample=" + rate + ":resampler=soxr:precision=28" : "aresample=" + rate + ":filter_size=64:phase_shift=12");
            af.Add("aformat=sample_fmts=flt");

            return new Plan
            {
                Key = key, Rate = rate, Channels = ch, Output = outPath, Dff = key.StartsWith("dff"), Expected = dur,
                DsfType = DsfType(names), DffIds = DffIds(names), Names = names, Requested = requested,
                DecArgs = "-hide_banner -nostdin -loglevel error " + trim + "-i " + Q(job.Path) + " -map 0:a:0 -vn -sn -dn -af " + Q(string.Join(",", af.ToArray())) +
                          " -ac " + ch + " -ar " + rate + " -c:a pcm_f32le -f f32le -"
            };
        }

        // ---------------- modulator ----------------
        class Modulator
        {
            double e1, e2, e3, e4, e5, e6, e7, f1, f2, f3, f4, f5, f6, f7;
            readonly double c1 = B[1] - A[1], c2 = B[2] - A[2], c3 = B[3] - A[3], c4 = B[4] - A[4], c5 = B[5] - A[5], c6 = B[6] - A[6], c7 = B[7] - A[7];
            readonly double a1 = A[1], a2 = A[2], a3 = A[3], a4 = A[4], a5 = A[5], a6 = A[6], a7 = A[7];
            public int Bits, Acc;            // bits already in Acc (0..7)
            public long Resets;

            /// Returns true for a "1" bit.
            public bool Next(double x)
            {
                x *= 0.5;                                        // 0 dBFS = 50 % modulation
                if (x > 0.5) x = 0.5; else if (x < -0.5) x = -0.5;
                double f = c1 * e1 + c2 * e2 + c3 * e3 + c4 * e4 + c5 * e5 + c6 * e6 + c7 * e7
                         - (a1 * f1 + a2 * f2 + a3 * f3 + a4 * f4 + a5 * f5 + a6 * f6 + a7 * f7);
                if (f > 4 || f < -4 || double.IsNaN(f))            // safety: never let the loop run away
                {
                    e1 = e2 = e3 = e4 = e5 = e6 = e7 = f1 = f2 = f3 = f4 = f5 = f6 = f7 = 0; f = 0; Resets++;
                }
                double v = x + f;
                double q = v >= 0 ? 1.0 : -1.0;
                e7 = e6; e6 = e5; e5 = e4; e4 = e3; e3 = e2; e2 = e1; e1 = q - v;
                f7 = f6; f6 = f5; f5 = f4; f4 = f3; f3 = f2; f2 = f1; f1 = f;
                return q > 0;
            }
        }

        // ---------------- writers ----------------
        static void W32(Stream s, uint v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }
        static void W64(Stream s, ulong v) { W32(s, (uint)v); W32(s, (uint)(v >> 32)); }
        static void B16(Stream s, ushort v) { s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void B32(Stream s, uint v) { s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v); }
        static void B64(Stream s, ulong v) { B32(s, (uint)(v >> 32)); B32(s, (uint)v); }
        static void Id(Stream s, string id) { var b = Encoding.ASCII.GetBytes(id); s.Write(b, 0, 4); }

        const int DsfBlock = 4096;

        /// Runs ffmpeg + modulator + writer. Returns 0 on success, -1 when cancelled.
        public static int Run(Plan plan, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            var errs = new List<string>();
            string tmp = plan.Output + ".part";
            Process p = null;
            try
            {
                try { string od = Path.GetDirectoryName(plan.Output); if (!string.IsNullOrEmpty(od)) Directory.CreateDirectory(od); } catch { }
                var psi = new ProcessStartInfo(Runner.FFmpeg, plan.DecArgs) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8 };
                p = Process.Start(psi);
                p.ErrorDataReceived += (o, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) { lock (errs) { errs.Add(e.Data); if (errs.Count > 30) errs.RemoveAt(0); } onLog(e.Data); } };
                p.BeginErrorReadLine();

                int ch = plan.Channels;
                var mods = Enumerable.Range(0, ch).Select(i => new Modulator()).ToArray();
                long samples = 0;                                 // per channel
                double expected = plan.Expected > 0 ? plan.Expected * plan.Rate : 0;

                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.Read, 1 << 20))
                {
                    long dataStart = WriteHeader(fs, plan, 0);
                    var dsfBlocks = plan.Dff ? null : Enumerable.Range(0, ch).Select(i => new byte[DsfBlock]).ToArray();
                    int dsfFill = 0;                              // bytes in each channel block
                    const int Frames = 1 << 16;
                    var raw = new byte[Frames * ch * 4];
                    var flt = new float[Frames * ch];
                    var outBytes = Enumerable.Range(0, ch).Select(i => new byte[Frames / 8 + 2]).ToArray();
                    var outCount = new int[ch];
                    var dffBuf = new byte[(Frames / 8 + 2) * ch];
                    var src = p.StandardOutput.BaseStream;
                    int have = 0;
                    var sw = Stopwatch.StartNew(); long lastP = 0;
                    while (true)
                    {
                        if (cancelled()) { try { p.Kill(); } catch { } lastErrors = "Cancelled"; fs.Close(); try { File.Delete(tmp); } catch { } return -1; }
                        int n = src.Read(raw, have, raw.Length - have);
                        bool eof = n <= 0;
                        if (!eof) have += n;
                        int frames = have / (4 * ch);
                        if (!eof && have < raw.Length) continue;                 // fill the chunk
                        if (frames > 0)
                        {
                            Buffer.BlockCopy(raw, 0, flt, 0, frames * ch * 4);
                            int fr = frames;
                            Parallel.For(0, ch, c =>
                            {
                                var m = mods[c]; var ob = outBytes[c]; int oc = 0;
                                for (int i = 0; i < fr; i++)
                                {
                                    bool one = m.Next(flt[i * ch + c]);
                                    if (plan.Dff) { m.Acc = (m.Acc << 1) | (one ? 1 : 0); }              // MSB first
                                    else { if (one) m.Acc |= 1 << m.Bits; }                               // LSB first
                                    if (++m.Bits == 8) { ob[oc++] = (byte)m.Acc; m.Acc = 0; m.Bits = 0; }
                                }
                                outCount[c] = oc;
                            });
                            samples += frames;
                            int nb = outCount[0];
                            if (plan.Dff)
                            {
                                for (int i = 0; i < nb; i++) for (int c = 0; c < ch; c++) dffBuf[i * ch + c] = outBytes[c][i];
                                fs.Write(dffBuf, 0, nb * ch);
                            }
                            else
                            {
                                int pos = 0;
                                while (pos < nb)
                                {
                                    int take = Math.Min(DsfBlock - dsfFill, nb - pos);
                                    for (int c = 0; c < ch; c++) Buffer.BlockCopy(outBytes[c], pos, dsfBlocks[c], dsfFill, take);
                                    dsfFill += take; pos += take;
                                    if (dsfFill == DsfBlock) { for (int c = 0; c < ch; c++) fs.Write(dsfBlocks[c], 0, DsfBlock); dsfFill = 0; }
                                }
                            }
                            int used = frames * ch * 4;
                            Buffer.BlockCopy(raw, used, raw, 0, have - used);
                            have -= used;
                            if (expected > 0 && sw.ElapsedMilliseconds - lastP > 150) { lastP = sw.ElapsedMilliseconds; onProgress(Math.Min(0.999, samples / expected)); }
                        }
                        if (eof) break;
                    }
                    // last partial byte (padded with silence-like alternating bits) and last block
                    bool partial = mods[0].Bits > 0;
                    if (partial)
                    {
                        for (int c = 0; c < ch; c++)
                        {
                            var m = mods[c];
                            while (m.Bits < 8) { bool one = (m.Bits & 1) == 0; if (plan.Dff) m.Acc = (m.Acc << 1) | (one ? 1 : 0); else if (one) m.Acc |= 1 << m.Bits; m.Bits++; }
                            outBytes[c][0] = (byte)m.Acc;
                        }
                        if (plan.Dff) { for (int c = 0; c < ch; c++) fs.WriteByte(outBytes[c][0]); }
                        else { for (int c = 0; c < ch; c++) dsfBlocks[c][dsfFill] = outBytes[c][0]; dsfFill++; }
                    }
                    if (!plan.Dff && dsfFill > 0)
                    {
                        for (int c = 0; c < ch; c++) { Array.Clear(dsfBlocks[c], dsfFill, DsfBlock - dsfFill); fs.Write(dsfBlocks[c], 0, DsfBlock); }
                    }
                    long dataBytes = fs.Position - dataStart;
                    if (plan.Dff && (dataBytes & 1) == 1) fs.WriteByte(0);        // DSDIFF chunks are padded to an even size
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        lock (errs) lastErrors = string.Join(Environment.NewLine, errs.ToArray());
                        fs.Close(); try { File.Delete(tmp); } catch { }
                        return p.ExitCode;
                    }
                    if (samples == 0) { lastErrors = "FFmpeg returned no audio."; fs.Close(); try { File.Delete(tmp); } catch { } return 3; }
                    fs.Position = 0;
                    WriteHeader(fs, plan, samples, dataBytes, fs.Length);
                    long resets = mods.Sum(m => m.Resets);
                    if (resets > 0) onLog("DSD modulator: " + resets + " overload reset(s) — lower the volume if you hear clicks.");
                }
                if (File.Exists(plan.Output)) File.Delete(plan.Output);
                File.Move(tmp, plan.Output);
                onProgress(1.0);
                lastErrors = "";
                return 0;
            }
            catch (Exception ex)
            {
                try { if (p != null && !p.HasExited) p.Kill(); } catch { }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                lock (errs) lastErrors = ex.Message + (errs.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, errs.ToArray()) : "");
                return -2;
            }
        }

        static long WriteHeader(Stream fs, Plan plan, long samples, long dataBytes = 0, long fileSize = 0)
        {
            int ch = plan.Channels;
            if (!plan.Dff)
            {
                // ---- DSF (Sony) ----
                Id(fs, "DSD "); W64(fs, 28); W64(fs, (ulong)fileSize); W64(fs, 0);
                Id(fs, "fmt "); W64(fs, 52);
                W32(fs, 1);                         // format version
                W32(fs, 0);                         // DSD raw
                W32(fs, (uint)plan.DsfType);
                W32(fs, (uint)ch);
                W32(fs, (uint)plan.Rate);
                W32(fs, 1);                         // bits per sample: 1 = LSB first
                W64(fs, (ulong)samples);
                W32(fs, DsfBlock);
                W32(fs, 0);
                Id(fs, "data"); W64(fs, (ulong)(12 + dataBytes));
                return fs.Position;
            }
            // ---- DSDIFF 1.5 (Philips) ----
            long padded = dataBytes + (dataBytes & 1);
            const string cmprName = "not compressed";
            long cmprSize = 4 + 1 + cmprName.Length;              // 19
            long propSize = 4 + (12 + 4) + (12 + 2 + 4 * ch) + (12 + cmprSize + (cmprSize & 1));
            long frmSize = 4 + (12 + 4) + (12 + propSize) + (12 + padded);
            Id(fs, "FRM8"); B64(fs, (ulong)frmSize); Id(fs, "DSD ");
            Id(fs, "FVER"); B64(fs, 4); B32(fs, 0x01050000);
            Id(fs, "PROP"); B64(fs, (ulong)propSize); Id(fs, "SND ");
            Id(fs, "FS  "); B64(fs, 4); B32(fs, (uint)plan.Rate);
            Id(fs, "CHNL"); B64(fs, (ulong)(2 + 4 * ch)); B16(fs, (ushort)ch); foreach (var idc in plan.DffIds) Id(fs, idc);
            Id(fs, "CMPR"); B64(fs, (ulong)cmprSize); Id(fs, "DSD "); fs.WriteByte((byte)cmprName.Length);
            var nb = Encoding.ASCII.GetBytes(cmprName); fs.Write(nb, 0, nb.Length);
            if ((cmprSize & 1) == 1) fs.WriteByte(0);
            Id(fs, "DSD "); B64(fs, (ulong)dataBytes);
            return fs.Position;
        }
    }
}
