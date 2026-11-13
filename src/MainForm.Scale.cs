using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FFmpegForge
{
    // Scale setup deliberately lives in its own non-modal tool window. The
    // original WinForms controls are REPARENTED (never cloned): snapshots,
    // saved settings, runtime install actions and existing handlers stay intact.
    public partial class MainForm
    {
        TableLayoutPanel secResVideo, secResImage;
        Form scaleWindow;
        SmoothSettingsPanel scaleViewport;
        SmoothSettingsTable scaleStack;
        FlatBtn btnOpenScale;
        Label lblScaleStatus, lblScaleCompatibility;
        SettingsWheelRouter wheelRouter;
        bool scaleRoutesWired, scaleRouteSwitching;
        string scaleLastChange = "";

        void BuildScaleWindow(SmoothSettingsTable mainStack, SmoothSettingsPanel mainViewport)
        {
            scaleWindow = new Form
            {
                Text = "FFmpeg Forge Lite — Scale / AI Upscaling",
                Font = fUI, ForeColor = Theme.Text, BackColor = Theme.Bg,
                AutoScaleMode = AutoScaleMode.None,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(Theme.S(780), Theme.S(860)),
                MinimumSize = new Size(Theme.S(620), Theme.S(540)),
                ShowInTaskbar = false
            };
            WindowPlacement.Restore(scaleWindow, settings, "ui_scale");
            scaleWindow.HandleCreated += (s, e) => DarkTitle.Apply(scaleWindow.Handle);
            scaleWindow.FormClosing += (s, e) =>
            {
                WindowPlacement.Save(scaleWindow, settings, "ui_scale");
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    scaleWindow.Hide();
                    SaveSettings();
                }
            };

            scaleViewport = new SmoothSettingsPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg,
                Padding = new Padding(Theme.S(18), Theme.S(16), Theme.S(18), Theme.S(16))
            };
            scaleStack = new SmoothSettingsTable
            {
                Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Bg
            };
            scaleStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var intro = Section("SCALE ROUTES / COMPATIBILITY");
            lblScaleCompatibility = new Label
            {
                AutoSize = true, ForeColor = Theme.Muted, Font = fUI,
                MaximumSize = new Size(Theme.S(680), 0),
                Margin = new Padding(0, Theme.S(4), 0, Theme.S(6))
            };
            AddRow(intro, null, lblScaleCompatibility);
            scaleStack.Controls.Add(intro);

            // Resolution is an output-size request, not a competing upscaler.
            // It is shared with the standard/AI pipelines via the original
            // ResPicker fields and is not turned off by route switching.
            foreach (TableLayoutPanel section in new[]
            {
                secResVideo, secResImage, secScale, secOpti,
                secSeed, secVision, secRtx, secCai
            })
            {
                if (section.Parent == mainStack) mainStack.Controls.Remove(section);
                section.Dock = DockStyle.Fill;
                scaleStack.Controls.Add(section);
            }
            scaleViewport.Controls.Add(scaleStack);
            scaleWindow.Controls.Add(scaleViewport);
            DarkNativeScroll.Attach(scaleWindow);

            var launch = Section("SCALE & AI UPSCALING");
            btnOpenScale = Btn("Open Scale / AI Upscaling…", 0);
            btnOpenScale.Primary = true;
            btnOpenScale.Height = Theme.S(42);
            btnOpenScale.Dock = DockStyle.Fill;
            btnOpenScale.Click += (s, e) => ShowScaleWindow();
            AddRow(launch, null, btnOpenScale);
            lblScaleStatus = new Label
            {
                AutoSize = true, ForeColor = Theme.Muted, Font = fSmall,
                MaximumSize = new Size(Theme.S(380), 0),
                Margin = new Padding(0, Theme.S(4), 0, 0)
            };
            AddRow(launch, null, lblScaleStatus);
            mainStack.Controls.Add(launch);
            // Keep the launch section near Format/Video, never at the bottom.
            mainStack.Controls.SetChildIndex(launch, Math.Min(2, mainStack.Controls.Count - 1));

            wheelRouter = new SettingsWheelRouter(mainViewport, scaleViewport);
            Application.AddMessageFilter(wheelRouter);
            FormClosed += (s, e) =>
            {
                Application.RemoveMessageFilter(wheelRouter);
                if (scaleWindow != null && !scaleWindow.IsDisposed) scaleWindow.Dispose();
            };
            UpdateScaleCompatibility();
        }

        void ShowScaleWindow()
        {
            if (scaleWindow == null || scaleWindow.IsDisposed || category == MediaKind.Audio) return;
            UpdateScaleCategory();
            UpdateScaleCompatibility();
            if (!scaleWindow.Visible) scaleWindow.Show(this);
            else { if (scaleWindow.WindowState == FormWindowState.Minimized) scaleWindow.WindowState = FormWindowState.Normal; scaleWindow.Activate(); }
        }

        void UpdateScaleCategory()
        {
            if (secResVideo == null || secResImage == null || scaleWindow == null) return;
            secResVideo.Visible = category == MediaKind.Video;
            secResImage.Visible = category == MediaKind.Image;
            secScale.Visible = secOpti.Visible = secSeed.Visible = secVision.Visible = secCai.Visible = category != MediaKind.Audio;
            secRtx.Visible = category != MediaKind.Audio;
            // TrueHDR and DLSS use video-only paths. VSR can also upscale stills.
            chkHdr.Visible = chkDlss.Visible = category == MediaKind.Video;
            UpdateRtxRows();
            scaleWindow.Text = "FFmpeg Forge Lite — Scale / AI Upscaling (" + category + ")";
            if (btnOpenScale != null) btnOpenScale.Enabled = category != MediaKind.Audio;
            UpdateScaleCompatibility();
        }

        void WireScaleRouteEvents()
        {
            if (scaleRoutesWired) return;
            scaleRoutesWired = true;
            // Only real route checkboxes participate; Custom AI is an
            // independent preprocessing stage, allowed before any route.
            foreach (var c in new[] { chkScale, chkOpti, chkSeed, chkVision, chkVsr, chkDlss, chkHdr })
                c.CheckedChanged += ScaleRouteChanged;
            chkCai.CheckedChanged += (s, e) => UpdateScaleCompatibility();
            UpdateScaleCompatibility();
        }

        void ScaleRouteChanged(object sender, EventArgs e)
        {
            if (scaleRouteSwitching) return;
            var clicked = sender as CheckBox;
            if (clicked == null || !clicked.Checked)
            {
                UpdateScaleCompatibility();
                return;
            }
            // This is a choice of EXECUTION ROUTE, not a ban on successive
            // scale passes. Scale+Opti share one graph, VSR+TrueHDR share RTX.
            // Do not silently prevent Custom AI followed by scaling: source
            // implements that as a separate pre-processing pass.
            var displaced = new List<string>();
            scaleRouteSwitching = true;
            try
            {
                Action<CheckBox> uncheck = c =>
                {
                    if (c.Checked && c != clicked) { displaced.Add(c.Text.Split('—')[0].Trim()); c.Checked = false; }
                };
                if (clicked == chkScale || clicked == chkOpti)
                {
                    uncheck(chkSeed); uncheck(chkVision);
                    uncheck(chkVsr); uncheck(chkDlss); uncheck(chkHdr);
                }
                else if (clicked == chkSeed)
                {
                    uncheck(chkScale); uncheck(chkOpti); uncheck(chkVision);
                    uncheck(chkVsr); uncheck(chkDlss); uncheck(chkHdr);
                }
                else if (clicked == chkVision)
                {
                    uncheck(chkScale); uncheck(chkOpti); uncheck(chkSeed);
                    uncheck(chkVsr); uncheck(chkDlss); uncheck(chkHdr);
                }
                else // VSR / DLSS / TrueHDR share the RTX route.
                {
                    uncheck(chkScale); uncheck(chkOpti);
                    uncheck(chkSeed); uncheck(chkVision);
                }
            }
            finally { scaleRouteSwitching = false; }
            scaleLastChange = displaced.Count == 0 ? "" :
                "Switched route: disabled " + string.Join(", ", displaced.ToArray()) + ".";
            UpdateScaleCompatibility();
        }

        void UpdateScaleCompatibility()
        {
            if (lblScaleCompatibility == null || lblScaleStatus == null) return;
            bool canScale = category != MediaKind.Audio;
            bool normal = chkScale != null && chkScale.Checked;
            bool opti = chkOpti != null && chkOpti.Checked;
            bool seed = chkSeed != null && chkSeed.Checked;
            bool vision = chkVision != null && chkVision.Checked;
            bool vsr = chkVsr != null && chkVsr.Checked;
            bool dlss = chkDlss != null && chkDlss.Checked;
            bool trueHdr = chkHdr != null && chkHdr.Checked && category == MediaKind.Video;
            bool custom = chkCai != null && chkCai.Checked;
            var active = new List<string>();
            if (normal) active.Add("Scale");
            if (opti) active.Add("OptiScaler");
            if (seed) active.Add("SeedVR2");
            if (vision) active.Add("VisionNext / RazorNext");
            if (vsr) active.Add("RTX VSR");
            if (dlss && category == MediaKind.Video) active.Add("RTX DLSS");
            if (trueHdr) active.Add("RTX TrueHDR");
            if (custom) active.Insert(0, "Custom AI preprocessing");
            lblScaleStatus.Text = !canScale ? "Audio output: scaling unavailable." :
                active.Count == 0 ? "No scale/AI processing selected. Original resolution." :
                "Active: " + string.Join(" + ", active.ToArray());

            bool bad = ((normal || opti) && (seed || vision || vsr || dlss || trueHdr)) ||
                       (seed && (vision || vsr || dlss || trueHdr)) ||
                       (vision && (vsr || dlss || trueHdr));
            // Saved legacy settings may contain a conflict. Do not erase them
            // silently during load; explain the exact issue before conversion.
            string warning = bad ? "\n⚠ Conflicting saved routes: click a desired route to resolve them." : "";
            lblScaleCompatibility.ForeColor = bad ? Theme.Accent1 : Theme.Muted;
            lblScaleCompatibility.Text =
                "✓ Standard Scale + OptiScaler: compatible. OptiScaler is the final supersampling / RCAS / downscale stage.\n" +
                "✓ OptiScaler by itself: EWA Lanczos Sharp is used as its input scaler.\n" +
                "✓ RTX VSR + TrueHDR: compatible in the NVIDIA RTX pipeline. Native RTX filters can also chain DLSS; actual filter/driver support is checked at runtime.\n" +
                "✓ Custom Python / ONNX AI: separate preprocessing pass, may precede any of the routes (requires an additional encode/intermediate).\n" +
                "↔ Alternative output routes: Scale/OptiScaler, SeedVR2, VisionNext/RazorNext, and RTX cannot be mixed in the current executable. Selecting one switches off incompatible routes.\n" +
                "Note: output resolution is a size request, not a second AI model. Image output only supports RTX VSR (not DLSS/TrueHDR)." +
                warning + (scaleLastChange.Length > 0 ? "\n" + scaleLastChange : "");
        }
    }

    // WinForms can deliver the mouse wheel to a focused inner combo/slider
    // even while the pointer is over a different settings card. Route the
    // wheel to the settings viewport under the POINTER, without Focus() and
    // without changing the active child control. Open combo dropdowns retain
    // their native wheel behavior.
    internal sealed class SettingsWheelRouter : IMessageFilter
    {
        private readonly ScrollableControl[] viewports;
        private const int WM_MOUSEWHEEL = 0x020A;
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);

        internal SettingsWheelRouter(params ScrollableControl[] panels) { viewports = panels; }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL) return false;
            Point mouse = Control.MousePosition;
            // Do not scroll a background settings panel whose rectangle lies
            // beneath another window. Resolve the TOPMOST child under pointer.
            Control hoveredControl = Control.FromChildHandle(WindowFromPoint(mouse));
            Form owner = hoveredControl != null ? hoveredControl.FindForm() : null;
            if (owner == null) return false;
            foreach (var panel in viewports)
            {
                if (panel == null || panel.IsDisposed || !panel.Visible || !panel.IsHandleCreated) continue;
                if (panel.FindForm() != owner) continue;
                if (!panel.RectangleToScreen(panel.ClientRectangle).Contains(mouse)) continue;
                // A prior CLICK that gave a genuine interactive control focus
                // must keep native wheel behavior while hovered. Mere pointer
                // movement never receives focus and therefore scrolls the card.
                if (AnyDropdownOpen(panel)) return false;
                if (hoveredControl != null && hoveredControl.ContainsFocus &&
                    (hoveredControl is ComboBox || hoveredControl is TrackBar ||
                     hoveredControl is NumericUpDown || hoveredControl is ListBox ||
                     hoveredControl is ListView || hoveredControl is TextBoxBase))
                    return false;
                var bar = panel.VerticalScroll;
                int limit = Math.Max(0, bar.Maximum - bar.LargeChange + 1);
                if (limit == 0) return false;
                int delta = unchecked((short)((m.WParam.ToInt64() >> 16) & 0xffff));
                int ticks = Math.Max(1, Math.Abs(delta) / 120);
                int step = Math.Max(Theme.S(36), bar.SmallChange) * Math.Max(1, Math.Min(3, SystemInformation.MouseWheelScrollLines));
                int target = Math.Max(0, Math.Min(limit, -panel.AutoScrollPosition.Y - Math.Sign(delta) * ticks * step));
                panel.AutoScrollPosition = new Point(0, target);
                return true;
            }
            return false;
        }

        private static bool AnyDropdownOpen(Control c)
        {
            var cb = c as ComboBox;
            if (cb != null && cb.DroppedDown) return true;
            foreach (Control child in c.Controls)
                if (AnyDropdownOpen(child)) return true;
            return false;
        }
    }
}
