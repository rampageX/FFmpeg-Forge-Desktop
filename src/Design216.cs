using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace FFmpegForge
{
    /// 2.1.6 design: artwork (embedded JPEG), cached cover-scaled backgrounds.
    public static class Art
    {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>();
        public static Image Get(string name)
        {
            Image im;
            if (cache.TryGetValue(name, out im)) return im;
            try
            {
                using (var s = typeof(Art).Assembly.GetManifestResourceStream(name))
                    if (s != null) { var ms = new MemoryStream(); s.CopyTo(ms); ms.Position = 0; im = Image.FromStream(ms); }
            }
            catch { im = null; }
            cache[name] = im;
            return im;
        }

        /// Image scaled to cover (w, h), cropped around anchorX (0..1) / anchorY.
        public static Bitmap Cover(Image src, int w, int h, float anchorX, float anchorY)
        {
            if (src == null || w < 2 || h < 2) return null;
            var bmp = new Bitmap(w, h);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float sc = Math.Max(w / (float)src.Width, h / (float)src.Height);
                float sw = src.Width * sc, sh = src.Height * sc;
                float x = (w - sw) * anchorX, y = (h - sh) * anchorY;
                g.DrawImage(src, new RectangleF(x, y, sw, sh));
            }
            return bmp;
        }
    }

    /// Cached cover background for a control (re-scaled only when the size changes).
    public class ArtCache
    {
        readonly string name; readonly float ax, ay;
        Bitmap bmp; Size size;
        public ArtCache(string name, float ax, float ay) { this.name = name; this.ax = ax; this.ay = ay; }
        public Bitmap For(Size s)
        {
            if (bmp != null && s == size) return bmp;
            if (bmp != null) bmp.Dispose();
            size = s;
            bmp = Art.Cover(Art.Get(name), s.Width, s.Height, ax, ay);
            return bmp;
        }
    }

    /// Section title with a gold icon (OUTPUT FORMAT, SCALE, SEEDVR2…).
    public class SectionHeader : Control
    {
        public string IconName;
        public SectionHeader(string text, string icon)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Text = text; IconName = icon;
            Font = new Font("Segoe UI Semibold", 10.5f);
            ForeColor = Theme.Gold;
            BackColor = Color.Transparent;
            Height = Theme.S(34);
            Dock = DockStyle.Fill;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }

        public static string IconFor(string title)
        {
            string t = (title ?? "").ToUpperInvariant();
            if (t.StartsWith("OUTPUT")) return "stack";
            if (t.StartsWith("VIDEO")) return "film";
            if (t.StartsWith("AUDIO TRACKS")) return "speaker";
            if (t.StartsWith("AUDIO")) return "note";
            if (t.StartsWith("IMAGE")) return "image";
            if (t.StartsWith("SCALE")) return "frame";
            if (t.StartsWith("DENOISER")) return "denoise";
            if (t.StartsWith("OPTISCALER")) return "sliders";
            if (t.StartsWith("NVIDIA")) return "chip";
            if (t.StartsWith("FILTER")) return "funnel";
            if (t.StartsWith("TRIM")) return "scissors";
            if (t.StartsWith("DESTINATION")) return "folder";
            if (t.Contains("VISIONNEXT")) return "cube";
            if (t.StartsWith("PYTHON")) return "code";
            if (t.StartsWith("SEEDVR2")) return "sparkle";
            if (t.Contains("SPATIAL")) return "speaker";
            return "dot";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int isz = Theme.S(22);
            Icons.Draw(g, IconName, new RectangleF(0, (Height - isz) / 2f, isz, isz), Theme.Gold);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(isz + Theme.S(14), 0, Width - isz - Theme.S(14), Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// Status text with a coloured dot (green ready, gold working, red error).
    public class DotLabel : Label
    {
        public DotLabel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        Color Dot
        {
            get
            {
                string t = Text ?? "";
                if (t.StartsWith("Convert") || t.StartsWith("Writing") || t.StartsWith("Measur") || t.Contains("%")) return Theme.Gold;
                if (t.Contains("failed") || t.StartsWith("Error") || t.Contains("not found")) return Theme.Error;
                return Theme.Success;
            }
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int d = Theme.S(12);
            var c = Dot;
            var dr = new RectangleF(Theme.S(2), (Height - d) / 2f, d, d);
            using (var gl = new GraphicsPath())
            {
                var gr = RectangleF.Inflate(dr, d * 0.6f, d * 0.6f);
                gl.AddEllipse(gr);
                using (var pb = new PathGradientBrush(gl) { CenterColor = Color.FromArgb(90, c), SurroundColors = new[] { Color.FromArgb(0, c) } }) g.FillPath(pb, gl);
            }
            using (var b = new SolidBrush(c)) g.FillEllipse(b, dr);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(d + Theme.S(14), 0, Width - d - Theme.S(14), Height), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// Activity log: one row per message with a status icon, the time and the text (details in parentheses dimmed).
    public class LogView : Control
    {
        class Entry { public DateTime Time; public string Text; public Color Color; public string Icon; public bool Sub; }
        readonly List<Entry> items = new List<Entry>();
        int top;              // first visible row
        int selected = -1;
        bool dragThumb; int dragOffset;
        readonly ToolTip tip = new ToolTip { AutoPopDelay = 20000, InitialDelay = 400 };
        int tipRow = -1;
        public Font MonoFont;

        public LogView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            Font = new Font("Segoe UI", 9.5f);
            TabStop = true;
            var menu = new ContextMenuStrip { ShowImageMargin = false, BackColor = Theme.Card, ForeColor = Theme.Text };
            menu.Items.Add("Copy line", null, (s, e) => CopyLine());
            menu.Items.Add("Copy all", null, (s, e) => { try { Clipboard.SetText(AllText); } catch { } });
            menu.Items.Add("Clear log", null, (s, e) => Clear());
            ContextMenuStrip = menu;
        }

        int RowH { get { return Theme.S(29); } }
        int Visible { get { return Math.Max(1, Height / RowH); } }
        int MaxTop { get { return Math.Max(0, items.Count - Visible); } }
        public int Count { get { return items.Count; } }

        public string AllText
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var it in items) sb.AppendLine((it.Sub ? "         " : it.Time.ToString("HH:mm:ss") + " ") + it.Text);
                return sb.ToString();
            }
        }
        public override string Text { get { return AllText; } set { } }

        public void Clear() { items.Clear(); top = 0; selected = -1; Invalidate(); }

        public void Add(string text, Color c)
        {
            if (text == null) return;
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.Trim().Length == 0) continue;
                bool atEnd = top >= MaxTop;
                var e = new Entry { Time = DateTime.Now, Color = c };
                e.Sub = raw.StartsWith("   ");
                e.Text = e.Sub ? raw.Trim() : raw;
                e.Icon = e.Sub ? null : IconFor(c, raw);
                if (!e.Sub)
                {
                    // the icon replaces the leading symbol of a line (✓ ✗ ▶ ⏹ ✦ …)
                    string t = e.Text.TrimStart();
                    if (t.Length > 1 && "✓✗▶⏹✦●▲⚠".IndexOf(t[0]) >= 0) t = t.Substring(1).TrimStart();
                    e.Text = t;
                }
                items.Add(e);
                if (items.Count > 6000) { items.RemoveRange(0, 1000); if (selected >= 0) selected = Math.Max(-1, selected - 1000); }
                if (atEnd) top = MaxTop;
            }
            Invalidate();
        }

        static string IconFor(Color c, string text)
        {
            string t = text.TrimStart();
            if (c.ToArgb() == Theme.Success.ToArgb() || t.StartsWith("✓")) return "check";
            if (c.ToArgb() == Theme.Error.ToArgb() || t.StartsWith("✗")) return "error";
            if (c.ToArgb() == Theme.Gold.ToArgb() || c.ToArgb() == Theme.Accent1.ToArgb()) return t.StartsWith("━") || t.StartsWith("✦") ? "sparkle" : "warn";
            if (t.StartsWith("▶")) return "play";
            return "info";
        }

        static Color IconColor(string icon)
        {
            switch (icon)
            {
                case "check": return Theme.Success;
                case "error": return Theme.Error;
                case "warn": case "sparkle": return Theme.Gold;
                case "play": return Theme.Blue;
                default: return Color.FromArgb(70, 140, 250);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int rh = RowH, n = Visible + 1;
            int sbw = items.Count > Visible ? Theme.S(10) : 0;
            int w = Width - sbw - Theme.S(6);
            using (var fd = new Font(Font.FontFamily, Font.Size - 0.5f))
            using (var sep = new Pen(Color.FromArgb(30, 36, 48)))
                for (int i = 0; i < n; i++)
                {
                    int idx = top + i;
                    if (idx >= items.Count) break;
                    var it = items[idx];
                    int y = i * rh;
                    if (idx == selected) using (var sb2 = new SolidBrush(Theme.Selection)) g.FillRectangle(sb2, 0, y, w, rh);
                    g.DrawLine(sep, Theme.S(12), y + rh - 1, w - Theme.S(6), y + rh - 1);
                    int x = Theme.S(16);
                    int isz = Theme.S(17);
                    if (it.Icon != null)
                    {
                        if (it.Icon == "play")
                            using (var b = new SolidBrush(IconColor("play"))) g.FillPolygon(b, new[] { new PointF(x + isz * 0.22f, y + (rh - isz) / 2f + isz * 0.12f), new PointF(x + isz * 0.92f, y + rh / 2f), new PointF(x + isz * 0.22f, y + (rh + isz) / 2f - isz * 0.12f) });
                        else Icons.Draw(g, it.Icon, new RectangleF(x, y + (rh - isz) / 2f, isz, isz), IconColor(it.Icon));
                    }
                    int tx = x + isz + Theme.S(18);
                    if (!it.Sub)
                    {
                        TextRenderer.DrawText(g, it.Time.ToString("HH:mm:ss"), Font, new Rectangle(tx, y, Theme.S(80), rh), Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }
                    int mx = tx + Theme.S(84) + (it.Sub ? Theme.S(14) : 0);
                    var rect = new Rectangle(mx, y, Math.Max(10, w - mx - Theme.S(8)), rh);
                    Color tc = it.Sub ? Blend(it.Color, Theme.Muted) : MsgColor(it.Color);
                    // trailing "(details)" dimmed, like "FFmpeg detected   (version 9.0)"
                    string main = it.Text, det = null;
                    if (!it.Sub && main.EndsWith(")") )
                    {
                        int k = main.LastIndexOf(" (", StringComparison.Ordinal);
                        if (k > 0 && main.Length - k < 60) { det = main.Substring(k + 1); main = main.Substring(0, k); }
                    }
                    var font = it.Sub && MonoFont != null ? MonoFont : Font;
                    var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                    TextRenderer.DrawText(g, main, font, rect, tc, flags);
                    if (det != null)
                    {
                        int mw = TextRenderer.MeasureText(g, main, font, new Size(int.MaxValue, rh), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
                        if (mw + Theme.S(10) < rect.Width)
                            TextRenderer.DrawText(g, det, fd, new Rectangle(rect.X + mw + Theme.S(8), y, rect.Width - mw - Theme.S(8), rh), Theme.Muted, flags);
                    }
                }
            if (sbw > 0)
            {
                var tr = ThumbRect();
                using (var p = Theme.Round(tr, tr.Width / 2f)) using (var b = new SolidBrush(dragThumb ? Color.FromArgb(90, 100, 124) : Color.FromArgb(58, 66, 84))) g.FillPath(b, p);
            }
        }

        static Color MsgColor(Color c)
        {
            if (c.ToArgb() == Theme.Success.ToArgb()) return Theme.Success;
            if (c.ToArgb() == Theme.Error.ToArgb()) return Theme.Error;
            if (c.ToArgb() == Theme.Gold.ToArgb()) return Theme.Gold;
            if (c.ToArgb() == Theme.Muted.ToArgb()) return Color.FromArgb(170, 178, 196);
            if (c.ToArgb() == Theme.Text.ToArgb()) return Theme.Text;
            return c;
        }
        static Color Blend(Color a, Color b) { return Color.FromArgb((a.R * 2 + b.R) / 3, (a.G * 2 + b.G) / 3, (a.B * 2 + b.B) / 3); }

        RectangleF ThumbRect()
        {
            int sbw = Theme.S(6);
            float h = Math.Max(Theme.S(24), Height * Visible / (float)Math.Max(1, items.Count));
            float y = MaxTop == 0 ? 0 : (Height - h) * top / (float)MaxTop;
            return new RectangleF(Width - sbw - Theme.S(4), y + Theme.S(2), sbw, h - Theme.S(4));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            top = Math.Max(0, Math.Min(MaxTop, top - Math.Sign(e.Delta) * 3));
            Invalidate();
            base.OnMouseWheel(e);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            var tr = ThumbRect();
            if (items.Count > Visible && e.X >= Width - Theme.S(14))
            {
                if (tr.Contains(e.Location)) { dragThumb = true; dragOffset = (int)(e.Y - tr.Y); }
                else { top = Math.Max(0, Math.Min(MaxTop, top + (e.Y < tr.Y ? -Visible : Visible))); }
                Invalidate(); return;
            }
            int idx = top + e.Y / RowH;
            selected = idx < items.Count ? idx : -1;
            Invalidate();
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragThumb)
            {
                var tr = ThumbRect();
                float range = Height - tr.Height;
                if (range > 0) top = Math.Max(0, Math.Min(MaxTop, (int)Math.Round((e.Y - dragOffset) / range * MaxTop)));
                Invalidate();
                return;
            }
            int idx = top + e.Y / RowH;
            if (idx != tipRow)
            {
                tipRow = idx;
                if (idx < items.Count && items[idx].Text.Length > 60) tip.SetToolTip(this, items[idx].Text.Length > 1500 ? items[idx].Text.Substring(0, 1500) + "…" : items[idx].Text);
                else tip.SetToolTip(this, null);
            }
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragThumb = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { CopyLine(); base.OnMouseDoubleClick(e); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.C) { if (selected >= 0) CopyLine(); else try { Clipboard.SetText(AllText); } catch { } }
            if (e.Control && e.KeyCode == Keys.A) { try { Clipboard.SetText(AllText); } catch { } }
            if (e.KeyCode == Keys.End) { top = MaxTop; Invalidate(); }
            if (e.KeyCode == Keys.Home) { top = 0; Invalidate(); }
            if (e.KeyCode == Keys.PageDown) { top = Math.Min(MaxTop, top + Visible); Invalidate(); }
            if (e.KeyCode == Keys.PageUp) { top = Math.Max(0, top - Visible); Invalidate(); }
            base.OnKeyDown(e);
        }
        protected override void OnResize(EventArgs e) { top = Math.Min(top, MaxTop); base.OnResize(e); }
        void CopyLine()
        {
            if (selected < 0 || selected >= items.Count) return;
            var it = items[selected];
            try { Clipboard.SetText(it.Time.ToString("HH:mm:ss") + " " + it.Text); } catch { }
        }
    }

    /// Dark title bar on Windows 10 / 11 (DWM immersive dark mode); ignored elsewhere.
    public static class DarkTitle
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        public static void Apply(IntPtr hwnd)
        {
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) return;
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
                int caption = 0x00130E0C;   // COLORREF 0x00BBGGRR: RGB(12, 14, 19), Windows 11 caption colour
                DwmSetWindowAttribute(hwnd, 35, ref caption, 4);
            }
            catch { }
        }
    }
}
