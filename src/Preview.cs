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
            return Regex.IsMatch(chain, @"(?:^|,)\\s*tonemap\\s*=", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(chain, @"\\btonemapping\\s*=", RegexOptions.IgnoreCase) ||
                   Regex.IsMatch(chain, @"\\bcolor_trc\\s*=\\s*(?:bt709|iec61966-2-1)\\b", RegexOptions.IgnoreCase);
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

    /// Before / after comparison window: a slider over the two pictures, or side by side.
    public class PreviewWindow : Form
    {
        readonly CompareView view;
        readonly Label info;
        readonly FlatBtn modeBtn;
        readonly Timer timer;
        DateTime tBefore, tAfter;
        string folderIn, folderOut, staticBefore;
        DateTime tFolder;

        public PreviewWindow()
        {
            Text = "FFmpeg Forge — Preview (before / after)";
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.Manual;
            Size = new Size(Theme.S(980), Theme.S(640));
            MinimumSize = new Size(Theme.S(480), Theme.S(320));
            ShowInTaskbar = false;
            KeyPreview = true;

            view = new CompareView { Dock = DockStyle.Fill };
            var bar = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(40), BackColor = Theme.Panel, Padding = new Padding(Theme.S(10), Theme.S(6), Theme.S(10), Theme.S(6)) };
            info = new Label { Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 8.5f), Text = "Waiting for the first frame…" };
            modeBtn = new FlatBtn("Side by side") { Dock = DockStyle.Right, Width = Theme.S(130) };
            modeBtn.Click += (s, e) => ToggleMode();
            bar.Controls.Add(info);
            bar.Controls.Add(modeBtn);
            Controls.Add(view);
            Controls.Add(bar);

            timer = new Timer { Interval = 400 };
            timer.Tick += (s, e) => Poll();
            timer.Start();
            FormClosed += (s, e) => { timer.Stop(); view.SetImages(null, null); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Left) { view.Split = Math.Max(0, view.Split - 0.05f); view.Invalidate(); }
                if (e.KeyCode == Keys.Right) { view.Split = Math.Min(1, view.Split + 0.05f); view.Invalidate(); }
                if (e.KeyCode == Keys.Space) ToggleMode();
            };
        }

        void ToggleMode() { view.SideBySide = !view.SideBySide; modeBtn.Text = view.SideBySide ? "Slider" : "Side by side"; view.Invalidate(); UpdateInfo(); }

        string status = "";
        public void SetStatus(string s) { status = s ?? ""; UpdateInfo(); }

        /// New job: forget the old pictures. staticBefore = the source picture (picture → video jobs).
        public void NewJob(string title, string staticBeforeImage, string inFolder, string outFolder)
        {
            tBefore = tAfter = tFolder = DateTime.MinValue;
            folderIn = inFolder; folderOut = outFolder; staticBefore = staticBeforeImage;
            view.SetImages(null, null);
            view.Title = title;
            Image sb = staticBefore != null ? Load(staticBefore) : null;
            if (sb != null) view.SetImages(sb, null);
            UpdateInfo();
        }

        public void SetFolders(string inFolder, string outFolder) { folderIn = inFolder; folderOut = outFolder; }

        void UpdateInfo()
        {
            info.Text = (view.Title ?? "") + (status.Length > 0 ? "   ·   " + status : "") +
                        "   ·   drag the line (or ← →) to compare, Space = " + (view.SideBySide ? "slider" : "side by side");
        }

        static Image Load(string path)
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
                if (fa.Exists && fa.LastWriteTimeUtc != tAfter) { na = Load(fa.FullName); if (na != null) { tAfter = fa.LastWriteTimeUtc; changed = true; } }
                if (fb.Exists && fb.LastWriteTimeUtc != tBefore) { nb = Load(fb.FullName); if (nb != null) { tBefore = fb.LastWriteTimeUtc; changed = true; } }
                // AI upscalers working on picture folders (VisionNext): newest upscaled frame + its source frame
                if (na == null && tAfter == DateTime.MinValue && folderOut != null && Directory.Exists(folderOut))
                {
                    var last = new DirectoryInfo(folderOut).GetFiles("*.png", SearchOption.AllDirectories).OrderBy(f => f.Name).LastOrDefault();
                    if (last != null && last.LastWriteTimeUtc != tFolder && DateTime.UtcNow - last.LastWriteTimeUtc > TimeSpan.FromMilliseconds(300))
                    {
                        var m = Regex.Match(last.Name, @"^\d+");
                        string src = m.Success && folderIn != null ? Path.Combine(folderIn, m.Value + ".png") : null;
                        var a = Load(last.FullName);
                        var b = src != null && File.Exists(src) ? Load(src) : null;
                        if (a != null) { tFolder = last.LastWriteTimeUtc; na = a; nb = b; changed = true; }
                    }
                }
                if (changed) view.SetImages(nb ?? view.TakeBefore(), na ?? view.TakeAfter());
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

    /// Draws the two pictures: a draggable vertical split (slider) or two panes side by side.
    public class CompareView : Control
    {
        Image before, after;
        public float Split = 0.5f;
        public bool SideBySide;
        public string Title;
        bool dragging;

        public CompareView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            Cursor = Cursors.VSplit;
        }

        public Image TakeBefore() { var b = before; before = null; return b; }
        public Image TakeAfter() { var a = after; after = null; return a; }

        public void SetImages(Image b, Image a)
        {
            if (before != null && !ReferenceEquals(before, b)) before.Dispose();
            if (after != null && !ReferenceEquals(after, a)) after.Dispose();
            before = b; after = a;
            Invalidate();
        }

        static RectangleF FitRect(Image img, Rectangle box)
        {
            if (img == null || img.Width == 0 || img.Height == 0) return box;
            float s = Math.Min((float)box.Width / img.Width, (float)box.Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            return new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
        }

        protected override void OnMouseDown(MouseEventArgs e) { dragging = true; Move(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Move(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; }
        new void Move(int x)
        {
            if (SideBySide) return;
            var r = FitRect(after ?? before, ClientRectangle);
            if (r.Width <= 0) return;
            Split = Math.Max(0, Math.Min(1, (x - r.X) / r.Width));
            Invalidate();
        }

        static void Tag(Graphics g, string text, float x, float y, bool right)
        {
            using (var f = new Font("Segoe UI Semibold", 9f))
            {
                var sz = g.MeasureString(text, f);
                var r = new RectangleF(right ? x - sz.Width - 14 : x, y, sz.Width + 14, sz.Height + 6);
                using (var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(b, r);
                using (var t = new SolidBrush(Color.White)) g.DrawString(text, f, t, r.X + 7, r.Y + 3);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (before == null && after == null)
            {
                TextRenderer.DrawText(g, "The preview appears here as soon as the first frames are encoded…", Font, ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            if (SideBySide)
            {
                int half = ClientSize.Width / 2;
                var lb = new Rectangle(0, 0, half - 2, ClientSize.Height);
                var rb = new Rectangle(half + 2, 0, ClientSize.Width - half - 2, ClientSize.Height);
                if (before != null) g.DrawImage(before, FitRect(before, lb));
                if (after != null) g.DrawImage(after, FitRect(after, rb));
                Tag(g, "ORIGINAL", 10, 10, false);
                Tag(g, "FINAL", ClientSize.Width - 10, 10, true);
                return;
            }
            // slider: both pictures in the same rectangle (the final picture's shape)
            var main = after ?? before;
            var rect = FitRect(main, ClientRectangle);
            float sx = rect.X + rect.Width * Split;
            if (after != null)
            {
                g.DrawImage(after, rect);
            }
            if (before != null)
            {
                var st = g.Save();
                g.SetClip(new RectangleF(rect.X, rect.Y, sx - rect.X, rect.Height));
                g.DrawImage(before, rect);
                g.Restore(st);
            }
            using (var p = new Pen(Color.White, 2)) g.DrawLine(p, sx, rect.Top, sx, rect.Bottom);
            float cy = rect.Y + rect.Height / 2;
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, sx - 13, cy - 13, 26, 26);
            using (var b = new SolidBrush(Theme.Accent1))
            {
                g.FillPolygon(b, new[] { new PointF(sx - 3, cy - 6), new PointF(sx - 9, cy), new PointF(sx - 3, cy + 6) });
                g.FillPolygon(b, new[] { new PointF(sx + 3, cy - 6), new PointF(sx + 9, cy), new PointF(sx + 3, cy + 6) });
            }
            Tag(g, "ORIGINAL", rect.X + 10, rect.Y + 10, false);
            Tag(g, after != null ? "FINAL" : "FINAL (waiting…)", rect.Right - 10, rect.Y + 10, true);
        }
    }
}
