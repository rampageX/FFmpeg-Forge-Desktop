using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FFmpegForge
{
    // =====================================================================================================
    //  2.3.0 — DVD / Blu-ray ripping with the bundled FFmpeg:
    //   DVD-Video : FFmpeg's dvdvideo demuxer (libdvdnav / libdvdread) — titles, chapters, audio languages,
    //               DVD subtitles (with palette), angles. Disc drive, VIDEO_TS folder or .iso image.
    //   Blu-ray   : FFmpeg's bluray protocol (libbluray) — playlists (.mpls). Durations, chapters and
    //               audio / subtitle languages are read from the playlist file itself (MPLS STN table).
    //               Playlists that libbluray will not open (no 3-minute title on the disc) are read straight
    //               from their .m2ts clips.
    //   Copy-protected discs (CSS / AACS / BD+) are NOT supported: FFmpeg Forge does not circumvent copy
    //   protection. They are detected and reported.
    // =====================================================================================================

    public class DiscStream
    {
        public int Index;                 // FFmpeg stream index in the input
        public string Type;               // video / audio / subtitle
        public string Codec = "";
        public string Lang = "";          // ISO 639-2 (fre, eng…) or ""
        public int Channels;
        public string Layout = "";
        public int Width, Height;
        public string Pid = "";           // "0x1100" (Blu-ray / MPEG-PS id)
        public string FieldOrder = "";
        public bool Selected = true;

        public string Describe()
        {
            string l = string.IsNullOrEmpty(Lang) ? "" : LangName(Lang) + " · ";
            if (Type == "audio")
                return l + CodecName(Codec) + " " + (Channels == 6 ? "5.1" : Channels == 8 ? "7.1" : Channels == 2 ? "stereo" : Channels == 1 ? "mono" : Channels + " ch");
            if (Type == "subtitle") return l + (Codec == "dvd_subtitle" ? "DVD subtitles" : Codec == "hdmv_pgs_subtitle" ? "PGS subtitles" : Codec);
            return CodecName(Codec) + " " + Width + "×" + Height;
        }

        public static string CodecName(string c)
        {
            switch (c)
            {
                case "ac3": return "Dolby Digital";
                case "eac3": return "Dolby Digital Plus";
                case "truehd": return "Dolby TrueHD";
                case "dts": return "DTS";
                case "pcm_dvd": case "pcm_bluray": return "LPCM";
                case "mp2": return "MPEG audio";
                case "mpeg2video": return "MPEG-2";
                case "h264": return "H.264";
                case "hevc": return "H.265";
                case "vc1": return "VC-1";
                default: return c;
            }
        }

        static readonly Dictionary<string, string> Langs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            {"fre","Français"},{"fra","Français"},{"fr","Français"},{"eng","English"},{"en","English"},{"ger","Deutsch"},{"deu","Deutsch"},{"de","Deutsch"},
            {"spa","Español"},{"es","Español"},{"ita","Italiano"},{"it","Italiano"},{"por","Português"},{"pt","Português"},{"dut","Nederlands"},{"nld","Nederlands"},
            {"jpn","日本語"},{"ja","日本語"},{"chi","中文"},{"zho","中文"},{"kor","한국어"},{"rus","Русский"},{"pol","Polski"},{"swe","Svenska"},{"dan","Dansk"},
            {"nor","Norsk"},{"fin","Suomi"},{"tur","Türkçe"},{"ara","العربية"},{"heb","עברית"},{"cze","Čeština"},{"ces","Čeština"},{"hun","Magyar"},{"gre","Ελληνικά"},{"ell","Ελληνικά"} };
        public static string LangName(string l) { string n; return Langs.TryGetValue(l, out n) ? n : l; }
    }

    public class DiscTitle
    {
        public int Number;                // DVD title number (1…99) or Blu-ray playlist number (00800 → 800)
        public double Duration;
        public int Chapters;
        public List<double> ChapterTimes = new List<double>();    // Blu-ray (from the playlist marks)
        public List<string> Clips = new List<string>();           // Blu-ray: .m2ts files of the playlist
        public bool Direct;               // Blu-ray: read the clips directly (libbluray would not open this playlist)
        public bool MultiAngle;
        public List<DiscStream> Streams = new List<DiscStream>();
        public bool Selected;
        public string Error;              // could not be probed
        public string Name(bool bluray) { return bluray ? "Playlist " + Number.ToString("00000") : "Title " + Number.ToString("00"); }
        public IEnumerable<DiscStream> Video { get { return Streams.Where(s => s.Type == "video"); } }
        public IEnumerable<DiscStream> Audio { get { return Streams.Where(s => s.Type == "audio"); } }
        public IEnumerable<DiscStream> Subs { get { return Streams.Where(s => s.Type == "subtitle"); } }
    }

    public class DiscSource
    {
        public string Path;               // drive root (E:\), folder that holds VIDEO_TS / BDMV, or .iso
        public bool IsDvd, IsBluray, IsIso;
        public string Label = "Disc";
        public bool Protected;            // AACS / BD+ folder found (Blu-ray) or unreadable DVD video (CSS)
        public string ProtectionNote;
        public bool HasLongPlaylist;      // libbluray opens playlists only when the disc has one of 3 minutes or more
        public List<DiscTitle> Titles = new List<DiscTitle>();
        public string Kind { get { return IsBluray ? "Blu-ray" : "DVD"; } }

        /// Path as FFmpeg should receive it: no trailing backslash (it would escape the closing quote).
        public string FfPath
        {
            get
            {
                string p = Path;
                if (p.EndsWith("\\") || p.EndsWith("/")) p = p.TrimEnd('\\', '/') + "/";
                return p;
            }
        }
    }

    /// Minimal Blu-ray playlist (.mpls) reader: play items (clips, IN/OUT), languages of the STN table, chapter marks.
    public class MplsInfo
    {
        public double Duration;
        public List<string> Clips = new List<string>();
        public List<double> Chapters = new List<double>();
        public Dictionary<int, string> PidLang = new Dictionary<int, string>();
        public bool MultiAngle;

        static int U16(byte[] b, int o) { return (b[o] << 8) | b[o + 1]; }
        static long U32(byte[] b, int o) { return ((long)b[o] << 24) | ((long)b[o + 1] << 16) | ((long)b[o + 2] << 8) | b[o + 3]; }

        public static MplsInfo Read(string file)
        {
            try
            {
                var b = File.ReadAllBytes(file);
                if (b.Length < 40 || Encoding.ASCII.GetString(b, 0, 4) != "MPLS") return null;
                var m = new MplsInfo();
                int plStart = (int)U32(b, 8), markStart = (int)U32(b, 12);
                int nItems = U16(b, plStart + 6);
                int o = plStart + 10;
                var ins = new List<long>(); var durs = new List<long>();
                for (int i = 0; i < nItems; i++)
                {
                    int len = U16(b, o);
                    int p = o + 2;
                    string clip = Encoding.ASCII.GetString(b, p, 5);
                    int flags = U16(b, p + 9);
                    bool multi = (flags & 0x10) != 0;
                    long tin = U32(b, p + 12), tout = U32(b, p + 16);
                    m.Clips.Add(clip);
                    ins.Add(tin); durs.Add(Math.Max(0, tout - tin));
                    if (multi) m.MultiAngle = true;
                    if (i == 0)
                    {
                        // STN table: after the fixed part (34 bytes) and the angle list
                        int q = p + 32;
                        if (multi) { int na = b[q]; q += 2 + (na - 1) * 10; }
                        int stnLen = U16(b, q);
                        if (stnLen > 0)
                        {
                            int nv = b[q + 4], na = b[q + 5], npg = b[q + 6], nig = b[q + 7], nsa = b[q + 8], nsv = b[q + 9];
                            int r = q + 16;
                            int total = nv + na + npg + nig + nsa + nsv;
                            for (int k = 0; k < total && r < b.Length - 4; k++)
                            {
                                int elen = b[r];
                                int etype = b[r + 1];
                                int pid = etype == 1 ? U16(b, r + 2) : etype == 2 ? U16(b, r + 4) : etype == 3 ? U16(b, r + 3) : U16(b, r + 4);
                                r += 1 + elen;
                                int alen = b[r];
                                int coding = b[r + 1];
                                string lang = null;
                                if ((coding >= 0x80 && coding <= 0x86) || coding == 0xA1 || coding == 0xA2) lang = Encoding.ASCII.GetString(b, r + 3, 3);
                                else if (coding == 0x90 || coding == 0x91) lang = Encoding.ASCII.GetString(b, r + 2, 3);
                                else if (coding == 0x92) lang = Encoding.ASCII.GetString(b, r + 3, 3);
                                if (!string.IsNullOrEmpty(lang) && lang.All(char.IsLetter) && !m.PidLang.ContainsKey(pid)) m.PidLang[pid] = lang.ToLowerInvariant();
                                r += 1 + alen;
                            }
                        }
                    }
                    o += 2 + len;
                }
                long sum = durs.Sum();
                m.Duration = sum / 45000.0;
                // chapter marks (entry marks)
                if (markStart > 0 && markStart + 6 < b.Length)
                {
                    int nm = U16(b, markStart + 4);
                    var pre = new List<long>(); long acc = 0;
                    foreach (var d in durs) { pre.Add(acc); acc += d; }
                    for (int i = 0; i < nm; i++)
                    {
                        int mo = markStart + 6 + i * 14;
                        if (mo + 14 > b.Length) break;
                        if (b[mo + 1] != 1) continue;
                        int item = U16(b, mo + 2);
                        if (item >= durs.Count) continue;
                        long ts = U32(b, mo + 4);
                        double t = (pre[item] + Math.Max(0, ts - ins[item])) / 45000.0;
                        if (t < m.Duration - 1 && (m.Chapters.Count == 0 || t - m.Chapters[m.Chapters.Count - 1] > 0.5)) m.Chapters.Add(t);
                    }
                }
                return m;
            }
            catch { return null; }
        }
    }

    public static class DiscScanner
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string FFprobePath
        {
            get
            {
                if (!string.IsNullOrEmpty(Runner.FFprobe) && File.Exists(Runner.FFprobe)) return Runner.FFprobe;
                try
                {
                    string d = Path.GetDirectoryName(Runner.FFmpeg);
                    string p = Path.Combine(d ?? "", Path.DirectorySeparatorChar == '\\' ? "ffprobe.exe" : "ffprobe");
                    if (File.Exists(p)) return p;
                }
                catch { }
                return "ffprobe";
            }
        }

        public static string Capture(string exe, string args, int timeoutMs, Func<bool> cancelled)
        {
            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                var outp = p.StandardOutput.ReadToEndAsync();
                var sw = Stopwatch.StartNew();
                while (!p.WaitForExit(150))
                {
                    if ((cancelled != null && cancelled()) || sw.ElapsedMilliseconds > timeoutMs) { try { p.Kill(); } catch { } break; }
                }
                try { p.WaitForExit(3000); } catch { }
                return outp.Result + "\n" + err.Result;
            }
        }

        /// Lines FFmpeg / libdvdread print on every DVD opened without libdvdcss (folder or unencrypted disc): harmless.
        public static bool Noise(string l)
        {
            if (string.IsNullOrEmpty(l)) return true;
            return l.Contains("libdvdread:") || l.Contains("libdvdnav:") || l.Contains("CSS authentication not available") ||
                   l.Contains("Referenced QT chapter track not found") || l.StartsWith("Svt[info]") || l.Contains("no timestamp for SPN") ||
                   l.Contains("codec frame size is not set");
        }

        // ------------------------------------------------------------------ source detection
        public static DiscSource Detect(string path, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(path)) { error = "Choose a disc drive, a folder or an ISO image."; return null; }
            var s = new DiscSource { Path = path };
            try
            {
                if (File.Exists(path) && path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                {
                    s.IsIso = true;
                    s.Label = Path.GetFileNameWithoutExtension(path);
                    return s;     // DVD or Blu-ray is found out by probing
                }
                if (!Directory.Exists(path)) { error = "Not found: " + path + (path.Length <= 3 ? " (no disc in the drive?)" : ""); return null; }
                string root = path;
                // the user may pick VIDEO_TS / BDMV themselves
                string leaf = Path.GetFileName(path.TrimEnd('\\', '/'));
                if (leaf.Equals("VIDEO_TS", StringComparison.OrdinalIgnoreCase) || leaf.Equals("BDMV", StringComparison.OrdinalIgnoreCase))
                    root = Path.GetDirectoryName(path.TrimEnd('\\', '/'));
                s.Path = root;
                if (Directory.Exists(Path.Combine(root, "BDMV")) && Directory.Exists(Path.Combine(root, "BDMV", "PLAYLIST"))) s.IsBluray = true;
                else if (Directory.Exists(Path.Combine(root, "VIDEO_TS"))) s.IsDvd = true;
                else { error = "No DVD-Video (VIDEO_TS) or Blu-ray (BDMV) structure found in " + root + "."; return null; }
                s.Label = VolumeLabel(root);
                if (s.IsBluray)
                {
                    if (Directory.Exists(Path.Combine(root, "AACS")) || Directory.Exists(Path.Combine(root, "BDSVM")) || Directory.Exists(Path.Combine(root, "BDMV", "BDSVM")))
                    {
                        s.Protected = true;
                        s.ProtectionNote = "This Blu-ray is copy-protected (AACS" + (Directory.Exists(Path.Combine(root, "BDSVM")) ? " / BD+" : "") + "). " +
                                           "FFmpeg Forge only rips discs without copy protection (your own videos, home-made or unprotected discs).";
                    }
                }
                return s;
            }
            catch (Exception ex) { error = ex.Message; return null; }
        }

        static string VolumeLabel(string root)
        {
            try
            {
                if (root.Length <= 3)
                {
                    var di = new DriveInfo(root.Substring(0, 1));
                    if (di.IsReady && !string.IsNullOrWhiteSpace(di.VolumeLabel)) return di.VolumeLabel.Trim();
                }
            }
            catch { }
            string n = Path.GetFileName(root.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(n) ? "Disc" : n;
        }

        // ------------------------------------------------------------------ ffprobe parsing
        static Dictionary<string, string> Fields(string line)
        {
            var d = new Dictionary<string, string>();
            foreach (var part in line.Split('|'))
            {
                int i = part.IndexOf('=');
                if (i > 0) d[part.Substring(0, i)] = part.Substring(i + 1);
            }
            return d;
        }

        static void ParseProbe(string o, DiscTitle t)
        {
            foreach (var raw in o.Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("stream|"))
                {
                    var f = Fields(l);
                    string type; f.TryGetValue("codec_type", out type);
                    if (type != "video" && type != "audio" && type != "subtitle") continue;
                    var st = new DiscStream { Type = type };
                    string v;
                    if (f.TryGetValue("index", out v)) int.TryParse(v, out st.Index);
                    if (f.TryGetValue("codec_name", out v)) st.Codec = v;
                    if (f.TryGetValue("channels", out v)) int.TryParse(v, out st.Channels);
                    if (f.TryGetValue("channel_layout", out v)) st.Layout = v;
                    if (f.TryGetValue("width", out v)) int.TryParse(v, out st.Width);
                    if (f.TryGetValue("height", out v)) int.TryParse(v, out st.Height);
                    if (f.TryGetValue("id", out v)) st.Pid = v;
                    if (f.TryGetValue("field_order", out v)) st.FieldOrder = v;
                    if (f.TryGetValue("tag:language", out v) && v != "und") st.Lang = v;
                    if (type == "video" && st.Width == 0) continue;     // an empty video stream
                    if (t.Streams.Any(x => x.Index == st.Index)) continue;  // MPEG-TS: ffprobe lists the streams again inside the program
                    t.Streams.Add(st);
                }
                else if (l.StartsWith("format|"))
                {
                    var f = Fields(l); string v; double d;
                    if (f.TryGetValue("duration", out v) && double.TryParse(v, NumberStyles.Float, Inv, out d) && t.Duration <= 0) t.Duration = d;
                }
                else if (l.StartsWith("chapter|")) t.Chapters++;
            }
        }

        const string ProbeEntries = "-show_entries format=duration:stream=index,codec_type,codec_name,channels,channel_layout,width,height,id,field_order:stream_tags=language:chapter=id -of compact=p=1";

        public static string InputArgs(DiscSource s, DiscTitle t)
        {
            if (s.IsDvd) return "-f dvdvideo -title " + t.Number + " -i \"" + s.FfPath + "\"";
            if (t.Direct)
            {
                var files = t.Clips.Select(c => Path.Combine(s.Path, "BDMV", "STREAM", c + ".m2ts")).ToList();
                return "-analyzeduration 30M -probesize 60M -i \"" + (files.Count == 1 ? files[0] : "concat:" + string.Join("|", files.ToArray())) + "\"";
            }
            return "-analyzeduration 30M -probesize 60M" + (t.Number >= 0 ? " -playlist " + t.Number : "") + " -i \"bluray:" + s.FfPath + "\"";
        }

        // ------------------------------------------------------------------ scanning
        public static void Scan(DiscSource s, double minSeconds, Action<string> status, Func<bool> cancelled, out string error)
        {
            error = null;
            s.Titles.Clear();
            if (s.IsIso)
            {
                // DVD image? (title 1)  Otherwise Blu-ray image (libbluray reads the ISO; main playlist only).
                status("Reading the ISO image…");
                var t1 = new DiscTitle { Number = 1 };
                string o = Capture(FFprobePath, "-v error -f dvdvideo -title 1 " + ProbeEntries + " -i \"" + s.FfPath + "\"", 60000, cancelled);
                ParseProbe(o, t1);
                if (t1.Streams.Any(x => x.Type == "video")) { s.IsDvd = true; }
                else
                {
                    s.IsBluray = true;
                    var tb = new DiscTitle { Number = -1 };
                    o = Capture(FFprobePath, "-v error " + InputArgs(s, tb) + " " + ProbeEntries, 90000, cancelled);
                    if (o.IndexOf("aacs", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("encrypt", StringComparison.OrdinalIgnoreCase) >= 0 || o.IndexOf("bd+", StringComparison.OrdinalIgnoreCase) >= 0)
                    { s.Protected = true; s.ProtectionNote = "This Blu-ray image is copy-protected (AACS / BD+). FFmpeg Forge only rips discs without copy protection."; return; }
                    ParseProbe(o, tb);
                    if (!tb.Streams.Any(x => x.Type == "video")) { error = "This ISO image is neither a readable DVD-Video nor a Blu-ray (or it is copy-protected).\n" + LastLines(o); s.IsBluray = false; return; }
                    s.HasLongPlaylist = true;
                    tb.Selected = true;
                    s.Titles.Add(tb);
                    return;
                }
            }
            if (s.IsDvd) ScanDvd(s, minSeconds, status, cancelled, out error);
            else ScanBluray(s, minSeconds, status, cancelled, out error);
        }

        static string LastLines(string o)
        {
            var ls = o.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !Noise(x) && !x.StartsWith("stream|") && !x.StartsWith("format|") && !x.StartsWith("chapter|")).ToList();
            return string.Join("\n", ls.Skip(Math.Max(0, ls.Count - 4)).ToArray());
        }

        static void ScanDvd(DiscSource s, double minSeconds, Action<string> status, Func<bool> cancelled, out string error)
        {
            error = null;
            int count = DvdTitleCount(s.Path);
            int max = count > 0 ? count : 99;
            string lastOut = "";
            var shortOnes = new List<DiscTitle>();
            for (int n = 1; n <= max; n++)
            {
                if (cancelled()) return;
                status("Reading DVD title " + n + (count > 0 ? " / " + count : "") + "…");
                var t = new DiscTitle { Number = n };
                string o = Capture(FFprobePath, "-v error " + InputArgs(s, t) + " " + ProbeEntries, 60000, cancelled);
                lastOut = o;
                if (o.Contains("Title " + n + " not found") || (count <= 0 && !o.Contains("stream|"))) { if (count <= 0) break; continue; }
                ParseProbe(o, t);
                if (!t.Streams.Any(x => x.Type == "video")) { t.Error = LastLines(o); }
                if (t.Duration < minSeconds && t.Error == null) { shortOnes.Add(t); continue; }
                s.Titles.Add(t);
            }
            if (!s.Titles.Any(x => x.Error == null) && shortOnes.Count > 0) s.Titles.AddRange(shortOnes);     // only short titles on this disc: show them
            if (s.Titles.Count == 0) { error = "No DVD title could be read (the disc may be copy-protected or damaged).\n" + LastLines(lastOut); return; }
            var main = s.Titles.Where(x => x.Error == null).OrderByDescending(x => x.Duration).FirstOrDefault();
            if (main != null)
            {
                main.Selected = true;
                // copy protection (CSS): the picture cannot be decoded without the disc keys
                status("Checking that the DVD can be read (" + main.Name(false) + ")…");
                string why;
                if (!CheckDvdReadable(s, main, cancelled, out why)) { s.Protected = true; s.ProtectionNote = why; }
            }
        }

        /// Number of titles from VIDEO_TS.IFO (TT_SRPT), or -1 (ISO / unreadable).
        static int DvdTitleCount(string root)
        {
            try
            {
                string ifo = Path.Combine(root, "VIDEO_TS", "VIDEO_TS.IFO");
                if (!File.Exists(ifo)) return -1;
                var b = File.ReadAllBytes(ifo);
                if (b.Length < 0xC8 || Encoding.ASCII.GetString(b, 0, 12) != "DVDVIDEO-VMG") return -1;
                long sector = ((long)b[0xC4] << 24) | ((long)b[0xC5] << 16) | ((long)b[0xC6] << 8) | b[0xC7];
                long off = sector * 2048;
                if (off + 2 > b.Length) return -1;
                int n = (b[off] << 8) | b[off + 1];
                return n >= 1 && n <= 99 ? n : -1;
            }
            catch { return -1; }
        }

        /// Decodes 20 s of the title: a CSS-encrypted disc read without its keys gives a stream of decoding errors.
        public static bool CheckDvdReadable(DiscSource s, DiscTitle t, Func<bool> cancelled, out string why)
        {
            why = null;
            double start = t.Duration > 120 ? Math.Min(300, t.Duration / 3) : 0;
            string o = Capture(Runner.FFmpeg, "-hide_banner -nostdin -v error " + InputArgs(s, t) + " -map 0:v:0 -ss " + start.ToString("0", Inv) + " -t 20 -f null -", 120000, cancelled);
            var errs = o.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !Noise(x)).ToList();
            bool io = errs.Any(x => x.IndexOf("Input/output error", StringComparison.OrdinalIgnoreCase) >= 0 || x.IndexOf("read failed", StringComparison.OrdinalIgnoreCase) >= 0);
            if (errs.Count >= 25 || io)
            {
                why = "The video of this DVD cannot be read: it is most likely copy-protected (CSS). " +
                      "FFmpeg Forge only rips discs without copy protection (your own videos, home-made or unprotected discs)." +
                      (io ? "" : " (" + errs.Count + " decoding errors in 20 seconds)");
                return false;
            }
            return true;
        }

        static void ScanBluray(DiscSource s, double minSeconds, Action<string> status, Func<bool> cancelled, out string error)
        {
            error = null;
            if (s.Protected) return;
            string pl = Path.Combine(s.Path, "BDMV", "PLAYLIST");
            var found = new List<Tuple<int, MplsInfo>>();
            foreach (var f in Directory.GetFiles(pl, "*.mpls").OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                int num;
                if (!int.TryParse(Path.GetFileNameWithoutExtension(f), out num)) continue;
                var m = MplsInfo.Read(f);
                if (m == null || m.Clips.Count == 0) continue;
                found.Add(Tuple.Create(num, m));
            }
            if (found.Count == 0) { error = "No playlist (.mpls) could be read in " + pl + "."; return; }
            s.HasLongPlaylist = found.Any(x => x.Item2.Duration >= 180);
            // identical playlists (same clips, same length) are listed once
            var seen = new HashSet<string>();
            var keep = new List<Tuple<int, MplsInfo>>();
            if (!found.Any(x => x.Item2.Duration >= minSeconds)) minSeconds = 0;     // only short playlists: show them
            foreach (var x in found.OrderByDescending(x => x.Item2.Duration))
            {
                if (x.Item2.Duration < minSeconds) continue;
                string key = string.Join(",", x.Item2.Clips.ToArray()) + "|" + Math.Round(x.Item2.Duration);
                if (!seen.Add(key)) continue;
                keep.Add(x);
            }
            if (keep.Count == 0) { error = "No usable playlist on this Blu-ray."; return; }
            int i = 0;
            foreach (var x in keep.Take(40))
            {
                if (cancelled()) return;
                i++;
                status("Reading Blu-ray playlist " + x.Item1.ToString("00000") + " (" + i + " / " + Math.Min(40, keep.Count) + ")…");
                var t = new DiscTitle { Number = x.Item1, Duration = x.Item2.Duration, ChapterTimes = x.Item2.Chapters, Chapters = x.Item2.Chapters.Count, MultiAngle = x.Item2.MultiAngle };
                t.Clips = x.Item2.Clips;
                bool clipsOk = t.Clips.All(c => File.Exists(Path.Combine(s.Path, "BDMV", "STREAM", c + ".m2ts")));
                t.Direct = !s.HasLongPlaylist && clipsOk;
                if (!s.HasLongPlaylist && !clipsOk) { t.Error = "clips not found"; s.Titles.Add(t); continue; }
                string o = Capture(FFprobePath, "-v error " + InputArgs(s, t) + " " + ProbeEntries, 90000, cancelled);
                double d = t.Duration;
                ParseProbe(o, t);
                t.Duration = d > 0 ? d : t.Duration;
                t.Chapters = t.ChapterTimes.Count;
                foreach (var st in t.Streams)
                {
                    int pid;
                    if (!string.IsNullOrEmpty(st.Pid) && int.TryParse(st.Pid.Replace("0x", ""), NumberStyles.HexNumber, Inv, out pid))
                    {
                        string lang;
                        if (string.IsNullOrEmpty(st.Lang) && x.Item2.PidLang.TryGetValue(pid, out lang)) st.Lang = lang;
                    }
                }
                if (!t.Streams.Any(z => z.Type == "video")) t.Error = LastLines(o);
                s.Titles.Add(t);
            }
            var main = s.Titles.Where(z => z.Error == null).OrderByDescending(z => z.Duration).FirstOrDefault();
            if (main != null) main.Selected = true;
            else error = "No playlist of this Blu-ray could be read.\n" + (s.Titles.Count > 0 ? s.Titles[0].Error : "");
        }
    }

    // =====================================================================================================
    //  Rip settings and FFmpeg command
    // =====================================================================================================
    public class RipOptions
    {
        public string Container = "mkv";      // mkv mp4 mov webm | audio only: flac mp3 m4a wav mka
        public string VCodec = "copy";        // copy x264 x265 av1 nv264 nv265 nvav1
        public int Quality = 1;               // 0 very high … 3 small file
        public int Speed = 1;                 // 0 slow … 2 fast
        public int Height;                    // 0 = original
        public int Deint;                     // 0 auto, 1 off, 2 always
        public string ACodec = "copy";        // copy aac ac3 eac3 flac opus mp3
        public int AKbps;                     // 0 = automatic
        public bool Downmix;
        public int Subs;                      // 0 keep selected, 1 none, 2 burn the first selected
        public bool Chapters = true;
        public string OutDir;
        public bool AddToList = true;

        public bool AudioOnly { get { return Container == "flac" || Container == "mp3" || Container == "m4a" || Container == "wav" || Container == "mka"; } }
    }

    public static class RipBuilder
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Q(string s) { return "\"" + s + "\""; }

        static bool AudioFits(string container, string codec)
        {
            switch (container)
            {
                case "mkv": case "mka": return codec != "pcm_dvd" && codec != "pcm_bluray";
                case "mp4": case "m4a": return codec == "aac" || codec == "ac3" || codec == "eac3" || codec == "mp3" || codec == "alac" || codec == "flac" || codec == "opus";
                case "mov": return codec == "aac" || codec == "ac3" || codec == "eac3" || codec == "mp3" || codec == "alac" || codec == "pcm_s16le" || codec == "pcm_s24le";
                case "webm": return codec == "opus" || codec == "vorbis";
                default: return false;
            }
        }

        static bool VideoFits(string container, string codec)
        {
            switch (container)
            {
                case "mkv": return true;
                case "mp4": case "mov": return codec == "mpeg2video" || codec == "h264" || codec == "hevc" || codec == "mpeg4";
                case "webm": return codec == "vp9" || codec == "av1";
                default: return false;
            }
        }

        /// Encoder arguments for one audio output stream (k), or null + error.
        static string AudioEnc(string codec, int k, DiscStream a, RipOptions o, string container)
        {
            int ch = o.Downmix ? 2 : Math.Max(1, a.Channels);
            string ac = o.Downmix ? " -ac:a:" + k + " 2" : "";
            switch (codec)
            {
                case "aac": return "-c:a:" + k + " aac -b:a:" + k + " " + (o.AKbps > 0 ? o.AKbps : ch > 2 ? 384 : 192) + "k" + ac;
                case "ac3": return "-c:a:" + k + " ac3 -b:a:" + k + " " + Math.Min(640, o.AKbps > 0 ? o.AKbps : ch > 2 ? 640 : 224) + "k" + (ch > 6 ? " -ac:a:" + k + " 6" : ac);
                case "eac3": return "-c:a:" + k + " eac3 -b:a:" + k + " " + (o.AKbps > 0 ? o.AKbps : ch > 2 ? 768 : 256) + "k" + ac;
                case "flac": return "-c:a:" + k + " flac -compression_level:a:" + k + " 8" + ac;
                case "opus":
                    return "-c:a:" + k + " libopus -b:a:" + k + " " + (o.AKbps > 0 ? o.AKbps : ch > 2 ? 320 : 160) + "k" +
                           (ch > 2 ? " -mapping_family:a:" + k + " 1 -filter:a:" + k + " aformat=channel_layouts=7.1|5.1|stereo" : "") + ac;
                case "mp3": return "-c:a:" + k + " libmp3lame -b:a:" + k + " " + Math.Min(320, o.AKbps > 0 ? o.AKbps : 256) + "k -ac:a:" + k + " 2";
                case "wav": return "-c:a:" + k + " pcm_s24le" + ac;
            }
            return null;
        }

        static string Fallback(string container, DiscStream a)
        {
            switch (container)
            {
                case "mkv": case "mka": return "flac";            // LPCM → FLAC (lossless)
                case "webm": return "opus";
                default: return a.Channels > 2 ? "ac3" : "aac";
            }
        }

        public static string OutputName(DiscSource s, DiscTitle t, RipOptions o)
        {
            string label = new string((s.Label ?? "Disc").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim();
            if (label.Length == 0) label = "Disc";
            string name = label + " - " + (t.Number < 0 ? "Main movie" : t.Name(s.IsBluray));
            string p = Path.Combine(o.OutDir, name + "." + o.Container);
            int n = 2;
            while (File.Exists(p)) p = Path.Combine(o.OutDir, name + " (" + n++ + ")." + o.Container);
            return p;
        }

        /// Builds the FFmpeg arguments. chaptersFile (Blu-ray) must be written by the caller with WriteChapters().
        public static string Build(DiscSource s, DiscTitle t, RipOptions o, string outPath, string chaptersFile, List<string> notes, out string error)
        {
            error = null;
            var v = t.Video.FirstOrDefault();
            var audio = t.Audio.Where(x => x.Selected).ToList();
            var subs = t.Subs.Where(x => x.Selected).ToList();
            if (o.AudioOnly)
            {
                if (audio.Count == 0) { error = "Tick at least one audio track for an audio-only rip."; return null; }
                if (o.Container != "mka" && o.Container != "m4a" && audio.Count > 1) { notes.Add(o.Container.ToUpperInvariant() + " holds one audio track: only the first ticked track (" + audio[0].Describe() + ") is ripped."); audio = audio.Take(1).ToList(); }
            }
            else if (v == null) { error = "This title has no video stream."; return null; }

            bool copyV = !o.AudioOnly && o.VCodec == "copy";
            bool burn = !o.AudioOnly && o.Subs == 2 && subs.Count > 0;
            if (burn && copyV) { error = "Burning subtitles into the picture needs a video encoder: choose H.264 / H.265 / AV1 instead of \"Copy\"."; return null; }
            if (copyV && !VideoFits(o.Container, v.Codec)) { error = DiscStream.CodecName(v.Codec) + " video cannot be copied into " + o.Container.ToUpperInvariant() + ": choose MKV, or a video encoder (H.264 / H.265 / AV1)."; return null; }
            if (!o.AudioOnly && o.Container == "webm" && o.VCodec != "av1" && o.VCodec != "nvav1") { error = "WebM needs AV1 video: choose AV1 (SVT-AV1 or NVIDIA)."; return null; }

            var a = new StringBuilder();
            a.Append("-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats ");
            a.Append(DiscScanner.InputArgs(s, t)).Append(' ');
            bool chapIn = s.IsBluray && o.Chapters && chaptersFile != null && t.ChapterTimes.Count > 1;
            if (chapIn) a.Append("-f ffmetadata -i ").Append(Q(chaptersFile)).Append(' ');

            // ---------- video ----------
            if (!o.AudioOnly)
            {
                var chain = new List<string>();
                if (!copyV)
                {
                    bool interlaced = v.FieldOrder == "tt" || v.FieldOrder == "bb" || v.FieldOrder == "tb" || v.FieldOrder == "bt";
                    if (o.Deint == 2) chain.Add("bwdif=mode=send_frame:deint=all");
                    else if (o.Deint == 0 && (s.IsDvd || interlaced || v.Codec == "mpeg2video" || v.Codec == "vc1")) chain.Add("bwdif=mode=send_frame:deint=interlaced");
                }
                var post = new List<string>();
                if (!copyV && o.Height > 0 && v.Height > o.Height) post.Add("scale=-2:" + o.Height + ":flags=lanczos");
                if (burn)
                {
                    string g = "[0:" + v.Index + "]" + (chain.Count > 0 ? string.Join(",", chain.ToArray()) : "null") + "[dv];[dv][0:" + subs[0].Index + "]overlay=eof_action=pass:repeatlast=0" +
                               (post.Count > 0 ? "," + string.Join(",", post.ToArray()) : "") + "[vout]";
                    a.Append("-filter_complex ").Append(Q(g)).Append(" -map \"[vout]\" ");
                }
                else
                {
                    a.Append("-map 0:").Append(v.Index).Append(' ');
                    chain.AddRange(post);
                    if (chain.Count > 0) a.Append("-vf ").Append(Q(string.Join(",", chain.ToArray()))).Append(' ');
                }
                a.Append(VideoArgs(o)).Append(' ');
            }

            // ---------- audio ----------
            int k = 0;
            foreach (var st in audio)
            {
                a.Append("-map 0:").Append(st.Index).Append(' ');
                string want = o.Container == "flac" ? "flac" : o.Container == "mp3" ? "mp3" : o.Container == "wav" ? "wav" : o.Container == "m4a" && o.ACodec != "aac" && o.ACodec != "copy" ? "aac" : o.ACodec;
                if (want == "copy" && o.Downmix) want = Fallback(o.Container, st) == "flac" ? "flac" : "aac";
                if (want == "copy" && o.Container == "m4a" && st.Codec != "aac" && st.Codec != "alac") want = "aac";
                if (want == "copy" && !AudioFits(o.Container, st.Codec))
                {
                    string fb = Fallback(o.Container, st);
                    notes.Add("Audio " + (k + 1) + " (" + st.Describe() + ") cannot be copied into " + o.Container.ToUpperInvariant() + ": converted to " + fb.ToUpperInvariant() + ".");
                    want = fb;
                }
                if (want != "copy" && want != "flac" && want != "wav" && want != "mp3" && !AudioFits(o.Container == "m4a" ? "mp4" : o.Container, want == "opus" ? "opus" : want))
                { error = want.ToUpperInvariant() + " audio cannot be stored in " + o.Container.ToUpperInvariant() + "."; return null; }
                if (want == "copy") a.Append("-c:a:").Append(k).Append(" copy ");
                else a.Append(AudioEnc(want, k, st, o, o.Container)).Append(' ');
                if (!string.IsNullOrEmpty(st.Lang)) a.Append("-metadata:s:a:").Append(k).Append(" language=").Append(st.Lang).Append(' ');
                a.Append("-disposition:a:").Append(k).Append(k == 0 ? " default " : " 0 ");
                k++;
            }

            // ---------- subtitles ----------
            if (!o.AudioOnly && o.Subs == 0 && subs.Count > 0)
            {
                if (o.Container == "mkv")
                {
                    int j = 0;
                    foreach (var st in subs)
                    {
                        a.Append("-map 0:").Append(st.Index).Append(' ');
                        if (!string.IsNullOrEmpty(st.Lang)) a.Append("-metadata:s:s:").Append(j).Append(" language=").Append(st.Lang).Append(' ');
                        a.Append("-disposition:s:").Append(j).Append(" 0 ");
                        j++;
                    }
                    a.Append("-c:s copy ");
                }
                else notes.Add(o.Container.ToUpperInvariant() + " cannot store DVD / Blu-ray picture subtitles: they are left out (choose MKV, or burn one into the picture).");
            }

            // ---------- chapters, container ----------
            bool chapOk = o.Chapters && (o.Container == "mkv" || o.Container == "mp4" || o.Container == "mov" || o.Container == "m4a" || o.Container == "mka" || o.Container == "webm");
            if (chapOk && chapIn) a.Append("-map_chapters 1 ");
            else if (chapOk && s.IsDvd) a.Append("-map_chapters 0 ");
            else a.Append("-map_chapters -1 ");
            if (o.AudioOnly) a.Append("-vn -sn ");
            a.Append("-max_muxing_queue_size 4096 ");
            if (o.Container == "mp4" || o.Container == "mov" || o.Container == "m4a") a.Append("-movflags +faststart ");
            if (!o.AudioOnly && !copyV && (o.Container == "mp4" || o.Container == "mov") && (o.VCodec == "x265" || o.VCodec == "nv265")) a.Append("-tag:v hvc1 ");
            if (copyV && (o.Container == "mp4" || o.Container == "mov") && v.Codec == "hevc") a.Append("-tag:v hvc1 ");
            a.Append("-strict -2 ").Append(Q(outPath));
            return a.ToString();
        }

        static string VideoArgs(RipOptions o)
        {
            int q = Math.Max(0, Math.Min(3, o.Quality)), sp = Math.Max(0, Math.Min(2, o.Speed));
            string[] xs = { "slow", "medium", "fast" };
            switch (o.VCodec)
            {
                case "copy": return "-c:v copy";
                case "x264": return "-c:v libx264 -preset " + xs[sp] + " -crf " + new[] { 17, 19, 22, 25 }[q] + " -pix_fmt yuv420p";
                case "x265": return "-c:v libx265 -preset " + xs[sp] + " -crf " + new[] { 18, 21, 24, 27 }[q] + " -pix_fmt yuv420p10le -x265-params log-level=error";
                case "av1": return "-c:v libsvtav1 -preset " + new[] { 4, 6, 8 }[sp] + " -crf " + new[] { 22, 27, 32, 38 }[q] + " -pix_fmt yuv420p10le";
                case "nv264": return "-c:v h264_nvenc -preset " + new[] { "p6", "p5", "p3" }[sp] + " -rc vbr -cq " + new[] { 18, 21, 24, 28 }[q] + " -b:v 0 -pix_fmt yuv420p";
                case "nv265": return "-c:v hevc_nvenc -preset " + new[] { "p6", "p5", "p3" }[sp] + " -rc vbr -cq " + new[] { 19, 22, 25, 29 }[q] + " -b:v 0 -pix_fmt p010le";
                case "nvav1": return "-c:v av1_nvenc -preset " + new[] { "p6", "p5", "p3" }[sp] + " -rc vbr -cq " + new[] { 22, 26, 30, 35 }[q] + " -b:v 0 -pix_fmt p010le";
            }
            return "-c:v libx264 -crf 20 -pix_fmt yuv420p";
        }

        /// FFmetadata chapter file for a Blu-ray playlist.
        public static void WriteChapters(DiscTitle t, string file)
        {
            var sb = new StringBuilder(";FFMETADATA1\n");
            for (int i = 0; i < t.ChapterTimes.Count; i++)
            {
                long st = (long)Math.Round(t.ChapterTimes[i] * 1000);
                long en = (long)Math.Round((i + 1 < t.ChapterTimes.Count ? t.ChapterTimes[i + 1] : t.Duration) * 1000);
                if (en <= st) continue;
                sb.Append("[CHAPTER]\nTIMEBASE=1/1000\nSTART=").Append(st).Append("\nEND=").Append(en).Append("\ntitle=Chapter ").Append((i + 1).ToString("00")).Append('\n');
            }
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
