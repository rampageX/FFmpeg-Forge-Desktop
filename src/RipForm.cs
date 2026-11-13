using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace FFmpegForge
{
    /// 2.3.1 — "Rip DVD / Blu-ray" window: source (drive, folder, ISO) → titles → tracks → format → rip.
    public class RipForm : Form
    {
        readonly Font fUI = new Font("Segoe UI", 9.5f), fSmall = new Font("Segoe UI", 8.5f), fSec = new Font("Segoe UI Semibold", 8.5f), fTitle = new Font("Segoe UI Semibold", 13f);
        ComboBox cbSource, cbFormat, cbVideo, cbQuality, cbSpeed, cbRes, cbDeint, cbAudio, cbKbps, cbSubs;
        CheckBox chkShort, chkDownmix, chkChapters, chkAdd;
        TextBox txtOut;
        DarkList lvTitles;
        CheckedListBox clAudio, clSubs;
        Label lblStatus, lblNote, lblTracks;
        ProgressBar bar;
        RichTextBox log;
        FlatBtn btnScan, btnRip, btnCancel;
        DiscSource src;
        DiscTitle focused;
        volatile bool cancel, busy;
        bool fill;
        readonly Action<string[]> addToList;
        readonly Dictionary<string, string> settings;

        static readonly string[] FormatKeys = { "mkv", "mp4", "mov", "webm", "flac", "mp3", "m4a", "wav", "mka" };
        static readonly string[] VideoKeys = { "copy", "x264", "x265", "av1", "nv264", "nv265", "nvav1" };
        static readonly string[] AudioKeys = { "copy", "aac", "ac3", "eac3", "flac", "opus", "mp3" };
        static readonly int[] Heights = { 0, 1080, 720, 576, 480 };
        static readonly int[] Kbps = { 0, 128, 160, 192, 256, 320, 384, 448, 640 };

        public RipForm(Action<string[]> addToList, Dictionary<string, string> settings)
        {
            this.addToList = addToList;
            this.settings = settings ?? new Dictionary<string, string>();
            Text = "FFmpeg Forge — Rip DVD / Blu-ray";
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = fUI;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(Theme.S(1180), Theme.S(860));
            MinimumSize = new Size(Theme.S(980), Theme.S(720));
            ShowInTaskbar = false; MinimizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Build();
            DarkNativeScroll.Attach(this);
            LoadSettings();
            WindowPlacement.Restore(this, this.settings, "ui_rip");
            RefreshDrives();
            UpdateOptionState();
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkTitle.Apply(Handle); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy)
            {
                if (MessageBox.Show(this, "A rip is running. Stop it and close?", "Rip DVD / Blu-ray", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
                cancel = true;
            }
            SaveSettings();
            base.OnFormClosing(e);
        }

        // ------------------------------------------------------------------ UI
        ComboBox Combo(params object[] items)
        {
            var c = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text, Font = fUI, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), Theme.S(10), Theme.S(3)), IntegralHeight = false, MaxDropDownItems = 16 };
            c.Items.AddRange(items);
            if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        CheckBox Check(string t, bool on)
        {
            return new CheckBox { Text = t, Checked = on, AutoSize = true, ForeColor = Theme.Text, Font = fUI, Margin = new Padding(0, Theme.S(6), Theme.S(14), Theme.S(4)), FlatStyle = FlatStyle.Flat };
        }

        Label Lbl(string t, Font f = null, Color? c = null)
        {
            return new Label { Text = t, AutoSize = true, ForeColor = c ?? Theme.Muted, Font = f ?? fUI, Margin = new Padding(0, Theme.S(8), Theme.S(8), Theme.S(2)) };
        }

        FlatBtn Btn(string t, int w, bool primary = false)
        {
            var b = new FlatBtn(t) { Width = Theme.S(w), Height = Theme.S(38), Primary = primary, Margin = new Padding(0, Theme.S(2), Theme.S(10), Theme.S(2)) };
            return b;
        }

        void Build()
        {
            // All rows fit the available client width; add native scrollbars only
            // when OS text scaling or a small screen makes content truly overflow.
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, RowCount = 5, BackColor = Theme.Bg, Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(14)) };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // source
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));       // titles + tracks
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(286))); // compact fixed-size options: no autosize width explosion
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 22));       // log
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));          // buttons

            // ---------- source ----------
            var srcBox = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 6, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, Theme.S(8)) };
            srcBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            srcBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) srcBox.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var t = Lbl("RIP DVD / BLU-RAY", fTitle, Theme.Gold); t.Margin = new Padding(0, Theme.S(4), Theme.S(18), 0);
            cbSource = Combo(); cbSource.DropDownWidth = Theme.S(520);
            var bRefresh = Btn("Refresh", 96); bRefresh.Icon = "refresh"; bRefresh.Click += (s, e) => RefreshDrives();
            var bFolder = Btn("Folder…", 104); bFolder.Icon = "folder"; bFolder.Click += (s, e) => PickFolder();
            var bIso = Btn("ISO image…", 120); bIso.Icon = "file"; bIso.Click += (s, e) => PickIso();
            btnScan = Btn("Read disc", 128, true); btnScan.Icon = "search"; btnScan.Click += (s, e) => StartScan();
            srcBox.Controls.Add(t, 0, 0); srcBox.Controls.Add(cbSource, 1, 0); srcBox.Controls.Add(bRefresh, 2, 0); srcBox.Controls.Add(bFolder, 3, 0); srcBox.Controls.Add(bIso, 4, 0); srcBox.Controls.Add(btnScan, 5, 0);
            lblStatus = Lbl("Insert a DVD or a Blu-ray, or pick a VIDEO_TS / BDMV folder or an ISO image, then click \"Read disc\".", fSmall);
            srcBox.Controls.Add(lblStatus, 0, 1); srcBox.SetColumnSpan(lblStatus, 6);
            root.Controls.Add(srcBox, 0, 0);

            // ---------- titles | tracks ----------
            var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Theme.Bg, Margin = new Padding(0) };
            mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            var left = new CardTable { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(Theme.S(10)), ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, Theme.S(10), 0) };
            left.RowStyles.Add(new RowStyle(SizeType.AutoSize)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var lh = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Panel, WrapContents = false, Margin = new Padding(0) };
            lh.Controls.Add(Lbl("TITLES TO RIP", fSec, Theme.Gold));
            chkShort = Check("Hide titles shorter than 1 minute", true); chkShort.Font = fSmall; chkShort.ForeColor = Theme.Muted;
            chkShort.CheckedChanged += (s, e) => { if (src != null && !busy) StartScan(); };
            lh.Controls.Add(chkShort);
            left.Controls.Add(lh, 0, 0);
            lvTitles = new DarkList { Dock = DockStyle.Fill, Font = fUI, MultiSelect = false };
            lvTitles.Columns.Add("", Theme.S(34));
            lvTitles.Columns.Add("Title", Theme.S(130));
            lvTitles.Columns.Add("Duration", Theme.S(86));
            lvTitles.Columns.Add("Chapters", Theme.S(76));
            lvTitles.Columns.Add("Video", Theme.S(130));
            lvTitles.Columns.Add("Audio / subtitles", Theme.S(220));
            lvTitles.DrawColumnHeader += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
                var r = e.Bounds; r.X += Theme.S(6);
                TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), fSec, r, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            };
            lvTitles.DrawItem += (s, e) => { };
            lvTitles.Resize += (s, e) =>
            {
                int used = 0;
                for (int c = 0; c < lvTitles.Columns.Count - 1; c++) used += lvTitles.Columns[c].Width;
                lvTitles.Columns[lvTitles.Columns.Count - 1].Width = Math.Max(Theme.S(120), lvTitles.ClientSize.Width - used - 2);
            };
            lvTitles.DrawSubItem += DrawTitle;
            lvTitles.MouseClick += (s, e) =>
            {
                var hit = lvTitles.HitTest(e.Location);
                if (hit.Item == null) return;
                var tt = hit.Item.Tag as DiscTitle;
                if (tt == null) return;
                if (e.X < lvTitles.Columns[0].Width) { tt.Selected = !tt.Selected; lvTitles.Invalidate(); UpdateRipButton(); }
            };
            lvTitles.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Space && lvTitles.SelectedItems.Count == 1) { var tt = (DiscTitle)lvTitles.SelectedItems[0].Tag; tt.Selected = !tt.Selected; lvTitles.Invalidate(); UpdateRipButton(); e.Handled = true; }
            };
            lvTitles.DoubleClick += (s, e) => { if (lvTitles.SelectedItems.Count == 1) { var tt = (DiscTitle)lvTitles.SelectedItems[0].Tag; tt.Selected = !tt.Selected; lvTitles.Invalidate(); UpdateRipButton(); } };
            lvTitles.SelectedIndexChanged += (s, e) => { if (lvTitles.SelectedItems.Count == 1) ShowTracks((DiscTitle)lvTitles.SelectedItems[0].Tag); };
            left.Controls.Add(lvTitles, 0, 1);
            mid.Controls.Add(left, 0, 0);

            var right = new CardTable { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(Theme.S(10)), ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize)); right.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
            lblTracks = Lbl("AUDIO TRACKS", fSec, Theme.Gold);
            right.Controls.Add(lblTracks, 0, 0);
            clAudio = new CheckedListBox { Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = fUI, CheckOnClick = true, IntegralHeight = false };
            clAudio.ItemCheck += (s, e) => { if (fill || focused == null) return; var a = focused.Audio.ToList(); if (e.Index < a.Count) a[e.Index].Selected = e.NewValue == CheckState.Checked; BeginInvoke((Action)(() => lvTitles.Invalidate())); };
            right.Controls.Add(clAudio, 0, 1);
            right.Controls.Add(Lbl("SUBTITLES", fSec, Theme.Gold), 0, 2);
            clSubs = new CheckedListBox { Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = fUI, CheckOnClick = true, IntegralHeight = false };
            clSubs.ItemCheck += (s, e) => { if (fill || focused == null) return; var a = focused.Subs.ToList(); if (e.Index < a.Count) a[e.Index].Selected = e.NewValue == CheckState.Checked; BeginInvoke((Action)(() => lvTitles.Invalidate())); };
            right.Controls.Add(clSubs, 0, 3);
            mid.Controls.Add(right, 1, 0);
            root.Controls.Add(mid, 0, 1);

            // ---------- options ----------
            // Fixed label widths + proportional input columns. In the old layout
            // AutoSize and Percent fought each other, making the option card wider
            // than even a maximized window. Keep the 3-group format but bound it.
            var opt = new CardTable { Dock = DockStyle.Fill, AutoSize = false, BackColor = Theme.Panel,
                Padding = new Padding(Theme.S(12), Theme.S(8), Theme.S(12), Theme.S(10)),
                ColumnCount = 6, RowCount = 6,
                Margin = new Padding(0, Theme.S(10), 0, Theme.S(10)) };
            opt.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(64)));
            opt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
            opt.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(84)));
            opt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
            opt.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Theme.S(88)));
            opt.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            for (int i = 0; i < 3; i++) opt.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(36)));
            opt.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(38))); // output path
            opt.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(40))); // checkboxes
            opt.RowStyles.Add(new RowStyle(SizeType.Percent, 100));           // wrapping notes
            cbFormat = Combo("MKV — keeps everything (recommended)", "MP4 — plays everywhere", "MOV — Apple / editing", "WebM — AV1 + Opus",
                             "FLAC — audio only, lossless", "MP3 — audio only", "M4A (AAC) — audio only", "WAV — audio only, uncompressed", "MKA — audio only, all tracks");
            cbVideo = Combo("Copy — original quality, no re-encoding", "H.264 (x264)", "H.265 / HEVC (x265, 10-bit)", "AV1 (SVT-AV1, 10-bit)", "H.264 — NVIDIA NVENC", "H.265 — NVIDIA NVENC (10-bit)", "AV1 — NVIDIA NVENC (RTX 40/50)");
            cbQuality = Combo("Very high", "High (recommended)", "Medium", "Small file"); cbQuality.SelectedIndex = 1;
            cbSpeed = Combo("Slow — best compression", "Medium", "Fast"); cbSpeed.SelectedIndex = 1;
            cbRes = Combo("Original", "1080p (max)", "720p (max)", "576p (max)", "480p (max)");
            cbDeint = Combo("Auto (interlaced frames only)", "Off", "Always");
            cbAudio = Combo("Copy — original (lossless)", "AAC", "Dolby Digital (AC-3)", "Dolby Digital Plus (E-AC-3)", "FLAC (lossless)", "Opus", "MP3");
            cbKbps = Combo("Automatic", "128 kb/s", "160 kb/s", "192 kb/s", "256 kb/s", "320 kb/s", "384 kb/s", "448 kb/s", "640 kb/s");
            cbSubs = Combo("Keep the ticked subtitles (MKV)", "No subtitles", "Burn the first ticked subtitle into the picture");
            // Visible fields stay compact; long selections remain readable in
            // the dropdown popup rather than expanding the entire form.
            cbFormat.MaximumSize = new Size(Theme.S(300), 0);
            cbVideo.MaximumSize = new Size(Theme.S(345), 0);
            cbQuality.MaximumSize = new Size(Theme.S(190), 0);
            cbSpeed.MaximumSize = new Size(Theme.S(210), 0);
            cbRes.MaximumSize = new Size(Theme.S(245), 0);
            cbDeint.MaximumSize = new Size(Theme.S(190), 0);
            cbAudio.MaximumSize = new Size(Theme.S(300), 0);
            cbKbps.MaximumSize = new Size(Theme.S(245), 0);
            cbSubs.MaximumSize = new Size(Theme.S(190), 0);
            cbFormat.DropDownWidth = Theme.S(310);
            cbVideo.DropDownWidth = Theme.S(365);
            cbAudio.DropDownWidth = Theme.S(305);
            cbSubs.DropDownWidth = Theme.S(365);
            cbDeint.DropDownWidth = Theme.S(230);
            chkDownmix = Check("Downmix audio to stereo", false);
            chkChapters = Check("Keep chapters", true);
            chkAdd = Check("Add the ripped files to the list (to convert / upscale them further)", true);
            AddOpt(opt, 0, 0, "Format", cbFormat); AddOpt(opt, 0, 1, "Video", cbVideo); AddOpt(opt, 0, 2, "Quality", cbQuality);
            AddOpt(opt, 1, 0, "Speed", cbSpeed); AddOpt(opt, 1, 1, "Resolution", cbRes); AddOpt(opt, 1, 2, "Deinterlace", cbDeint);
            AddOpt(opt, 2, 0, "Audio", cbAudio); AddOpt(opt, 2, 1, "Audio bitrate", cbKbps); AddOpt(opt, 2, 2, "Subtitles", cbSubs);
            var outRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, BackColor = Theme.Panel, Margin = new Padding(0) };
            outRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); outRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); outRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            txtOut = new TextBox { Dock = DockStyle.Fill, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Font = fUI, Margin = new Padding(0, Theme.S(6), Theme.S(10), Theme.S(3)) };
            txtOut.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "FFmpeg Forge Rips");
            var bOut = Btn("Browse…", 100); bOut.Icon = "folder"; bOut.Click += (s, e) => { using (var d = new FolderBrowserDialog { Description = "Folder for the ripped files", SelectedPath = txtOut.Text }) if (d.ShowDialog(this) == DialogResult.OK) txtOut.Text = d.SelectedPath; };
            outRow.Controls.Add(Lbl("Save to"), 0, 0); outRow.Controls.Add(txtOut, 1, 0); outRow.Controls.Add(bOut, 2, 0);
            opt.Controls.Add(outRow, 0, 3); opt.SetColumnSpan(outRow, 6);
            var checks = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0) };
            checks.Controls.Add(chkDownmix); checks.Controls.Add(chkChapters); checks.Controls.Add(chkAdd);
            opt.Controls.Add(checks, 0, 4); opt.SetColumnSpan(checks, 6);
            lblNote = Lbl("", fSmall, Theme.Gold);
            lblNote.AutoSize = false;
            lblNote.Dock = DockStyle.Fill;
            lblNote.Margin = new Padding(Theme.S(2), Theme.S(4), Theme.S(4), 0);
            opt.Controls.Add(lblNote, 0, 5); opt.SetColumnSpan(lblNote, 6);
            foreach (var c in new Control[] { cbFormat, cbVideo, cbAudio, cbSubs, cbRes, cbDeint, cbQuality, cbSpeed, cbKbps }) ((ComboBox)c).SelectedIndexChanged += (s, e) => UpdateOptionState();
            chkDownmix.CheckedChanged += (s, e) => UpdateOptionState();
            root.Controls.Add(opt, 0, 2);

            // ---------- log ----------
            log = new RichTextBox { Dock = DockStyle.Fill, BackColor = Theme.Panel, ForeColor = Theme.Muted, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9f), ReadOnly = true, DetectUrls = false, Margin = new Padding(0) };
            root.Controls.Add(log, 0, 3);

            // ---------- bottom ----------
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, BackColor = Theme.Bg, Margin = new Padding(0, Theme.S(10), 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            // This used to be a native ProgressBar stretched by Dock=Fill:
            // its Windows light theme created the large white bottom strip.
            bar = new DarkProgressBar { Dock = DockStyle.Fill, Height = Theme.S(12),
                Margin = new Padding(0, Theme.S(17), Theme.S(16), Theme.S(17)), Maximum = 1000 };
            btnRip = Btn("RIP", 170, true); btnRip.Glow = true; btnRip.Icon = "refresh"; btnRip.Height = Theme.S(44); btnRip.Click += (s, e) => StartRip();
            btnCancel = Btn("Cancel", 110); btnCancel.Icon = "x"; btnCancel.Height = Theme.S(44); btnCancel.Enabled = false; btnCancel.Click += (s, e) => { cancel = true; Log("Cancelling…", Theme.Gold); };
            var bClose = Btn("Close", 110); bClose.Height = Theme.S(44); bClose.Click += (s, e) => Close();
            bottom.Controls.Add(bar, 0, 0); bottom.Controls.Add(btnCancel, 1, 0); bottom.Controls.Add(bClose, 2, 0); bottom.Controls.Add(btnRip, 3, 0);
            root.Controls.Add(bottom, 0, 4);
            Controls.Add(root);
            UpdateRipButton();
        }

        void AddOpt(TableLayoutPanel t, int row, int col, string label, Control c)
        {
            var l = Lbl(label); l.Margin = new Padding(0, Theme.S(9), Theme.S(8), 0);
            t.Controls.Add(l, col * 2, row); t.Controls.Add(c, col * 2 + 1, row);
        }

        void DrawTitle(object sender, DrawListViewSubItemEventArgs e)
        {
            var t = e.Item.Tag as DiscTitle;
            var g = e.Graphics;
            bool sel = e.Item.Selected;
            using (var b = new SolidBrush(sel ? Theme.Selection : Theme.Panel)) g.FillRectangle(b, e.Bounds);
            if (t == null) return;
            if (e.ColumnIndex == 0)
            {
                int s = Theme.S(16);
                var r = new Rectangle(e.Bounds.X + Theme.S(9), e.Bounds.Y + (e.Bounds.Height - s) / 2, s, s);
                using (var p = Theme.Round(r, Theme.S(3)))
                {
                    if (t.Selected) using (var b = new SolidBrush(Theme.Gold)) g.FillPath(b, p);
                    using (var pen = new Pen(t.Selected ? Theme.Gold : Theme.Border, 1.5f)) g.DrawPath(pen, p);
                }
                if (t.Selected)
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    using (var pen = new Pen(Theme.Bg, Theme.S(2)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round })
                        g.DrawLines(pen, new[] { new PointF(r.X + s * 0.24f, r.Y + s * 0.52f), new PointF(r.X + s * 0.43f, r.Y + s * 0.70f), new PointF(r.X + s * 0.78f, r.Y + s * 0.32f) });
                }
                return;
            }
            string txt = e.SubItem.Text;
            var col = t.Error != null ? Theme.Error : e.ColumnIndex == 1 ? Theme.Text : Theme.Muted;
            var rr = e.Bounds; rr.X += Theme.S(6); rr.Width -= Theme.S(6);
            TextRenderer.DrawText(g, txt, e.ColumnIndex == 1 ? fSec : fUI, rr, col, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void Log(string s, Color? c = null)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => Log(s, c))); } catch { } return; }
            log.SelectionStart = log.TextLength; log.SelectionLength = 0;
            log.SelectionColor = c ?? Theme.Muted;
            log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + "\n");
            log.ScrollToCaret();
        }

        void Status(string s, Color? c = null)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke((Action)(() => Status(s, c))); } catch { } return; }
            lblStatus.Text = s; lblStatus.ForeColor = c ?? Theme.Muted;
        }

        static string Dur(double s)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, s));
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        // ------------------------------------------------------------------ source
        class SrcItem { public string Path, Text; public override string ToString() { return Text; } }

        void RefreshDrives()
        {
            var keep = cbSource.SelectedItem as SrcItem;
            cbSource.Items.Clear();
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(x => x.DriveType == DriveType.CDRom))
                {
                    string label = "", kind = "";
                    bool ready = false;
                    try { ready = d.IsReady; if (ready) label = d.VolumeLabel; } catch { }
                    if (ready)
                    {
                        if (Directory.Exists(Path.Combine(d.RootDirectory.FullName, "BDMV"))) kind = "Blu-ray";
                        else if (Directory.Exists(Path.Combine(d.RootDirectory.FullName, "VIDEO_TS"))) kind = "DVD";
                        else kind = "no DVD / Blu-ray video";
                    }
                    cbSource.Items.Add(new SrcItem { Path = d.RootDirectory.FullName, Text = d.Name + "   " + (ready ? kind + (string.IsNullOrEmpty(label) ? "" : "  —  " + label) : "(empty drive)") });
                }
            }
            catch { }
            if (keep != null && !cbSource.Items.Cast<SrcItem>().Any(x => x.Path == keep.Path)) cbSource.Items.Add(keep);
            if (cbSource.Items.Count == 0) cbSource.Items.Add(new SrcItem { Path = "", Text = "No disc drive found — pick a folder (VIDEO_TS / BDMV) or an ISO image" });
            int idx = keep != null ? cbSource.Items.Cast<SrcItem>().ToList().FindIndex(x => x.Path == keep.Path) : -1;
            if (idx < 0) idx = cbSource.Items.Cast<SrcItem>().ToList().FindIndex(x => x.Text.Contains("DVD") || x.Text.Contains("Blu-ray"));
            cbSource.SelectedIndex = Math.Max(0, idx);
        }

        void SetSource(string path, string text)
        {
            var it = new SrcItem { Path = path, Text = text };
            cbSource.Items.Add(it); cbSource.SelectedItem = it;
            StartScan();
        }

        void PickFolder()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose the DVD / Blu-ray folder (the one that contains VIDEO_TS or BDMV, or that folder itself)" })
                if (d.ShowDialog(this) == DialogResult.OK) SetSource(d.SelectedPath, d.SelectedPath + "   (folder)");
        }

        void PickIso()
        {
            using (var d = new OpenFileDialog { Filter = "Disc images (*.iso)|*.iso|All files|*.*", Title = "Choose a DVD or Blu-ray ISO image" })
                if (d.ShowDialog(this) == DialogResult.OK) SetSource(d.FileName, d.FileName + "   (ISO image)");
        }

        // ------------------------------------------------------------------ scan
        void StartScan()
        {
            if (busy) return;
            var it = cbSource.SelectedItem as SrcItem;
            string err;
            var s = DiscScanner.Detect(it == null ? null : it.Path, out err);
            if (s == null) { Status(err, Theme.Error); return; }
            src = s; focused = null;
            lvTitles.Items.Clear(); clAudio.Items.Clear(); clSubs.Items.Clear();
            if (s.Protected) { Status(s.ProtectionNote, Theme.Error); Log(s.ProtectionNote, Theme.Error); UpdateRipButton(); return; }
            busy = true; cancel = false; SetBusy(true);
            double min = chkShort.Checked ? 60 : 0;
            Log("Reading " + (s.IsIso ? "ISO image " : s.Kind + " ") + s.Path + "…", Theme.Text);
            new Thread(() =>
            {
                string e2 = null;
                try { DiscScanner.Scan(s, min, m => Status(m), () => cancel, out e2); }
                catch (Exception ex) { e2 = ex.Message; }
                try { BeginInvoke((Action)(() => ScanDone(s, e2))); } catch { }
            }) { IsBackground = true }.Start();
        }

        void ScanDone(DiscSource s, string err)
        {
            busy = false; SetBusy(false);
            if (s != src) return;
            if (s.Protected) { Status(s.ProtectionNote, Theme.Error); Log(s.ProtectionNote, Theme.Error); FillTitles(); UpdateRipButton(); return; }
            if (err != null && s.Titles.Count == 0) { Status(err.Split('\n')[0], Theme.Error); Log(err, Theme.Error); UpdateRipButton(); return; }
            FillTitles();
            int n = s.Titles.Count(t => t.Error == null);
            Status(s.Kind + (s.IsIso ? " image" : "") + " \"" + s.Label + "\" — " + n + " title" + (n > 1 ? "s" : "") + (chkShort.Checked ? " (1 minute or longer)" : "") +
                   ". The longest one is ticked; tick the others you want, and choose the tracks on the right.", Theme.Success);
            Log(s.Kind + " \"" + s.Label + "\": " + n + " title(s) found.", Theme.Success);
            if (err != null) Log(err, Theme.Gold);
            if (s.IsBluray && !s.HasLongPlaylist) Log("No playlist of 3 minutes or more on this Blu-ray: the clips (.m2ts) are read directly.", Theme.Muted);
        }

        void FillTitles()
        {
            lvTitles.BeginUpdate();
            lvTitles.Items.Clear();
            foreach (var t in src.Titles)
            {
                var v = t.Video.FirstOrDefault();
                string audio = t.Audio.Count() + " audio" + (t.Audio.Any() ? " (" + string.Join(", ", t.Audio.Select(a => string.IsNullOrEmpty(a.Lang) ? DiscStream.CodecName(a.Codec) : a.Lang).Distinct().Take(5).ToArray()) + ")" : "") +
                               (t.Subs.Any() ? " · " + t.Subs.Count() + " subtitles" : "");
                var it = new ListViewItem(new[] { "", t.Number < 0 ? "Main movie" : t.Name(src.IsBluray) + (t.MultiAngle ? "  (angles)" : ""), Dur(t.Duration), t.Chapters > 0 ? t.Chapters.ToString() : "—",
                                                  t.Error != null ? "unreadable" : v == null ? "—" : DiscStream.CodecName(v.Codec) + " " + v.Width + "×" + v.Height, t.Error ?? audio }) { Tag = t };
                lvTitles.Items.Add(it);
            }
            lvTitles.EndUpdate();
            var main = src.Titles.FirstOrDefault(t => t.Selected) ?? src.Titles.FirstOrDefault();
            if (main != null) { int i = src.Titles.IndexOf(main); lvTitles.Items[i].Selected = true; lvTitles.Items[i].Focused = true; lvTitles.EnsureVisible(i); ShowTracks(main); }
            UpdateRipButton();
        }

        void ShowTracks(DiscTitle t)
        {
            focused = t;
            fill = true;
            clAudio.Items.Clear(); clSubs.Items.Clear();
            int i = 1;
            var al = t.Audio.ToList(); var sl = t.Subs.ToList();
            foreach (var a in al) clAudio.Items.Add(i++ + ".  " + a.Describe());
            for (int k = 0; k < al.Count; k++) clAudio.SetItemChecked(k, al[k].Selected);
            i = 1;
            foreach (var s in sl) clSubs.Items.Add(i++ + ".  " + s.Describe());
            for (int k = 0; k < sl.Count; k++) clSubs.SetItemChecked(k, sl[k].Selected);
            if (clSubs.Items.Count == 0) clSubs.Items.Add("(no subtitles)");
            fill = false;
            lblTracks.Text = "AUDIO TRACKS — " + (t.Number < 0 ? "MAIN MOVIE" : t.Name(src != null && src.IsBluray).ToUpperInvariant());
        }

        // ------------------------------------------------------------------ options
        RipOptions Options()
        {
            return new RipOptions
            {
                Container = FormatKeys[Math.Max(0, cbFormat.SelectedIndex)],
                VCodec = VideoKeys[Math.Max(0, cbVideo.SelectedIndex)],
                Quality = cbQuality.SelectedIndex, Speed = cbSpeed.SelectedIndex,
                Height = Heights[Math.Max(0, cbRes.SelectedIndex)], Deint = cbDeint.SelectedIndex,
                ACodec = AudioKeys[Math.Max(0, cbAudio.SelectedIndex)], AKbps = Kbps[Math.Max(0, cbKbps.SelectedIndex)],
                Downmix = chkDownmix.Checked, Subs = cbSubs.SelectedIndex, Chapters = chkChapters.Checked,
                OutDir = txtOut.Text.Trim(), AddToList = chkAdd.Checked
            };
        }

        void UpdateOptionState()
        {
            var o = Options();
            bool audioOnly = o.AudioOnly;
            bool copy = o.VCodec == "copy";
            cbVideo.Enabled = !audioOnly;
            cbQuality.Enabled = cbSpeed.Enabled = cbRes.Enabled = cbDeint.Enabled = !audioOnly && !copy;
            cbSubs.Enabled = !audioOnly;
            bool fixedAudio = o.Container == "flac" || o.Container == "mp3" || o.Container == "wav";
            cbAudio.Enabled = !fixedAudio;
            cbKbps.Enabled = !(o.ACodec == "copy" && !o.Downmix) && o.ACodec != "flac" && o.Container != "flac" && o.Container != "wav";
            var n = new List<string>();
            if (audioOnly) n.Add("Audio only: the picture and subtitles are left out." + (o.Container == "flac" || o.Container == "mp3" || o.Container == "wav" ? " One audio track (the first ticked one)." : ""));
            else
            {
                if (copy) n.Add("Copy keeps the original picture bit for bit (fast, same size as the disc). Choose an encoder to make the file smaller or to burn subtitles.");
                if (o.Container != "mkv" && o.Subs == 0) n.Add(o.Container.ToUpperInvariant() + " cannot store DVD / Blu-ray subtitles (they are pictures): use MKV, or burn one in.");
                if (o.Container == "webm") n.Add("WebM: AV1 video and Opus audio only.");
                if (o.Subs == 2 && copy) n.Add("Burning subtitles needs a video encoder (not Copy).");
                if (o.VCodec.StartsWith("nv")) n.Add("NVENC needs an NVIDIA GPU (AV1: RTX 40 / 50 series).");
            }
            if (o.ACodec == "copy" && !audioOnly) n.Add("Copy audio: LPCM tracks are converted to FLAC in MKV (lossless); other tracks the format cannot hold are converted automatically.");
            lblNote.Text = string.Join("  ", n.ToArray());
        }

        void UpdateRipButton()
        {
            btnRip.Enabled = !busy && src != null && !src.Protected && src.Titles.Any(t => t.Selected && t.Error == null);
            int n = src == null ? 0 : src.Titles.Count(t => t.Selected && t.Error == null);
            btnRip.Text = n > 1 ? "RIP " + n + " TITLES" : "RIP";
        }

        void SetBusy(bool on)
        {
            btnScan.Enabled = !on; btnCancel.Enabled = on; cbSource.Enabled = !on; chkShort.Enabled = !on;
            UpdateRipButton();
        }

        // ------------------------------------------------------------------ rip
        void StartRip()
        {
            if (busy || src == null) return;
            var o = Options();
            if (string.IsNullOrEmpty(o.OutDir)) { Status("Choose the folder for the ripped files.", Theme.Error); return; }
            try { Directory.CreateDirectory(o.OutDir); } catch (Exception ex) { Status("Cannot create " + o.OutDir + ": " + ex.Message, Theme.Error); return; }
            var titles = src.Titles.Where(t => t.Selected && t.Error == null).ToList();
            if (titles.Count == 0) return;
            // check every command before starting
            foreach (var t in titles)
            {
                string err; var notes = new List<string>();
                if (RipBuilder.Build(src, t, o, Path.Combine(o.OutDir, "check." + o.Container), Path.Combine(Path.GetTempPath(), "x.txt"), notes, out err) == null)
                { Status((t.Number < 0 ? "Main movie" : t.Name(src.IsBluray)) + ": " + err, Theme.Error); Log(err, Theme.Error); return; }
            }
            SaveSettings();
            busy = true; cancel = false; SetBusy(true); bar.Value = 0;
            var s = src;
            new Thread(() => RipThread(s, titles, o)) { IsBackground = true }.Start();
        }

        void RipThread(DiscSource s, List<DiscTitle> titles, RipOptions o)
        {
            var done = new List<string>();
            int fails = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < titles.Count && !cancel; i++)
            {
                var t = titles[i];
                string name = t.Number < 0 ? "Main movie" : t.Name(s.IsBluray);
                if (s.IsDvd)
                {
                    Status("Checking " + name + "…");
                    string why;
                    if (!DiscScanner.CheckDvdReadable(s, t, () => cancel, out why)) { Log(name + ": " + why, Theme.Error); fails++; continue; }
                }
                string outPath = RipBuilder.OutputName(s, t, o);
                string chap = null;
                if (s.IsBluray && t.ChapterTimes.Count > 1)
                {
                    chap = Path.Combine(Path.GetTempPath(), "FFmpegForge-chapters-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".txt");
                    try { RipBuilder.WriteChapters(t, chap); } catch { chap = null; }
                }
                var notes = new List<string>(); string err;
                string args = RipBuilder.Build(s, t, o, outPath, chap, notes, out err);
                if (args == null) { Log(name + ": " + err, Theme.Error); fails++; continue; }
                foreach (var n in notes) Log("   " + n, Theme.Gold);
                Log("▶ " + name + " (" + Dur(t.Duration) + ") → " + outPath, Theme.Text);
                Log("   ffmpeg " + args, Theme.Muted);
                Status("Ripping " + name + " (" + (i + 1) + " / " + titles.Count + ")…");
                int idx = i;
                var tsw = System.Diagnostics.Stopwatch.StartNew();
                string lastErr;
                int code = Runner.Run(Runner.FFmpeg, args, t.Duration, p =>
                {
                    double all = (idx + p) / titles.Count;
                    string eta = p > 0.02 ? "  ·  " + Dur(tsw.Elapsed.TotalSeconds * (1 - p) / p) + " left" : "";
                    try { BeginInvoke((Action)(() => { bar.Value = (int)Math.Round(Math.Min(1, all) * 1000); lblStatus.Text = "Ripping " + name + "  " + (p * 100).ToString("0", CultureInfo.InvariantCulture) + " %" + eta; })); } catch { }
                }, l => { if (!DiscScanner.Noise(l)) Log("   " + l, Theme.Muted); }, () => cancel, out lastErr);
                if (chap != null) try { File.Delete(chap); } catch { }
                if (code == 0 && File.Exists(outPath))
                {
                    long size = 0; try { size = new FileInfo(outPath).Length; } catch { }
                    Log("✔ " + name + " ripped in " + Dur(tsw.Elapsed.TotalSeconds) + " — " + (size / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB", Theme.Success);
                    done.Add(outPath);
                }
                else
                {
                    if (!cancel) { Log("✖ " + name + " failed (code " + code + ")" + (string.IsNullOrEmpty(lastErr) ? "" : ": " + lastErr.Split('\n').LastOrDefault(x => x.Trim().Length > 0 && !DiscScanner.Noise(x))), Theme.Error); fails++; }
                    try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                }
            }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    busy = false; SetBusy(false);
                    bar.Value = cancel ? bar.Value : 1000;
                    string msg = cancel ? "Rip cancelled. " + done.Count + " title(s) finished." :
                                 "Finished: " + done.Count + " title(s) ripped" + (fails > 0 ? ", " + fails + " failed" : "") + " in " + Dur(sw.Elapsed.TotalSeconds) + " → " + o.OutDir;
                    Status(msg, fails > 0 || cancel ? Theme.Gold : Theme.Success);
                    Log(msg, fails > 0 || cancel ? Theme.Gold : Theme.Success);
                    if (done.Count > 0 && o.AddToList && addToList != null) { addToList(done.ToArray()); Log("Added to the FFmpeg Forge list.", Theme.Muted); }
                    if (!cancel && done.Count > 0) System.Media.SystemSounds.Asterisk.Play();
                }));
            }
            catch { }
        }

        // ------------------------------------------------------------------ settings
        int Gi(string k, int def) { string v; int r; return settings.TryGetValue(k, out v) && int.TryParse(v, out r) ? r : def; }
        void SetIdx(ComboBox c, int v) { if (v >= 0 && v < c.Items.Count) c.SelectedIndex = v; }

        void LoadSettings()
        {
            SetIdx(cbFormat, Gi("rip_fmt", 0)); SetIdx(cbVideo, Gi("rip_v", 0)); SetIdx(cbQuality, Gi("rip_q", 1)); SetIdx(cbSpeed, Gi("rip_sp", 1));
            SetIdx(cbRes, Gi("rip_res", 0)); SetIdx(cbDeint, Gi("rip_deint", 0)); SetIdx(cbAudio, Gi("rip_a", 0)); SetIdx(cbKbps, Gi("rip_kbps", 0)); SetIdx(cbSubs, Gi("rip_subs", 0));
            chkDownmix.Checked = Gi("rip_down", 0) == 1; chkChapters.Checked = Gi("rip_chap", 1) == 1; chkAdd.Checked = Gi("rip_add", 1) == 1; chkShort.Checked = Gi("rip_short", 1) == 1;
            string d; if (settings.TryGetValue("rip_out", out d) && !string.IsNullOrWhiteSpace(d)) txtOut.Text = d;
        }

        void SaveSettings()
        {
            WindowPlacement.Save(this, settings, "ui_rip");
            settings["rip_fmt"] = cbFormat.SelectedIndex.ToString(); settings["rip_v"] = cbVideo.SelectedIndex.ToString(); settings["rip_q"] = cbQuality.SelectedIndex.ToString();
            settings["rip_sp"] = cbSpeed.SelectedIndex.ToString(); settings["rip_res"] = cbRes.SelectedIndex.ToString(); settings["rip_deint"] = cbDeint.SelectedIndex.ToString();
            settings["rip_a"] = cbAudio.SelectedIndex.ToString(); settings["rip_kbps"] = cbKbps.SelectedIndex.ToString(); settings["rip_subs"] = cbSubs.SelectedIndex.ToString();
            settings["rip_down"] = chkDownmix.Checked ? "1" : "0"; settings["rip_chap"] = chkChapters.Checked ? "1" : "0"; settings["rip_add"] = chkAdd.Checked ? "1" : "0";
            settings["rip_short"] = chkShort.Checked ? "1" : "0"; settings["rip_out"] = txtOut.Text.Trim();
        }
    }
}
