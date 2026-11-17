using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FFmpegForge
{
    /// Live before / after preview during a video export (beta).
    /// The export command gets two extra low-rate outputs (1 picture per second of video, atomic JPEG writes):
    ///   before.jpg  = the source frame        after.jpg = the same frame through the job's video filters
    /// so the preview costs one decode and a few filtered frames, not a second encode.
    public static class PreviewFrames
    {
        public static string Dir;
        public static string Before { get { return Path.Combine(Dir, "before.jpg"); } }
        public static string After { get { return Path.Combine(Dir, "after.jpg"); } }

        static bool? supported;
        /// The preview needs FFmpeg's image2 "atomic_writing" option (FFmpeg 4.4+).
        public static bool Supported
        {
            get
            {
                if (supported != null) return supported.Value;
                supported = false;
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo(Runner.FFmpeg, "-hide_banner -h muxer=image2") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    using (var p = System.Diagnostics.Process.Start(psi))
                    {
                        string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                        p.WaitForExit(10000);
                        supported = o.Contains("atomic_writing");
                    }
                }
                catch { }
                return supported.Value;
            }
        }

        public static void Reset()
        {
            if (Dir == null) Dir = Path.Combine(Path.GetTempPath(), "FFmpegForge-preview-" + System.Diagnostics.Process.GetCurrentProcess().Id);
            try { Directory.CreateDirectory(Dir); } catch { }
            foreach (var f in new[] { Before, After }) try { if (File.Exists(f)) File.Delete(f); } catch { }
        }

        public static void Cleanup()
        {
            try { if (Dir != null && Directory.Exists(Dir)) Directory.Delete(Dir, true); } catch { }
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        const string Fit = "scale='min(1280,iw)':-2:flags=bicubic";

        // The JPEG comparison window is SDR even when Windows HDR is enabled.
        // Follow FGS's display-only chain (PQ/HLG -> linear -> Hable -> BT.709);
        // NEVER put this chain in the actual video output.
        internal static string DisplayFilter(Job source, bool hdr10Output)
        {
            string transfer = hdr10Output ? "smpte2084" : (source == null ? null : source.ColorTransfer);
            if (transfer != "smpte2084" && transfer != "arib-std-b67") return null;
            string prim = hdr10Output ? "bt2020" : (source == null ? null : source.ColorPrimaries);
            if (prim != "bt2020" && prim != "bt709" && prim != "smpte170m" && prim != "bt470bg")
                prim = "bt2020";
            string matrix = hdr10Output ? "bt2020nc" : (source == null ? null : source.ColorSpace);
            if (matrix != "bt2020nc" && matrix != "bt2020c" && matrix != "bt709" &&
                matrix != "smpte170m" && matrix != "bt470bg")
                matrix = "bt2020nc";
            string range = !hdr10Output && source != null && source.ColorRange == "pc" ? "pc" : "tv";
            return "zscale=rin=" + range + ":pin=" + prim + ":tin=" + transfer + ":min=" + matrix +
                   ":t=linear:npl=100,format=gbrpf32le,tonemap=tonemap=hable:desat=2," +
                   "zscale=p=bt709:t=bt709:m=bt709:r=tv,format=p010le," +
                   "setparams=range=tv:color_primaries=bt709:color_trc=bt709:colorspace=bt709";
        }

        // User-supplied HDR->SDR filters must not be tone-mapped a second time.
        internal static bool AlreadySdr(string chain)
        {
            if (string.IsNullOrEmpty(chain)) return false;
            return Regex.IsMatch(chain, @"(?:^|,)\s*tonemap\s*=", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(chain, @"\btonemapping\s*=", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(chain, @"\bcolor_trc\s*=\s*(?:bt709|iec61966-2-1)\b", RegexOptions.IgnoreCase);
        }

        static string Output(string map, string chain, string path, double seconds, string display)
        {
            string vf = "fps=1" + (string.IsNullOrEmpty(chain) ? "" : "," + chain) +
                        (string.IsNullOrEmpty(display) ? "," + Fit :
                            "," + display + "," + Fit + ":out_range=full,setsar=1,format=rgb24");
            return " -map " + map + " -vf " + Q(vf) + " -an -sn -dn" + (seconds > 0 ? " -t " + Formats.Inv(seconds + 1) : "") +
                   " -c:v mjpeg -q:v 3 -strict unofficial -threads 1 -y -f tee " +
                   // tee + onfail=ignore + a null slave: if the preview picture cannot be written (TEMP folder cleaned by
                   // Windows / an antivirus, file locked…), the preview just stops; the export itself goes on.
                   Q("[f=image2:update=1:atomic_writing=1:onfail=ignore]" + path.Replace('\\', '/') + "|[f=null]-");
        }

        static readonly Regex VfRx = new Regex("-vf \"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Compiled);

        static string Rotation(ConvSettings s)
        {
            var r = new List<string>();
            CommandBuilder.AddRotate(r, s.Rotate);
            return string.Join(",", r.ToArray());
        }

        /// Adds the preview outputs to an ordinary FFmpeg Forge command (input 0 = the source).
        public static string ForCommand(string args, ConvSettings s, double seconds, Job source)
        {
            if (args == null || args.Contains("[vout]")) return args;
            string vf = null;
            var ms = VfRx.Matches(args);
            if (ms.Count > 0) vf = ms[ms.Count - 1].Groups[1].Value.Replace("\\\"", "\"");
            bool hdr10Output = s.SdrHdr > 0 || s.RtxHdr;
            string before = DisplayFilter(source, false);
            string after = (hdr10Output || !AlreadySdr(vf)) ? DisplayFilter(source, hdr10Output) : null;
            return args + Output("0:v:0", Rotation(s), Before, seconds, before) +
                          Output("0:v:0", vf, After, seconds, after);
        }

        /// Adds the "before" output to a decoder command (input 0 = the source).
        public static string ForDecoder(string decArgs, ConvSettings s, double seconds, Job source)
        {
            return decArgs == null ? null : decArgs + Output("0:v:0", Rotation(s), Before, seconds, DisplayFilter(source, false));
        }

        /// Adds the preview outputs to an encoder command whose input 0 is the processed picture and
        /// input 1 the source (RTX driver engine, SeedVR2 and VisionNext final encodes).
        public static string ForEncoder(string encArgs, ConvSettings s, bool sourceIsInput1, bool afterNeedsRotation, double seconds, Job source)
        {
            if (encArgs == null) return null;
            string a = encArgs;
            if (sourceIsInput1) a += Output("1:v:0", Rotation(s), Before, seconds, DisplayFilter(source, false));
            bool hdr10Output = s.SdrHdr > 0 || s.RtxHdr;
            a += Output("0:v:0", afterNeedsRotation ? Rotation(s) : null, After, seconds,
                        DisplayFilter(source, hdr10Output));
            return a;
        }
    }

    /// 2.4.0 — Live preview window, "cinema film strip" design: the current frame (before / after slider or both
    /// side by side) runs on a 35 mm strip with sprocket holes, edge printing and the previous frames sliding away.
    public class PreviewWindow : Form
    {
        readonly FilmStripView view;
        readonly PreviewHeader header;
        readonly Label info;
        readonly FlatBtn modeBtn;
        readonly Timer timer;
        DateTime tBefore, tAfter;
        string folderIn, folderOut, staticBefore;
        DateTime tFolder;
        /// Job being converted: progress, duration and start time for the header (set by the main window).
        public Job Job;

        public PreviewWindow()
        {
            Text = "FFmpeg Forge — Live preview";
            BackColor = FilmStripView.Backdrop;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Theme.S(1060), Theme.S(660));
            MinimumSize = new Size(Theme.S(560), Theme.S(360));
            ShowInTaskbar = false;
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            header = new PreviewHeader { Dock = DockStyle.Top, Height = Theme.S(58) };
            view = new FilmStripView { Dock = DockStyle.Fill };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(46), BackColor = Color.FromArgb(16, 14, 12), Padding = new Padding(Theme.S(14), Theme.S(7), Theme.S(12), Theme.S(7)) };
            info = new Label { Dock = DockStyle.Fill, ForeColor = Color.FromArgb(150, 140, 125), TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.5f), Text = "Waiting for the first frame…" };
            modeBtn = new FlatBtn("Side by side") { Dock = DockStyle.Right, Width = Theme.S(140), Icon = "film" };
            modeBtn.Click += (s, e) => ToggleMode();
            bar.Controls.Add(info);
            bar.Controls.Add(modeBtn);
            Controls.Add(view);
            Controls.Add(bar);
            Controls.Add(header);

            timer = new Timer { Interval = 400 };
            timer.Tick += (s, e) => { Poll(); UpdateHeader(); };
            timer.Start();
            FormClosed += (s, e) => { timer.Stop(); view.Clear(); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Left) { view.Split = Math.Max(0, view.Split - 0.05f); view.Invalidate(); }
                if (e.KeyCode == Keys.Right) { view.Split = Math.Min(1, view.Split + 0.05f); view.Invalidate(); }
                if (e.KeyCode == Keys.Space) ToggleMode();
            };
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkTitle.Apply(Handle); }

        void ToggleMode() { view.SideBySide = !view.SideBySide; modeBtn.Text = view.SideBySide ? "Slider" : "Side by side"; view.Invalidate(); UpdateInfo(); }

        string status = "";
        public void SetStatus(string s)
        {
            status = s ?? "";
            header.Recording = status.StartsWith("convert", StringComparison.OrdinalIgnoreCase);
            header.Done = status.StartsWith("done", StringComparison.OrdinalIgnoreCase);
            header.Failed = status.StartsWith("fail", StringComparison.OrdinalIgnoreCase) || status.StartsWith("cancel", StringComparison.OrdinalIgnoreCase);
            UpdateInfo(); UpdateHeader();
        }

        /// New job: forget the old pictures. staticBefore = the source picture (picture → video jobs).
        public void NewJob(string title, string staticBeforeImage, string inFolder, string outFolder)
        {
            tBefore = tAfter = tFolder = DateTime.MinValue;
            folderIn = inFolder; folderOut = outFolder; staticBefore = staticBeforeImage;
            view.NewReel();
            view.Title = title;
            header.Title = title ?? "";
            Image sb = staticBefore != null ? LoadImg(staticBefore) : null;
            if (sb != null) view.SetImages(sb, null);
            UpdateInfo(); UpdateHeader();
        }

        public void SetFolders(string inFolder, string outFolder) { folderIn = inFolder; folderOut = outFolder; }

        void UpdateInfo()
        {
            info.Text = "Drag the line (or ← →) to compare the original and the final picture   ·   Space = " + (view.SideBySide ? "slider" : "side by side") +
                        (status.Length > 0 ? "   ·   " + status : "");
        }

        void UpdateHeader()
        {
            var j = Job;
            if (j != null)
            {
                header.Progress = Math.Max(0, Math.Min(1, j.Progress));
                double dur = j.Duration > 0 ? j.Duration : 0;
                header.Timecode = dur > 0 ? Tc(dur * header.Progress) + " / " + Tc(dur) : "";
                double el = j.Started > DateTime.MinValue ? (DateTime.Now - j.Started).TotalSeconds : 0;
                header.Eta = header.Recording && header.Progress > 0.02 && el > 1 ? "about " + Short(el * (1 - header.Progress) / header.Progress) + " left" : "";
            }
            header.Frames = view.FrameCount;
            header.Invalidate();
        }

        static string Tc(double s) { var t = TimeSpan.FromSeconds(Math.Max(0, s)); return string.Format("{0:00}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds); }
        static string Short(double s) { var t = TimeSpan.FromSeconds(Math.Max(0, s)); return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds); }

        static Image LoadImg(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length < 100) return null;
                byte[] b;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) { b = new byte[fs.Length]; int n = 0; while (n < b.Length) { int r = fs.Read(b, n, b.Length - n); if (r <= 0) break; n += r; } }
                using (var ms = new MemoryStream(b))
                using (var img = Image.FromStream(ms))
                    return new Bitmap(img);
            }
            catch { return null; }
        }

        void Poll()
        {
            if (PreviewFrames.Dir == null) return;
            try
            {
                bool changed = false;
                Image nb = null, na = null;
                var fb = new FileInfo(PreviewFrames.Before); var fa = new FileInfo(PreviewFrames.After);
                if (fa.Exists && fa.LastWriteTimeUtc != tAfter) { na = LoadImg(fa.FullName); if (na != null) { tAfter = fa.LastWriteTimeUtc; changed = true; } }
                if (fb.Exists && fb.LastWriteTimeUtc != tBefore) { nb = LoadImg(fb.FullName); if (nb != null) { tBefore = fb.LastWriteTimeUtc; changed = true; } }
                // AI upscalers working on picture folders (VisionNext): newest upscaled frame + its source frame
                if (na == null && tAfter == DateTime.MinValue && folderOut != null && Directory.Exists(folderOut))
                {
                    var last = new DirectoryInfo(folderOut).GetFiles("*.png", SearchOption.AllDirectories).OrderBy(f => f.Name).LastOrDefault();
                    if (last != null && last.LastWriteTimeUtc != tFolder && DateTime.UtcNow - last.LastWriteTimeUtc > TimeSpan.FromMilliseconds(300))
                    {
                        var m = Regex.Match(last.Name, @"^\d+");
                        string src = m.Success && folderIn != null ? Path.Combine(folderIn, m.Value + ".png") : null;
                        var a = LoadImg(last.FullName);
                        var b = src != null && File.Exists(src) ? LoadImg(src) : null;
                        if (a != null) { tFolder = last.LastWriteTimeUtc; na = a; nb = b; changed = true; }
                    }
                }
                if (changed) view.SetImages(nb ?? view.CurrentBefore, na ?? view.CurrentAfter);
            }
            catch { }
        }

        public void ShowNear(Form owner)
        {
            if (!Visible)
            {
                var wa = Screen.FromControl(owner).WorkingArea;
                int x = Math.Max(wa.Left, Math.Min(owner.Left + Theme.S(40), wa.Right - Width));
                int y = Math.Max(wa.Top, Math.Min(owner.Top + Theme.S(80), wa.Bottom - Height));
                Location = new Point(x, y);
                Show(owner);
            }
            else Activate();
        }
    }

    /// Header of the preview: film-reel icon, LIVE PREVIEW, file name, REC light, timecode, progress along a film edge.
    public class PreviewHeader : Control
    {
        public string Title = "", Timecode = "", Eta = "";
        public double Progress;
        public int Frames;
        public bool Recording, Done, Failed;
        bool blink;
        readonly Timer t;

        public PreviewHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            t = new Timer { Interval = 600 };
            t.Tick += (s, e) => { blink = !blink; if (Recording) Invalidate(); };
            t.Start();
            Disposed += (s, e) => t.Dispose();
        }

        static void Reel(Graphics g, RectangleF r, Color c)
        {
            using (var pen = new Pen(c, r.Width * 0.08f))
            {
                g.DrawEllipse(pen, r);
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, hr = r.Width * 0.14f, rr = r.Width * 0.27f;
                using (var b = new SolidBrush(c))
                {
                    g.FillEllipse(b, cx - hr * 0.55f, cy - hr * 0.55f, hr * 1.1f, hr * 1.1f);
                    for (int i = 0; i < 5; i++)
                    {
                        double a = -Math.PI / 2 + i * 2 * Math.PI / 5;
                        float hx = cx + (float)Math.Cos(a) * rr, hy = cy + (float)Math.Sin(a) * rr;
                        g.FillEllipse(b, hx - hr, hy - hr, hr * 2, hr * 2);
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(28, 23, 18), Color.FromArgb(14, 12, 10), 90f)) g.FillRectangle(bg, ClientRectangle);
            int pad = Theme.S(16);
            float ih = Height * 0.56f;
            var ir = new RectangleF(pad, (Height - ih) / 2 - Theme.S(2), ih, ih);
            // hole punched in a holder: hole in backdrop colour, reel in gold
            using (var b = new SolidBrush(Color.FromArgb(10, 9, 8))) g.FillEllipse(b, ir);
            Reel(g, ir, Theme.Gold);
            float x = ir.Right + Theme.S(12);
            using (var f1 = new Font("Segoe UI Semibold", 11.5f))
            using (var f2 = new Font("Segoe UI", 9f))
            using (var fm = new Font("Consolas", 10.5f, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "LIVE PREVIEW", f1, new Point((int)x, Theme.S(8)), Theme.Gold, TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Title, f2, new Rectangle((int)x, Theme.S(30), Math.Max(10, Width - (int)x - Theme.S(330)), Theme.S(18)), Color.FromArgb(205, 196, 182), TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                // right: REC light, timecode, frames
                int rx = Width - pad;
                string tc = Timecode.Length > 0 ? Timecode : "--:--:--";
                var tcs = TextRenderer.MeasureText(tc, fm);
                rx -= tcs.Width;
                TextRenderer.DrawText(g, tc, fm, new Point(rx, Theme.S(9)), Color.FromArgb(236, 226, 205), TextFormatFlags.NoPadding);
                string sub = (Frames > 0 ? Frames + " frame" + (Frames > 1 ? "s" : "") + " previewed" : "") + (Eta.Length > 0 ? (Frames > 0 ? "  ·  " : "") + Eta : "");
                var ss = TextRenderer.MeasureText(sub, f2);
                TextRenderer.DrawText(g, sub, f2, new Point(Width - pad - ss.Width, Theme.S(31)), Color.FromArgb(150, 140, 125), TextFormatFlags.NoPadding);
                string tag = Failed ? "STOPPED" : Done ? "DONE" : Recording ? "REC" : "READY";
                Color tc2 = Failed ? Theme.Error : Done ? Theme.Success : Color.FromArgb(235, 70, 60);
                var ts = TextRenderer.MeasureText(tag, f1);
                int tx = rx - Theme.S(26) - ts.Width;
                var pill = new RectangleF(tx - Theme.S(26), Theme.S(6), ts.Width + Theme.S(38), Theme.S(24));
                using (var p = Theme.Round(pill, Theme.S(12)))
                {
                    using (var b = new SolidBrush(Color.FromArgb(40, tc2))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(120, tc2))) g.DrawPath(pen, p);
                }
                float d = Theme.S(10);
                if (!Recording || blink || Done || Failed)
                    using (var b = new SolidBrush(tc2)) g.FillEllipse(b, pill.X + Theme.S(10), pill.Y + (pill.Height - d) / 2, d, d);
                TextRenderer.DrawText(g, tag, f1, new Point(tx, Theme.S(8)), tc2, TextFormatFlags.NoPadding);
            }
            // progress: an exposed strip of film along the bottom edge
            int ph = Theme.S(4);
            var track = new Rectangle(0, Height - ph, Width, ph);
            using (var b = new SolidBrush(Color.FromArgb(40, 34, 28))) g.FillRectangle(b, track);
            if (Progress > 0)
                using (var b = new LinearGradientBrush(new Rectangle(0, track.Y, Math.Max(1, Width), ph), Theme.GoldDeep, Theme.Gold, 0f))
                    g.FillRectangle(b, 0, track.Y, (float)(Width * Progress), ph);
        }
    }

    /// The film strip: sprocket holes, edge printing, the live frame (slider or side by side) and the previous frames.
    public class FilmStripView : Control
    {
        public static readonly Color Backdrop = Color.FromArgb(10, 9, 8);
        static readonly Color FilmBase = Color.FromArgb(22, 18, 14);
        static readonly Color FilmEdge = Color.FromArgb(34, 28, 21);
        static readonly Color EdgeInk = Color.FromArgb(214, 160, 70);

        Image before, after;
        readonly List<Image> history = new List<Image>();     // previous final frames (thumbnails), newest first
        public float Split = 0.5f;
        public bool SideBySide;
        public string Title;
        public int FrameCount;
        bool dragging;
        float slide;                      // 0 = at rest, 1 = a new frame has just arrived (strip moves left)
        readonly Timer anim;
        DateTime animStart;
        RectangleF lastFrame;

        public FilmStripView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Backdrop;
            Cursor = Cursors.VSplit;
            anim = new Timer { Interval = 16 };
            anim.Tick += (s, e) =>
            {
                double t = (DateTime.Now - animStart).TotalMilliseconds / 420.0;
                if (t >= 1) { slide = 0; anim.Stop(); }
                else { double k = 1 - t; slide = (float)(k * k * k); }   // ease-out
                Invalidate();
            };
            Disposed += (s, e) => anim.Dispose();
        }

        public Image CurrentBefore { get { return before; } }
        public Image CurrentAfter { get { return after; } }

        public void Clear()
        {
            SetImages(null, null);
            foreach (var h in history) h.Dispose();
            history.Clear();
        }

        public void NewReel() { Clear(); FrameCount = 0; slide = 0; Invalidate(); }

        public void SetImages(Image b, Image a)
        {
            bool newFrame = a != null && !ReferenceEquals(a, after);
            if (newFrame && after != null)
            {
                // the frame that leaves the gate goes into the history (small copy)
                try
                {
                    int tw = 320, th = Math.Max(1, after.Height * tw / Math.Max(1, after.Width));
                    var thumb = new Bitmap(tw, th);
                    using (var g = Graphics.FromImage(thumb)) { g.InterpolationMode = InterpolationMode.HighQualityBilinear; g.DrawImage(after, 0, 0, tw, th); }
                    history.Insert(0, thumb);
                    while (history.Count > 6) { history[history.Count - 1].Dispose(); history.RemoveAt(history.Count - 1); }
                }
                catch { }
            }
            if (before != null && !ReferenceEquals(before, b)) before.Dispose();
            if (after != null && !ReferenceEquals(after, a)) after.Dispose();
            before = b; after = a;
            if (newFrame)
            {
                FrameCount++;
                if (FrameCount > 1) { slide = 1; animStart = DateTime.Now; anim.Start(); }
            }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e) { dragging = true; MoveSplit(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) MoveSplit(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; }
        void MoveSplit(int x)
        {
            if (SideBySide || lastFrame.Width <= 0) return;
            Split = Math.Max(0, Math.Min(1, (x - lastFrame.X) / lastFrame.Width));
            Invalidate();
        }

        static void Cover(Graphics g, Image img, RectangleF box)
        {
            // fill the film frame (centre crop), like a projected picture
            if (img == null) return;
            float s = Math.Max(box.Width / img.Width, box.Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            var st = g.Save();
            g.SetClip(box);
            g.DrawImage(img, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
            g.Restore(st);
        }

        static void Fit(Graphics g, Image img, RectangleF box)
        {
            if (img == null) return;
            float s = Math.Min(box.Width / img.Width, box.Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            g.DrawImage(img, box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }

        static void Label(Graphics g, string text, float x, float y, bool right, Color accent)
        {
            using (var f = new Font("Segoe UI Semibold", 8.5f))
            {
                var sz = g.MeasureString(text, f);
                var r = new RectangleF(right ? x - sz.Width - 22 : x, y, sz.Width + 22, sz.Height + 6);
                using (var p = Theme.Round(r, r.Height / 2))
                {
                    using (var b = new SolidBrush(Color.FromArgb(185, 12, 10, 8))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(150, accent))) g.DrawPath(pen, p);
                }
                using (var b = new SolidBrush(accent)) g.FillEllipse(b, r.X + 8, r.Y + r.Height / 2 - 3, 6, 6);
                using (var t = new SolidBrush(Color.FromArgb(240, 232, 218))) g.DrawString(text, f, t, r.X + 16, r.Y + 3);
            }
        }

        /// A frame cell on the strip: thin black gate line, the picture (or an unexposed frame), soft vignette.
        static void FrameCell(Graphics g, RectangleF r, Image img, float dim, bool cover)
        {
            using (var p = Theme.Round(r, Math.Max(2, r.Height * 0.025f)))
            {
                using (var b = new SolidBrush(Color.FromArgb(6, 5, 4))) g.FillPath(b, p);
                if (img != null)
                {
                    var st = g.Save(); g.SetClip(p, CombineMode.Intersect);
                    if (cover) Cover(g, img, r); else Fit(g, img, r);
                    if (dim > 0) using (var b = new SolidBrush(Color.FromArgb((int)(255 * dim), 12, 10, 8))) g.FillRectangle(b, r);
                    g.Restore(st);
                }
                else
                {
                    // unexposed frame: warm dark base with a faint diagonal sheen
                    using (var b = new LinearGradientBrush(r, Color.FromArgb(30, 25, 20), Color.FromArgb(16, 13, 10), 35f)) g.FillPath(b, p);
                }
                using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0), 2f)) g.DrawPath(pen, p);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            int W = ClientSize.Width, H = ClientSize.Height;
            // backdrop with a warm projector glow behind the strip
            g.Clear(Backdrop);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(-W * 0.15f, H * 0.05f, W * 1.3f, H * 0.9f);
                using (var pb = new PathGradientBrush(path) { CenterColor = Color.FromArgb(34, 27, 20), SurroundColors = new[] { Backdrop } })
                    g.FillPath(pb, path);
            }

            // ---- strip geometry
            float margin = Math.Max(10, H * 0.05f);
            float stripH = H - margin * 2;
            float band = stripH * 0.105f;                      // perforated margins
            float frameH = stripH - band * 2 - stripH * 0.02f;
            var main = after ?? before;
            float aspect = main != null && main.Height > 0 ? (float)main.Width / main.Height : 16f / 9f;
            aspect = Math.Max(1.0f, Math.Min(2.4f, aspect));
            float frameW = frameH * aspect;
            bool sbs = SideBySide && (before != null || after != null);
            float gateW = sbs ? frameW * 2 + frameH * 0.05f : frameW;
            if (gateW > W * 0.86f) { float k = W * 0.86f / gateW; frameW *= k; frameH *= k; gateW *= k; band = Math.Max(band * k, stripH * 0.08f); }
            float gap = frameH * 0.06f;
            float stripY = margin + (stripH - (frameH + band * 2 + stripH * 0.02f)) / 2;
            float sH = frameH + band * 2 + stripH * 0.02f;
            float frameY = stripY + band + stripH * 0.01f;
            float pitch = (sbs ? frameW : frameW) + gap;      // distance between two frames on the strip
            float offset = slide * pitch;                      // strip moving left after a new frame
            float gateX = (W - gateW) / 2;

            // ---- film base with soft edges and a slight curl shadow
            var strip = new RectangleF(-4, stripY, W + 8, sH);
            using (var b = new LinearGradientBrush(strip, FilmEdge, FilmBase, 90f))
            {
                var bl = new ColorBlend { Positions = new[] { 0f, 0.12f, 0.5f, 0.88f, 1f }, Colors = new[] { FilmEdge, FilmBase, Color.FromArgb(18, 15, 12), FilmBase, FilmEdge } };
                b.InterpolationColors = bl;
                g.FillRectangle(b, strip);
            }
            using (var pen = new Pen(Color.FromArgb(60, 214, 160, 70))) { g.DrawLine(pen, 0, stripY, W, stripY); g.DrawLine(pen, 0, stripY + sH, W, stripY + sH); }

            // ---- sprocket holes (move with the strip)
            float holeW = band * 0.42f, holeH = band * 0.50f, holePitch = band * 0.95f;
            float holeR = holeH * 0.22f;
            float phase = ((gateX + offset) % holePitch + holePitch) % holePitch;
            for (float hx = phase - holePitch; hx < W + holePitch; hx += holePitch)
                foreach (float hy in new[] { stripY + (band - holeH) / 2, stripY + sH - band + (band - holeH) / 2 })
                {
                    var hr = new RectangleF(hx, hy, holeW, holeH);
                    using (var p = Theme.Round(hr, holeR))
                    {
                        using (var b = new SolidBrush(Backdrop)) g.FillPath(b, p);
                        using (var pen = new Pen(Color.FromArgb(55, 0, 0, 0), 1.5f)) g.DrawPath(pen, p);
                    }
                }

            // ---- frames: previous ones on the left, unexposed ones on the right
            var mainRect = new RectangleF(gateX + offset, frameY, gateW, frameH);
            int n = 0;
            for (float fx = mainRect.X - pitch; fx + frameW > -pitch && n < history.Count + 3; fx -= pitch, n++)
            {
                Image h = n < history.Count ? history[n] : null;
                FrameCell(g, new RectangleF(fx + (sbs ? frameW + frameH * 0.05f - frameW : 0), frameY, frameW, frameH), h, 0.38f + 0.1f * n, true);
            }
            for (float fx = mainRect.Right + gap; fx < W + pitch; fx += pitch) FrameCell(g, new RectangleF(fx, frameY, frameW, frameH), null, 0, true);

            // ---- edge printing: frame numbers and the brand along the margins (they travel with the frames)
            using (var ef = new Font("Consolas", Math.Max(6f, band * 0.26f / (g.DpiY / 72f)), FontStyle.Bold))
            using (var eb = new SolidBrush(Color.FromArgb(200, EdgeInk)))
            {
                float ty = stripY + sH - band * 0.98f + (band - ef.GetHeight(g)) / 2 + band * 0.42f;
                int no = Math.Max(1, FrameCount);
                for (int k = -3; k <= 3; k++)
                {
                    float fx = mainRect.X + k * pitch;
                    if (fx > W || fx + frameW < 0) continue;
                    int num = no + k;
                    if (num < 1) continue;
                    string a = num.ToString("0000") + "  FFMPEG FORGE LITE";
                    string b2 = (k % 2 == 0 ? "35MM  ·  LIVE PREVIEW" : "SAFETY FILM  ·  " + num.ToString("0000") + "A");
                    g.DrawString(a, ef, eb, fx + frameH * 0.04f, stripY + band * 0.80f - ef.GetHeight(g) * 0.15f);
                    g.DrawString(b2, ef, eb, fx + frameH * 0.04f, ty - ef.GetHeight(g) * 0.95f);
                }
            }

            // ---- the gate: the live frame
            if (before == null && after == null)
            {
                FrameCell(g, mainRect, null, 0, true);
                TextRenderer.DrawText(g, "The film starts rolling as soon as the first frames are encoded…", Font, Rectangle.Round(mainRect), Color.FromArgb(170, 158, 140),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                lastFrame = RectangleF.Empty;
            }
            else if (sbs)
            {
                var lr = new RectangleF(mainRect.X, frameY, frameW, frameH);
                var rr = new RectangleF(mainRect.Right - frameW, frameY, frameW, frameH);
                FrameCell(g, lr, before, 0, false);
                FrameCell(g, rr, after, 0, false);
                Label(g, "ORIGINAL", lr.X + 10, lr.Y + 10, false, Color.FromArgb(170, 160, 145));
                Label(g, after != null ? "FINAL" : "FINAL  (waiting…)", rr.Right - 10, rr.Y + 10, true, Theme.Gold);
                lastFrame = RectangleF.Empty;
            }
            else
            {
                var r = mainRect;
                using (var p = Theme.Round(r, Math.Max(2, r.Height * 0.025f)))
                {
                    using (var b = new SolidBrush(Color.FromArgb(6, 5, 4))) g.FillPath(b, p);
                    var st = g.Save(); g.SetClip(p, CombineMode.Intersect);
                    float sx = r.X + r.Width * Split;
                    if (after != null) Cover(g, after, r);
                    if (before != null)
                    {
                        var s2 = g.Save();
                        g.SetClip(new RectangleF(r.X, r.Y, (after != null ? sx : r.Right) - r.X, r.Height), CombineMode.Intersect);
                        Cover(g, before, r);
                        g.Restore(s2);
                    }
                    // projector vignette
                    using (var vp = new GraphicsPath())
                    {
                        vp.AddEllipse(r.X - r.Width * 0.18f, r.Y - r.Height * 0.25f, r.Width * 1.36f, r.Height * 1.5f);
                        using (var vb = new PathGradientBrush(vp) { CenterColor = Color.FromArgb(0, 0, 0, 0), SurroundColors = new[] { Color.FromArgb(120, 0, 0, 0) } })
                        { vb.FocusScales = new PointF(0.62f, 0.55f); g.FillRectangle(vb, r); }
                    }
                    g.Restore(st);
                    using (var pen = new Pen(Color.FromArgb(90, 0, 0, 0), 2f)) g.DrawPath(pen, p);
                    if (after != null && before != null)
                    {
                        using (var pen = new Pen(Color.FromArgb(235, 245, 230, 200), 2)) g.DrawLine(pen, sx, r.Top, sx, r.Bottom);
                        float cy = r.Y + r.Height / 2, hr = Math.Max(11, r.Height * 0.035f);
                        using (var b = new SolidBrush(Color.FromArgb(245, 238, 222))) g.FillEllipse(b, sx - hr, cy - hr, hr * 2, hr * 2);
                        using (var pen = new Pen(Theme.Gold, 2)) g.DrawEllipse(pen, sx - hr, cy - hr, hr * 2, hr * 2);
                        using (var b = new SolidBrush(Theme.GoldDeep))
                        {
                            float a = hr * 0.45f;
                            g.FillPolygon(b, new[] { new PointF(sx - a * 0.3f, cy - a), new PointF(sx - a * 1.25f, cy), new PointF(sx - a * 0.3f, cy + a) });
                            g.FillPolygon(b, new[] { new PointF(sx + a * 0.3f, cy - a), new PointF(sx + a * 1.25f, cy), new PointF(sx + a * 0.3f, cy + a) });
                        }
                    }
                }
                Label(g, "ORIGINAL", r.X + 10, r.Y + 10, false, Color.FromArgb(170, 160, 145));
                Label(g, after != null ? "FINAL" : "FINAL  (waiting…)", r.Right - 10, r.Y + 10, true, Theme.Gold);
                lastFrame = r;
            }
        }
    }
}
