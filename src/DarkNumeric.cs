using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FFmpegForge
{
    // Adapted from FGS ThemeNumericUpDown: own only the small spinner paint
    // and the exposed outer frame. WinForms retains native hit-testing, key
    // input, selection, mouse capture and repeat behaviour.
    public sealed class DarkNumeric : NumericUpDown
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct PaintRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct PaintInfo
        {
            public IntPtr DC; public int Erase; public PaintRect Rect;
            public int Restore, IncUpdate;
            public int R0, R1, R2, R3, R4, R5, R6, R7;
        }
        [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr hwnd, out PaintInfo info);
        [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr hwnd, ref PaintInfo info);

        private sealed class SpinnerWindow : NativeWindow, IDisposable
        {
            private readonly DarkNumeric owner;
            private readonly Control buttons;
            public SpinnerWindow(DarkNumeric parent, Control target)
            {
                owner = parent; buttons = target;
                buttons.HandleCreated += Created;
                buttons.HandleDestroyed += Destroyed;
                if (buttons.IsHandleCreated) AssignHandle(buttons.Handle);
            }
            private void Created(object sender, EventArgs e) { AssignHandle(buttons.Handle); }
            private void Destroyed(object sender, EventArgs e) { ReleaseHandle(); }
            protected override void WndProc(ref Message m)
            {
                if (!owner.IsDisposed && !owner.Disposing)
                {
                    if (m.Msg == 0x0014) { m.Result = new IntPtr(1); return; } // WM_ERASEBKGND
                    if (m.Msg == 0x000F) // WM_PAINT: replace native white buttons BEFORE they flash
                    {
                        PaintInfo info;
                        IntPtr dc = BeginPaint(Handle, out info);
                        if (dc != IntPtr.Zero)
                        {
                            try
                            {
                                int w = buttons.ClientSize.Width, h = buttons.ClientSize.Height;
                                if (w > 0 && h > 0)
                                {
                                    using (var bitmap = new Bitmap(w, h))
                                    {
                                        using (Graphics buffer = Graphics.FromImage(bitmap))
                                            owner.DrawSpinner(buffer, new Rectangle(0, 0, w, h));
                                        using (Graphics g = Graphics.FromHdc(dc))
                                            g.DrawImageUnscaled(bitmap, 0, 0);
                                    }
                                }
                            }
                            finally { EndPaint(Handle, ref info); }
                            m.Result = IntPtr.Zero;
                            return;
                        }
                    }
                    if (m.Msg == 0x0318 && m.WParam != IntPtr.Zero) // WM_PRINTCLIENT
                    {
                        using (Graphics g = Graphics.FromHdc(m.WParam))
                            owner.DrawSpinner(g, buttons.ClientRectangle);
                        m.Result = IntPtr.Zero;
                        return;
                    }
                }
                base.WndProc(ref m);
            }
            public void Dispose()
            {
                buttons.HandleCreated -= Created;
                buttons.HandleDestroyed -= Destroyed;
                ReleaseHandle();
            }
        }

        private SpinnerWindow spinner;
        private Control spinnerControl;
        private bool hovered, focused;
        public DarkNumeric()
        {
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            BorderStyle = BorderStyle.FixedSingle;
        }

        protected override void OnControlAdded(ControlEventArgs e)
        {
            base.OnControlAdded(e);
            e.Control.MouseEnter += ChildEnter;
            e.Control.MouseLeave += ChildLeave;
            if (!(e.Control is TextBoxBase))
            {
                if (spinner != null) spinner.Dispose();
                spinnerControl = e.Control;
                spinner = new SpinnerWindow(this, e.Control);
            }
        }
        protected override void OnControlRemoved(ControlEventArgs e)
        {
            e.Control.MouseEnter -= ChildEnter;
            e.Control.MouseLeave -= ChildLeave;
            if (e.Control == spinnerControl)
            {
                if (spinner != null) spinner.Dispose();
                spinner = null;
                spinnerControl = null;
            }
            base.OnControlRemoved(e);
        }
        private void ChildEnter(object sender, EventArgs e) { hovered = true; RefreshFrame(); }
        private void ChildLeave(object sender, EventArgs e) { UpdateHover(); }
        private void UpdateHover()
        {
            hovered = IsHandleCreated && RectangleToScreen(ClientRectangle).Contains(Cursor.Position);
            RefreshFrame();
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); RefreshFrame(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); UpdateHover(); }
        protected override void OnEnter(EventArgs e) { focused = true; base.OnEnter(e); RefreshFrame(); }
        protected override void OnLeave(EventArgs e) { focused = false; base.OnLeave(e); RefreshFrame(); }
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            RefreshFrame();
            foreach (Control child in Controls) child.Invalidate();
        }

        // The UpDownBase parent otherwise paints its own light 1px frame/gap.
        protected override void OnPaint(PaintEventArgs e) { DrawFrame(e.Graphics); }
        private void RefreshFrame()
        {
            if (!IsHandleCreated || IsDisposed || Disposing) return;
            using (Graphics g = Graphics.FromHwnd(Handle)) DrawFrame(g);
        }
        private void DrawFrame(Graphics g)
        {
            if (ClientSize.Width < 3 || ClientSize.Height < 3) return;
            using (Region exposed = new Region(ClientRectangle))
            using (Brush b = new SolidBrush(Theme.Card))
            {
                foreach (Control child in Controls) if (child.Visible) exposed.Exclude(child.Bounds);
                g.FillRegion(b, exposed);
            }
            if (BorderStyle == BorderStyle.None) return;
            using (Pen inner = new Pen(Theme.Card))
                g.DrawRectangle(inner, 1, 1, ClientSize.Width - 3, ClientSize.Height - 3);
            Color edge = !Enabled ? Theme.Border : focused ? Theme.Blue : hovered ? Theme.Muted : Theme.Border;
            using (Pen pen = new Pen(edge))
                g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
        private void DrawSpinner(Graphics g, Rectangle bounds)
        {
            int w = bounds.Width, h = bounds.Height;
            if (w < 3 || h < 4) return;
            using (Brush b = new SolidBrush(Theme.Card)) g.FillRectangle(b, bounds);
            using (Pen p = new Pen(Theme.Border))
            {
                g.DrawRectangle(p, 0, 0, w - 1, h - 1);
                g.DrawLine(p, 0, h / 2, w - 1, h / 2);
            }
            int rows = Math.Min(4, Math.Min(w / 2, h / 2));
            int up = (h / 2 - rows) / 2, down = h / 2 + (h - h / 2 - rows) / 2;
            using (Brush b = new SolidBrush(Enabled ? Theme.Text : Theme.Muted))
            for (int i = 0; i < rows; ++i)
            {
                int uw = 1 + i * 2, dw = 1 + (rows - 1 - i) * 2;
                g.FillRectangle(b, w / 2 - uw / 2, up + i, uw, 1);
                g.FillRectangle(b, w / 2 - dw / 2, down + i, dw, 1);
            }
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && spinner != null) { spinner.Dispose(); spinner = null; spinnerControl = null; }
            base.Dispose(disposing);
        }
    }

    // Same dark progress-paint policy as FGS. No animations or native white track.
    public sealed class DarkProgressBar : ProgressBar
    {
        public DarkProgressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Theme.Card;
            ForeColor = Theme.Blue;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Rectangle r = ClientRectangle;
            if (r.Width < 2 || r.Height < 2) return;
            using (Brush background = new SolidBrush(Theme.Card))
                e.Graphics.FillRectangle(background, r);
            Rectangle fill = Rectangle.Inflate(r, -1, -1);
            double p = Math.Max(0, Math.Min(1, (Value - Minimum) /
                (double)Math.Max(1, Maximum - Minimum)));
            fill.Width = (int)Math.Round(fill.Width * p);
            if (fill.Width > 0 && fill.Height > 0)
                using (Brush brush = Theme.ProgressBrush(r))
                    e.Graphics.FillRectangle(brush, fill);
            using (Pen edge = new Pen(Theme.Border))
                e.Graphics.DrawRectangle(edge, 0, 0, r.Width - 1, r.Height - 1);
        }
        protected override void WndProc(ref Message m)
        {
            int msg = m.Msg;
            base.WndProc(ref m);
            // ProgressBar has no OnValueChanged virtual method in .NET 4.
            // Invalidate only after native range / position messages.
            if (msg == 0x0402 || msg == 0x0401 || msg == 0x0406)
                Invalidate(); // PBM_SETPOS, PBM_SETRANGE, PBM_SETRANGE32
        }
    }
}
