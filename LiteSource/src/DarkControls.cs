using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FFmpegForge
{
    // FGS-style native scrollbar theming: change the native theme when handles are
    // created, not on every paint/scroll notification. Never paint scrollbar tracks.
    internal static class DarkNativeScroll
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        private static readonly HashSet<Control> Installed = new HashSet<Control>();

        internal static void Attach(Control root)
        {
            if (root == null || root.IsDisposed || !Installed.Add(root)) return;
            root.ControlAdded += Added;
            root.HandleCreated += Created;
            root.Disposed += Disposed;
            Apply(root);
            foreach (Control child in root.Controls) Attach(child);
        }

        private static bool NeedsNativeScrollTheme(Control c)
        {
            var box = c as ScrollableControl;
            // A general Panel with AutoScroll=false has no native scrollbar to skin.
            return c is ListView || c is ListBox || c is RichTextBox ||
                   (c is TextBoxBase && ((TextBoxBase)c).Multiline) ||
                   (box != null && box.AutoScroll);
        }

        private static void Apply(Control c)
        {
            if (!NeedsNativeScrollTheme(c) || !c.IsHandleCreated || c.IsDisposed || c.Disposing) return;
            try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        private static void Added(object sender, ControlEventArgs e) { Attach(e.Control); }
        private static void Created(object sender, EventArgs e) { Apply((Control)sender); }

        private static void Disposed(object sender, EventArgs e)
        {
            var c = (Control)sender;
            c.ControlAdded -= Added;
            c.HandleCreated -= Created;
            c.Disposed -= Disposed;
            Installed.Remove(c);
        }
    }

    // Parent compositing is deliberately scoped to the right-hand settings
    // card. Do NOT enable WS_EX_COMPOSITED on the Form, scrolling viewport or ListView.
    // This mirrors FGS SettingsPaintPolicy and avoids native child white flashes
    // during autosizing while preserving fast list scrolling and video previews.
    internal static class SmoothPaintPolicy
    {
        internal static CreateParams Apply(CreateParams cp)
        {
            if ((cp.ClassStyle & 0xE0) == 0) cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return cp;
        }
    }

    internal sealed class SmoothSettingsPanel : Panel
    {
        internal SmoothSettingsPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        // The outer scrolling viewport is buffered, but NOT composited:
        // nested WS_EX_COMPOSITED here would multiply redraw costs.

    }

    internal sealed class SmoothSettingsTable : TableLayoutPanel
    {
        internal SmoothSettingsTable()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        // Layout table is buffered; its individual section cards are composited.

    }

    internal sealed class SmoothCardTable : CardTable
    {
        // One composited layer per card, without nested composited ancestors.
        protected override CreateParams CreateParams { get { return SmoothPaintPolicy.Apply(base.CreateParams); } }
    }

    // Batch AutoSize/Visible transitions: one coherent layout on release rather
    // than a chain of intermediate paints. No timer, animation or busy-wait.
    internal sealed class UiLayoutBatch : IDisposable
    {
        private readonly List<Control> controls = new List<Control>();
        private bool disposed;

        private UiLayoutBatch(Control child)
        {
            for (Control c = child; c != null; c = c.Parent)
            {
                if (c is Form) break;
                controls.Add(c);
                c.SuspendLayout();
                if (c is SmoothSettingsPanel) break;
            }
        }

        internal static UiLayoutBatch Begin(Control child) { return new UiLayoutBatch(child); }

        internal static void SetVisible(Control c, bool visible)
        {
            if (c != null && c.Visible != visible) c.Visible = visible;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int i = 0; i < controls.Count; i++)
                if (!controls[i].IsDisposed) controls[i].ResumeLayout(true);
        }
    }

    // Native ComboBox, owner-drawn items and FGS-style late chrome overlay.
    // Native Windows still owns keyboard, IME, focus and popup behaviour.
    // The draw occurs ONLY after relevant native paint messages, not on a timer.
    public class DarkCombo : ComboBox
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ComboInfo
        {
            public int Size;
            public NativeRect Item;
            public NativeRect Button;
            public int State;
            public IntPtr Combo, Edit, List;
        }
        [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr hwnd, ref ComboInfo info);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr brush);
        [DllImport("gdi32.dll")] private static extern int SetTextColor(IntPtr dc, int color);
        [DllImport("gdi32.dll")] private static extern int SetBkColor(IntPtr dc, int color);
        private IntPtr disabledBrush;
        private bool hovered, repairQueued;

        private static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        public DarkCombo()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            FlatStyle = FlatStyle.Flat;
            BackColor = Theme.Card;
            ForeColor = Theme.Text;
            ItemHeight = Theme.S(23);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Graphics == null) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            bool disabled = !Enabled;
            Color bg = selected ? Theme.Selection : Theme.Card;
            using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);
            int index = e.Index;
            string label = index >= 0 && index < Items.Count
                ? (GetItemText(Items[index]) ?? "") : (Text ?? "");
            var bounds = new Rectangle(e.Bounds.X + Theme.S(7), e.Bounds.Y,
                Math.Max(0, e.Bounds.Width - Theme.S(12)), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, label, Font, bounds,
                disabled ? Theme.Muted : Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetWindowTheme(Handle, "", ""); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            ApplyPopupTheme();
        }

        private void ApplyPopupTheme()
        {
            if (!IsHandleCreated || Disposing || IsDisposed) return;
            var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
            if (!GetComboBoxInfo(Handle, ref info) || info.List == IntPtr.Zero) return;
            try { SetWindowTheme(info.List, "DarkMode_Explorer", null); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        protected override void OnDropDown(EventArgs e) { ApplyPopupTheme(); base.OnDropDown(e); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); PaintChrome(); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; base.OnMouseLeave(e); PaintChrome(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); PaintChrome(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); PaintChrome(); }
        protected override void OnDropDownClosed(EventArgs e) { base.OnDropDownClosed(e); PaintChrome(); }

        private void QueueResizeRepair()
        {
            if (repairQueued || !IsHandleCreated || !Visible || IsDisposed || Disposing) return;
            repairQueued = true;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    repairQueued = false;
                    if (IsDisposed || Disposing || !IsHandleCreated || !Visible) return;
                    Invalidate();
                });
            }
            catch (InvalidOperationException) { repairQueued = false; }
        }

        protected override void WndProc(ref Message m)
        {
            int msg = m.Msg;
            // A disabled native EDIT child may ignore WinForms BackColor.
            if (!Enabled && (msg == 0x0138 || msg == 0x0133))
            {
                if (disabledBrush == IntPtr.Zero) disabledBrush = CreateSolidBrush(ColorRef(Theme.Card));
                if (disabledBrush != IntPtr.Zero)
                {
                    SetTextColor(m.WParam, ColorRef(Theme.Muted));
                    SetBkColor(m.WParam, ColorRef(Theme.Card));
                    m.Result = disabledBrush;
                    return;
                }
            }
            base.WndProc(ref m);
            if (msg == 0x0047 || msg == 0x0005) QueueResizeRepair(); // WINDOWPOSCHANGED, SIZE
            if (msg == 0x000F || msg == 0x0085 || msg == 0x0014 || msg == 0x000A)
                PaintChrome(); // PAINT, NCPAINT, ERASEBKGND, ENABLE
        }

        // Do not redraw the dropdown item text or popup; paint only the native
        // arrow region, its separator and the one-pixel border after native paint.
        private void PaintChrome()
        {
            if (!IsHandleCreated || Disposing || IsDisposed || DropDownStyle == ComboBoxStyle.Simple ||
                Width < 10 || Height < 8) return;
            var info = new ComboInfo { Size = Marshal.SizeOf(typeof(ComboInfo)) };
            bool known = GetComboBoxInfo(Handle, ref info);
            int left = known ? info.Button.Left : Width - SystemInformation.VerticalScrollBarWidth;
            int right = known ? info.Button.Right : Width - 1;
            left = Math.Max(1, Math.Min(Width - 2, left));
            right = Math.Max(left + 1, Math.Min(Width - 1, right));
            using (var g = Graphics.FromHwnd(Handle))
            {
                var arrow = new Rectangle(left, 1, Math.Max(1, right - left), Math.Max(1, Height - 2));
                using (var b = new SolidBrush(Theme.Card))
                {
                    g.FillRectangle(b, arrow);
                    g.FillRectangle(b, Math.Max(1, left - 2), 2, 2, Math.Max(1, Height - 4));
                }
                Color edge = !Enabled ? Theme.Border : Focused || DroppedDown ? Theme.Blue : hovered ? Theme.Muted : Theme.Border;
                using (var p = new Pen(edge))
                    g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
                int cx = (left + right) / 2, cy = Height / 2;
                using (var p = new Pen(Enabled ? Theme.Text : Theme.Muted, Math.Max(1f, Theme.Scale)))
                    g.DrawLines(p, new[] { new Point(cx - Theme.S(3), cy - Theme.S(1)),
                        new Point(cx, cy + Theme.S(2)), new Point(cx + Theme.S(3), cy - Theme.S(1)) });
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            repairQueued = false;
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disabledBrush != IntPtr.Zero) { DeleteObject(disabledBrush); disabledBrush = IntPtr.Zero; }
            base.Dispose(disposing);
        }
    }
}
