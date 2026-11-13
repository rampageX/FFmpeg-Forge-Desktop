using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace FFmpegForge
{
    public static class Theme
    {
        // FFmpeg Forge 2.0 — "Royal blue & gold" premium dark theme
        public static readonly Color Bg = Color.FromArgb(12, 14, 19);            // window / page background
        public static readonly Color Panel = Color.FromArgb(21, 25, 33);         // cards
        public static readonly Color Card = Color.FromArgb(29, 34, 45);          // inputs inside cards
        public static readonly Color CardHover = Color.FromArgb(38, 45, 60);
        public static readonly Color Border = Color.FromArgb(44, 51, 66);
        public static readonly Color Text = Color.FromArgb(236, 239, 245);
        public static readonly Color Muted = Color.FromArgb(138, 148, 168);
        public static readonly Color Gold = Color.FromArgb(245, 184, 61);
        public static readonly Color GoldDeep = Color.FromArgb(222, 146, 28);
        public static readonly Color Blue = Color.FromArgb(59, 130, 246);
        public static readonly Color BlueDeep = Color.FromArgb(37, 84, 220);
        public static readonly Color Accent1 = Gold;                              // titles, highlights, warnings
        public static readonly Color Accent2 = Blue;                              // actions
        public static readonly Color Success = Color.FromArgb(52, 211, 153);
        public static readonly Color Error = Color.FromArgb(248, 96, 112);
        public static readonly Color Selection = Color.FromArgb(28, 43, 72);
        public static readonly Color Side = Color.FromArgb(9, 11, 15);            // sidebar

        public static float Scale = 1f;
        public static int S(int px) { return (int)Math.Round(px * Scale); }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            if (d > r.Height) d = r.Height;
            if (d > r.Width) d = r.Width;
            if (d < 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static RectangleF Safe(RectangleF r) { if (r.Width < 1) r.Width = 1; if (r.Height < 1) r.Height = 1; return r; }

        /// Primary actions: royal blue.
        public static LinearGradientBrush AccentBrush(RectangleF r)
        {
            return new LinearGradientBrush(Safe(r), BlueDeep, Blue, LinearGradientMode.Horizontal);
        }

        /// Highlights: gold.
        public static LinearGradientBrush GoldBrush(RectangleF r)
        {
            return new LinearGradientBrush(Safe(r), Gold, GoldDeep, LinearGradientMode.ForwardDiagonal);
        }

        /// Progress: blue to gold.
        public static LinearGradientBrush ProgressBrush(RectangleF r)
        {
            var b = new LinearGradientBrush(Safe(r), Blue, Gold, LinearGradientMode.Horizontal);
            var cb = new ColorBlend(3);
            cb.Colors = new[] { BlueDeep, Blue, Gold };
            cb.Positions = new[] { 0f, 0.55f, 1f };
            b.InterpolationColors = cb;
            return b;
        }
    }

    /// Rounded card (settings section): rounded corners by region, gold title drawn by the section's label.
    public class CardTable : TableLayoutPanel
    {
        public CardTable()
        {
            DoubleBuffered = true;
            BackColor = Theme.Panel;
        }
        public bool Border = true;
        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width > 2 && Height > 2)
                using (var p = Theme.Round(new RectangleF(0, 0, Width, Height), Theme.S(12))) Region = new Region(p);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Border || Width < 4 || Height < 4) return;
            var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Theme.S(12)))
            using (var pen = new Pen(Color.FromArgb(38, 46, 62))) g.DrawPath(pen, p);
            using (var hl = new LinearGradientBrush(new RectangleF(0, 0, Width, 2), Color.FromArgb(0, 255, 255, 255), Color.FromArgb(28, 255, 214, 140), LinearGradientMode.Horizontal))
                g.FillRectangle(hl, Theme.S(14), 0, Width - Theme.S(28), 1);
        }
    }

    /// Rounded flat button with a gradient "Primary" variant and a "Toggled" state.
    public class FlatBtn : Control
    {
        bool hover, down;
        public bool Primary;
        public bool Gold;
        public bool Glass;           // translucent button over artwork (header)
        public bool Glow;            // CONVERT: blue → gold gradient with a light rim
        public string Icon;          // vector icon drawn before the text (Icons.Draw name)
        public Color? IconColor;
        bool toggled;
        public bool Toggled { get { return toggled; } set { toggled = value; Invalidate(); } }

        public FlatBtn(string text)
        {
            Text = text;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            Cursor = Cursors.Hand;
            Font = new Font("Segoe UI Semibold", 9.5f);
            ForeColor = Theme.Text;
            BackColor = Color.Transparent;
            Height = Theme.S(34);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fc;
            using (var path = Theme.Round(r, Theme.S(9)))
            {
                if (Glow && Enabled)
                {
                    using (var b = new LinearGradientBrush(r, Color.FromArgb(37, 99, 235), Color.FromArgb(245, 184, 61), LinearGradientMode.Horizontal))
                    {
                        var cb = new ColorBlend(4);
                        cb.Colors = new[] { Color.FromArgb(37, 99, 235), Color.FromArgb(59, 130, 246), Color.FromArgb(96, 140, 230), Color.FromArgb(245, 184, 61) };
                        cb.Positions = new[] { 0f, 0.55f, 0.82f, 1f };
                        b.InterpolationColors = cb;
                        g.FillPath(b, path);
                    }
                    using (var hl = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height / 2 + 1), Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                        g.FillPath(hl, path);
                    if (hover) using (var w = new SolidBrush(Color.FromArgb(down ? 36 : 22, 255, 255, 255))) g.FillPath(w, path);
                    using (var pen = new Pen(Color.FromArgb(150, 255, 214, 140), 1.2f)) g.DrawPath(pen, path);
                    fc = Color.White;
                }
                else if (Glass && Enabled)
                {
                    using (var b = new SolidBrush(Color.FromArgb(hover ? 200 : 165, 12, 16, 28))) g.FillPath(b, path);
                    using (var pen = new Pen(hover ? Color.FromArgb(200, Theme.Gold) : Color.FromArgb(70, 255, 255, 255), 1.2f)) g.DrawPath(pen, path);
                    fc = ForeColor;
                }
                else if (!Enabled)
                {
                    using (var b = new SolidBrush(Color.FromArgb(24, 28, 37))) g.FillPath(b, path);
                    fc = Color.FromArgb(86, 94, 112);
                }
                else if (Gold)
                {
                    using (var b = Theme.GoldBrush(r)) g.FillPath(b, path);
                    if (hover) using (var w = new SolidBrush(Color.FromArgb(down ? 40 : 28, 255, 255, 255))) g.FillPath(w, path);
                    fc = Color.FromArgb(24, 18, 6);
                }
                else if (Primary)
                {
                    using (var b = Theme.AccentBrush(r)) g.FillPath(b, path);
                    // soft top light
                    using (var hl = new LinearGradientBrush(new RectangleF(r.X, r.Y, r.Width, r.Height / 2 + 1), Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                        g.FillPath(hl, path);
                    if (hover) using (var w = new SolidBrush(Color.FromArgb(down ? 36 : 22, 255, 255, 255))) g.FillPath(w, path);
                    fc = Color.White;
                }
                else if (toggled)
                {
                    using (var b = new SolidBrush(Theme.Selection)) g.FillPath(b, path);
                    using (var pen = new Pen(Theme.Gold, Theme.S(1) * 1.4f)) g.DrawPath(pen, path);
                    fc = Theme.Gold;
                }
                else
                {
                    using (var b = new SolidBrush(down ? Theme.Border : hover ? Theme.CardHover : Theme.Card)) g.FillPath(b, path);
                    using (var pen = new Pen(hover ? Color.FromArgb(70, 82, 106) : Theme.Border)) g.DrawPath(pen, path);
                    fc = ForeColor;
                }
            }
            if (string.IsNullOrEmpty(Icon))
            {
                TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), fc,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                return;
            }
            float isz = Math.Min(Height * 0.5f, Theme.S(20));
            if (Font.Size > 10.5f) isz = Math.Min(Height * 0.48f, Theme.S(24));
            int gap = Theme.S(10);
            var ts = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            float total = isz + (ts.Width > 0 ? gap + ts.Width : 0);
            float x0 = Math.Max(Theme.S(10), (Width - total) / 2f);
            Color ic = !Enabled ? fc : (IconColor ?? fc);
            Icons.Draw(g, Icon, new RectangleF(x0, (Height - isz) / 2f, isz, isz), ic);
            if (ts.Width > 0)
                TextRenderer.DrawText(g, Text, Font, new Rectangle((int)(x0 + isz + gap), 0, (int)Math.Max(10, Width - (x0 + isz + gap) - Theme.S(6)), Height), fc,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }
    }

    /// Gradient progress bar (blue to gold).
    public class GradientBar : Control
    {
        double value;
        public double Value { get { return value; } set { this.value = Math.Max(0, Math.Min(1, value)); Invalidate(); } }
        public string Caption = "";

        public GradientBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(8);
            BackColor = Theme.Side;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            using (var p = Theme.Round(r, Height / 2f))
            using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
            if (value > 0)
            {
                var f = new RectangleF(0, 0, Math.Max(Height, (float)((Width - 1) * value)), Height - 1);
                using (var p = Theme.Round(f, Height / 2f))
                using (var b = Theme.ProgressBrush(new RectangleF(0, 0, Width, Height))) g.FillPath(b, p);
            }
        }
    }

    /// FFmpeg Forge logo: gold rounded tile with a navy lightning bolt.
    public static class Header
    {
        public static void DrawLogo(Graphics g, RectangleF lr)
        {
            // glow
            using (var glow = new GraphicsPath())
            {
                var gr = RectangleF.Inflate(lr, lr.Width * 0.35f, lr.Height * 0.35f);
                glow.AddEllipse(gr);
                using (var pb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, Theme.Gold), SurroundColors = new[] { Color.FromArgb(0, Theme.Gold) } })
                    g.FillPath(pb, glow);
            }
            using (var p = Theme.Round(lr, lr.Width * 0.26f))
            {
                using (var b = new LinearGradientBrush(lr, Color.FromArgb(255, 214, 120), Theme.GoldDeep, 50f)) g.FillPath(b, p);
                using (var hl = new LinearGradientBrush(new RectangleF(lr.X, lr.Y, lr.Width, lr.Height / 2), Color.FromArgb(70, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), LinearGradientMode.Vertical))
                    g.FillPath(hl, p);
            }
            float u = lr.Width / 10f;
            var bolt = new PointF[] {
                new PointF(lr.X + 5.8f*u, lr.Y + 1.5f*u),
                new PointF(lr.X + 2.5f*u, lr.Y + 5.7f*u),
                new PointF(lr.X + 4.8f*u, lr.Y + 5.7f*u),
                new PointF(lr.X + 3.9f*u, lr.Y + 8.6f*u),
                new PointF(lr.X + 7.5f*u, lr.Y + 4.2f*u),
                new PointF(lr.X + 5.2f*u, lr.Y + 4.2f*u),
            };
            using (var w = new SolidBrush(Color.FromArgb(14, 22, 44))) g.FillPolygon(w, bolt);
        }
    }

    /// Application header (2.1.6 design): cinematic artwork, big gold logo, "FFmpeg FORGE", version chip,
    /// FFmpeg status pill (StatusPill) — the "FFmpeg location…" button is added by the main window.
    public class TopHeader : Control
    {
        public string Right1 = "";
        public Color Right1Color = Theme.Muted;
        public int FilterCount, FormatCount;
        public string Version = "2.3.2";
        public readonly FlatBtn StatusPill;


        public TopHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(126);
            BackColor = Theme.Side;
            StatusPill = new FlatBtn("Detecting FFmpeg…") { Glass = true, Icon = "search", Height = Theme.S(46), Font = new Font("Segoe UI", 9.5f), Cursor = Cursors.Default };
            StatusPill.IconColor = Theme.Gold;
            Controls.Add(StatusPill);
        }

        public Action Relayout;
        string lastR1; int lastC;
        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            if (Right1 != lastR1 || Right1Color.ToArgb() != lastC) { lastR1 = Right1; lastC = Right1Color.ToArgb(); if (Relayout != null) Relayout(); }
            base.OnInvalidated(e);
        }

        /// Places the status pill to the left of x (the FFmpeg location button).
        public void PlacePill(int rightEdge)
        {
            string t = Right1 ?? "";
            t = t.TrimStart('●', '▲', ' ');
            if (t.Length == 0) t = "Detecting FFmpeg…";
            bool ok = Right1Color.ToArgb() == Theme.Success.ToArgb(), bad = Right1Color.ToArgb() == Theme.Error.ToArgb();
            StatusPill.Text = t;
            StatusPill.Icon = ok ? "check" : bad ? "warn" : "search";
            StatusPill.IconColor = ok ? Theme.Success : bad ? Theme.Error : Theme.Gold;
            StatusPill.ForeColor = ok ? Color.FromArgb(200, 240, 222) : bad ? Color.FromArgb(255, 190, 196) : Theme.Text;
            int w = TextRenderer.MeasureText(t, StatusPill.Font).Width + Theme.S(74);
            StatusPill.Width = Math.Min(Theme.S(380), Math.Max(Theme.S(170), w));
            StatusPill.Location = new Point(rightEdge - StatusPill.Width - Theme.S(14), (Height - StatusPill.Height) / 2);
            StatusPill.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            // Flat dark header: no decorative artwork or image scaling.
            using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(16, 22, 38), Theme.Side, LinearGradientMode.Horizontal))
                g.FillRectangle(bg, ClientRectangle);

            int ls = Theme.S(76);
            var lr = new RectangleF(Theme.S(40), (Height - ls) / 2f, ls, ls);
            Header.DrawLogo(g, lr);

            float x = lr.Right + Theme.S(28);
            using (var f1 = new Font("Segoe UI", 30f, FontStyle.Bold))
            using (var f2 = new Font("Segoe UI", 11f))
            using (var f3 = new Font("Segoe UI Semibold", 8.5f))
            {
                string a = "FFmpeg", b = " FORGE";
                var sa = g.MeasureString(a, f1, PointF.Empty, StringFormat.GenericTypographic);
                var sb = g.MeasureString(b, f1, PointF.Empty, StringFormat.GenericTypographic);
                float y = Height / 2f - sa.Height + Theme.S(4);
                using (var sh = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) { g.DrawString(a, f1, sh, x + 2, y + 2, StringFormat.GenericTypographic); g.DrawString(b, f1, sh, x + sa.Width + 2, y + 2, StringFormat.GenericTypographic); }
                using (var w = new SolidBrush(Color.White)) g.DrawString(a, f1, w, x, y, StringFormat.GenericTypographic);
                var gr = new RectangleF(x + sa.Width, y, sb.Width, sb.Height);
                using (var br = new LinearGradientBrush(gr, Color.FromArgb(255, 214, 120), Color.FromArgb(232, 150, 30), LinearGradientMode.Vertical))
                    g.DrawString(b, f1, br, x + sa.Width, y, StringFormat.GenericTypographic);
                // version chip
                string v = "VERSION " + Version;
                var vs = TextRenderer.MeasureText(v, f3);
                var vr = new RectangleF(x + sa.Width + sb.Width + Theme.S(20), y + sa.Height / 2f - Theme.S(15), vs.Width + Theme.S(26), Theme.S(30));
                using (var p = Theme.Round(vr, vr.Height / 2))
                {
                    using (var bb = new SolidBrush(Color.FromArgb(150, 14, 12, 8))) g.FillPath(bb, p);
                    using (var pen = new Pen(Theme.Gold, Theme.S(2))) g.DrawPath(pen, p);
                }
                TextRenderer.DrawText(g, v, f3, Rectangle.Round(vr), Theme.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                // subtitle
                float sy = y + sa.Height + Theme.S(12);
                float sx = x + Theme.S(2);
                Action<string, Color> part = (txt, col) =>
                {
                    TextRenderer.DrawText(g, txt, f2, new Point((int)sx, (int)sy), col, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    sx += TextRenderer.MeasureText(g, txt, f2, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                };
                Color light = Color.FromArgb(178, 188, 208), dim = Color.FromArgb(150, 160, 182);
                part("Convert anything into everything.", light);
                part("     Video  •  Audio  •  Image     —     " + (FormatCount > 0 ? FormatCount : Formats.All.Count) + " output formats" + (FilterCount > 0 ? "  •  " + FilterCount + " filters" : ""), dim);
            }
            using (var line = Theme.ProgressBrush(new RectangleF(0, Height - 2, Width, 2)))
                g.FillRectangle(line, 0, Height - 2, Width, 2);
        }
    }

    /// Left navigation sidebar: brand, pages with icons, FFmpeg status.
    public class SideBar : Control
    {
        public class Item { public string Text, Icon; public int Badge; public bool Dim; }
        public readonly System.Collections.Generic.List<Item> Items = new System.Collections.Generic.List<Item>();
        int selected, hoverIdx = -1;
        public int Selected { get { return selected; } set { selected = value; Invalidate(); if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty); } }
        public event EventHandler SelectedChanged;
        public string Right1 = "";
        public Color Right1Color = Theme.Muted;
        public int FilterCount, FormatCount;
        public string Version = "2.3.2";

        public SideBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Width = Theme.S(236);
            BackColor = Theme.Side;
        }

        int Top0 { get { return Theme.S(118); } }
        int ItemH { get { return Theme.S(46); } }
        Rectangle ItemRect(int i) { return new Rectangle(Theme.S(14), Top0 + i * (ItemH + Theme.S(4)), Width - Theme.S(28), ItemH); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = -1;
            for (int i = 0; i < Items.Count; i++) if (ItemRect(i).Contains(e.Location)) h = i;
            if (h != hoverIdx) { hoverIdx = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverIdx = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            for (int i = 0; i < Items.Count; i++) if (ItemRect(i).Contains(e.Location)) { Selected = i; break; }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(14, 17, 25), Theme.Side, LinearGradientMode.Vertical)) g.FillRectangle(bg, ClientRectangle);
            // blue glow behind the brand
            using (var halo = new GraphicsPath())
            {
                halo.AddEllipse(-Theme.S(110), -Theme.S(120), Theme.S(420), Theme.S(260));
                using (var pb = new PathGradientBrush(halo) { CenterColor = Color.FromArgb(46, Theme.Blue), SurroundColors = new[] { Color.FromArgb(0, Theme.Blue) } })
                    g.FillPath(pb, halo);
            }
            using (var pen = new Pen(Color.FromArgb(30, 36, 48))) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);

            // brand
            int ls = Theme.S(42);
            var lr = new RectangleF(Theme.S(22), Theme.S(26), ls, ls);
            Header.DrawLogo(g, lr);
            using (var f1 = new Font("Segoe UI", 13.5f, FontStyle.Bold))
            using (var f2 = new Font("Segoe UI Semibold", 7.5f))
            {
                float x = lr.Right + Theme.S(12), y = lr.Y - Theme.S(2);
                var sa = g.MeasureString("FFmpeg", f1, PointF.Empty, StringFormat.GenericTypographic);
                using (var w = new SolidBrush(Color.White)) g.DrawString("FFmpeg", f1, w, x, y, StringFormat.GenericTypographic);
                var fr = new RectangleF(x + sa.Width + Theme.S(4), y, Theme.S(80), sa.Height);
                using (var gb = Theme.GoldBrush(fr)) g.DrawString("FORGE", f1, gb, fr.X, y, StringFormat.GenericTypographic);
                // version chip
                string v = "VERSION " + Version;
                var vs = g.MeasureString(v, f2);
                var vr = new RectangleF(x, y + sa.Height + Theme.S(5), vs.Width + Theme.S(8), Theme.S(17));
                using (var p = Theme.Round(vr, vr.Height / 2))
                {
                    using (var b = new SolidBrush(Color.FromArgb(40, Theme.Gold))) g.FillPath(b, p);
                    using (var pen = new Pen(Color.FromArgb(120, Theme.Gold))) g.DrawPath(pen, p);
                }
                TextRenderer.DrawText(g, v, f2, Rectangle.Round(vr), Theme.Gold, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            using (var m = new Font("Segoe UI Semibold", 7.5f))
                TextRenderer.DrawText(g, "WORKSPACE", m, new Point(Theme.S(26), Top0 - Theme.S(24)), Color.FromArgb(92, 102, 124));

            // items
            using (var f = new Font("Segoe UI Semibold", 10f))
            using (var fb = new Font("Segoe UI", 7.5f, FontStyle.Bold))
                for (int i = 0; i < Items.Count; i++)
                {
                    var it = Items[i];
                    var r = ItemRect(i);
                    bool sel = i == selected, hov = i == hoverIdx;
                    if (sel || hov)
                        using (var p = Theme.Round(r, Theme.S(10)))
                        {
                            if (sel)
                                using (var b = new LinearGradientBrush(r, Color.FromArgb(34, 52, 92), Color.FromArgb(20, 28, 44), LinearGradientMode.Horizontal)) g.FillPath(b, p);
                            else using (var b = new SolidBrush(Color.FromArgb(20, 25, 35))) g.FillPath(b, p);
                        }
                    if (sel)
                        using (var p = Theme.Round(new RectangleF(r.X, r.Y + Theme.S(10), Theme.S(4), r.Height - Theme.S(20)), Theme.S(2)))
                        using (var gb = Theme.GoldBrush(r)) g.FillPath(gb, p);
                    Color ic = sel ? Theme.Gold : (it.Dim ? Color.FromArgb(70, 78, 96) : Color.FromArgb(150, 160, 182));
                    var irc = new RectangleF(r.X + Theme.S(16), r.Y + (r.Height - Theme.S(20)) / 2f, Theme.S(20), Theme.S(20));
                    Icons.Draw(g, it.Icon, irc, ic);
                    Color tc = sel ? Color.White : (it.Dim ? Color.FromArgb(80, 88, 106) : Color.FromArgb(196, 204, 220));
                    TextRenderer.DrawText(g, it.Text, f, new Rectangle(r.X + Theme.S(48), r.Y, r.Width - Theme.S(48), r.Height), tc, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                    if (it.Badge > 0)
                    {
                        string bt = it.Badge > 999 ? "999+" : it.Badge.ToString();
                        var bs = TextRenderer.MeasureText(bt, fb);
                        var br = new RectangleF(r.Right - Theme.S(14) - Math.Max(Theme.S(22), bs.Width + Theme.S(4)), r.Y + (r.Height - Theme.S(20)) / 2f, Math.Max(Theme.S(22), bs.Width + Theme.S(4)), Theme.S(20));
                        using (var p = Theme.Round(br, br.Height / 2)) using (var b = Theme.AccentBrush(br)) g.FillPath(b, p);
                        TextRenderer.DrawText(g, bt, fb, Rectangle.Round(br), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }

            // status card at the bottom
            var sr = new RectangleF(Theme.S(14), Height - Theme.S(150), Width - Theme.S(28), Theme.S(94));
            using (var p = Theme.Round(sr, Theme.S(12)))
            {
                using (var b = new SolidBrush(Color.FromArgb(18, 22, 31))) g.FillPath(b, p);
                using (var pen = new Pen(Color.FromArgb(34, 40, 54))) g.DrawPath(pen, p);
            }
            using (var f = new Font("Segoe UI Semibold", 7.5f))
                TextRenderer.DrawText(g, "ENGINE", f, new Point((int)sr.X + Theme.S(14), (int)sr.Y + Theme.S(10)), Color.FromArgb(92, 102, 124));
            using (var f = new Font("Segoe UI", 8.5f))
            {
                string st = string.IsNullOrEmpty(Right1) ? "FFmpeg…" : Right1;
                TextRenderer.DrawText(g, st, f, new Rectangle((int)sr.X + Theme.S(12), (int)sr.Y + Theme.S(26), (int)sr.Width - Theme.S(20), Theme.S(20)), Right1Color, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                string cnt = (FormatCount > 0 ? FormatCount : Formats.All.Count) + " formats" + (FilterCount > 0 ? "  ·  " + FilterCount + " filters" : "");
                TextRenderer.DrawText(g, cnt, f, new Point((int)sr.X + Theme.S(14), (int)sr.Y + Theme.S(46)), Theme.Muted);
            }
        }
    }

    /// Vector icons (20 × 20 grid), drawn so they stay sharp at any DPI.
    public static class Icons
    {
        public static void Draw(Graphics g, string name, RectangleF r, Color c)
        {
            if (string.IsNullOrEmpty(name)) return;
            var oldSm = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            float u = r.Width / 20f;
            Func<float, float, PointF> P = (x, y) => new PointF(r.X + x * u, r.Y + y * u);
            Func<float, float, float, float, RectangleF> R = (x, y, w, h) => new RectangleF(r.X + x * u, r.Y + y * u, w * u, h * u);
            using (var pen = new Pen(c, 1.7f * u) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var thin = new Pen(c, 1.2f * u) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var br = new SolidBrush(c))
            {
                switch (name)
                {
                    case "file":
                        g.DrawLines(pen, new[] { P(12, 2), P(5, 2), P(5, 18), P(16, 18), P(16, 6), P(12, 2), P(12, 6), P(16, 6) });
                        break;
                    case "files":
                        using (var p = Theme.Round(R(4, 2, 11, 15), 2 * u)) g.DrawPath(pen, p);
                        g.DrawLine(pen, P(7, 7), P(12, 7)); g.DrawLine(pen, P(7, 10.5f), P(12, 10.5f)); g.DrawLine(pen, P(7, 14), P(10, 14));
                        g.DrawLine(pen, P(17, 5), P(17, 18)); g.DrawLine(pen, P(17, 18), P(8, 18));
                        break;
                    case "plus":
                        g.DrawLine(pen, P(10, 3.5f), P(10, 16.5f)); g.DrawLine(pen, P(3.5f, 10), P(16.5f, 10));
                        break;
                    case "trash":
                        g.DrawLine(pen, P(3, 5), P(17, 5)); g.DrawLines(pen, new[] { P(8, 5), P(8, 2.8f), P(12, 2.8f), P(12, 5) });
                        g.DrawLines(pen, new[] { P(4.8f, 5), P(5.8f, 17.5f), P(14.2f, 17.5f), P(15.2f, 5) });
                        g.DrawLine(thin, P(8.3f, 8.5f), P(8.6f, 14.5f)); g.DrawLine(thin, P(11.7f, 8.5f), P(11.4f, 14.5f));
                        break;
                    case "broom":
                        g.DrawLine(pen, P(16.5f, 2.5f), P(10.5f, 9.5f));
                        g.FillPolygon(br, new[] { P(9, 8), P(12.5f, 11), P(9.5f, 17.5f), P(2.5f, 13.5f), P(4, 11.5f) });
                        using (var bb = new Pen(Color.FromArgb(160, 0, 0, 0), 0.9f * u)) { g.DrawLine(bb, P(5, 14.6f), P(8, 11)); g.DrawLine(bb, P(7.3f, 15.8f), P(10, 12)); }
                        break;
                    case "terminal":
                        using (var p = Theme.Round(R(2, 3.5f, 16, 13), 2.2f * u)) g.DrawPath(pen, p);
                        g.DrawLines(pen, new[] { P(5.5f, 8), P(8.3f, 10.3f), P(5.5f, 12.6f) }); g.DrawLine(pen, P(10, 13), P(14, 13));
                        break;
                    case "funnel":
                        g.DrawPolygon(pen, new[] { P(2.5f, 3.5f), P(17.5f, 3.5f), P(11.5f, 10.5f), P(11.5f, 16.5f), P(8.5f, 15), P(8.5f, 10.5f) });
                        break;
                    case "disc":
                        g.DrawEllipse(pen, R(2, 2, 16, 16));
                        g.DrawEllipse(thin, R(7.6f, 7.6f, 4.8f, 4.8f));
                        g.DrawArc(thin, R(4.6f, 4.6f, 10.8f, 10.8f), 200, 70);
                        break;
                    case "folder":
                        g.DrawLines(pen, new[] { P(2, 6), P(2, 16.5f), P(18, 16.5f), P(18, 7), P(10, 7), P(8, 4.5f), P(2, 4.5f), P(2, 6) });
                        break;
                    case "x":
                        g.DrawLine(pen, P(4.5f, 4.5f), P(15.5f, 15.5f)); g.DrawLine(pen, P(15.5f, 4.5f), P(4.5f, 15.5f));
                        break;
                    case "refresh":
                        g.DrawArc(pen, R(3, 3, 14, 14), 200, 230); g.DrawArc(pen, R(3, 3, 14, 14), 20, 120);
                        g.FillPolygon(br, new[] { P(15.6f, 1.6f), P(18.4f, 7.8f), P(12.3f, 6.9f) });
                        g.FillPolygon(br, new[] { P(4.4f, 18.4f), P(1.6f, 12.2f), P(7.7f, 13.1f) });
                        break;
                    case "search":
                        g.DrawEllipse(pen, R(3, 3, 10.5f, 10.5f)); g.DrawLine(pen, P(11.8f, 11.8f), P(17, 17));
                        break;
                    case "check":
                        g.FillEllipse(br, R(1, 1, 18, 18));
                        using (var w = new Pen(Color.FromArgb(14, 20, 30), 2.1f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(w, new[] { P(5.8f, 10.3f), P(8.8f, 13.2f), P(14.4f, 7.2f) });
                        break;
                    case "info":
                        g.FillEllipse(br, R(1, 1, 18, 18));
                        using (var w = new SolidBrush(Color.FromArgb(14, 20, 30))) { g.FillEllipse(w, R(8.7f, 4.6f, 2.6f, 2.6f)); g.FillRectangle(w, R(8.8f, 8.6f, 2.4f, 7)); }
                        break;
                    case "error":
                        g.FillEllipse(br, R(1, 1, 18, 18));
                        using (var w = new Pen(Color.FromArgb(14, 20, 30), 2.1f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        { g.DrawLine(w, P(6.8f, 6.8f), P(13.2f, 13.2f)); g.DrawLine(w, P(13.2f, 6.8f), P(6.8f, 13.2f)); }
                        break;
                    case "warn":
                        using (var p = new GraphicsPath()) { p.AddPolygon(new[] { P(10, 1.8f), P(19, 17.8f), P(1, 17.8f) }); g.FillPath(br, p); }
                        using (var w = new SolidBrush(Color.FromArgb(14, 20, 30))) { g.FillRectangle(w, R(8.9f, 6.8f, 2.2f, 6)); g.FillEllipse(w, R(8.8f, 14.1f, 2.4f, 2.4f)); }
                        break;
                    case "film":
                        using (var p = Theme.Round(R(2, 2.5f, 16, 15), 2 * u)) g.DrawPath(pen, p);
                        g.DrawLine(thin, P(5.5f, 2.5f), P(5.5f, 17.5f)); g.DrawLine(thin, P(14.5f, 2.5f), P(14.5f, 17.5f)); g.DrawLine(thin, P(5.5f, 10), P(14.5f, 10));
                        foreach (var y in new[] { 5.3f, 8.5f, 11.7f, 14.9f }) { g.FillRectangle(br, R(2.9f, y - 0.6f, 1.6f, 1.2f)); g.FillRectangle(br, R(15.5f, y - 0.6f, 1.6f, 1.2f)); }
                        break;
                    case "video":
                        using (var p = Theme.Round(R(2, 4, 16, 12), 2.5f * u)) g.DrawPath(pen, p);
                        g.FillPolygon(br, new[] { P(8.3f, 7.2f), P(13.3f, 10), P(8.3f, 12.8f) });
                        break;
                    case "note":
                        g.DrawLine(pen, P(7.5f, 15), P(7.5f, 3.5f)); g.DrawLine(pen, P(7.5f, 3.5f), P(16.5f, 2.5f)); g.DrawLine(pen, P(16.5f, 2.5f), P(16.5f, 13.5f));
                        g.FillEllipse(br, R(2.8f, 12.6f, 5.4f, 4.6f)); g.FillEllipse(br, R(11.8f, 11.2f, 5.4f, 4.6f));
                        break;
                    case "image":
                        using (var p = Theme.Round(R(2, 3, 16, 14), 2 * u)) g.DrawPath(pen, p);
                        g.FillEllipse(br, R(5, 5.8f, 3.2f, 3.2f));
                        g.DrawLines(thin, new[] { P(3, 15.5f), P(8.5f, 10.5f), P(11.5f, 13), P(14, 10.8f), P(17.2f, 14) });
                        break;
                    case "stack":
                        foreach (var y in new[] { 4f, 9f, 14f })
                        {
                            var e = R(3, y - 2.6f, 14, 5.2f);
                            if (y < 5) g.FillEllipse(br, e); else g.DrawArc(pen, e, 0, 180);
                        }
                        g.DrawLine(pen, P(3, 4), P(3, 14)); g.DrawLine(pen, P(17, 4), P(17, 14));
                        break;
                    case "frame":
                        using (var p = Theme.Round(R(2.5f, 2.5f, 15, 15), 2.2f * u)) g.DrawPath(pen, p);
                        g.DrawLines(thin, new[] { P(6.5f, 9.5f), P(6.5f, 6.5f), P(9.5f, 6.5f) });
                        g.DrawLines(thin, new[] { P(13.5f, 10.5f), P(13.5f, 13.5f), P(10.5f, 13.5f) });
                        g.DrawLine(thin, P(7, 13), P(13, 7));
                        break;
                    case "upscale":
                        using (var p = Theme.Round(R(2.5f, 2.5f, 15, 15), 2.2f * u)) g.DrawPath(thin, p);
                        g.DrawLine(pen, P(6.5f, 13.5f), P(13.5f, 6.5f)); g.DrawLines(pen, new[] { P(9, 6.5f), P(13.5f, 6.5f), P(13.5f, 11) });
                        break;
                    case "ai":
                    case "sparkle":
                        {
                            Func<float, float, float, PointF[]> star = (cx, cy, sz) => new[] {
                                P(cx, cy - sz), P(cx + sz * 0.26f, cy - sz * 0.26f), P(cx + sz, cy), P(cx + sz * 0.26f, cy + sz * 0.26f),
                                P(cx, cy + sz), P(cx - sz * 0.26f, cy + sz * 0.26f), P(cx - sz, cy), P(cx - sz * 0.26f, cy - sz * 0.26f) };
                            g.FillPolygon(br, star(8.5f, 11, 7.2f));
                            g.FillPolygon(br, star(16, 4.5f, 3.2f));
                            g.FillPolygon(br, star(16.2f, 15.8f, 2));
                        }
                        break;
                    case "denoise":
                        {
                            Func<float, float, float, PointF[]> star = (cx, cy, sz) => new[] {
                                P(cx, cy - sz), P(cx + sz * 0.26f, cy - sz * 0.26f), P(cx + sz, cy), P(cx + sz * 0.26f, cy + sz * 0.26f),
                                P(cx, cy + sz), P(cx - sz * 0.26f, cy + sz * 0.26f), P(cx - sz, cy), P(cx - sz * 0.26f, cy - sz * 0.26f) };
                            g.FillPolygon(br, star(9, 10.5f, 6.5f));
                            foreach (var d in new[] { new[] { 16f, 4f }, new[] { 17f, 9f }, new[] { 15.5f, 15f }, new[] { 3f, 4f } }) g.FillEllipse(br, R(d[0] - 0.9f, d[1] - 0.9f, 1.8f, 1.8f));
                        }
                        break;
                    case "cube":
                        g.DrawPolygon(pen, new[] { P(10, 2), P(17.5f, 6), P(17.5f, 14), P(10, 18), P(2.5f, 14), P(2.5f, 6) });
                        g.DrawLine(pen, P(2.5f, 6), P(10, 10)); g.DrawLine(pen, P(17.5f, 6), P(10, 10)); g.DrawLine(pen, P(10, 10), P(10, 18));
                        break;
                    case "format":
                        g.DrawPolygon(pen, new[] { P(10, 2), P(18, 6), P(18, 14), P(10, 18), P(2, 14), P(2, 6) });
                        g.DrawLine(pen, P(2, 6), P(10, 10)); g.DrawLine(pen, P(18, 6), P(10, 10)); g.DrawLine(pen, P(10, 10), P(10, 18));
                        break;
                    case "speaker":
                        g.FillPolygon(br, new[] { P(2.5f, 7.5f), P(6, 7.5f), P(10.5f, 3.5f), P(10.5f, 16.5f), P(6, 12.5f), P(2.5f, 12.5f) });
                        g.DrawArc(pen, R(9.5f, 6.5f, 5, 7), -55, 110); g.DrawArc(pen, R(8.5f, 3.5f, 9.5f, 13), -55, 110);
                        break;
                    case "audio":
                    case "wave":
                        foreach (var x in new[] { new[] { 2f, 8f, 12f }, new[] { 5.5f, 5f, 15f }, new[] { 9f, 2.5f, 17.5f }, new[] { 12.5f, 6f, 14f }, new[] { 16f, 8.5f, 11.5f } })
                            g.DrawLine(pen, P(x[0] + 1, x[1]), P(x[0] + 1, x[2]));
                        break;
                    case "sliders":
                    case "filters":
                        g.DrawLine(pen, P(3, 5), P(17, 5)); g.DrawLine(pen, P(3, 10), P(17, 10)); g.DrawLine(pen, P(3, 15), P(17, 15));
                        foreach (var k in new[] { new[] { 7f, 5f }, new[] { 13f, 10f }, new[] { 9f, 15f } })
                        { var cr = R(k[0] - 2, k[1] - 2, 4, 4); using (var bb = new SolidBrush(Theme.Panel)) g.FillEllipse(bb, cr); g.DrawEllipse(pen, cr); }
                        break;
                    case "scissors":
                        g.DrawEllipse(pen, R(2.5f, 12, 5, 5)); g.DrawEllipse(pen, R(12.5f, 12, 5, 5));
                        g.DrawLine(pen, P(6.6f, 12.6f), P(15, 2.5f)); g.DrawLine(pen, P(13.4f, 12.6f), P(5, 2.5f));
                        break;
                    case "chip":
                        using (var p = Theme.Round(R(4.5f, 4.5f, 11, 11), 1.6f * u)) g.DrawPath(pen, p);
                        g.FillRectangle(br, R(8, 8, 4, 4));
                        foreach (var k in new[] { 7.5f, 10f, 12.5f }) { g.DrawLine(thin, P(k, 1.8f), P(k, 4.5f)); g.DrawLine(thin, P(k, 15.5f), P(k, 18.2f)); g.DrawLine(thin, P(1.8f, k), P(4.5f, k)); g.DrawLine(thin, P(15.5f, k), P(18.2f, k)); }
                        break;
                    case "clock":
                        g.DrawEllipse(pen, R(2.5f, 2.5f, 15, 15)); g.DrawLines(pen, new[] { P(10, 5.8f), P(10, 10), P(13, 12) });
                        break;
                    case "output":
                        g.DrawLines(pen, new[] { P(2, 6), P(2, 16.5f), P(18, 16.5f), P(18, 7), P(10, 7), P(8, 4.5f), P(2, 4.5f), P(2, 6) });
                        g.DrawLine(pen, P(10, 9.5f), P(10, 14)); g.DrawLine(pen, P(7.8f, 11.8f), P(10, 14)); g.DrawLine(pen, P(12.2f, 11.8f), P(10, 14));
                        break;
                    case "code":
                        g.DrawLines(pen, new[] { P(6.5f, 5), P(2, 10), P(6.5f, 15) });
                        g.DrawLines(pen, new[] { P(13.5f, 5), P(18, 10), P(13.5f, 15) });
                        g.DrawLine(pen, P(11.5f, 3.5f), P(8.5f, 16.5f));
                        break;
                    case "log":
                        g.DrawPolygon(pen, new[] { P(10, 2), P(18, 10), P(10, 18), P(2, 10) });
                        g.DrawLine(thin, P(7, 9), P(13, 9)); g.DrawLine(thin, P(7, 12), P(11, 12));
                        break;
                    case "erase":
                        g.DrawPolygon(pen, new[] { P(8, 16), P(2.8f, 10.8f), P(10.8f, 2.8f), P(17.2f, 9.2f), P(10.4f, 16) });
                        g.DrawLine(thin, P(6, 7.6f), P(12.4f, 14)); g.DrawLine(pen, P(10.4f, 16), P(17.5f, 16));
                        break;
                    case "dot":
                        g.FillEllipse(br, R(5, 5, 10, 10));
                        break;
                    default:
                        g.DrawEllipse(pen, R(4, 4, 12, 12));
                        break;
                }
            }
            g.SmoothingMode = oldSm;
        }
    }

    /// Big page title shown above each page.
    public class PageHeader : Control
    {
        public string Title = "", Sub = "", Chip = "";
        public PageHeader()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = Theme.S(84);
            BackColor = Theme.Bg;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);
            using (var halo = new GraphicsPath())
            {
                halo.AddEllipse(Width - Theme.S(420), -Theme.S(200), Theme.S(620), Theme.S(330));
                using (var pb = new PathGradientBrush(halo) { CenterColor = Color.FromArgb(22, Theme.Gold), SurroundColors = new[] { Color.FromArgb(0, Theme.Gold) } })
                    g.FillPath(pb, halo);
            }
            int x = Theme.S(28);
            using (var f1 = new Font("Segoe UI Semibold", 19f))
            using (var f2 = new Font("Segoe UI", 9.5f))
            {
                TextRenderer.DrawText(g, Title, f1, new Point(x - Theme.S(4), Theme.S(14)), Color.White, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, Sub, f2, new Point(x, Theme.S(52)), Theme.Muted, TextFormatFlags.NoPrefix);
            }
            if (!string.IsNullOrEmpty(Chip))
                using (var f = new Font("Segoe UI Semibold", 9f))
                {
                    var cs = TextRenderer.MeasureText(Chip, f, Size.Empty, TextFormatFlags.NoPrefix);
                    var cr = new RectangleF(Width - cs.Width - Theme.S(56), Theme.S(26), cs.Width + Theme.S(28), Theme.S(30));
                    using (var p = Theme.Round(cr, cr.Height / 2))
                    {
                        using (var b = new SolidBrush(Color.FromArgb(26, 31, 42))) g.FillPath(b, p);
                        using (var pen = new Pen(Color.FromArgb(140, Theme.Gold))) g.DrawPath(pen, p);
                    }
                    using (var d = new SolidBrush(Theme.Gold)) g.FillEllipse(d, cr.X + Theme.S(12), cr.Y + cr.Height / 2 - Theme.S(3), Theme.S(6), Theme.S(6));
                    TextRenderer.DrawText(g, Chip, f, new Rectangle((int)cr.X + Theme.S(22), (int)cr.Y, (int)cr.Width - Theme.S(26), (int)cr.Height), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                }
            using (var pen = new Pen(Color.FromArgb(28, 33, 44))) g.DrawLine(pen, x, Height - 1, Width - Theme.S(28), Height - 1);
        }
    }

    /// Dark, double-buffered ListView.
    public class DarkList : ListView
    {
        public DarkList()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            DoubleBuffered = true;
            OwnerDraw = true;
            View = View.Details;
            FullRowSelect = true;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Panel;
            ForeColor = Theme.Text;
            HeaderStyle = ColumnHeaderStyle.Nonclickable;
            SmallImageList = new ImageList { ImageSize = new Size(1, Theme.S(30)) };   // taller rows (2.1.6 design)
        }
    }

    /// Dark drag & drop zone shown when the list is empty (no artwork).
    public class DropHint : Control
    {
        public bool Highlight;

        public DropHint()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Panel;
            Cursor = Cursors.Hand;
        }

        static readonly string[] Chips = { "Video", "Audio", "Image", "AI Denoise", "AI Upscale", "Dolby Atmos", "DSD" };
        static readonly string[] ChipIcons = { "film", "note", "image", "denoise", "upscale", "speaker", "wave" };
        static readonly Color[] ChipColors = {
            Color.FromArgb(59, 130, 246), Color.FromArgb(168, 85, 247), Color.FromArgb(34, 197, 94), Color.FromArgb(245, 184, 61),
            Color.FromArgb(139, 92, 246), Color.FromArgb(34, 211, 238), Color.FromArgb(236, 72, 153) };

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(BackColor);
            var outer = new RectangleF(0, 0, Width - 1, Height - 1);
            // Empty file list: plain dark surface, original interaction and labels.
            using (var clip = Theme.Round(outer, Theme.S(14)))
                if (Highlight) using (var hb = new SolidBrush(Color.FromArgb(40, Theme.Blue))) g.FillPath(hb, clip);
            var r = new RectangleF(Theme.S(16), Theme.S(16), Width - Theme.S(32), Height - Theme.S(32));
            using (var p = Theme.Round(r, Theme.S(14)))
            using (var pen = new Pen(Highlight ? Theme.Gold : Color.FromArgb(120, 150, 180, 230), Theme.S(1) * 1.3f) { DashStyle = DashStyle.Dash })
                g.DrawPath(pen, p);

            float cy = Height / 2f;
            int ls = Theme.S(Math.Min(104, Math.Max(64, Height / 5)));
            float block = ls + Theme.S(170);
            float top = Math.Max(Theme.S(26), cy - block / 2f);
            var lr = new RectangleF((Width - ls) / 2f, top, ls, ls);
            Header.DrawLogo(g, lr);
            using (var f1 = new Font("Segoe UI", 25f, FontStyle.Bold))
            using (var f2 = new Font("Segoe UI", 11.5f))
            using (var f3 = new Font("Segoe UI Semibold", 9f))
            {
                // "Drop your files here" with "here" in gold
                string a = "Drop your files ", b = "here";
                var tf = (StringFormat)StringFormat.GenericTypographic.Clone();
                tf.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                var sa = g.MeasureString(a, f1, PointF.Empty, tf);
                var sb = g.MeasureString(b, f1, PointF.Empty, tf);
                float tx = (Width - sa.Width - sb.Width) / 2f, ty = lr.Bottom + Theme.S(22);
                using (var sh = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.DrawString(a + b, f1, sh, tx + 2, ty + 2, tf);
                using (var w = new SolidBrush(Color.White)) g.DrawString(a, f1, w, tx, ty, tf);
                using (var gb = new LinearGradientBrush(new RectangleF(tx + sa.Width, ty, sb.Width + 1, sb.Height + 1), Color.FromArgb(255, 214, 120), Color.FromArgb(232, 150, 30), LinearGradientMode.Vertical))
                    g.DrawString(b, f1, gb, tx + sa.Width, ty, tf);
                var sf = new StringFormat { Alignment = StringAlignment.Center };
                using (var m = new SolidBrush(Color.FromArgb(178, 188, 208)))
                    g.DrawString("or click to browse  —  videos, music, pictures, whole folders", f2, m, new RectangleF(0, ty + sa.Height + Theme.S(14), Width, Theme.S(28)), sf);

                // feature chips
                int ch = Theme.S(40), isz = Theme.S(17), pad = Theme.S(16), gap = Theme.S(12);
                var ws = new float[Chips.Length]; float total = 0;
                for (int i = 0; i < Chips.Length; i++) { ws[i] = TextRenderer.MeasureText(Chips[i], f3).Width + isz + pad * 2 + Theme.S(4); total += ws[i] + gap; }
                total -= gap;
                float scale = total > Width - Theme.S(60) ? (Width - Theme.S(60)) / total : 1f;
                float x = (Width - total * scale) / 2f, y = ty + sa.Height + Theme.S(62);
                for (int i = 0; i < Chips.Length; i++)
                {
                    var cr = new RectangleF(x, y, ws[i] * scale, ch);
                    var col = ChipColors[i];
                    using (var p = Theme.Round(cr, Theme.S(9)))
                    {
                        using (var b2 = new LinearGradientBrush(cr, Color.FromArgb(200, 16, 20, 34), Color.FromArgb(200, Color.FromArgb(col.R / 6, col.G / 6, col.B / 6)), LinearGradientMode.Vertical)) g.FillPath(b2, p);
                        using (var pen = new Pen(Color.FromArgb(210, col), Theme.S(1) * 1.4f)) g.DrawPath(pen, p);
                    }
                    float ix = cr.X + pad * scale;
                    Icons.Draw(g, ChipIcons[i], new RectangleF(ix, cr.Y + (ch - isz) / 2f, isz, isz), Color.FromArgb(Math.Min(255, col.R + 40), Math.Min(255, col.G + 40), Math.Min(255, col.B + 40)));
                    TextRenderer.DrawText(g, Chips[i], f3, new Rectangle((int)(ix + isz + Theme.S(8)), (int)cr.Y, (int)(cr.Right - ix - isz - Theme.S(8)), ch), Color.FromArgb(230, 234, 242),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    x += (ws[i] + gap) * scale;
                }
            }
        }
    }
}
