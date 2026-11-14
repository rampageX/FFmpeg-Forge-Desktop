using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace FFmpegForge
{
    public partial class MainForm : Form
    {
        // ---- controls ----
        TopHeader header;
        TableLayoutPanel secFormat, secCai;
        readonly List<Control> caiRows = new List<Control>();
        // compression assistant (2.1)
        CardTable advCard; Label lblAdvTitle, lblAdvMsg, lblAdvFacts, lblAdvNote; FlatBtn btnAdvApply, btnAdvMeasure, btnAdvIgnore, btnOptPreset;
        CheckBox chkSuggest; Job advJob, advForced; bool advMeasuring;
        ComboBox cbCover; NumericUpDown nuCoverAt; Label lblCoverFile; string coverPath;
        TableLayoutPanel secTracks; CheckBox chkKeepVideo; ComboBox cbExistingAudio, cbTrackLang; ListBox lbTracks; TextBox txtTrackTitle; NumericUpDown nuTrackDelay; CheckBox chkTrackDefault; Label lblTracksInfo; bool trackSync;
        readonly List<ExtraTrack> extraTracks = new List<ExtraTrack>();
        ComboBox cbSeqExport; Label lblSeqInfo; CheckBox chkSeqPng, chkSeqJpg;
        ComboBox cbHdr, cbHdrPeak, cbHdrImg, cbHdrPeakImg; Label lblHdrInfo, lblHdrImgInfo, lblHdrPeak, lblHdrPeakImg;
        ComboBox cbDeint, cbDeintOrder; Label lblDeintOrder, lblDeintInfo; FlatBtn btnQtgmcInstall;
        CheckBox chkCai; ComboBox cbCaiModel, cbCaiSize, cbCaiProc, cbCaiTile, cbCaiPrec; Label lblCaiInfo, lblCaiRuntime; Control caiRuntimeBtn;
        DarkList list;
        DropHint dropHint;
        LogView log;
        GradientBar overall;
        Label statusLbl;
        FlatBtn btnConvert, btnCancel, btnOpenOut;
        FlatBtn catVideo, catAudio, catImage;
        ComboBox cbFormat;
        Label formatHint;
        TableLayoutPanel secVideo, secAudio, secImage, secFilters, secTrim, secOut;
        // video
        ComboBox cbVCodec, cbQuality, cbPreset, cbFps, cbRotate, cbACodec;
        NumericUpDown nuNvCrf; Label lblNvCrf, lblNvCrfHint;
        ResPicker resVideo, resImage;
        NumericUpDown nuVBitrate, nuStill;
        CheckBox chkSubs, chkPreview;
        PreviewWindow previewWin;
        // Scale (Échelle)
        TableLayoutPanel secScale, secOpti, secDn;
        // Upscale VisionNext
        TableLayoutPanel secVision;
        CheckBox chkVision;
        ComboBox cbVisionModel, cbVisionScale, cbVisionTile, cbVisionEngine, cbVisionMem;
        Control visionVeBar, visionInstallBtn, visionTileLbl, visionMemLbl;
        CheckBox chkVisionGpu, chkVisionCpu;
        Label lblVisionStatus, lblVisionInfo;
        readonly List<Control> visionRows = new List<Control>();
        // SeedVR2
        TableLayoutPanel secSeed;
        CheckBox chkSeed, chkSeedTiled, chkSeedOffload, chkSeedCompile;
        ComboBox cbSeedModel, cbSeedSize, cbSeedBatch, cbSeedColor, cbSeedAttn;
        TrackBar tbSeedInNoise, tbSeedLatNoise;
        Label lblSeedInNoise, lblSeedLatNoise, lblSeedStatus, lblSeedInfo;
        NumericUpDown nuSeedSeed, nuSeedChunk, nuSeedOverlap, nuSeedSwap;
        TextBox txtSeedGpu;
        readonly List<Control> seedRows = new List<Control>(), seedVidRows = new List<Control>();
        CheckBox chkDn;
        ComboBox cbDnModel; Label lblDnModel; Control dnModelBar; Control dnSupportBtn;
        Label lblDnInfo;
        CheckBox chkOpti, chkOptiContrast;
        ComboBox cbOptiMult, cbOptiKernel;
        TrackBar tbOptiSharp;
        Label lblOptiSharpVal, lblOptiInfo;
        readonly List<Control> optiRows = new List<Control>();
        static readonly double[] OptiMults = { 1.25, 1.5, 2, 2.5, 3 };
        CheckBox chkScale, chkScaleOpt, chkScaleFid, chkScale444;
        ComboBox cbScaleType, cbScaleFactor;
        TrackBar tbScaleSharp;
        Label lblScaleSharpVal, lblScaleInfo;
        readonly List<Control> scaleRows = new List<Control>();
        // NVIDIA RTX AI
        TableLayoutPanel secRtx;
        CheckBox chkVsr, chkHdr, chkDlss;
        ComboBox cbVsrQ, cbVsrScale, cbDlssMode, cbRtxEngine;
        NumericUpDown nuHdrContrast, nuHdrSat, nuHdrGray, nuHdrLum;
        Label lblRtxStatus;
        FlatBtn btnVsrParams, btnHdrParams, btnDlssParams;
        string vsrExpr, hdrExpr, dlssExpr;
        readonly List<Control> vsrRows = new List<Control>(), hdrRows = new List<Control>(), dlssRows = new List<Control>();
        Label lblACodec;
        // audio
        ComboBox cbAKbps, cbRate, cbCh, cbUpmix;
        // combo order -> ChannelLayouts style code (0 auto, 5 spatial, 1 music, 2 cinema, 3 live, 4 off)
        static readonly int[] UpmixCodes = { 0, ChannelLayouts.StyleSpatial, 1, 2, 3, 4 };
        Label lblUpmix;
        NumericUpDown nuVolume;
        CheckBox chkNorm;
        // image
        ComboBox cbExtract, cbImgRotate;
        TrackBar tbImgQ;
        Label lblImgQ;
        NumericUpDown nuExtractVal;
        Label lblExtractVal;
        // filters
        CheckedListBox filterList;
        Control filterTitle;
        TextBox txtGraph, txtExtraInputs;
        FlatBtn btnPresets;
        // trim / output
        TextBox txtStart, txtEnd, txtOutDir, txtCustom;
        CheckBox chkSameDir, chkOverwrite;
        FlatBtn btnBrowseOut;

        MediaKind category = MediaKind.Video;
        volatile bool running, cancelRequested;
        string lastOutput;
        readonly List<FilterEntry> filters = new List<FilterEntry>();
        readonly Dictionary<string, string> settings = new Dictionary<string, string>();
        readonly string settingsPath = Path.Combine(AppPaths.DataDir, "settings.ini");
        Font fUI, fSmall, fSection, fMono;
        ToolTip tips;

        const string StQueued = "Queued", StConverting = "Converting…", StDone = "Done", StError = "Error", StCancelled = "Cancelled";

        public MainForm()
        {
            using (var g = CreateGraphics()) Theme.Scale = g.DpiX / 96f;
            fUI = new Font("Segoe UI", 9.5f);
            fSmall = new Font("Segoe UI", 8.5f);
            fSection = new Font("Segoe UI Semibold", 8.5f);
            fMono = new Font("Consolas", 9f);
            tips = new ToolTip { AutoPopDelay = 15000 };

            Text = "FFmpeg Forge 2.3.2 — Universal Video • Audio • Image converter" + (AppPaths.Portable ? "  (portable)" : "");
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = fUI;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Theme.S(1460), Theme.S(960));
            MinimumSize = new Size(Theme.S(1060), Theme.S(700));
            AllowDrop = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            LoadSettings();
            WindowPlacement.Restore(this, settings, "ui_main");
            BuildUI();
            DarkNativeScroll.Attach(this);  // attach once; theme handles on creation, never per paint
            ApplySettings();
            WireScaleRouteEvents();

            DragEnter += OnDragEnter; DragDrop += OnDragDrop; DragLeave += (s, e) => { dropHint.Highlight = false; dropHint.Invalidate(); };
            FormClosing += OnClosing;
            Shown += (s, e) => InitFFmpeg();
        }

        // =====================================================================
        //  UI construction
        // =====================================================================
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); DarkTitle.Apply(Handle); }

        void BuildUI()
        {
            SuspendLayout();
            // ---------- center ----------
            var center = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Padding = new Padding(Theme.S(22), Theme.S(16), Theme.S(14), Theme.S(12)) };

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(62), BackColor = Theme.Bg, WrapContents = false, Padding = new Padding(0, 0, 0, Theme.S(14)) };
            var bAddF = Btn("Add files", 158); bAddF.Primary = true; bAddF.Icon = "file"; bAddF.Click += (s, e) => AddFilesDialog();
            var bAddD = Btn("Add folder", 142); bAddD.Icon = "plus"; bAddD.Click += (s, e) => AddFolderDialog();
            var bRem = Btn("Remove", 120); bRem.Icon = "trash"; bRem.Click += (s, e) => RemoveSelected();
            var bClr = Btn("Clear all", 118); bClr.Icon = "broom"; bClr.Click += (s, e) => ClearAll();
            var bCmd = Btn("Show command", 168); bCmd.Icon = "terminal"; bCmd.Click += (s, e) => PreviewCommand();
            var bFl = Btn("Filter library", 140); bFl.Icon = "funnel"; bFl.Click += (s, e) => AddFilter();
            // 2.3.0: rip DVD / Blu-ray (drive, VIDEO_TS / BDMV folder or ISO) with the format of your choice
            var bRip = Btn("Rip DVD/BD", 124); bRip.Gold = true; bRip.Icon = "disc"; bRip.Click += (s, e) => OpenRip();
            tips.SetToolTip(bRip, "Rip a DVD or a Blu-ray (disc drive, VIDEO_TS / BDMV folder or ISO image): choose the titles, audio tracks,\nsubtitles and the video / audio format (MKV copy, MP4 H.264 / H.265 / AV1, FLAC, MP3…).\nDiscs without copy protection only (your own videos, home-made or unprotected discs).");
            bAddF.Width = Theme.S(128); bAddD.Width = Theme.S(124); bRem.Width = Theme.S(104); bClr.Width = Theme.S(104); bCmd.Width = Theme.S(144); bFl.Width = Theme.S(130);
            foreach (var b in new[] { bAddF, bAddD, bRem, bClr, bCmd, bFl, bRip }) { b.Height = Theme.S(46); b.Margin = new Padding(0, 0, Theme.S(9), 0); toolbar.Controls.Add(b); }

            var listHost = new CardTable { Dock = DockStyle.Fill, BackColor = Theme.Panel, Padding = new Padding(Theme.S(6)), ColumnCount = 1, RowCount = 1 };
            list = new DarkList { Dock = DockStyle.Fill, Font = fUI, AllowDrop = true, ShowItemToolTips = true };
            list.Columns.Add("File", Theme.S(300));
            list.Columns.Add("Type", Theme.S(70));
            list.Columns.Add("Info", Theme.S(150));
            list.Columns.Add("Duration", Theme.S(80));
            list.Columns.Add("Size", Theme.S(80));
            list.Columns.Add("Status", Theme.S(170));
            list.DrawColumnHeader += List_DrawColumnHeader;
            list.DrawItem += (s, e) => { };
            list.DrawSubItem += List_DrawSubItem;
            list.DragEnter += OnDragEnter; list.DragDrop += OnDragDrop;
            list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveSelected(); if (e.Control && e.KeyCode == Keys.A) foreach (ListViewItem it in list.Items) it.Selected = true; };
            list.DoubleClick += (s, e) => OpenSelectedOutput();
            list.SelectedIndexChanged += (s, e) => { if (advForced != null && list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag != advForced) advForced = null; UpdateAdvisor(); };
            list.Resize += (s, e) => FitColumns();
            list.ContextMenuStrip = BuildContextMenu();

            dropHint = new DropHint { Dock = DockStyle.Fill, AllowDrop = true };
            dropHint.Click += (s, e) => AddFilesDialog();
            dropHint.DragEnter += OnDragEnter; dropHint.DragDrop += OnDragDrop;
            dropHint.DragLeave += (s, e) => { dropHint.Highlight = false; dropHint.Invalidate(); };
            var listInner = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0) };
            listInner.Controls.Add(list);
            listInner.Controls.Add(dropHint);
            dropHint.BringToFront();
            listHost.Controls.Add(listInner, 0, 0);

            var logHost = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(226), BackColor = Theme.Bg, Padding = new Padding(0, Theme.S(16), 0, 0) };
            var logCard = new CardTable { Dock = DockStyle.Fill, Padding = new Padding(Theme.S(18), Theme.S(10), Theme.S(14), Theme.S(10)), ColumnCount = 1, RowCount = 2 };
            logCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            logCard.RowStyles.Add(new RowStyle(SizeType.Absolute, Theme.S(44)));
            logCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var logHead = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0) };
            var logTitle = new SectionHeader("ACTIVITY LOG", "log") { Dock = DockStyle.Fill, BackColor = Theme.Panel };
            var bClearLog = Btn("Clear log", 112); bClearLog.Icon = "erase"; bClearLog.Height = Theme.S(32); bClearLog.Font = fSmall; bClearLog.Dock = DockStyle.Right;
            bClearLog.Click += (s, e) => log.Clear();
            logHead.Controls.Add(logTitle); logHead.Controls.Add(bClearLog);
            var logSep = new Panel { Height = 1, Dock = DockStyle.Bottom, BackColor = Color.FromArgb(36, 42, 56) };
            logHead.Controls.Add(logSep);
            log = new LogView { Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(2), 0, 0), Font = fUI, MonoFont = fMono };
            logCard.Controls.Add(logHead, 0, 0);
            logCard.Controls.Add(log, 0, 1);
            logHost.Controls.Add(logCard);

            BuildAdvisorCard();
            center.Controls.Add(listHost);
            center.Controls.Add(logHost);
            center.Controls.Add(advCard);
            center.Controls.Add(toolbar);
            toolbar.SendToBack();
            listHost.BringToFront();

            // ---------- options panel ----------
            var stack = new SmoothSettingsTable { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, BackColor = Theme.Bg };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // Format
            secFormat = Section("OUTPUT FORMAT");
            var cats = new TableLayoutPanel { Dock = DockStyle.Top, Height = Theme.S(60), ColumnCount = 3, Margin = new Padding(0, 0, 0, Theme.S(10)), BackColor = Theme.Panel };
            for (int i = 0; i < 3; i++) cats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            catVideo = Btn("VIDEO", 0); catAudio = Btn("AUDIO", 0); catImage = Btn("IMAGE", 0);
            catVideo.Icon = "film"; catAudio.Icon = "note"; catImage.Icon = "image";
            foreach (var b in new[] { catVideo, catAudio, catImage }) { b.Dock = DockStyle.Fill; b.Height = Theme.S(52); b.Font = new Font("Segoe UI Semibold", 10f); b.Margin = new Padding(0, 0, Theme.S(8), 0); cats.Controls.Add(b); }
            catImage.Margin = new Padding(0);
            catVideo.Click += (s, e) => SetCategory(MediaKind.Video);
            catAudio.Click += (s, e) => SetCategory(MediaKind.Audio);
            catImage.Click += (s, e) => SetCategory(MediaKind.Image);
            AddRow(secFormat, null, cats);
            cbFormat = Combo(); cbFormat.Dock = DockStyle.Top; cbFormat.Margin = new Padding(0, 0, 0, Theme.S(2));
            cbFormat.Font = new Font("Segoe UI Semibold", 10.5f);
            cbFormat.SelectedIndexChanged += (s, e) => OnFormatChanged();
            AddRow(secFormat, null, cbFormat);
            formatHint = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = fSmall, Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)) };
            AddRow(secFormat, null, formatHint);
            btnOptPreset = Btn(AdvisorText.Preset, 0); btnOptPreset.Toggled = true; btnOptPreset.Dock = DockStyle.Fill; btnOptPreset.Height = Theme.S(34);
            btnOptPreset.Margin = new Padding(0, Theme.S(6), 0, Theme.S(2));
            btnOptPreset.Click += (s2, e2) => OptimizedPreset();
            AddRow(secFormat, null, btnOptPreset);
            tips.SetToolTip(btnOptPreset, "Optimized compression: analyses the selected video (codec, bitrate, resolution, frame rate, tracks)\n" +
                "and fills the settings with a smaller, excellent-quality encode (H.265 / AV1, audio kept when possible).\n" +
                "Nothing starts by itself and every setting stays editable.");
            stack.Controls.Add(secFormat);

            // Video
            secVideo = Section("VIDEO SETTINGS");
            cbVCodec = Combo(Formats.VideoCodecs.Cast<object>().ToArray());
            cbQuality = Combo("Maximum", "Very high (recommended)", "Balanced", "Compact", "Very compact");
            SkipSeparator(cbVCodec);
            nuVBitrate = Num(0, 10000000, 0, 0); nuVBitrate.Increment = 500;
            cbPreset = Combo("Ultra fast", "Very fast", "Fast", "Medium", "Slow (better)", "Very slow (best)");
            cbFps = Combo("Original", "12", "15", "23.976", "24", "25", "29.97", "30", "48", "50", "59.94", "60", "120");
            cbRotate = Combo("None", "90° clockwise", "180°", "90° counter-clockwise", "Mirror horizontal", "Mirror vertical");
            nuStill = Num(1, 36000, 5, 1);
            chkSubs = Check("Keep all tracks + subtitles (MKV)");
            AddRow(secVideo, "Video codec", cbVCodec);
            AddRow(secVideo, "Quality", cbQuality);
            nuNvCrf = Num(0, 51, 0, 0);
            lblNvCrf = AddRow(secVideo, "CRF NVENC (0 = auto)", nuNvCrf);
            lblNvCrfHint = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            AddRow(secVideo, null, lblNvCrfHint);
            tips.SetToolTip(nuNvCrf, "Constant quality for H.264 / H.265 NVIDIA NVENC (FFmpeg -rc vbr -cq), like x264 / x265 CRF:\n0 = automatic (from the Quality setting)  ·  1 = best / biggest  ·  51 = worst / smallest.\nGood values: 18–23 for H.264, 20–26 for H.265. A bitrate above 0 overrides it.");
            nuNvCrf.ValueChanged += (s2, e2) => UpdateNvCrfRow();
            nuVBitrate.ValueChanged += (s2, e2) => UpdateNvCrfRow();
            cbVCodec.SelectedIndexChanged += (s2, e2) => UpdateNvCrfRow();
            AddRow(secVideo, "Bitrate (kbps, 0 = auto)", nuVBitrate);
            AddRow(secVideo, "Encoding speed", cbPreset);
            secResVideo = Section("VIDEO OUTPUT RESOLUTION");
            resVideo = MakeRes(secResVideo, new[] {
                "Original", "8K UHD — 7680×4320", "DCI 4K — 4096×2160", "4K UHD — 3840×2160", "1440p — 2560×1440", "DCI 2K — 2048×1080",
                "Full HD — 1920×1080", "HD — 1280×720", "PAL SD — 720×576", "NTSC SD — 720×480",
                "Height 2160 (keep ratio)", "Height 1080 (keep ratio)", "Height 720 (keep ratio)", "Height 480 (keep ratio)", "Height 360 (keep ratio)", "Height 240 (keep ratio)",
                "Vertical 1080×1920 (Reels / Shorts / TikTok)", "Vertical 720×1280", "Square 1080×1080", "Portrait 4:5 — 1080×1350", "Cinema 2.39:1 — 1920×804", "Custom…" },
                new[] { 0, 7680, 4096, 3840, 2560, 2048, 1920, 1280, 720, 720, 0, 0, 0, 0, 0, 0, 1080, 720, 1080, 1080, 1920, -1 },
                new[] { 0, 4320, 2160, 2160, 1440, 1080, 1080, 720, 576, 480, 2160, 1080, 720, 480, 360, 240, 1920, 1280, 1080, 1350, 804, -1 });
            AddRow(secVideo, "Frame rate", cbFps);
            AddRow(secVideo, "Rotation", cbRotate);
            // Deinterlacing (QTGMC) — interlaced sources only (DV, DVD, broadcast 1080i…)
            cbDeint = Combo("Off", "QTGMC — double rate (50p / 60p)", "QTGMC — same rate (25p / 30p)");
            AddRow(secVideo, "Deinterlacing", cbDeint);
            cbDeintOrder = Combo("Auto (detected)", "Top field first (TFF)", "Bottom field first (BFF)");
            lblDeintOrder = AddRow(secVideo, "   Field order", cbDeintOrder);
            lblDeintInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            AddRow(secVideo, null, lblDeintInfo);
            btnQtgmcInstall = Btn("Install QTGMC (VapourSynth)…", 0);
            btnQtgmcInstall.Dock = DockStyle.Fill; btnQtgmcInstall.Margin = new Padding(0, Theme.S(2), 0, Theme.S(6));
            AddRow(secVideo, null, btnQtgmcInstall);
            btnQtgmcInstall.Click += (s2, e2) => InstallQtgmc();
            // SDR → HDR (2.1.6)
            cbHdr = Combo("Off", "HDR10 (PQ, BT.2020, 10-bit)", "Dolby Vision 8.1");
            AddRow(secVideo, "SDR → HDR", cbHdr);
            cbHdrPeak = Combo(Hdr.Peaks.Select(p => (object)(p + " nits")).ToArray()); cbHdrPeak.SelectedIndex = 1;
            lblHdrPeak = AddRow(secVideo, "   Peak brightness", cbHdrPeak);
            lblHdrInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            AddRow(secVideo, null, lblHdrInfo);
            cbHdr.SelectedIndexChanged += (s2, e2) => UpdateHdrRows();
            cbHdrPeak.SelectedIndexChanged += (s2, e2) => UpdateHdrRows();
            tips.SetToolTip(cbHdr, "SDR → HDR (inverse tone mapping by FFmpeg): the SDR picture is placed in BT.2020 PQ with SDR white at 203 nits\n" +
                "(ITU-R BT.2408) and the highlights are expanded up to the chosen peak brightness.\n" +
                "HDR10: H.265 10-bit (VP9 / AV1 for WebM) with HDR10 static metadata.\n" +
                "Dolby Vision 8.1: the same HDR10 picture plus Dolby Vision dynamic metadata (RPU) measured on every frame\n" +
                "with dovi_tool — plays as Dolby Vision on Dolby Vision screens and as HDR10 everywhere else. MP4, MOV or MKV.");
            cbDeint.SelectedIndexChanged += (s2, e2) => UpdateDeintRows();
            cbDeintOrder.SelectedIndexChanged += (s2, e2) => UpdateDeintRows();
            tips.SetToolTip(cbDeint, "QTGMC (QTempGaussMC, VapourSynth / vsjetpack): motion-compensated deinterlacing for interlaced videos\n" +
                "(DV, DVD, TV recordings, 1080i camcorders). Double rate keeps every field: 25i → 50p, 29.97i → 59.94p (smoothest motion).\n" +
                "Same rate gives one frame per frame: 25i → 25p, 29.97i → 29.97p.\n" +
                "Only interlaced sources are processed; progressive videos are left as they are. Runs first, before the Denoiser IA and the upscalers.");
            tips.SetToolTip(cbDeintOrder, "Auto uses the field order written in the file. If motion judders back and forth after deinterlacing,\n" +
                "force the other field order. Forcing TFF or BFF also deinterlaces files that are not flagged as interlaced.");
            AddRow(secVideo, "Image → video length (s)", nuStill);
            AddRow(secVideo, null, chkSubs);
            // cover / thumbnail (MP4, M4V, MOV: attached picture; MKV: cover.jpg attachment)
            cbCover = Combo("None", "Frame of the video at (s)", "Image file…");
            AddRow(secVideo, "Cover / thumbnail", cbCover);
            nuCoverAt = Num(0, 86400, 3, 1);
            var coverAtLbl = AddRow(secVideo, "   Frame time (s)", nuCoverAt);
            lblCoverFile = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            AddRow(secVideo, null, lblCoverFile);
            Action coverRows = () =>
            {
                int m = cbCover.SelectedIndex;
                nuCoverAt.Visible = coverAtLbl.Visible = m == 1;
                lblCoverFile.Visible = m != 0;
                lblCoverFile.Text = m == 2 ? (coverPath != null ? "Cover: " + Path.GetFileName(coverPath) : "Choose an image (JPG / PNG)…") + "\n" : "";
                if (m != 0) lblCoverFile.Text += "Embedded as cover art in MP4 / M4V (shown by Windows Explorer and most players) or as a cover.jpg attachment in MKV. Other formats (MOV, WebM, AVI…): not supported.";
            };
            cbCover.SelectedIndexChanged += (s2, e2) =>
            {
                if (cbCover.SelectedIndex == 2 && !loadingSettings && (coverPath == null || !File.Exists(coverPath)))
                    using (var d = new OpenFileDialog { Title = "Choose the cover image", Filter = "Pictures (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp" })
                    {
                        if (d.ShowDialog(this) == DialogResult.OK) coverPath = d.FileName; else { cbCover.SelectedIndex = 0; return; }
                    }
                coverRows();
            };
            lblCoverFile.Click += (s2, e2) =>
            {
                if (cbCover.SelectedIndex != 2) return;
                using (var d = new OpenFileDialog { Title = "Choose the cover image", Filter = "Pictures (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp" })
                    if (d.ShowDialog(this) == DialogResult.OK) { coverPath = d.FileName; coverRows(); }
            };
            tips.SetToolTip(cbCover, "Thumbnail of the converted video: a frame of the result (at the time you choose) or your own picture.\n" +
                "MP4 / M4V: cover art (attached picture). MKV: cover.jpg attachment (some players and Explorer add-ons show it).\n" +
                "The video itself is not changed: the cover is added after the conversion, without re-encoding.");
            coverRows();
            cbSeqExport = Combo("Video file (the chosen format)", "Image sequence — PNG (lossless)", "Image sequence — JPEG (high quality)");
            // "PNG/JPG SEQUENCE": two exclusive ticks (none ticked = normal video file); cbSeqExport keeps the value
            cbSeqExport.Visible = false;
            chkSeqPng = Check("PNG"); chkSeqJpg = Check("JPG");
            chkSeqPng.Margin = new Padding(0, Theme.S(5), Theme.S(28), Theme.S(3)); chkSeqJpg.Margin = new Padding(0, Theme.S(5), 0, Theme.S(3));
            var seqBox = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0) };
            seqBox.Controls.Add(chkSeqPng); seqBox.Controls.Add(chkSeqJpg);
            AddRow(secVideo, "PNG/JPG SEQUENCE", seqBox);
            bool seqSync = false;
            chkSeqPng.CheckedChanged += (s2, e2) => { if (seqSync) return; seqSync = true; if (chkSeqPng.Checked) chkSeqJpg.Checked = false; cbSeqExport.SelectedIndex = chkSeqPng.Checked ? 1 : chkSeqJpg.Checked ? 2 : 0; seqSync = false; };
            chkSeqJpg.CheckedChanged += (s2, e2) => { if (seqSync) return; seqSync = true; if (chkSeqJpg.Checked) chkSeqPng.Checked = false; cbSeqExport.SelectedIndex = chkSeqJpg.Checked ? 2 : chkSeqPng.Checked ? 1 : 0; seqSync = false; };
            cbSeqExport.SelectedIndexChanged += (s2, e2) => { if (seqSync) return; seqSync = true; chkSeqPng.Checked = cbSeqExport.SelectedIndex == 1; chkSeqJpg.Checked = cbSeqExport.SelectedIndex == 2; seqSync = false; };
            lblSeqInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            AddRow(secVideo, null, lblSeqInfo);
            tips.SetToolTip(chkSeqPng, "Tick PNG or JPG to export the video as numbered pictures instead of a video file.\nNothing ticked = normal video file.");
            tips.SetToolTip(chkSeqJpg, "Tick PNG or JPG to export the video as numbered pictures instead of a video file.\nNothing ticked = normal video file.");
            tips.SetToolTip(cbSeqExport, "Image sequence: every frame of the final render is saved as a numbered picture (name_000001.png…)\n" +
                "in a folder next to the video. Everything else still applies: resolution, frame rate, trim, filters, Denoiser IA,\n" +
                "Python / ONNX AI, Scale, OptiScaler, NVIDIA RTX, SeedVR2 and AI Upscale (VisionNext / RazorNext).");
            cbSeqExport.SelectedIndexChanged += (s2, e2) =>
            {
                int k = cbSeqExport.SelectedIndex;
                lblSeqInfo.Text = k == 0 ? "" : "Every frame of the final render → " + (k == 1 ? "PNG pictures (lossless, 16-bit when the render is 10-bit)" : "JPG pictures (quality 2, full 4:4:4 colour)") +
                    "\nin a folder name_" + (k == 1 ? "PNG" : "JPG") + " next to the video. All the video options and AI effects apply; no audio.";
                lblSeqInfo.Visible = k != 0;
                UpdateNavSummary();
            };
            lblSeqInfo.Visible = false;
            chkPreview = Check("Live before / after preview while exporting");
            chkPreview.Checked = true;
            AddRow(secVideo, null, chkPreview);
            tips.SetToolTip(chkPreview, "Opens a preview window during video exports: the original frame and the final rendered frame,\nwith a slider to compare them (or side by side). Updated about once per second of video.");
            stack.Controls.Add(secVideo);

            // Scale (Échelle)
            secScale = Section("SCALE  (ÉCHELLE)");
            chkScale = Check("Scale — upscale with a game-style upscaler");
            AddRow(secScale, null, chkScale);
            cbScaleType = Combo(Scaling.Types.Cast<object>().ToArray());
            cbScaleType.DropDownWidth = Theme.S(380);
            cbScaleFactor = Combo("×1.5", "×2", "×2.5", "×3", "×4", "Use the Resolution setting");
            cbScaleFactor.SelectedIndex = 1;
            tbScaleSharp = new TrackBar { Minimum = 0, Maximum = 100, Value = 100, TickStyle = TickStyle.None, BackColor = Theme.Panel, Dock = DockStyle.Fill, AutoSize = false, Height = Theme.S(28) };
            lblScaleSharpVal = new Label { Text = "100 %", AutoSize = false, Width = Theme.S(48), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right };
            tbScaleSharp.ValueChanged += (s2, e2) => lblScaleSharpVal.Text = tbScaleSharp.Value + " %";
            var shHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            shHost.Controls.Add(tbScaleSharp); shHost.Controls.Add(lblScaleSharpVal);
            chkScaleOpt = Check("Optimized (GPU, high precision)");
            chkScaleOpt.Checked = true;
            chkScaleFid = Check("Screen quality encode (10-bit, finer CRF)");
            chkScaleFid.Checked = true;
            chkScale444 = Check("Full chroma 4:4:4 (same colour detail as the screen)");
            chkScale444.Checked = true;
            lblScaleInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
            scaleRows.Add(AddRow(secScale, "   Type", cbScaleType)); scaleRows.Add(cbScaleType);
            scaleRows.Add(AddRow(secScale, "   Factor", cbScaleFactor)); scaleRows.Add(cbScaleFactor);
            scaleRows.Add(AddRow(secScale, "   Sharpness", shHost)); scaleRows.Add(shHost);
            AddRow(secScale, null, chkScaleOpt); scaleRows.Add(chkScaleOpt);
            AddRow(secScale, null, chkScaleFid); scaleRows.Add(chkScaleFid);
            AddRow(secScale, null, chkScale444); scaleRows.Add(chkScale444);
            tips.SetToolTip(chkScaleFid, "Keeps what the upscaler adds: H.265 / AV1 / VP9 in 10-bit, CRF 4 steps finer,\nedge-preserving encoder tuning, and the final pixel conversion done by libplacebo.\nThis is what brings the file closest to a live Lossless Scaling picture.");
            tips.SetToolTip(chkScale444, "4:4:4 keeps full colour resolution (like the screen) for H.265 and VP9. Larger files;\nsome TVs and phones cannot play 4:4:4 — VLC, mpv and PCs can.");
            AddRow(secScale, null, lblScaleInfo); scaleRows.Add(lblScaleInfo);
            tips.SetToolTip(chkScaleOpt, "Optimized: GPU processing through FFmpeg's libplacebo (Vulkan) with blue-noise dithering —\nfaster and more precise. FSR, NIS, SGSR, Anime4K and EWA always run on the GPU.");
            tips.SetToolTip(cbScaleType, "FSR 1 (AMD), NIS (NVIDIA) and SGSR (Qualcomm) are open-source game upscalers ported to GLSL;\nAnime4K is made for anime. xBR / hqx / Integer / Nearest suit pixel art.");
            chkScale.CheckedChanged += (s2, e2) => UpdateScaleRows();
            cbScaleType.SelectedIndexChanged += (s2, e2) => UpdateScaleRows();
            chkScaleOpt.CheckedChanged += (s2, e2) => UpdateScaleRows();
            UpdateScaleRows();
            stack.Controls.Add(secScale);
            BuildSeedSection(stack);
            BuildVisionSection(stack);

            // Denoiser (Neat Video-style)
            secDn = Section("DENOISER IA  (AI NOISE + GRAIN REMOVAL)");
            chkDn = Check("Denoiser IA — AI noise and grain removal");
            AddRow(secDn, null, chkDn);
            cbDnModel = Combo(AiDenoise.Models.Select(m => (object)m.Label).ToArray());
            cbDnModel.DropDownWidth = Theme.S(300);
            lblDnModel = AddRow(secDn, "   AI model", cbDnModel);
            var bSup = Btn("Install model support…", 0); bSup.Dock = DockStyle.Fill; bSup.Height = Theme.S(30); bSup.Font = fSmall;
            bSup.Click += (s2, e2) => InstallDnModelSupport();
            var bMod = Btn("Open models folder", 0); bMod.Dock = DockStyle.Fill; bMod.Height = Theme.S(30); bMod.Font = fSmall;
            bMod.Click += (s2, e2) =>
            {
                string d = AiDenoise.ModelDirs[1];
                foreach (var x in AiDenoise.ModelDirs) if (Directory.Exists(x) && Directory.GetFiles(x, "*.pth").Length > 0) { d = x; break; }
                try { Directory.CreateDirectory(d); Process.Start(new ProcessStartInfo(d) { UseShellExecute = true }); } catch (Exception ex) { Log(ex.Message, Theme.Error); }
            };
            var dnBar = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            dnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); dnBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            dnBar.Controls.Add(bSup, 0, 0); dnBar.Controls.Add(bMod, 1, 0);
            AddRow(secDn, null, dnBar); dnModelBar = dnBar; dnSupportBtn = bSup;
            tips.SetToolTip(cbDnModel, "SCUNet: FFmpeg Forge's built-in AI (real camera noise, film grain) with temporal stabilisation.\n" +
                "Nomos8k HAT-L: the finest detail and strongest restoration, very heavy (slowest).\n" +
                "NomosUni DAT: strong denoising and compression clean-up, natural look (slow).\n" +
                "PurePhoto SPAN: light clean-up for good sources, very fast.\n" +
                "HAT-L, DAT and SPAN are ×4 restoration models: FFmpeg Forge runs them and brings the result back to\n" +
                "the source size (supersampling), so the picture is cleaned without changing the resolution.");
            cbDnModel.SelectedIndexChanged += (s2, e2) =>
            {
                UpdateDnRows();
                if (!chkDn.Checked || loadingSettings) return;
                if (!AiDenoise.Ready) { OfferAiInstall(); return; }
                if (DnModelKey() != "scunet" && !AiDenoise.HasSpandrel) InstallDnModelSupport();
            };
            lblDnInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            AddRow(secDn, null, lblDnInfo);
            tips.SetToolTip(chkDn, "AI denoiser: SCUNet neural network (real camera noise, film grain, compression noise) on your GPU,\nwith Neat Video-style temporal stabilisation so the clean picture does not flicker.\nRuns first, before Scale, RTX, SeedVR2 or VisionNext. Videos and images.");
            chkDn.CheckedChanged += (s2, e2) =>
            {
                UpdateDnRows();
                if (chkDn.Checked && !loadingSettings && !AiDenoise.Ready) OfferAiInstall();
                else if (chkDn.Checked && !loadingSettings && DnModelKey() != "scunet" && !AiDenoise.HasSpandrel) InstallDnModelSupport();
            };
            UpdateDnRows();
            stack.Controls.Add(secDn);
            BuildCustomAiSection(stack);

            // OptiScaler (open source, GPL-3.0): output scaling + RCAS
            secOpti = Section("OPTISCALER  (OUTPUT SCALING + RCAS)");
            chkOpti = Check("OptiScaler — supersampled upscaling for a cleaner render");
            AddRow(secOpti, null, chkOpti);
            cbOptiMult = Combo("×1.25", "×1.5  (OptiScaler default)", "×2", "×2.5", "×3");
            cbOptiMult.SelectedIndex = 1;
            cbOptiKernel = Combo(Scaling.OptiKernels.Cast<object>().ToArray());
            cbOptiKernel.SelectedIndex = 2;
            tbOptiSharp = new TrackBar { Minimum = 0, Maximum = 100, Value = 40, TickStyle = TickStyle.None, BackColor = Theme.Panel, Dock = DockStyle.Fill, AutoSize = false, Height = Theme.S(28) };
            lblOptiSharpVal = new Label { Text = "40 %", AutoSize = false, Width = Theme.S(48), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right };
            tbOptiSharp.ValueChanged += (s2, e2) => lblOptiSharpVal.Text = tbOptiSharp.Value + " %";
            var osHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            osHost.Controls.Add(tbOptiSharp); osHost.Controls.Add(lblOptiSharpVal);
            chkOptiContrast = Check("Contrast adaptive RCAS");
            lblOptiInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
            optiRows.Add(AddRow(secOpti, "   Output scaling", cbOptiMult)); optiRows.Add(cbOptiMult);
            optiRows.Add(AddRow(secOpti, "   Downscaler", cbOptiKernel)); optiRows.Add(cbOptiKernel);
            optiRows.Add(AddRow(secOpti, "   RCAS sharpness", osHost)); optiRows.Add(osHost);
            AddRow(secOpti, null, chkOptiContrast); optiRows.Add(chkOptiContrast);
            AddRow(secOpti, null, lblOptiInfo); optiRows.Add(lblOptiInfo);
            tips.SetToolTip(chkOpti, "OptiScaler's Output Scaling: the upscaler (Scale type, or EWA Lanczos Sharp when Scale is off)\nrenders the picture larger than the output, RCAS sharpens it, then a high-quality\ndownscaler brings it to the output size — finer edges, less aliasing and ringing.\nGPU (libplacebo / Vulkan). Works for videos and images.");
            tips.SetToolTip(cbOptiMult, "Supersampling multiplier: the upscaler renders at output size × this value (OptiScaler: 0.5–3.0, default 1.5).");
            tips.SetToolTip(cbOptiKernel, "Downscaler used by OptiScaler's Output Scaling.\nLanczos 2 / Bicubic: sharpest.  Kaiser: less ringing.  Magic Kernel: smoothest, no aliasing.");
            tips.SetToolTip(chkOptiContrast, "OptiScaler's contrast adaptation: sharpens less on already high-contrast edges.");
            chkOpti.CheckedChanged += (s2, e2) => UpdateOptiRows();
            UpdateOptiRows();
            stack.Controls.Add(secOpti);

            // NVIDIA RTX AI
            secRtx = Section("NVIDIA RTX AI  (RTX GPU)");
            lblRtxStatus = new Label { Text = "Detecting RTX engines…", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(6)) };
            AddRow(secRtx, null, lblRtxStatus);
            cbRtxEngine = Combo("Auto (best available)", "FFmpeg RTX filters only", "NVIDIA driver (built-in)", "RTXVideoProcessor.exe only");
            cbRtxEngine.SelectedIndexChanged += (s2, e2) => UpdateRtxStatus();
            AddRow(secRtx, "Engine", cbRtxEngine);
            var bProc = Btn("Locate RTXVideoProcessor.exe…", 0); bProc.Dock = DockStyle.Fill; bProc.Height = Theme.S(30); bProc.Font = fSmall;
            bProc.Click += (s2, e2) => LocateRtxProcessor();
            AddRow(secRtx, null, bProc);

            chkVsr = Check("RTX Video Super Resolution (AI upscale + cleanup)");
            AddRow(secRtx, null, chkVsr);
            cbVsrQ = Combo("1 — Low", "2 — Medium", "3 — High", "4 — Ultra");
            cbVsrQ.SelectedIndex = 3;
            cbVsrScale = Combo("×2", "×3", "×4");
            btnVsrParams = Btn("All VSR filter parameters…", 0); btnVsrParams.Dock = DockStyle.Fill; btnVsrParams.Height = Theme.S(30); btnVsrParams.Font = fSmall;
            btnVsrParams.Click += (s2, e2) => EditRtxParams(Rtx.VsrFilters, ref vsrExpr, btnVsrParams, "VSR");
            vsrRows.Add(AddRow(secRtx, "   VSR quality", cbVsrQ)); vsrRows.Add(cbVsrQ);
            vsrRows.Add(AddRow(secRtx, "   Upscale factor", cbVsrScale)); vsrRows.Add(cbVsrScale);
            AddRow(secRtx, null, btnVsrParams); vsrRows.Add(btnVsrParams);

            chkHdr = Check("RTX TrueHDR (SDR → HDR10)");
            AddRow(secRtx, null, chkHdr);
            nuHdrContrast = Num(0, 200, 125, 0); nuHdrSat = Num(0, 200, 100, 0); nuHdrGray = Num(10, 100, 25, 0); nuHdrLum = Num(400, 10000, 1000, 0);
            nuHdrLum.Increment = 50;
            btnHdrParams = Btn("All TrueHDR filter parameters…", 0); btnHdrParams.Dock = DockStyle.Fill; btnHdrParams.Height = Theme.S(30); btnHdrParams.Font = fSmall;
            btnHdrParams.Click += (s2, e2) => EditRtxParams(Rtx.HdrFilters, ref hdrExpr, btnHdrParams, "TrueHDR");
            hdrRows.Add(AddRow(secRtx, "   Contrast", nuHdrContrast)); hdrRows.Add(nuHdrContrast);
            hdrRows.Add(AddRow(secRtx, "   Saturation", nuHdrSat)); hdrRows.Add(nuHdrSat);
            hdrRows.Add(AddRow(secRtx, "   Middle gray", nuHdrGray)); hdrRows.Add(nuHdrGray);
            hdrRows.Add(AddRow(secRtx, "   Peak brightness (nits)", nuHdrLum)); hdrRows.Add(nuHdrLum);
            AddRow(secRtx, null, btnHdrParams); hdrRows.Add(btnHdrParams);

            chkDlss = Check("DLSS Super Resolution (NVIDIA NGX AI)");
            AddRow(secRtx, null, chkDlss);
            cbDlssMode = Combo("DLAA — native size, AI clean-up (×1)", "Quality (×1.5)", "Balanced (×1.72)", "Performance (×2)", "Ultra Performance (×3)");
            cbDlssMode.SelectedIndex = 3;
            btnDlssParams = Btn("All DLSS / NGX filter parameters…", 0); btnDlssParams.Dock = DockStyle.Fill; btnDlssParams.Height = Theme.S(30); btnDlssParams.Font = fSmall;
            btnDlssParams.Click += (s2, e2) => EditRtxParams(Rtx.DlssFilters, ref dlssExpr, btnDlssParams, "DLSS");
            dlssRows.Add(AddRow(secRtx, "   DLSS mode", cbDlssMode)); dlssRows.Add(cbDlssMode);
            AddRow(secRtx, null, btnDlssParams); dlssRows.Add(btnDlssParams);

            var rtxHint = new Label { Text = "Needs an NVIDIA RTX GPU and a recent driver. TrueHDR needs a 10-bit codec (H.265, AV1, VP9, ProRes…). RTXVideoProcessor: VSR ×2 + TrueHDR, NVENC output, MP4/MKV/MOV.", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(6), 0, 0) };
            AddRow(secRtx, null, rtxHint);
            EventHandler rtxToggle = (s2, e2) => UpdateRtxRows();
            chkVsr.CheckedChanged += rtxToggle; chkHdr.CheckedChanged += rtxToggle; chkDlss.CheckedChanged += rtxToggle;
            UpdateRtxRows();
            stack.Controls.Add(secRtx);

            // Audio
            secAudio = Section("AUDIO SETTINGS");
            cbACodec = Combo(Formats.AudioCodecsForVideo.Cast<object>().ToArray());
            SkipSeparator(cbACodec);
            cbAKbps = Combo("Auto", "64", "96", "128", "160", "192", "256", "320", "448", "640");
            cbRate = Combo("Original", "8000 Hz", "16000 Hz", "22050 Hz", "32000 Hz", "44100 Hz", "48000 Hz", "88200 Hz", "96000 Hz", "192000 Hz");
            cbCh = Combo(ChannelLayouts.All.Select(l => (object)new Choice(l.Key, l.Label)).ToArray());
            cbCh.DropDownWidth = Theme.S(380);
            tips.SetToolTip(cbCh, "Immersive layouts (5.1.2 … 9.1.6) are written as discrete channel beds: FFmpeg cannot encode Dolby Atmos objects or DTS:X metadata.\nUse PCM 24-bit (WAV/W64/CAF/MOV/MKV) or Opus for more than 8 channels.\n\"Passthrough\" copies an existing Atmos / DTS:X track bit-for-bit.\n\"Spatial Audio — headphones\" renders the immersive 7.1.4 mix in 3D for any headphones (stereo file), like Apple Music's Spatial Audio.");
            nuVolume = Num(0, 1000, 100, 0); nuVolume.Increment = 10;
            chkNorm = Check("Normalize loudness (EBU R128)");
            lblACodec = AddRow(secAudio, "Audio codec", cbACodec);
            AddRow(secAudio, "Bitrate (kbps)", cbAKbps);
            AddRow(secAudio, "Sample rate", cbRate);
            AddRow(secAudio, "Channels", cbCh);
            cbUpmix = Combo("Auto — Spatial Music (audio) / Cinema (video)",
                            "Spatial Music — Dolby Atmos music mix (Apple Music style)",
                            "Music (classic) — vocals forward, instruments spread, air above",
                            "Cinema — dialogue locked to center, ambience overhead",
                            "Live concert — big room, audience all around",
                            "Off — keep the extra channels silent");
            cbUpmix.DropDownWidth = Theme.S(380);
            tips.SetToolTip(cbUpmix, "Immersive upmix (when the source has fewer channels than the chosen layout):\n" +
                "• lead vocal / dialogue (centered sound) is steered to the center speaker, closer to you\n" +
                "• instruments keep their stereo position across front, wides and sides\n" +
                "• bass and kick drum feed the LFE (subwoofer)\n" +
                "• stereo width, reverb, room and cymbals feed the wides and the height speakers (the voice never goes up)\n" +
                "Spatial Music: dry lead vocal in the center, instruments opened up to the sides and wides,\n" +
                "  cymbals and air above, and a separate (decorrelated) reverb in every surround / height speaker — the\n" +
                "  enveloping sound of the Dolby Atmos music mixes on Apple Music.\n" +
                "Channel-based mix: not Dolby Atmos objects.\n" +
                "On headphones, choose Channels → \"Spatial Audio — headphones\" to hear it in 3D.");
            lblUpmix = AddRow(secAudio, "Immersive upmix", cbUpmix);
            cbCh.SelectedIndexChanged += (s2, e2) => UpdateUpmixRow();
            AddRow(secAudio, "Volume (%)", nuVolume);
            AddRow(secAudio, null, chkNorm);
            stack.Controls.Add(secAudio);

            // Audio tracks (2.1.6): change only the audio, add tracks, video untouched
            secTracks = Section("AUDIO TRACKS  (VIDEO UNTOUCHED)");
            chkKeepVideo = Check("Keep the video as is — no video re-encoding");
            AddRow(secTracks, null, chkKeepVideo);
            tips.SetToolTip(chkKeepVideo, "The picture is copied bit-for-bit (stream copy): same quality, very fast.\n" +
                "Only the audio changes: for example Opus → AAC (choose the codec in Audio settings), or audio tracks added.\n" +
                "Resolution, frame rate, rotation, video filters, upscalers and AI cannot be used in this mode.");
            cbExistingAudio = Combo("Convert (Audio settings codec)", "Keep as is (copy)", "Remove (keep only added tracks)");
            AddRow(secTracks, "Original audio tracks", cbExistingAudio);
            tips.SetToolTip(cbExistingAudio, "What happens to the audio tracks already in the video. Every track is processed (not only the first one).\n" +
                "Convert: uses the codec, bitrate, channels and volume of Audio settings (a track already in that codec is copied).\n" +
                "A track whose codec does not fit the output container is converted automatically.");
            lbTracks = new ListBox { Dock = DockStyle.Fill, Height = Theme.S(92), BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = fSmall, IntegralHeight = false, Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)), HorizontalScrollbar = true };
            var trackLbl = AddRow(secTracks, "Added audio tracks", new Label { AutoSize = true, ForeColor = Theme.Muted, Font = fSmall, Text = "added to every video in the list", Margin = new Padding(0, Theme.S(9), 0, 0) });
            AddRow(secTracks, null, lbTracks);
            var tBtns = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, Theme.S(4)) };
            var bAddTrack = Btn("Add audio track…", 168); bAddTrack.Icon = "plus"; bAddTrack.Height = Theme.S(32); bAddTrack.Font = fSmall;
            var bRemTrack = Btn("Remove", 104); bRemTrack.Icon = "trash"; bRemTrack.Height = Theme.S(32); bRemTrack.Font = fSmall;
            var bClrTrack = Btn("Clear", 90); bClrTrack.Icon = "broom"; bClrTrack.Height = Theme.S(32); bClrTrack.Font = fSmall;
            foreach (var b in new[] { bAddTrack, bRemTrack, bClrTrack }) { b.Margin = new Padding(0, 0, Theme.S(8), 0); tBtns.Controls.Add(b); }
            AddRow(secTracks, null, tBtns);
            cbTrackLang = Combo(AudioTracks.Languages.Select(l => (object)(l[0].Length > 0 ? l[1] + "  (" + l[0] + ")" : l[1])).ToArray());
            txtTrackTitle = Txt(""); SetCue(txtTrackTitle, "e.g. Commentary, Français 5.1…");
            nuTrackDelay = Num(-600000, 600000, 0, 0); nuTrackDelay.Increment = 10;
            chkTrackDefault = Check("Default track (played first)");
            var lLang = AddRow(secTracks, "   Language", cbTrackLang);
            var lTitle = AddRow(secTracks, "   Title", txtTrackTitle);
            var lDelay = AddRow(secTracks, "   Delay (ms)", nuTrackDelay);
            AddRow(secTracks, null, chkTrackDefault);
            lblTracksInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(400), 0), Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)) };
            AddRow(secTracks, null, lblTracksInfo);
            tips.SetToolTip(nuTrackDelay, "Shifts the added track: positive = it starts later, negative = earlier (to resynchronise a track from another source).");
            Action trackEditor = () =>
            {
                var t = lbTracks.SelectedItem as ExtraTrack;
                bool on = t != null;
                foreach (Control c in new Control[] { cbTrackLang, txtTrackTitle, nuTrackDelay, chkTrackDefault, lLang, lTitle, lDelay }) c.Visible = on;
                bRemTrack.Enabled = on; bClrTrack.Enabled = extraTracks.Count > 0;
                if (on)
                {
                    trackSync = true;
                    int li = Array.FindIndex(AudioTracks.Languages, l => l[0] == t.Lang); cbTrackLang.SelectedIndex = Math.Max(0, li);
                    txtTrackTitle.Text = t.Title; nuTrackDelay.Value = Math.Max(nuTrackDelay.Minimum, Math.Min(nuTrackDelay.Maximum, t.DelayMs)); chkTrackDefault.Checked = t.Default;
                    trackSync = false;
                }
                UpdateTracksInfo();
            };
            Action refreshTracks = () =>
            {
                var sel = lbTracks.SelectedItem;
                lbTracks.BeginUpdate(); lbTracks.Items.Clear();
                foreach (var t in extraTracks) lbTracks.Items.Add(t);
                lbTracks.EndUpdate();
                if (sel != null && extraTracks.Contains((ExtraTrack)sel)) lbTracks.SelectedItem = sel;
                else if (extraTracks.Count > 0) lbTracks.SelectedIndex = extraTracks.Count - 1;
                trackEditor();
            };
            lbTracks.SelectedIndexChanged += (s2, e2) => trackEditor();
            EventHandler edit = (s2, e2) =>
            {
                if (trackSync) return;
                var t = lbTracks.SelectedItem as ExtraTrack; if (t == null) return;
                int li = Math.Max(0, cbTrackLang.SelectedIndex);
                t.Lang = AudioTracks.Languages[li][0]; t.Title = txtTrackTitle.Text.Trim(); t.DelayMs = (int)nuTrackDelay.Value;
                if (chkTrackDefault.Checked && !t.Default) foreach (var o in extraTracks) o.Default = false;
                t.Default = chkTrackDefault.Checked;
                int i = lbTracks.SelectedIndex; trackSync = true; lbTracks.Items[i] = t; lbTracks.SelectedIndex = i; trackSync = false;
                lbTracks.Invalidate();
            };
            cbTrackLang.SelectedIndexChanged += edit; txtTrackTitle.TextChanged += edit; nuTrackDelay.ValueChanged += edit; chkTrackDefault.CheckedChanged += edit;
            bAddTrack.Click += (s2, e2) =>
            {
                using (var d = new OpenFileDialog { Title = "Add audio tracks", Multiselect = true,
                    Filter = "Audio files|" + string.Join(";", Formats.AudioExt.Select(x => "*." + x).ToArray()) + "|Videos (their first audio track)|" + string.Join(";", Formats.VideoExt.Select(x => "*." + x).ToArray()) + "|All files (*.*)|*.*" })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    foreach (var f in d.FileNames)
                    {
                        var t = new ExtraTrack { Path = f };
                        // guess the language from the file name (…_fr, .eng., [french]…)
                        string n = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                        foreach (var l in AudioTracks.Languages)
                            if (l[0].Length > 0 && (System.Text.RegularExpressions.Regex.IsMatch(n, "(^|[^a-z])(" + l[0] + "|" + l[1].ToLowerInvariant() + ")([^a-z]|$)")))
                            { t.Lang = l[0]; break; }
                        if (t.Lang.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(n, "(^|[^a-z])(fr|vf|vff|francais|français)([^a-z]|$)")) t.Lang = "fre";
                        if (t.Lang.Length == 0 && System.Text.RegularExpressions.Regex.IsMatch(n, "(^|[^a-z])(en|vo)([^a-z]|$)")) t.Lang = "eng";
                        extraTracks.Add(t);
                        Log("Audio track added: " + Path.GetFileName(f) + (t.Lang.Length > 0 ? "  (" + t.Lang + ")" : ""), Theme.Success);
                    }
                    if (!chkKeepVideo.Checked) { chkKeepVideo.Checked = true; Log("   \"Keep the video as is\" ticked: the audio tracks are added without re-encoding the video (untick it to re-encode the picture too).", Theme.Accent1); }
                    refreshTracks();
                }
            };
            bRemTrack.Click += (s2, e2) => { var t = lbTracks.SelectedItem as ExtraTrack; if (t != null) { extraTracks.Remove(t); refreshTracks(); } };
            bClrTrack.Click += (s2, e2) => { extraTracks.Clear(); refreshTracks(); };
            chkKeepVideo.CheckedChanged += (s2, e2) => UpdateTracksInfo();
            cbExistingAudio.SelectedIndexChanged += (s2, e2) => UpdateTracksInfo();
            refreshTracks();
            stack.Controls.Add(secTracks);

            // Image
            secImage = Section("IMAGE SETTINGS");
            tbImgQ = new TrackBar { Minimum = 1, Maximum = 100, Value = 90, TickStyle = TickStyle.None, BackColor = Theme.Panel, Dock = DockStyle.Fill, AutoSize = false, Height = Theme.S(28) };
            lblImgQ = new Label { Text = "90", AutoSize = false, Width = Theme.S(36), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right };
            tbImgQ.ValueChanged += (s, e) => lblImgQ.Text = tbImgQ.Value.ToString();
            var qHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            qHost.Controls.Add(tbImgQ); qHost.Controls.Add(lblImgQ);
            cbImgRotate = Combo("None", "90° clockwise", "180°", "90° counter-clockwise", "Mirror horizontal", "Mirror vertical");
            cbExtract = Combo("Single frame", "Every frame", "One frame every N s");
            cbExtract.SelectedIndexChanged += (s, e) => UpdateExtractLabel();
            nuExtractVal = Num(0, 360000, 1, 2);
            secResImage = Section("IMAGE OUTPUT RESOLUTION");
            resImage = MakeRes(secResImage, new[] {
                "Original", "Width 7680", "Width 3840", "Width 2560", "Width 1920", "Width 1280", "Width 1024", "Width 800", "Width 640", "Width 512",
                "Width 256", "Width 128", "Width 64", "Width 32", "Width 16", "1920×1080", "1080×1920", "1080×1080", "512×512 (icons)", "256×256 (icons)", "Custom…" },
                new[] { 0, 7680, 3840, 2560, 1920, 1280, 1024, 800, 640, 512, 256, 128, 64, 32, 16, 1920, 1080, 1080, 512, 256, -1 },
                new[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1080, 1920, 1080, 512, 256, -1 });
            AddRow(secImage, "Quality", qHost);
            AddRow(secImage, "Rotation", cbImgRotate);
            cbHdrImg = Combo("Off", "HDR10 (PQ, BT.2020)");
            AddRow(secImage, "SDR → HDR", cbHdrImg);
            cbHdrPeakImg = Combo(Hdr.Peaks.Select(p => (object)(p + " nits")).ToArray()); cbHdrPeakImg.SelectedIndex = 1;
            lblHdrPeakImg = AddRow(secImage, "   Peak brightness", cbHdrPeakImg);
            lblHdrImgInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(4)) };
            AddRow(secImage, null, lblHdrImgInfo);
            cbHdrImg.SelectedIndexChanged += (s2, e2) => UpdateHdrRows();
            tips.SetToolTip(cbHdrImg, "HDR picture: PNG 16-bit with the cICP HDR tag (PNG 3rd edition) or AVIF 10-bit, BT.2020 PQ.\n" +
                "Shown in HDR by Chrome, Edge, Windows Photos (HDR display on) and recent phones. Dolby Vision has no picture format.");
            AddRow(secImage, "From a video", cbExtract);
            lblExtractVal = AddRow(secImage, "Position (s)", nuExtractVal);
            stack.Controls.Add(secImage);

            // Filters
            secFilters = Section("FILTERS");
            filterTitle = secFilters.Controls[0];
            filterList = new CheckedListBox { Dock = DockStyle.Fill, Height = Theme.S(118), BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.None, Font = fMono, CheckOnClick = false, IntegralHeight = false, HorizontalScrollbar = true, Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)) };
            filterList.ItemCheck += (s, e) => { if (e.Index >= 0 && e.Index < filters.Count) filters[e.Index].Enabled = e.NewValue == CheckState.Checked; };
            filterList.DoubleClick += (s, e) => EditFilter();
            filterList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete) RemoveFilter(); };
            AddRow(secFilters, null, filterList);
            var fb = new TableLayoutPanel { Dock = DockStyle.Fill, Height = Theme.S(36), ColumnCount = 6, Margin = new Padding(0, 0, 0, Theme.S(4)) };
            foreach (var w in new[] { 34f, 26f, 13f, 9f, 9f, 9f }) fb.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, w));
            var bAdd = Btn("+ Add filter…", 0); bAdd.Primary = true; bAdd.Click += (s, e) => AddFilter();
            btnPresets = Btn("Presets ▾", 0); btnPresets.Click += (s, e) => ShowPresets();
            var bEdit = Btn("Edit", 0); bEdit.Click += (s, e) => EditFilter();
            var bDel = Btn("✕", 0); bDel.Click += (s, e) => RemoveFilter();
            var bUp = Btn("▲", 0); bUp.Click += (s, e) => MoveFilter(-1);
            var bDn = Btn("▼", 0); bDn.Click += (s, e) => MoveFilter(1);
            tips.SetToolTip(bDel, "Remove the selected filter"); tips.SetToolTip(bUp, "Move up"); tips.SetToolTip(bDn, "Move down");
            int col = 0;
            foreach (var b in new[] { bAdd, btnPresets, bEdit, bDel, bUp, bDn }) { b.Dock = DockStyle.Fill; b.Height = Theme.S(32); b.Margin = new Padding(0, 0, Theme.S(4), 0); fb.Controls.Add(b, col++, 0); }
            AddRow(secFilters, null, fb);
            var hint = new Label { Text = "Filters run in list order. Uncheck to disable temporarily. Double-click to edit.", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, 0, 0, Theme.S(6)) };
            AddRow(secFilters, null, hint);
            txtExtraInputs = Txt("");
            SetCue(txtExtraInputs, "logo.png ; music.mp3 …  ([1:v], [2:a]…)");
            var bExtra = Btn("…", 40); bExtra.Dock = DockStyle.Right; bExtra.Height = Theme.S(28);
            bExtra.Click += (s, e) => BrowseExtraInputs();
            var exHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            txtExtraInputs.Dock = DockStyle.Fill; exHost.Controls.Add(txtExtraInputs); exHost.Controls.Add(bExtra);
            AddRow(secFilters, "Extra inputs", exHost);
            tips.SetToolTip(txtExtraInputs, "Additional files given to FFmpeg as inputs #1, #2… (separate with ;).\nNeeded by multi-input filters such as overlay (watermark), amix, hstack, concat…");
            var gl = new Label { Text = "Filtergraph  (-filter_complex, advanced)", ForeColor = Theme.Muted, Font = fUI, AutoSize = true, Margin = new Padding(0, Theme.S(6), 0, Theme.S(2)) };
            AddRow(secFilters, null, gl);
            txtGraph = new TextBox { Multiline = true, Height = Theme.S(64), Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BackColor = Theme.Card, ForeColor = Color.FromArgb(255, 214, 140), BorderStyle = BorderStyle.FixedSingle, Font = fMono, Margin = new Padding(0, 0, 0, Theme.S(2)) };
            tips.SetToolTip(txtGraph, "Full FFmpeg filtergraph. Inputs: [0:v] [0:a] (main file), [1:v] [1:a] (extra inputs)…\nName your final outputs [vout] and/or [aout] — they are mapped automatically.\nExample (watermark):  [0:v][1:v]overlay=W-w-20:H-h-20[vout]");
            AddRow(secFilters, null, txtGraph);
            var gh = new Label { Text = "Example: [0:v][1:v]overlay=W-w-20:H-h-20[vout]   — end outputs with [vout] / [aout].", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0) };
            AddRow(secFilters, null, gh);
            stack.Controls.Add(secFilters);

            // Trim
            secTrim = Section("TRIM (OPTIONAL)");
            txtStart = Txt(""); txtEnd = Txt("");
            SetCue(txtStart, "e.g. 00:01:30 or 90"); SetCue(txtEnd, "empty = until the end");
            AddRow(secTrim, "Start", txtStart);
            AddRow(secTrim, "End", txtEnd);
            stack.Controls.Add(secTrim);

            // Output
            secOut = Section("DESTINATION");
            chkSameDir = Check("Same folder as the source file");
            chkSameDir.CheckedChanged += (s, e) => { txtOutDir.Enabled = btnBrowseOut.Enabled = !chkSameDir.Checked; };
            txtOutDir = Txt("");
            btnBrowseOut = Btn("…", 40); btnBrowseOut.Dock = DockStyle.Right; btnBrowseOut.Height = Theme.S(28);
            btnBrowseOut.Click += (s, e) => BrowseOutDir();
            var outHost = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            txtOutDir.Dock = DockStyle.Fill; outHost.Controls.Add(txtOutDir); outHost.Controls.Add(btnBrowseOut);
            chkOverwrite = Check("Overwrite existing files");
            txtCustom = Txt(""); SetCue(txtCustom, "e.g. -metadata title=\"My movie\"");
            AddRow(secOut, null, chkSameDir);
            AddRow(secOut, "Folder", outHost);
            AddRow(secOut, null, chkOverwrite);
            chkSuggest = Check(AdvisorText.Suggest); chkSuggest.Checked = true;
            AddRow(secOut, null, chkSuggest);
            chkSuggest.CheckedChanged += (s2, e2) => UpdateAdvisor();
            tips.SetToolTip(chkSuggest, "Shows a small card above the file list when an imported video could be made much smaller.\nThe \"Optimized compression\" button always works, even when this is off.");
            AddRow(secOut, "Extra FFmpeg arguments", txtCustom);
            stack.Controls.Add(secOut);

            // ---------- options column (1.1.5 layout: every section stacked on the right) ----------
            var right = new SmoothSettingsPanel { Dock = DockStyle.Right, Width = Theme.S(490), BackColor = Theme.Bg, AutoScroll = true, Padding = new Padding(Theme.S(4), Theme.S(16), Theme.S(18), Theme.S(16)) };
            stack.Dock = DockStyle.Top;
            foreach (Control c in stack.Controls) { c.Dock = DockStyle.Fill; if (c is CardTable) c.Margin = new Padding(0, 0, 0, Theme.S(14)); }
            right.Controls.Add(stack);
            BuildScaleWindow(stack, right);

            // ---------- footer ----------
            var footer = new Panel { Dock = DockStyle.Bottom, Height = Theme.S(96), BackColor = Theme.Side, Padding = new Padding(Theme.S(28), Theme.S(18), Theme.S(24), Theme.S(18)) };
            footer.Paint += (s2, e2) => { using (var pen = new Pen(Color.FromArgb(30, 36, 48))) e2.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
            var fRight = new FlowLayoutPanel { Dock = DockStyle.Right, Width = Theme.S(700), FlowDirection = FlowDirection.RightToLeft, WrapContents = false, BackColor = Theme.Side };
            btnConvert = Btn("CONVERT", 270); btnConvert.Glow = true; btnConvert.Icon = "refresh"; btnConvert.Height = Theme.S(60);
            btnConvert.Font = new Font("Segoe UI", 12f, FontStyle.Bold);
            btnConvert.Click += (s, e) => StartConversion();
            btnCancel = Btn("Cancel", 150); btnCancel.Icon = "x"; btnCancel.Height = Theme.S(56); btnCancel.Enabled = false;
            btnCancel.Click += (s, e) => { cancelRequested = true; Log("⏹  Cancelling…", Theme.Error); };
            btnOpenOut = Btn("Open folder", 206); btnOpenOut.Icon = "folder"; btnOpenOut.Height = Theme.S(56); btnOpenOut.Font = new Font("Segoe UI Semibold", 10.5f);
            btnCancel.Font = new Font("Segoe UI Semibold", 10.5f);
            btnOpenOut.Click += (s, e) => OpenOutputFolder();
            foreach (var b in new[] { btnConvert, btnCancel, btnOpenOut }) { b.Margin = new Padding(Theme.S(18), b == btnConvert ? 0 : Theme.S(2), 0, 0); fRight.Controls.Add(b); }
            var fLeft = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Side, Padding = new Padding(0, Theme.S(2), Theme.S(24), 0) };
            statusLbl = new DotLabel { Dock = DockStyle.Top, Height = Theme.S(30), ForeColor = Theme.Text, BackColor = Theme.Side, Text = "Ready.", Font = new Font("Segoe UI", 10.5f) };
            var barGap = new Panel { Dock = DockStyle.Top, Height = Theme.S(10), BackColor = Theme.Side };
            overall = new GradientBar { Dock = DockStyle.Top, Height = Theme.S(8) };
            fLeft.Controls.Add(overall); fLeft.Controls.Add(barGap); fLeft.Controls.Add(statusLbl);
            footer.Controls.Add(fLeft); footer.Controls.Add(fRight);
            fLeft.BringToFront();

            // ---------- header ----------
            header = new TopHeader { Dock = DockStyle.Top };
            var bFF = Btn("FFmpeg location…", 0); bFF.Width = Theme.S(196); bFF.Height = Theme.S(46); bFF.Font = new Font("Segoe UI", 9.5f);
            bFF.Glass = true; bFF.Icon = "folder";
            Action placeFF = () => { bFF.Location = new Point(header.Width - bFF.Width - Theme.S(26), (header.Height - bFF.Height) / 2); header.PlacePill(bFF.Left); };
            header.Relayout = () => placeFF();
            header.Resize += (s, e) => placeFF();
            bFF.Click += (s, e) => ChooseFFmpeg();
            header.Controls.Add(bFF); placeFF();

            Controls.Add(center);
            Controls.Add(right);
            Controls.Add(footer);
            Controls.Add(header);
            center.BringToFront();
            ResumeLayout(true);
        }

        void ShowPage(int i) { }

        // =====================================================================
        //  Compression assistant (2.1): card above the file list + "Optimized compression" preset
        // =====================================================================
        void BuildAdvisorCard()
        {
            advCard = new CardTable { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Visible = false,
                                      Padding = new Padding(Theme.S(20), Theme.S(12), Theme.S(14), Theme.S(12)), Margin = new Padding(0, 0, 0, Theme.S(10)), BackColor = Color.FromArgb(24, 29, 40) };
            advCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            advCard.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            advCard.Paint += (s2, e2) =>
            {
                var g = e2.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = Theme.GoldBrush(new RectangleF(0, 0, Theme.S(5), advCard.Height))) g.FillRectangle(b, 0, 0, Theme.S(5), advCard.Height);
                using (var pen = new Pen(Color.FromArgb(90, Theme.Gold))) using (var pth = Theme.Round(new RectangleF(0.5f, 0.5f, advCard.Width - 1.5f, advCard.Height - 1.5f), Theme.S(12))) g.DrawPath(pen, pth);
            };
            var left = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0) };
            lblAdvTitle = new Label { AutoSize = true, ForeColor = Theme.Gold, Font = new Font("Segoe UI Semibold", 10.5f), Margin = new Padding(0, 0, 0, Theme.S(2)), BackColor = Color.Transparent };
            lblAdvMsg = new Label { AutoSize = true, ForeColor = Theme.Text, Font = fUI, Margin = new Padding(0, 0, 0, Theme.S(6)), BackColor = Color.Transparent };
            lblAdvFacts = new Label { AutoSize = true, ForeColor = Theme.Text, Font = fSmall, Margin = new Padding(0, 0, 0, Theme.S(4)), BackColor = Color.Transparent };
            lblAdvNote = new Label { AutoSize = true, ForeColor = Theme.Muted, Font = fSmall, Margin = new Padding(0), BackColor = Color.Transparent };
            foreach (var l in new[] { lblAdvTitle, lblAdvMsg, lblAdvFacts, lblAdvNote }) left.Controls.Add(l);
            left.SizeChanged += (s2, e2) => { int w = Math.Max(Theme.S(240), left.Width - Theme.S(4)); foreach (Label l in left.Controls) l.MaximumSize = new Size(w, 0); };
            var right = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(Theme.S(12), 0, 0, 0) };
            btnAdvApply = Btn(AdvisorText.Apply, 230); btnAdvApply.Primary = true; btnAdvApply.Height = Theme.S(34);
            btnAdvMeasure = Btn(AdvisorText.Measure, 230); btnAdvMeasure.Height = Theme.S(30); btnAdvMeasure.Font = fSmall;
            btnAdvIgnore = Btn(AdvisorText.Ignore, 230); btnAdvIgnore.Height = Theme.S(30); btnAdvIgnore.Font = fSmall;
            foreach (var b in new[] { btnAdvApply, btnAdvMeasure, btnAdvIgnore }) { b.Margin = new Padding(0, 0, 0, Theme.S(6)); right.Controls.Add(b); }
            tips.SetToolTip(btnAdvMeasure, "Encodes three short samples (at 20 %, 50 % and 80 % of the video) with the recommended settings\nto measure the real size and speed on your computer. Runs in the background.");
            btnAdvApply.Click += (s2, e2) => { if (advJob != null && advJob.Advice is Advice) ApplyAdvice(advJob, (Advice)advJob.Advice); };
            btnAdvIgnore.Click += (s2, e2) => { if (advJob != null) advJob.AdviceDismissed = true; advForced = null; UpdateAdvisor(); };
            btnAdvMeasure.Click += (s2, e2) => MeasureAdvice();
            advCard.Controls.Add(left, 0, 0);
            advCard.Controls.Add(right, 1, 0);
        }

        /// Shows the card for the selected video (or the first one with a recommendation); hides it otherwise.
        void UpdateAdvisor()
        {
            if (advCard == null || list == null) return;
            Job target = null;
            if (advForced != null && list.Items.Cast<ListViewItem>().Any(it => it.Tag == advForced)) target = advForced;
            else if (chkSuggest == null || chkSuggest.Checked)
            {
                var sel = list.SelectedItems.Count == 1 ? (Job)list.SelectedItems[0].Tag : null;
                Func<Job, bool> ok = j => j != null && !j.AdviceDismissed && j.Advice is Advice && ((Advice)j.Advice).Recommend;
                if (ok(sel)) target = sel;
                else target = list.Items.Cast<ListViewItem>().Select(it => (Job)it.Tag).FirstOrDefault(ok);
            }
            advJob = target;
            var a = target != null ? target.Advice as Advice : null;
            if (a == null) { advCard.Visible = false; return; }
            Compression.RefreshTime(target, a);
            lblAdvTitle.Text = (a.Recommend ? "✦  " + AdvisorText.Title : "✓  " + (a.Headline ?? AdvisorText.TitleEfficient)) + "  —  " + Path.GetFileName(target.Path);
            lblAdvMsg.Text = a.Recommend ? AdvisorText.Message : a.Reason;
            var facts = new List<string>();
            facts.Add("Current size: " + Compression.Size(a.SrcBytes) + "   →   estimated: " + (a.EstHigh > 0 ? Compression.SizeText(a) + "   (" + (a.Measured ? AdvisorText.MeasuredNote : AdvisorText.Indicative) + ")" : "—"));
            if (a.VKey != null)
            {
                facts.Add("Video: " + a.VLabel + ", quality “Very high”, same resolution and frame rate   ·   Container: " + a.Ext.ToUpperInvariant() + (a.KeepTracks ? " (keeps every audio track and subtitle)" : ""));
                facts.Add("Audio: " + a.ALabel + "   ·   Conversion time: " + (a.Measuring ? AdvisorText.Measuring : Compression.TimeText(a)));
            }
            lblAdvFacts.Text = string.Join("\n", facts.ToArray());
            lblAdvNote.Text = (a.Recommend ? "Why: " + a.Reason + "\n" : "") + (a.Alt != null ? a.Alt + "\n" : "") + AdvisorText.NotGuaranteed;
            btnAdvApply.Text = a.Recommend ? AdvisorText.Apply : "Apply anyway";
            btnAdvApply.Enabled = a.VKey != null;
            btnAdvMeasure.Enabled = a.VKey != null && !advMeasuring && !running;
            btnAdvMeasure.Text = a.Measuring ? AdvisorText.Measuring : (a.Measured ? "Measure again" : AdvisorText.Measure);
            advCard.Visible = true;
            advCard.Invalidate();
        }

        void MeasureAdvice()
        {
            var j = advJob; var a = j != null ? j.Advice as Advice : null;
            if (a == null || advMeasuring || running) return;
            advMeasuring = true; a.Measuring = true; UpdateAdvisor();
            Log("Compression assistant: measuring " + Path.GetFileName(j.Path) + " (3 test encodes, below-normal priority)…", Theme.Muted);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string err; bool ok = false;
                try { ok = Compression.Measure(j, a, () => running || IsDisposed, out err); } catch (Exception ex) { err = ex.Message; }
                UI(() =>
                {
                    advMeasuring = false; a.Measuring = false;
                    if (ok) Log("Compression assistant: " + Path.GetFileName(j.Path) + " → " + Compression.SizeText(a) + ", time " + Compression.TimeText(a) + " (measured).", Theme.Success);
                    else Log("Compression assistant: measurement not possible (" + err + ").", Theme.Accent1);
                    SaveSettings();
                    UpdateAdvisor();
                });
            });
        }

        /// "Optimized compression" preset: analyses the selected (or first) video and applies the advice.
        void OptimizedPreset()
        {
            var sel = list.SelectedItems.Count >= 1 ? (Job)list.SelectedItems[0].Tag : null;
            var j = sel != null && sel.Kind == MediaKind.Video ? sel : list.Items.Cast<ListViewItem>().Select(it => (Job)it.Tag).FirstOrDefault(x => x.Kind == MediaKind.Video);
            if (j == null) { Log("Optimized compression: add a video first.", Theme.Accent1); return; }
            if (!j.Probed) { Log("Optimized compression: " + Path.GetFileName(j.Path) + " is still being analysed — try again in a moment.", Theme.Accent1); return; }
            var a = j.Advice as Advice;
            if (a == null) { a = Compression.Analyze(j); j.Advice = a; }
            j.AdviceDismissed = false; advForced = j;
            if (a.VKey == null) { Log("Optimized compression: " + a.Reason, Theme.Accent1); UpdateAdvisor(); return; }
            if (a.Recommend) ApplyAdvice(j, a);
            else Log("Optimized compression: " + a.Reason + " Settings not changed (\"Apply anyway\" is available).", Theme.Accent1);
            UpdateAdvisor();
        }

        void ApplyAdvice(Job j, Advice a)
        {
            if (running) return;
            SetCategory(MediaKind.Video);
            int fi = -1;
            for (int i = 0; i < cbFormat.Items.Count; i++)
            {
                var f = cbFormat.Items[i] as OutFormat;
                if (f == null || f.Ext != a.Ext || f.Dynamic) continue;
                if (fi < 0) fi = i;
                if (a.Ext == "mp4" && f.VCodec == "h264") { fi = i; break; }
                if (a.Ext == "mkv" && f.VCodec == "h264") { fi = i; break; }
            }
            if (fi >= 0) cbFormat.SelectedIndex = fi;
            Action<ComboBox, string> pick = (cb, key) => { for (int i = 0; i < cb.Items.Count; i++) { var c = cb.Items[i] as Choice; if (c != null && c.Key == key) { cb.SelectedIndex = i; return; } } };
            pick(cbVCodec, a.VKey);
            cbQuality.SelectedIndex = a.QualityIdx;
            cbPreset.SelectedIndex = a.PresetIdx;
            nuVBitrate.Value = 0; nuNvCrf.Value = 0;
            pick(cbACodec, a.ACodec);
            if (a.AKbps > 0) { int k = cbAKbps.Items.IndexOf(a.AKbps.ToString(CultureInfo.InvariantCulture)); if (k >= 0) cbAKbps.SelectedIndex = k; } else if (cbAKbps.Items.Count > 0) cbAKbps.SelectedIndex = 0;
            chkSubs.Checked = a.KeepTracks;
            cbSeqExport.SelectedIndex = 0;
            Log("✦ Optimized compression applied for " + Path.GetFileName(j.Path) + ": " + a.Ext.ToUpperInvariant() + " · " + a.VLabel + " (quality Very high, speed Medium) · audio " + a.ALabel +
                (a.KeepTracks ? " · all tracks kept" : "") + ". Every setting stays editable; nothing starts until you click CONVERT.", Theme.Success);
        }      // single-page layout (kept for callers)

        /// Convert button label (video file or image sequence).
        void UpdateNavSummary()
        {
            var fmt = cbFormat != null ? cbFormat.SelectedItem as OutFormat : null;
            if (btnConvert != null && fmt != null)
                btnConvert.Text = cbSeqExport != null && category == MediaKind.Video && cbSeqExport.SelectedIndex > 0 ? (cbSeqExport.SelectedIndex == 1 ? "EXPORT PNG SEQUENCE" : "EXPORT JPG SEQUENCE") : "CONVERT TO " + fmt.Ext.ToUpperInvariant();
        }

        // ---------- control factories ----------
        FlatBtn Btn(string text, int w)
        {
            var b = new FlatBtn(text);
            if (w > 0) b.Width = Theme.S(w);
            b.Height = Theme.S(36);
            return b;
        }

        ComboBox Combo(params object[] items)
        {
            var c = new DarkCombo { DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Theme.Card, ForeColor = Theme.Text, Font = fUI, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)), IntegralHeight = false, MaxDropDownItems = 18 };
            c.Items.AddRange(items);
            if (c.Items.Count > 0) c.SelectedIndex = 0;
            return c;
        }

        NumericUpDown Num(decimal min, decimal max, decimal val, int dec)
        {
            return new DarkNumeric { Minimum = min, Maximum = max, Value = val, DecimalPlaces = dec, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)), Font = fUI };
        }

        TextBox Txt(string t)
        {
            return new TextBox { Text = t, BackColor = Theme.Card, ForeColor = Theme.Text, BorderStyle = BorderStyle.FixedSingle, Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)), Font = fUI };
        }

        CheckBox Check(string t)
        {
            return new CheckBox { Text = t, ForeColor = Theme.Text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(0, Theme.S(5), 0, Theme.S(3)), Font = fUI };
        }

        Label SectionTitle(string t)
        {
            return new Label { Text = t, AutoSize = true, ForeColor = Theme.Gold, Font = new Font("Segoe UI Semibold", 9.5f), Margin = new Padding(0, Theme.S(2), 0, Theme.S(10)) };
        }

        TableLayoutPanel Section(string title)
        {
            var t = new SmoothCardTable { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, Theme.S(16)), Padding = new Padding(Theme.S(18), Theme.S(14), Theme.S(18), Theme.S(16)) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 56));
            var l = new SectionHeader(title.Replace("  (", "  (").Trim(), SectionHeader.IconFor(title)) { BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, Theme.S(6)) };
            t.Controls.Add(l, 0, 0);
            t.SetColumnSpan(l, 2);
            var line = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Color.FromArgb(36, 42, 56), Margin = new Padding(0, 0, 0, Theme.S(8)) };
            t.Controls.Add(line, 0, 1);
            t.SetColumnSpan(line, 2);
            t.RowCount = 2;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return t;
        }

        Label AddRow(TableLayoutPanel t, string label, Control c)
        {
            int row = t.RowCount;
            t.RowCount = row + 1;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            if (label == null)
            {
                t.Controls.Add(c, 0, row);
                t.SetColumnSpan(c, 2);
                return null;
            }
            var l = new Label { Text = label, AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = fUI, Margin = new Padding(0, Theme.S(3), Theme.S(8), Theme.S(3)), Height = Theme.S(30) };
            t.Controls.Add(l, 0, row);
            t.Controls.Add(c, 1, row);
            return l;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, string lp);
        static void SetCue(TextBox t, string cue)
        {
            t.HandleCreated += (s, e) => { try { SendMessage(t.Handle, 0x1501, (IntPtr)1, cue); } catch { } };
        }

        ContextMenuStrip DarkMenu()
        {
            var m = new ContextMenuStrip { BackColor = Theme.Card, ForeColor = Theme.Text, ShowImageMargin = false, Font = fUI };
            m.Renderer = new ToolStripProfessionalRenderer(new DarkColors());
            return m;
        }

        ContextMenuStrip BuildContextMenu()
        {
            var m = DarkMenu();
            m.Items.Add("Open converted file", null, (s, e) => OpenSelectedOutput());
            m.Items.Add("Open source file", null, (s, e) => { var j = SelectedJob(); if (j != null) Shell(j.Path); });
            m.Items.Add("Show in Explorer", null, (s, e) => { var j = SelectedJob(); if (j != null) Shell("explorer.exe", "/select,\"" + (File.Exists(j.Output ?? "") ? j.Output : j.Path) + "\""); });
            m.Items.Add("Show FFmpeg command", null, (s, e) => PreviewCommand());
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Remove from list", null, (s, e) => RemoveSelected());
            return m;
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Theme.CardHover; } }
            public override Color MenuItemBorder { get { return Theme.Border; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color ToolStripDropDownBackground { get { return Theme.Card; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Card; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Card; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Card; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
        }

        // =====================================================================
        //  List drawing
        // =====================================================================
        void List_DrawColumnHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Card)) e.Graphics.FillRectangle(b, e.Bounds);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            var r = e.Bounds; r.X += Theme.S(8); r.Width -= Theme.S(8);
            TextRenderer.DrawText(e.Graphics, e.Header.Text.ToUpperInvariant(), fSection, r, Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        void List_DrawSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            var job = e.Item.Tag as Job;
            var g = e.Graphics;
            Color bg = e.Item.Selected ? Theme.Selection : (e.ItemIndex % 2 == 0 ? Theme.Panel : Color.FromArgb(26, 29, 37));
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.Bounds);
            if (e.Item.Selected && e.ColumnIndex == 0)
                using (var a = Theme.AccentBrush(new RectangleF(e.Bounds.X, e.Bounds.Y, 3, e.Bounds.Height))) g.FillRectangle(a, e.Bounds.X, e.Bounds.Y, Theme.S(3), e.Bounds.Height);
            var r = e.Bounds; r.X += Theme.S(8); r.Width -= Theme.S(10);

            if (e.ColumnIndex == 5 && job != null)
            {
                if (job.Status == StConverting)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    var br = new RectangleF(r.X, r.Y + r.Height / 2f - Theme.S(4), r.Width - Theme.S(46), Theme.S(8));
                    using (var p = Theme.Round(br, Theme.S(4))) using (var b = new SolidBrush(Theme.Card)) g.FillPath(b, p);
                    if (job.Progress > 0)
                    {
                        var fr = new RectangleF(br.X, br.Y, Math.Max(br.Height, (float)(br.Width * job.Progress)), br.Height);
                        using (var p = Theme.Round(fr, Theme.S(4))) using (var b = Theme.AccentBrush(br)) g.FillPath(b, p);
                    }
                    var tr = new Rectangle((int)br.Right + Theme.S(4), r.Y, Theme.S(44), r.Height);
                    TextRenderer.DrawText(g, (int)(job.Progress * 100) + " %", fSmall, tr, Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                    return;
                }
                Color c = Theme.Muted; string t = job.Status;
                if (t == StDone) { c = Theme.Success; t = "✓  Done"; }
                else if (t == StError) { c = Theme.Error; t = "✗  Error"; }
                else if (t == StCancelled) { c = Theme.Accent1; }
                TextRenderer.DrawText(g, t, fUI, r, c, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                return;
            }
            if (e.ColumnIndex == 1 && job != null)
            {
                Color kc = job.Kind == MediaKind.Video ? Color.FromArgb(120, 170, 255) : job.Kind == MediaKind.Audio ? Color.FromArgb(190, 140, 255) : job.Kind == MediaKind.Image ? Color.FromArgb(90, 220, 200) : Theme.Muted;
                TextRenderer.DrawText(g, e.SubItem.Text, fSection, r, kc, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                return;
            }
            TextRenderer.DrawText(g, e.SubItem.Text, fUI, r, e.ColumnIndex == 0 ? Theme.Text : Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        void FitColumns()
        {
            if (list.Columns.Count < 6) return;
            int others = 0;
            for (int i = 1; i < 6; i++) others += list.Columns[i].Width;
            int w = list.ClientSize.Width - others - 2;
            if (w > Theme.S(160)) list.Columns[0].Width = w;
        }

        // =====================================================================
        //  Category & format
        // =====================================================================
        void SetCategory(MediaKind k)
        {
            category = k;
            catVideo.Toggled = k == MediaKind.Video;
            catAudio.Toggled = k == MediaKind.Audio;
            catImage.Toggled = k == MediaKind.Image;
            cbFormat.BeginUpdate();
            cbFormat.Items.Clear();
            var fl = Formats.Of(k);
            var dyn = Formats.DynamicOf(k);
            foreach (var f in fl) cbFormat.Items.Add(f);
            if (dyn.Count > 0)
            {
                cbFormat.Items.Add(new Separator("──────  ALL " + dyn.Count + " FORMATS OF YOUR FFMPEG BUILD  ──────"));
                foreach (var f in dyn) cbFormat.Items.Add(f);
            }
            cbFormat.EndUpdate();
            string last;
            int idx = 0;
            if (settings.TryGetValue("format_" + k, out last))
                for (int i = 0; i < cbFormat.Items.Count; i++) { var of = cbFormat.Items[i] as OutFormat; if (of != null && of.Label == last) idx = i; }
            cbFormat.SelectedIndex = idx;
            string kn = k == MediaKind.Video ? "video" : k == MediaKind.Audio ? "audio" : "image";
            formatHint.Text = dyn.Count > 0
                ? (fl.Count + dyn.Count) + " " + kn + " formats  (" + fl.Count + " tuned presets + " + dyn.Count + " from your FFmpeg build)"
                : fl.Count + " " + kn + " formats" + (FormatCatalog.Loaded ? "" : "  — reading every FFmpeg format…");
            secVideo.Visible = k == MediaKind.Video;
            secRtx.Visible = k != MediaKind.Audio;
            secScale.Visible = k != MediaKind.Audio;
            secSeed.Visible = k != MediaKind.Audio;
            secVision.Visible = k != MediaKind.Audio;
            UpdateVisionRows();
            UpdateSeedRows();
            secOpti.Visible = k != MediaKind.Audio;
            secCai.Visible = k != MediaKind.Audio;
            secDn.Visible = k != MediaKind.Audio;
            secAudio.Visible = k != MediaKind.Image;
            if (secTracks != null) secTracks.Visible = k == MediaKind.Video;
            secImage.Visible = k == MediaKind.Image;
            UpdateScaleCategory();
            OnFormatChanged();
        }

        OutFormat CurrentFormat { get { return cbFormat.SelectedItem as OutFormat; } }

        void OnFormatChanged()
        {
            if (cbFormat.SelectedItem is Separator)
            {
                int i = cbFormat.SelectedIndex;
                cbFormat.SelectedIndex = i + 1 < cbFormat.Items.Count ? i + 1 : Math.Max(0, i - 1);
                return;
            }
            var f = CurrentFormat;
            if (f == null) return;
            settings["format_" + category] = f.Label;
            if (f.Kind == MediaKind.Video)
            {
                bool special = f.VCodec == "gif" || f.VCodec == "apng" || f.VCodec == "webpanim";
                cbVCodec.Enabled = !special;
                cbACodec.Enabled = !special;
                nuVBitrate.Enabled = !special;
                cbPreset.Enabled = !special;
                cbVCodec.SelectedIndex = 0;
                cbACodec.SelectedIndex = 0;
            }
            if (f.Kind == MediaKind.Audio)
            {
                bool lossless = f.ACodec == "flac" || f.ACodec == "alac" || f.ACodec.StartsWith("pcm") || f.ACodec.StartsWith("codec:pcm") || f.ACodec == "wavpack" || f.ACodec == "tta" || f.ACodec == "amr" || DsdEngine.IsDsd(f.ACodec);
                cbAKbps.Enabled = !lossless;
                cbACodec.Enabled = true;
                cbACodec.SelectedIndex = 0;
            }
            else cbAKbps.Enabled = true;
            if (f.Kind == MediaKind.Image)
            {
                bool q = f.VCodec == "mjpeg" || f.VCodec == "webp" || f.VCodec == "avif" || f.VCodec == "jxl" || f.Dynamic;
                tbImgQ.Enabled = q;
            }
            UpdateNvCrfRow();
            UpdateNavSummary();
        }

        void UpdateExtractLabel()
        {
            int m = cbExtract.SelectedIndex;
            lblExtractVal.Text = m == 0 ? "Position (s)" : m == 2 ? "Interval (s)" : "—";
            nuExtractVal.Enabled = m != 1;
        }

        // =====================================================================
        //  Filters
        // =====================================================================
        void RefreshFilterList(int select)
        {
            filterList.BeginUpdate();
            filterList.Items.Clear();
            foreach (var f in filters) filterList.Items.Add(f, f.Enabled);
            filterList.EndUpdate();
            if (select >= 0 && select < filterList.Items.Count) filterList.SelectedIndex = select;
            int on = filters.Count(f => f.Enabled);
            filterTitle.Text = "FILTERS" + (filters.Count > 0 ? "   (" + on + " active)" : "") +
                (FilterCatalog.All.Count > 0 ? "   ·   " + FilterCatalog.All.Count + " available" : "");
        }

        bool EnsureCatalog()
        {
            if (string.IsNullOrEmpty(Runner.FFmpeg)) { ChooseFFmpeg(); if (string.IsNullOrEmpty(Runner.FFmpeg)) return false; }
            if (!FilterCatalog.Loaded || FilterCatalog.All.Count == 0)
            {
                Cursor = Cursors.WaitCursor;
                FilterCatalog.Load();
                Cursor = Cursors.Default;
                OnCatalogLoaded();
            }
            return true;
        }

        void OnCatalogLoaded()
        {
            string rp; settings.TryGetValue("rtxproc", out rp);
            Rtx.FindProcessor(rp);
            UpdateRtxStatus();
            UpdateScaleRows(); UpdateOptiRows();
            header.FilterCount = FilterCatalog.All.Count;
            header.Invalidate();
            RefreshFilterList(filterList.SelectedIndex);
            if (FilterCatalog.All.Count > 0) Log("Filter library: " + FilterCatalog.All.Count + " filters found in your FFmpeg build.", Theme.Muted);
            else Log("Could not read the filter list: " + FilterCatalog.LoadError, Theme.Error);
        }

        void AddFilter()
        {
            if (!EnsureCatalog()) return;
            using (var d = new FilterBrowser(null))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.GraphText != null) { AppendGraph(d.GraphText); return; }
                if (d.Result == null) return;
                filters.Add(d.Result);
                RefreshFilterList(filters.Count - 1);
                Log("+ " + d.Result, Theme.Text);
            }
        }

        void EditFilter()
        {
            int i = filterList.SelectedIndex;
            if (i < 0 || i >= filters.Count) { AddFilter(); return; }
            if (!EnsureCatalog()) return;
            using (var d = new FilterBrowser(filters[i]))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (d.GraphText != null) { AppendGraph(d.GraphText); return; }
                if (d.Result == null) return;
                filters[i] = d.Result;
                RefreshFilterList(i);
            }
        }

        void AppendGraph(string g)
        {
            string cur = txtGraph.Text.Trim().TrimEnd(';');
            txtGraph.Text = cur.Length == 0 ? g : cur + ";\r\n" + g;
            Log("Inserted into filtergraph: " + g, Theme.Text);
            if (!g.Contains("[vout]") && !g.Contains("[aout]"))
                Log("   Tip: label the final output of your filtergraph [vout] (video) or [aout] (audio).", Theme.Accent1);
            if (g.Contains("[1:") && txtExtraInputs.Text.Trim().Length == 0)
                Log("   Tip: this filter needs a second input — add a file in “Extra inputs”.", Theme.Accent1);
        }

        void RemoveFilter()
        {
            int i = filterList.SelectedIndex;
            if (i < 0 || i >= filters.Count) return;
            filters.RemoveAt(i);
            RefreshFilterList(Math.Min(i, filters.Count - 1));
        }

        void MoveFilter(int d)
        {
            int i = filterList.SelectedIndex, j = i + d;
            if (i < 0 || j < 0 || j >= filters.Count) return;
            var t = filters[i]; filters[i] = filters[j]; filters[j] = t;
            RefreshFilterList(j);
        }

        void ShowPresets()
        {
            var m = DarkMenu();
            m.ShowImageMargin = false;
            foreach (var grp in FilterPresets.All.GroupBy(p => p.Group))
            {
                var sub = new ToolStripMenuItem(grp.Key) { ForeColor = Theme.Text };
                sub.DropDown.BackColor = Theme.Card;
                ((ToolStripDropDownMenu)sub.DropDown).ShowImageMargin = false;
                sub.DropDown.Renderer = m.Renderer;
                foreach (var p in grp)
                {
                    var pp = p;
                    var it = new ToolStripMenuItem(p.Label) { ForeColor = Theme.Text, ToolTipText = (p.V != null ? "Video: " + p.V : "") + (p.A != null ? (p.V != null ? "\n" : "") + "Audio: " + p.A : "") };
                    it.Click += (s, e) =>
                    {
                        if (pp.V != null) filters.Add(new FilterEntry { Kind = 'V', Expr = pp.V });
                        if (pp.A != null) filters.Add(new FilterEntry { Kind = 'A', Expr = pp.A });
                        RefreshFilterList(filters.Count - 1);
                        Log("+ Preset “" + pp.Label + "”", Theme.Text);
                    };
                    sub.DropDownItems.Add(it);
                }
                m.Items.Add(sub);
            }
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Remove all filters", null, (s, e) => { filters.Clear(); txtGraph.Text = ""; RefreshFilterList(-1); });
            m.Show(btnPresets, new Point(0, btnPresets.Height));
        }

        void BrowseExtraInputs()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Title = "Extra inputs for filters (input #1, #2…)", Filter = Formats.OpenDialogFilter() })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                var cur = ExtraInputs();
                cur.AddRange(d.FileNames);
                txtExtraInputs.Text = string.Join(" ; ", cur.ToArray());
            }
        }

        List<string> ExtraInputs()
        {
            return txtExtraInputs.Text.Split(';').Select(x => x.Trim().Trim('"')).Where(x => x.Length > 0).ToList();
        }

        // =====================================================================
        //  FFmpeg
        // =====================================================================
        void InitFFmpeg()
        {
            string saved; settings.TryGetValue("ffmpeg", out saved);
            if (Runner.AutoDetect(saved))
            {
                header.Right1 = "Looking for FFmpeg…"; header.Invalidate();
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { InitFFmpegWork(); }
                    catch (Exception ex) { UI(() => Log("Could not read your FFmpeg build: " + ex.Message, Theme.Error)); }
                });
            }
            else
            {
                header.Right1 = "▲  FFmpeg not found"; header.Right1Color = Theme.Error; header.Invalidate();
                Log("FFmpeg not found. Click “FFmpeg location…” and select ffmpeg.exe (e.g. C:\\FFmpeg\\bin\\ffmpeg.exe).", Theme.Error);
            }
        }

        void InitFFmpegWork()
        {
                {
                    string v = Runner.Version();
                    FormatCatalog.Load();
                    UI(() => OnFormatsLoaded());
                    FilterCatalog.Load();
                    UI(() =>
                    {
                        bool ok = !v.StartsWith("Error");
                        header.Right1 = (ok ? "●  " : "▲  ") + v;
                        header.Right1Color = ok ? Theme.Success : Theme.Error;
                        header.Invalidate();
                        Log("FFmpeg: " + Runner.FFmpeg, Theme.Muted);
                        if (string.IsNullOrEmpty(Runner.FFprobe)) Log("ffprobe.exe not found: durations and progress will be unavailable.", Theme.Accent1);
                        OnCatalogLoaded();
                    });
                }
        }

        void OnFormatsLoaded()
        {
            if (FormatCatalog.LoadError != null) { Log("Could not read the format list: " + FormatCatalog.LoadError, Theme.Error); return; }
            FillCodecCombos();
            SetCategory(category);
            header.FormatCount = new[] { MediaKind.Video, MediaKind.Audio, MediaKind.Image }.Sum(k => Formats.Of(k).Count + Formats.DynamicOf(k).Count);
            header.Invalidate();
            Log("Format library: " + FormatCatalog.Dynamic.Count + " output formats (muxers + image types) and " + FormatCatalog.Encoders.Count(e => e.Type != 'S') + " encoders found in your FFmpeg build.", Theme.Muted);
        }

        void FillCodecCombos()
        {
            Action<ComboBox, IEnumerable<Choice>, char> fill = (cb, curated, type) =>
            {
                string keep = cb.SelectedItem is Choice ? ((Choice)cb.SelectedItem).Key : "auto";
                cb.BeginUpdate();
                cb.Items.Clear();
                foreach (var c in curated) cb.Items.Add(c);
                var encs = FormatCatalog.Of(type).ToList();
                if (encs.Count > 0)
                {
                    cb.Items.Add(new Separator("──  ALL " + encs.Count + " " + (type == 'V' ? "VIDEO" : "AUDIO") + " ENCODERS  ──"));
                    foreach (var e in encs) cb.Items.Add(new Choice("enc:" + e.Name, e.Name + "  —  " + e.Desc + (e.Experimental ? "  (experimental)" : "")));
                }
                cb.EndUpdate();
                int idx = 0;
                for (int i = 0; i < cb.Items.Count; i++) { var c = cb.Items[i] as Choice; if (c != null && c.Key == keep) { idx = i; break; } }
                cb.SelectedIndex = idx;
            };
            fill(cbVCodec, Formats.VideoCodecs, 'V');
            fill(cbACodec, Formats.AudioCodecsForVideo, 'A');
        }

        class Separator
        {
            public string Text; public Separator(string t) { Text = t; }
            public override string ToString() { return Text; }
        }

        void SkipSeparator(ComboBox cb)
        {
            cb.SelectedIndexChanged += (s, e) =>
            {
                if (cb.SelectedItem is Separator)
                {
                    int i = cb.SelectedIndex;
                    cb.SelectedIndex = i + 1 < cb.Items.Count ? i + 1 : Math.Max(0, i - 1);
                }
            };
        }

        // ---------- resolution picker (presets + unlimited custom size) ----------
        public class ResPicker
        {
            public ComboBox Preset, Mode;
            public NumericUpDown W, H;
            public int[] PW, PH;
            public bool Busy;
        }

        ResPicker MakeRes(TableLayoutPanel sec, string[] labels, int[] pw, int[] ph)
        {
            var r = new ResPicker { PW = pw, PH = ph };
            r.Preset = Combo(labels);
            r.Preset.MaxDropDownItems = 24;
            r.W = Num(0, 2000000000, 0, 0); r.H = Num(0, 2000000000, 0, 0);
            r.W.Increment = r.H.Increment = 2;
            r.W.Dock = r.H.Dock = DockStyle.None;
            r.W.Width = r.H.Width = Theme.S(92);
            r.W.Location = new Point(0, 0);
            var x = new Label { Text = "×", ForeColor = Theme.Muted, AutoSize = false, Width = Theme.S(22), Height = Theme.S(26), TextAlign = ContentAlignment.MiddleCenter, Location = new Point(Theme.S(94), 0) };
            r.H.Location = new Point(Theme.S(118), 0);
            var host = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            host.Controls.AddRange(new Control[] { r.W, x, r.H });
            r.Mode = Combo("Stretch to exact size", "Fit — keep ratio, black bars", "Fill — keep ratio, crop");
            r.Mode.SelectedIndex = 1;
            tips.SetToolTip(r.W, "Output width in pixels — no limit. 0 = automatic (keeps the aspect ratio).");
            tips.SetToolTip(r.H, "Output height in pixels — no limit. 0 = automatic (keeps the aspect ratio).");
            tips.SetToolTip(r.Mode, "Used when both width and height are set and the source has a different aspect ratio.");
            AddRow(sec, "Resolution", r.Preset);
            AddRow(sec, "Size  W × H  (0 = auto)", host);
            AddRow(sec, "Aspect handling", r.Mode);
            Action sync = () => { };
            r.Preset.SelectedIndexChanged += (s, e) =>
            {
                int i = r.Preset.SelectedIndex;
                if (i < 0 || r.PW[i] < 0) { r.W.Focus(); return; }   // "Custom…"
                r.Busy = true;
                r.W.Value = r.PW[i]; r.H.Value = r.PH[i];
                r.Busy = false;
                sync();
            };
            EventHandler toCustom = (s, e) =>
            {
                sync();
                if (r.Busy) return;
                int i = r.Preset.SelectedIndex;
                if (i >= 0 && r.PW[i] >= 0 && (r.PW[i] != r.W.Value || r.PH[i] != r.H.Value))
                {
                    r.Busy = true; r.Preset.SelectedIndex = r.PW.Length - 1; r.Busy = false;
                }
            };
            r.W.ValueChanged += toCustom; r.H.ValueChanged += toCustom;
            r.W.KeyUp += (s, e) => toCustom(s, e); r.H.KeyUp += (s, e) => toCustom(s, e);
            sync();
            return r;
        }

        // ---------- NVIDIA RTX AI ----------
        bool loadingSettings;

        TrackBar SliderRow(int min, int max, int val, out Label valLabel, out Panel host)
        {
            var tb = new TrackBar { Minimum = min, Maximum = max, Value = val, TickStyle = TickStyle.None, BackColor = Theme.Panel, Dock = DockStyle.Fill, AutoSize = false, Height = Theme.S(28) };
            var lb = new Label { Text = val + " %", AutoSize = false, Width = Theme.S(48), ForeColor = Theme.Text, TextAlign = ContentAlignment.MiddleRight, Dock = DockStyle.Right };
            tb.ValueChanged += (s2, e2) => lb.Text = tb.Value + " %";
            host = new Panel { Height = Theme.S(30), Dock = DockStyle.Fill, Margin = new Padding(0, Theme.S(3), 0, Theme.S(3)) };
            host.Controls.Add(tb); host.Controls.Add(lb);
            valLabel = lb;
            return tb;
        }

        string DnModelKey()
        {
            int i = cbDnModel == null ? 0 : cbDnModel.SelectedIndex;
            return i >= 0 && i < AiDenoise.Models.Length ? AiDenoise.Models[i].Key : "scunet";
        }

        void UpdateDnRows()
        {
            using (UiLayoutBatch.Begin(secDn))
            {
            bool on = chkDn.Checked;
            string key = DnModelKey();
            lblDnInfo.Visible = on;
            if (cbDnModel != null) { cbDnModel.Visible = on; if (lblDnModel != null) lblDnModel.Visible = on; }
            if (dnModelBar != null) dnModelBar.Visible = on && key != "scunet";
            if (dnSupportBtn != null) dnSupportBtn.Text = AiDenoise.HasSpandrel ? "Reinstall model support…" : "Install model support…";
            var probe = new ConvSettings { DnModel = key };
            bool ok = AiDenoise.ReadyFor(probe);
            lblDnInfo.Text = AiDenoise.StatusFor(key);
            lblDnInfo.ForeColor = ok ? Theme.Success : Theme.Accent1;
            }
        }

        void InstallDnModelSupport()
        {
            if (!AiDenoise.Ready) { OfferAiInstall(); return; }
            string root = AiDenoise.Root;
            if (MessageBox.Show(this,
                "The HAT-L, DAT and SPAN models are loaded by spandrel (MIT, from the chaiNNer project).\n\n" +
                "Install it now into the AI Denoiser folder?\n" + root + "\n\n(torchvision + spandrel, a few hundred MB; a console window shows the progress.)",
                "Denoiser IA models", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                string bat = AiDenoise.WriteModelSupportInstaller(root);
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = root, UseShellExecute = true });
                Log("Model support installation started in a console window (" + root + ").", Theme.Accent1);
                if (p != null)
                    new Thread(() => { try { p.WaitForExit(); } catch { } UI(() => { UpdateDnRows(); Log(AiDenoise.HasSpandrel ? "Denoiser IA model support installed." : "Model support installation did not finish — see the console window.", AiDenoise.HasSpandrel ? Theme.Success : Theme.Error); }); }) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("Could not start the installation: " + ex.Message, Theme.Error); }
        }

        void OfferAiInstall()
        {
            string root = AiDenoise.Root;
            if (MessageBox.Show(this,
                "Install the AI Denoiser now?\n\nFFmpeg Forge downloads Python 3.12, PyTorch (CUDA, about 3 GB) and the SCUNet model (72 MB, Apache-2.0) into:\n" + root +
                "\n\nA console window shows the progress. Without it, FFmpeg Forge uses its classic (non-AI) denoiser.",
                "Denoiser IA", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                string bat = AiDenoise.WriteInstaller(root);
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = root, UseShellExecute = true });
                Log("AI Denoiser installation started in a console window (" + root + ").", Theme.Accent1);
                if (p != null)
                    new Thread(() => { try { p.WaitForExit(); } catch { } UI(() => { UpdateDnRows(); Log(AiDenoise.Ready ? "AI Denoiser installed and ready." : "AI Denoiser installation did not finish — see the console window.", AiDenoise.Ready ? Theme.Success : Theme.Error); }); }) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("Could not start the installation: " + ex.Message, Theme.Error); }
        }

        void UpdateOptiRows()
        {
            using (UiLayoutBatch.Begin(secOpti))
            {
            foreach (var c in optiRows) if (c != null) c.Visible = chkOpti.Checked;
            bool lp = Scaling.HasLibplacebo;
            lblOptiInfo.Text = (FilterCatalog.Loaded && !lp)
                ? "⚠ Your FFmpeg build has no libplacebo (Vulkan) filter: OptiScaler needs a \"full\" FFmpeg build."
                : "Runs on: GPU (libplacebo / Vulkan)  ·  applied after the Scale type" +
                  "\nPort of OptiScaler's open-source shaders (GPL-3.0). DLSS, XeSS, FSR 2+ and MAS need a game's motion vectors and depth, so they cannot run on video files.";
            lblOptiInfo.ForeColor = lblOptiInfo.Text.StartsWith("⚠") ? Theme.Accent1 : Theme.Muted;
            }
        }

        void UpdateScaleRows()
        {
            using (UiLayoutBatch.Begin(secScale))
            {
            foreach (var c in scaleRows) if (c != null) c.Visible = chkScale.Checked;
            var t = cbScaleType.SelectedItem as Scaling.ScaleType;
            if (t == null) return;
            tbScaleSharp.Enabled = t.Sharpness;
            lblScaleSharpVal.ForeColor = t.Sharpness ? Theme.Text : Theme.Muted;
            bool lp = Scaling.HasLibplacebo;
            string where = t.GpuOnly ? "GPU (libplacebo / Vulkan)" : (chkScaleOpt.Checked && lp && (t.Key == "nearest" || t.Key == "bicubic" || t.Key == "lanczos" || t.Key == "spline" || t.Key == "bcas") ? "GPU (libplacebo / Vulkan)" : "CPU");
            string text = "Runs on: " + where + (t.Sharpness ? "" : "  ·  sharpness not used by this type");
            if (t.Key == "fsr2" || t.Key == "fsr3")
                text += "\nExperimental: AMD's FSR 2/3 need a game's motion vectors, so FFmpeg Forge estimates the motion from the video" +
                        (t.Key == "fsr3" ? " and generates in-between frames (×2 fps, CPU, slow)." : ".") + " FSR 4.1 is not possible: closed AI model, Radeon only.";
            if (FilterCatalog.Loaded && !lp && t.GpuOnly) text = "⚠ Your FFmpeg build has no libplacebo (Vulkan) filter: this type needs a \"full\" FFmpeg build.";
            lblScaleInfo.Text = text + "\nLS1 (Lossless Scaling's own algorithm) is proprietary and cannot be included.";
            lblScaleInfo.ForeColor = text.StartsWith("⚠") ? Theme.Accent1 : Theme.Muted;
            }
        }

        void UpdateUpmixRow()
        {
            using (UiLayoutBatch.Begin(secAudio))
            {
            var c = cbCh.SelectedItem as Choice;
            var l = c != null ? ChannelLayouts.Get(c.Key) : null;
            bool on = l != null && new[] { "5.1", "7.1", "5.1.2", "5.1.4", "7.1.2", "7.1.4", "7.1.6", "9.1.4", "9.1.6", "binaural" }.Contains(l.Key);
            cbUpmix.Visible = lblUpmix.Visible = on;
            }
        }

        void UpdateRtxRows()
        {
            using (UiLayoutBatch.Begin(secRtx))
            {
            foreach (var c in vsrRows) if (c != null) c.Visible = chkVsr.Checked;
            foreach (var c in hdrRows) if (c != null) c.Visible = chkHdr.Checked && category == MediaKind.Video;
            foreach (var c in dlssRows) if (c != null) c.Visible = chkDlss.Checked && category == MediaKind.Video;
            }
        }

        void UpdateRtxStatus()
        {
            if (lblRtxStatus == null) return;
            lblRtxStatus.Text = Rtx.StatusText();
            btnVsrParams.Enabled = Rtx.NativeVsr;
            btnHdrParams.Enabled = Rtx.NativeHdr;
            btnDlssParams.Enabled = Rtx.NativeDlss;
            bool any = Rtx.AnyNative || Rtx.HasProcessor;
            lblRtxStatus.ForeColor = any ? Theme.Success : Theme.Muted;
        }

        void LocateRtxProcessor()
        {
            using (var d = new OpenFileDialog { Title = "Select RTXVideoProcessor.exe (NVIDIA RTX Video SDK)", Filter = "RTXVideoProcessor.exe|RTXVideoProcessor.exe;*.exe" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Rtx.ProcessorPath = d.FileName;
                settings["rtxproc"] = d.FileName;
                SaveSettings();
                UpdateRtxStatus();
                Log("RTXVideoProcessor: " + d.FileName, Theme.Muted);
            }
        }

        void EditRtxParams(string[] names, ref string expr, FlatBtn btn, string what)
        {
            if (!EnsureCatalog()) return;
            string f = Rtx.Pick(names);
            if (f == null) { Log(what + ": your FFmpeg build has no " + string.Join(" / ", names) + " filter.", Theme.Accent1); return; }
            using (var d = new FilterBrowser(new FilterEntry { Kind = 'V', Expr = string.IsNullOrWhiteSpace(expr) ? f : expr }))
            {
                if (d.ShowDialog(this) != DialogResult.OK || d.Result == null) return;
                string e = d.Result.Expr.Trim();
                expr = e == f ? null : e;
                btn.Text = "All " + what + " filter parameters…" + (expr != null ? "  (custom ✓)" : "");
                Log(what + " parameters: " + (expr ?? "automatic"), Theme.Text);
            }
        }

        bool BuildJobCommand(Job job, ConvSettings s, string outPath, out string exe, out string args, out double expected, out string err)
        {
            Rtx.DriverPlan plan;
            return BuildJobCommand(job, s, outPath, out exe, out args, out expected, out err, out plan);
        }

        bool BuildJobCommand(Job job, ConvSettings s, string outPath, out string exe, out string args, out double expected, out string err, out Rtx.DriverPlan plan)
        {
            exe = Runner.FFmpeg;
            plan = null;
            string why;
            {
                string ak = s.ACodec == "auto" ? s.Format.ACodec : s.ACodec;
                if (DsdEngine.IsDsd(ak))
                {
                    expected = 0;
                    if (s.Format.Kind != MediaKind.Audio) { err = "DSD (.dsf / .dff) is an audio format: choose the AUDIO category."; args = null; return false; }
                    var dp = DsdEngine.Build(job, s, ak, outPath, out err);
                    expected = dp != null ? dp.Expected : 0;
                    args = dp != null ? dp.Text() : null;
                    return dp != null;
                }
            }
            if (s.SdrHdr > 0 && s.Format.Kind != MediaKind.Audio)
            {
                string herr = Hdr.Check(job, s);
                if (herr != null) { err = herr; args = null; expected = 0; return false; }
            }
            if (AudioTracks.Active(s))
            {
                // 2.1.6: change only the audio / add audio tracks (video copied when "Keep the video as is" is ticked)
                var notes = new List<string>();
                args = AudioTracks.Build(job, s, outPath, out expected, out err, notes);
                foreach (var n in notes) { string nn = n; UI(() => Log("   Audio tracks: " + nn, Theme.Accent1)); }
                return args != null;
            }
            if (s.Vision != null && s.Vision.Enabled && s.Format.Kind != MediaKind.Audio)
            {
                var vp = VisionEngine.Build(job, s, outPath, out err);
                expected = vp != null ? vp.Expected : 0;
                args = vp != null ? vp.Text() : null;
                return vp != null;
            }
            if (s.Seed != null && s.Seed.Enabled && s.Format.Kind != MediaKind.Audio)
            {
                var sp = SeedEngine.Build(job, s, outPath, out err);
                expected = sp != null ? sp.Expected : 0;
                args = sp != null ? sp.Text() : null;
                return sp != null;
            }
            if (s.Format.Kind == MediaKind.Image && s.RtxVsr)
            {
                // NVIDIA RTX Video on pictures: the built-in NVIDIA driver engine (ForgeRTX) processes the single frame
                if (!Rtx.HasDriver) { err = "NVIDIA RTX Video for pictures uses the built-in NVIDIA driver engine: ForgeRTX.exe is missing next to FFmpegForge.exe."; args = null; expected = 0; return false; }
                plan = Rtx.PlanDriver(job, s, outPath, out err);
                expected = 0;
                args = plan != null ? plan.Text() : null;
                exe = Rtx.DriverPath;
                return plan != null;
            }
            string engine = s.Format.Kind == MediaKind.Video && s.RtxActive ? Rtx.Engine(s, out why) : null;
            if (engine == "driver")
            {
                plan = Rtx.PlanDriver(job, s, outPath, out err);
                expected = plan != null ? plan.Expected : 0;
                args = plan != null ? plan.Text() : null;
                exe = Rtx.DriverPath;
                return plan != null;
            }
            if (engine == "processor")
            {
                exe = Rtx.ProcessorPath;
                args = Rtx.ProcessorArgs(job, s, outPath, out expected, out err);
            }
            else args = CommandBuilder.Build(job, s, outPath, out expected, out err);
            return args != null;
        }

        void ChooseFFmpeg()
        {
            using (var d = new OpenFileDialog { Title = "Select ffmpeg.exe", Filter = "ffmpeg.exe|ffmpeg.exe|Programs (*.exe)|*.exe", InitialDirectory = Directory.Exists(@"C:\FFmpeg\bin") ? @"C:\FFmpeg\bin" : "" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (Runner.SetFFmpeg(d.FileName)) { settings["ffmpeg"] = d.FileName; SaveSettings(); FilterCatalog.All.Clear(); FilterCatalog.Loaded = false; FormatCatalog.Dynamic.Clear(); FormatCatalog.Loaded = false; InitFFmpeg(); }
            }
        }

        // =====================================================================
        //  File management
        // =====================================================================
        void OpenRip()
        {
            using (var f = new RipForm(paths => { AddPaths(paths); }, settings))
                f.ShowDialog(this);
            SaveSettings();
        }

        void AddFilesDialog()
        {
            using (var d = new OpenFileDialog { Multiselect = true, Title = "Add files to convert", Filter = Formats.OpenDialogFilter() })
                if (d.ShowDialog(this) == DialogResult.OK) AddPaths(d.FileNames);
        }

        void AddFolderDialog()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose a folder: every media file inside (subfolders included) will be added." })
                if (d.ShowDialog(this) == DialogResult.OK) AddPaths(new[] { d.SelectedPath });
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) && !running)
            {
                e.Effect = DragDropEffects.Copy;
                dropHint.Highlight = true; dropHint.Invalidate();
            }
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            dropHint.Highlight = false; dropHint.Invalidate();
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null) AddPaths(files);
        }

        public void AddFromCommandLine(string[] a) { AddPaths(a); }

        void AddPaths(IEnumerable<string> paths)
        {
            if (running) return;
            var existing = new HashSet<string>(list.Items.Cast<ListViewItem>().Select(i => ((Job)i.Tag).Path), StringComparer.OrdinalIgnoreCase);
            var toAdd = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                    {
                        foreach (var f in Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories))
                            if (Formats.IsMediaFile(f)) toAdd.Add(f);
                    }
                    else if (File.Exists(p)) toAdd.Add(p);
                }
                catch (Exception ex) { Log("Cannot access " + p + " (" + ex.Message + ")", Theme.Error); }
            }
            var newJobs = new List<Job>();
            list.BeginUpdate();
            foreach (var f in toAdd)
            {
                if (!existing.Add(f)) continue;
                var job = new Job { Path = f, Kind = Formats.KindFromExt(f) };
                try { job.Size = new FileInfo(f).Length; } catch { }
                var it = new ListViewItem(new[] { Path.GetFileName(f), Formats.KindName(job.Kind), "analyzing…", "", FmtSize(job.Size), job.Status }) { Tag = job, ToolTipText = f };
                list.Items.Add(it);
                newJobs.Add(job);
            }
            list.EndUpdate();
            UpdateEmpty();
            if (newJobs.Count > 0)
            {
                statusLbl.Text = list.Items.Count + " file(s) in the list.";
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    foreach (var j in newJobs)
                    {
                        Runner.Probe(j);
                        try { if (j.Kind == MediaKind.Video) j.Advice = Compression.Analyze(j); } catch { }
                        var jj = j;
                        UI(() => { RefreshRow(jj); UpdateAdvisor(); });
                    }
                });
            }
        }

        void RefreshRow(Job j)
        {
            foreach (ListViewItem it in list.Items)
            {
                if (it.Tag != j) continue;
                it.SubItems[1].Text = Formats.KindName(j.Kind);
                it.SubItems[2].Text = j.Info;
                it.SubItems[3].Text = j.Kind == MediaKind.Image && !CommandBuilder.IsAnimatedImageSource(j) ? "—" : FmtDur(j.Duration);
                it.SubItems[4].Text = FmtSize(j.Size);
                string st = j.Status;
                if (j.Status == StConverting && j.Progress > 0.02 && j.Progress < 1 && j.Started != default(DateTime))
                {
                    double el = (DateTime.Now - j.Started).TotalSeconds;
                    if (el > 4)
                    {
                        string left = Compression.Time(el * (1 - j.Progress) / j.Progress);
                        st = "Converting… " + (int)(j.Progress * 100) + " % · ~" + left + " left";
                        if (running) statusLbl.Text = "Converting  —  " + Path.GetFileName(j.Path) + "  —  " + (int)(j.Progress * 100) + " %  ·  about " + left + " left";
                    }
                }
                it.SubItems[5].Text = st;
                list.Invalidate(it.Bounds);
                break;
            }
        }

        void RemoveSelected()
        {
            if (running) return;
            foreach (ListViewItem it in list.SelectedItems) list.Items.Remove(it);
            UpdateEmpty();
        }

        void ClearAll()
        {
            if (running) return;
            list.Items.Clear();
            overall.Value = 0;
            statusLbl.Text = "Ready.";
            UpdateEmpty();
        }

        void UpdateEmpty() { dropHint.Visible = list.Items.Count == 0; FitColumns(); UpdateNavSummary(); UpdateAdvisor(); }

        Job SelectedJob()
        {
            if (list.SelectedItems.Count > 0) return list.SelectedItems[0].Tag as Job;
            if (list.Items.Count > 0) return list.Items[0].Tag as Job;
            return null;
        }

        void OpenSelectedOutput()
        {
            var j = SelectedJob();
            if (j == null) return;
            if (!string.IsNullOrEmpty(j.Output) && File.Exists(j.Output)) Shell(j.Output);
            else if (!string.IsNullOrEmpty(j.Output)) Shell(Path.GetDirectoryName(j.Output));
        }

        void OpenOutputFolder()
        {
            string dir = null;
            if (!string.IsNullOrEmpty(lastOutput)) dir = Path.GetDirectoryName(lastOutput);
            else if (!chkSameDir.Checked && Directory.Exists(txtOutDir.Text)) dir = txtOutDir.Text;
            else { var j = SelectedJob(); if (j != null) dir = Path.GetDirectoryName(j.Path); }
            if (dir != null && Directory.Exists(dir)) Shell(dir);
        }

        void BrowseOutDir()
        {
            using (var d = new FolderBrowserDialog { Description = "Destination folder for converted files", SelectedPath = txtOutDir.Text })
                if (d.ShowDialog(this) == DialogResult.OK) txtOutDir.Text = d.SelectedPath;
        }

        static void Shell(string file, string args = null)
        {
            try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); } catch { }
        }

        // =====================================================================
        //  Conversion
        // =====================================================================
        static readonly int[] RateValues = { 0, 8000, 16000, 22050, 32000, 44100, 48000, 88200, 96000, 192000 };

        ConvSettings Snapshot()
        {
            var s = new ConvSettings();
            s.Format = CurrentFormat;
            s.VCodec = cbVCodec.SelectedItem is Choice ? ((Choice)cbVCodec.SelectedItem).Key : "auto";
            s.QualityIdx = cbQuality.SelectedIndex;
            s.NvCrf = (int)nuNvCrf.Value;
            s.SeqExport = s.Format.Kind == MediaKind.Video ? Math.Max(0, cbSeqExport.SelectedIndex) : 0;
            s.Deint = s.Format.Kind == MediaKind.Video ? Math.Max(0, cbDeint.SelectedIndex) : 0;
            s.DeintOrder = Math.Max(0, cbDeintOrder.SelectedIndex);
            s.VBitrate = (int)nuVBitrate.Value;
            s.PresetIdx = cbPreset.SelectedIndex;
            var rp = s.Format.Kind == MediaKind.Image ? resImage : resVideo;
            s.ResW = (int)rp.W.Value; s.ResH = (int)rp.H.Value; s.ScaleMode = rp.Mode.SelectedIndex;
            double fps = 0;
            if (cbFps.SelectedIndex > 0) double.TryParse(cbFps.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out fps);
            s.Fps = fps;
            s.Rotate = s.Format.Kind == MediaKind.Image ? cbImgRotate.SelectedIndex : cbRotate.SelectedIndex;
            s.KeepSubs = chkSubs.Checked;
            s.SdrHdr = s.Format == null ? 0 : s.Format.Kind == MediaKind.Video ? Math.Max(0, cbHdr.SelectedIndex) : s.Format.Kind == MediaKind.Image ? (cbHdrImg.SelectedIndex > 0 ? Hdr.Hdr10 : 0) : 0;
            s.HdrPeak = Hdr.Peaks[Math.Max(0, (s.Format != null && s.Format.Kind == MediaKind.Image ? cbHdrPeakImg : cbHdrPeak).SelectedIndex)];
            s.KeepVideo = s.Format != null && s.Format.Kind == MediaKind.Video && chkKeepVideo.Checked;
            s.ExistingAudio = s.Format != null && s.Format.Kind == MediaKind.Video ? Math.Max(0, cbExistingAudio.SelectedIndex) : 0;
            s.ExtraAudio = s.Format != null && s.Format.Kind == MediaKind.Video ? new List<ExtraTrack>(extraTracks) : new List<ExtraTrack>();
            s.RtxVsr = chkVsr.Checked && s.Format.Kind != MediaKind.Audio;
            s.RtxHdr = chkHdr.Checked && s.Format.Kind == MediaKind.Video;
            s.Dlss = chkDlss.Checked && s.Format.Kind == MediaKind.Video;
            s.VsrQuality = cbVsrQ.SelectedIndex + 1;
            s.VsrScale = cbVsrScale.SelectedIndex + 2;
            s.HdrContrast = (int)nuHdrContrast.Value; s.HdrSaturation = (int)nuHdrSat.Value;
            s.HdrMiddleGray = (int)nuHdrGray.Value; s.HdrMaxLum = (int)nuHdrLum.Value;
            s.DlssScale = new[] { 1.0, 1.5, 1.72, 2.0, 3.0 }[cbDlssMode.SelectedIndex];
            s.DlssQuality = new[] { 4, 4, 3, 2, 1 }[cbDlssMode.SelectedIndex];
            s.VsrExpr = vsrExpr; s.HdrExpr = hdrExpr; s.DlssExpr = dlssExpr;
            s.RtxEnginePref = new[] { "auto", "native", "driver", "processor" }[cbRtxEngine.SelectedIndex];
            s.ScaleEnabled = chkScale.Checked && s.Format.Kind != MediaKind.Audio;
            s.ScaleType = cbScaleType.SelectedItem is Scaling.ScaleType ? ((Scaling.ScaleType)cbScaleType.SelectedItem).Key : "fsr";
            s.ScaleFactor = new[] { 1.5, 2.0, 2.5, 3.0, 4.0, 0.0 }[Math.Max(0, cbScaleFactor.SelectedIndex)];
            if (s.ScaleFactor == 0 && s.ResW <= 0 && s.ResH <= 0) s.ScaleFactor = 2.0;
            s.ScaleSharpness = tbScaleSharp.Value;
            s.ScaleOptimized = chkScaleOpt.Checked;
            s.ScaleFidelity = chkScaleFid.Checked;
            s.ScaleFullChroma = chkScale444.Checked;
            s.DnEnabled = chkDn.Checked && s.Format.Kind != MediaKind.Audio;
            s.DnModel = DnModelKey();
            s.CaiEnabled = chkCai.Checked && cbCaiModel.SelectedItem != null && s.Format.Kind != MediaKind.Audio;
            s.CaiModel = cbCaiModel.SelectedItem as string;
            s.CaiKeep = cbCaiSize.SelectedIndex == 1;
            s.CaiGpu = cbCaiProc.SelectedIndex == 0;
            s.CaiFp32 = cbCaiPrec.SelectedIndex == 1;
            s.CaiTile = new[] { 0, 256, 512, 1024, 100000 }[Math.Max(0, cbCaiTile.SelectedIndex)];
            s.OptiEnabled = chkOpti.Checked && s.Format.Kind != MediaKind.Audio;
            s.OptiMult = OptiMults[Math.Max(0, cbOptiMult.SelectedIndex)];
            s.OptiKernel = Math.Max(0, cbOptiKernel.SelectedIndex) + 1;
            s.OptiSharpness = tbOptiSharp.Value;
            s.OptiContrast = chkOptiContrast.Checked;
            s.StillDuration = (double)nuStill.Value;
            s.Seed = SeedSnapshot(s.Format.Kind);
            s.Vision = VisionSnapshot(s.Format.Kind);
            s.ACodec = cbACodec.SelectedItem is Choice ? ((Choice)cbACodec.SelectedItem).Key : "auto";
            int kb = 0; if (cbAKbps.SelectedIndex > 0) int.TryParse(cbAKbps.Text, out kb);
            s.AKbps = cbAKbps.Enabled ? kb : 0;
            s.SampleRate = RateValues[cbRate.SelectedIndex];
            s.ChLayout = cbCh.SelectedItem is Choice ? ((Choice)cbCh.SelectedItem).Key : "";
            s.UpmixStyle = UpmixCodes[Math.Max(0, cbUpmix.SelectedIndex)];
            s.Normalize = chkNorm.Checked;
            s.VolumePct = (int)nuVolume.Value;
            s.ImgQuality = tbImgQ.Value;
            s.ExtractMode = cbExtract.SelectedIndex;
            s.ExtractAt = (double)nuExtractVal.Value;
            s.ExtractEvery = (double)nuExtractVal.Value;
            s.TrimStart = ParseTime(txtStart.Text);
            s.TrimEnd = ParseTime(txtEnd.Text);
            s.CustomArgs = txtCustom.Text;
            s.OutDir = chkSameDir.Checked ? null : txtOutDir.Text.Trim();
            s.Overwrite = chkOverwrite.Checked;
            s.CoverMode = s.Format.Kind == MediaKind.Video ? Math.Max(0, cbCover.SelectedIndex) : 0;
            s.CoverAt = (double)nuCoverAt.Value; s.CoverPath = coverPath;
            s.Filters = filters.Select(f => new FilterEntry { Kind = f.Kind, Expr = f.Expr, Enabled = f.Enabled }).ToList();
            s.FilterComplex = txtGraph.Text;
            s.ExtraInputs = ExtraInputs();
            return s;
        }

        static double ParseTime(string t)
        {
            t = (t ?? "").Trim().Replace(',', '.');
            if (t.Length == 0) return -1;
            double total = 0;
            foreach (var part in t.Split(':'))
            {
                double v;
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return -1;
                total = total * 60 + v;
            }
            return total;
        }

        static string MakeOutPath(Job job, ConvSettings s, bool sequence)
        {
            string dir = string.IsNullOrEmpty(s.OutDir) ? Path.GetDirectoryName(job.Path) : s.OutDir;
            string name = Path.GetFileNameWithoutExtension(job.Path);
            string ext = "." + s.Format.Ext;
            { string ak = s.ACodec == "auto" ? s.Format.ACodec : s.ACodec; if (s.Format.Kind == MediaKind.Audio && DsdEngine.IsDsd(ak)) ext = "." + DsdEngine.Ext(DsdEngine.ResolveKey(job, s, ak)); }
            Func<string, string> build = b => Path.Combine(dir, sequence ? b + "_%05d" + ext : b + ext);
            Func<string, bool> taken = b =>
            {
                string p = sequence ? Path.Combine(dir, b + "_00001" + ext) : Path.Combine(dir, b + ext);
                if (string.Equals(Path.GetFullPath(p), Path.GetFullPath(job.Path), StringComparison.OrdinalIgnoreCase)) return true;
                return !s.Overwrite && File.Exists(p);
            };
            string bn = name;
            int n = 1;
            while (taken(bn)) { n++; bn = name + " (" + n + ")"; }
            return build(bn);
        }

        static bool IsSequence(Job j, ConvSettings s)
        {
            bool srcVideo = j.Kind == MediaKind.Video || CommandBuilder.IsAnimatedImageSource(j);
            if (s.Seed != null && s.Seed.Enabled) return false;
            if (s.Vision != null && s.Vision.Enabled) return false;
            return s.Format.Kind == MediaKind.Image && srcVideo && s.ExtractMode != 0;
        }

        void PreviewCommand()
        {
            var j = SelectedJob();
            if (j == null) { Log("Add a file first.", Theme.Accent1); return; }
            var s = Snapshot();
            double exp; string err;
            string exe, args;
            BuildJobCommand(j, s, MakeOutPath(j, s, IsSequence(j, s)), out exe, out args, out exp, out err);
            if (args == null) { Log("Impossible: " + err, Theme.Error); return; }
            if (exe != Runner.FFmpeg)
            {
                Log("\"" + exe + "\" " + args, Theme.Text);
                try { Clipboard.SetText("\"" + exe + "\" " + args); Log("(command copied to clipboard)", Theme.Muted); } catch { }
                return;
            }
            string shown = args.Replace("-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats ", "-y ");
            Log("ffmpeg " + shown, Theme.Text);
            try { Clipboard.SetText("\"" + (Runner.FFmpeg.Length > 0 ? Runner.FFmpeg : "ffmpeg") + "\" " + shown); Log("(command copied to clipboard)", Theme.Muted); } catch { }
        }

        void StartConversion()
        {
            if (running) return;
            if (string.IsNullOrEmpty(Runner.FFmpeg)) { ChooseFFmpeg(); if (string.IsNullOrEmpty(Runner.FFmpeg)) return; }
            if (list.Items.Count > 0) ShowPage(0);     // follow the progress on the Files page
            if (list.Items.Count == 0) { AddFilesDialog(); if (list.Items.Count == 0) return; }
            var s = Snapshot();
            if (s.Format == null) return;
            if (s.OutDir != null)
            {
                if (s.OutDir.Length == 0) { MessageBox.Show(this, "Please choose a destination folder.", "FFmpeg Forge", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
                try { Directory.CreateDirectory(s.OutDir); }
                catch (Exception ex) { MessageBox.Show(this, "Invalid destination folder:\n" + ex.Message, "FFmpeg Forge", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            }
            foreach (var x in s.ExtraInputs)
                if (!File.Exists(x)) { MessageBox.Show(this, "Extra input not found:\n" + x, "FFmpeg Forge", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            SaveSettings();

            var jobs = new List<Job>();
            var targets = list.SelectedItems.Count > 1 ? list.SelectedItems.Cast<ListViewItem>() : list.Items.Cast<ListViewItem>();
            foreach (var it in targets) { var j = (Job)it.Tag; j.Status = StQueued; j.Progress = 0; j.Error = null; jobs.Add(j); }
            list.Invalidate();

            running = true; cancelRequested = false;
            btnConvert.Enabled = false; btnCancel.Enabled = true;
            overall.Value = 0;
            Log("", Theme.Muted);
            int nf = s.Filters.Count(f => f.Enabled) + (string.IsNullOrWhiteSpace(s.FilterComplex) ? 0 : 1);
            Log("━━━  Converting " + jobs.Count + " file(s) to " + s.Format.Label + (nf > 0 ? "  ·  " + nf + " filter(s)" : "") + "  ━━━", Theme.Accent1);

            usePreview = chkPreview.Checked && s.Format.Kind == MediaKind.Video && PreviewFrames.Supported;
            if (usePreview) PreviewFrames.Reset();
            var t = new Thread(() => Worker(jobs, s)) { IsBackground = true };
            t.Start();
        }

        bool usePreview;

        void Worker(List<Job> jobs, ConvSettings s)
        {
            // Image sequence export: the whole video pipeline renders a lossless 4:4:4 FFV1 file, then every frame of it
            // is written as PNG / JPEG. So every video option (filters, AI, Scale, RTX, VisionNext…) works unchanged.
            var sUser = s;
            int seqKind = s.Format.Kind == MediaKind.Video ? s.SeqExport : 0;
            bool seqHi = false;
            if (seqKind > 0)
            {
                var mkv = Formats.All.FirstOrDefault(f => f.Kind == MediaKind.Video && f.Ext == "mkv" && f.VCodec == "ffv1");
                if (mkv != null)
                {
                    s = s.Clone();
                    s.Format = mkv; s.VCodec = "ffv1"; s.ACodec = "none"; s.VBitrate = 0; s.NvCrf = 0; s.KeepSubs = false;
                    seqHi = (s.ScaleActive && s.ScaleFidelity) || s.RtxHdr;
                    s.CustomArgs = ((s.CustomArgs ?? "") + " -pix_fmt " + (seqHi ? "yuv444p16le" : "yuv444p")).Trim();
                }
                else seqKind = 0;
            }
            int ok = 0, fail = 0;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < jobs.Count; i++)
            {
                if (cancelRequested) { for (int k = i; k < jobs.Count; k++) jobs[k].Status = StCancelled; break; }
                var job = jobs[i];
                int index = i;
                if (!job.Probed && !string.IsNullOrEmpty(Runner.FFprobe)) Runner.Probe(job);

                bool seq = IsSequence(job, s);
                string outPath = MakeOutPath(job, s, seq);
                string seqDir = null, seqPattern = null, seqTemp = null;
                if (seqKind > 0)
                {
                    string bn = Path.GetFileNameWithoutExtension(job.Path);
                    string d0 = string.IsNullOrEmpty(sUser.OutDir) ? Path.GetDirectoryName(job.Path) : sUser.OutDir;
                    string tag = seqKind == 1 ? "_PNG" : "_JPG";
                    seqDir = Path.Combine(d0, bn + tag);
                    for (int k = 2; !sUser.Overwrite && Directory.Exists(seqDir) && Directory.GetFileSystemEntries(seqDir).Length > 0; k++) seqDir = Path.Combine(d0, bn + tag + " (" + k + ")");
                    seqPattern = Path.Combine(seqDir, bn + "_%06d." + (seqKind == 1 ? "png" : "jpg"));
                    seqTemp = Path.Combine(Path.GetTempPath(), "FFmpegForge-sequence-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".mkv");
                    outPath = seqTemp;
                }
                double expected; string err;
                string exe, args;
                Rtx.DriverPlan plan;
                BuildJobCommand(job, s, outPath, out exe, out args, out expected, out err, out plan);
                job.Output = outPath;
                if (s.DlssSkipped)
                    UI(() => Log("   ⚠ DLSS skipped: NVIDIA offers no DLSS for video files (it needs a game engine). RTX Video Super Resolution is applied instead.", Theme.Accent1));
                UI(() =>
                {
                    statusLbl.Text = "Converting " + (index + 1) + " / " + jobs.Count + "  —  " + Path.GetFileName(job.Path);
                    RefreshRow(job);
                });
                if (args == null)
                {
                    job.Status = StError; job.Error = err; fail++;
                    UI(() => { Log("✗ " + Path.GetFileName(job.Path) + ": " + err, Theme.Error); RefreshRow(job); });
                    continue;
                }
                bool srcHasPicture = job.Kind == MediaKind.Video || job.Kind == MediaKind.Image || CommandBuilder.IsAnimatedImageSource(job);
                bool pv = usePreview && srcHasPicture && s.Format.Kind == MediaKind.Video;
                // HDR display conversion is preview-only. If the selected FFmpeg lacks its
                // filters, skip preview rather than failing an RTX/AI export pipeline.
                if (pv && (job.IsHdr || s.SdrHdr > 0 || s.RtxHdr) &&
                    (!FilterCatalog.Has("zscale") || !FilterCatalog.Has("tonemap")))
                {
                    pv = false;
                    UI(() => Log("   HDR preview unavailable: selected FFmpeg needs zscale + tonemap; export continues unchanged.", Theme.Muted));
                }
                if (pv)
                {
                    PreviewFrames.Reset();
                    bool stillSrc = !(job.Kind == MediaKind.Video || CommandBuilder.IsAnimatedImageSource(job));
                    string title = (index + 1) + " / " + jobs.Count + " — " + Path.GetFileName(job.Path);
                    UI(() =>
                    {
                        if (previewWin == null || previewWin.IsDisposed) previewWin = new PreviewWindow();
                        previewWin.ShowNear(this);
                        previewWin.NewJob(title, stillSrc ? job.Path : null, null, null);
                        previewWin.SetStatus("converting…");
                    });
                }
                job.Status = StConverting; job.Started = DateTime.Now;
                UI(() => { Log("▶ " + Path.GetFileName(job.Path) + "  →  " + Path.GetFileName(outPath), Theme.Text); RefreshRow(job); });
                long lastUi = 0;
                var jsw = Stopwatch.StartNew();
                string lastErr = null;
                int code = 0;
                var rjob = job; var rs = s;
                double pBase = 0, pScale = 1;
                // ---- Deinterlacing (QTGMC, or FFmpeg bwdif when QTGMC is not installed): first stage, before every other effect
                AiDenoise.Stage diStage = null;
                if (s.Deint > 0 && s.Format.Kind == MediaKind.Video && job.Kind == MediaKind.Video && job.HasVideo && !Qtgmc.Wanted(job, s))
                    UI(() => Log("   Deinterlacing: " + Path.GetFileName(job.Path) + " is progressive (" + (string.IsNullOrEmpty(job.FieldOrder) ? "no field order" : job.FieldOrder) + ") — left as it is. Force TFF or BFF to deinterlace it anyway.", Theme.Muted));
                if (Qtgmc.Wanted(job, s) && !cancelRequested)
                {
                    bool q = Qtgmc.Ready;
                    string ordTxt = Qtgmc.OrderText(job, s);
                    bool plainPath = !q && !(s.Vision != null && s.Vision.Enabled) && !(s.Seed != null && s.Seed.Enabled) && !s.RtxActive && !s.DnEnabled && !s.CaiEnabled && s.Rotate == 0 && string.IsNullOrWhiteSpace(s.FilterComplex);
                    if (plainPath)
                    {
                        // no other stage: bwdif goes straight into the export's filter chain (no intermediate file)
                        rs = s.Clone();
                        rs.Filters = new List<FilterEntry>(s.Filters);
                        rs.Filters.Insert(0, new FilterEntry { Kind = 'V', Expr = Qtgmc.Bwdif(job, s) });
                        UI(() => Log("   Deinterlacing: QTGMC is not installed — FFmpeg bwdif (" + (s.Deint == 2 ? "same rate" : "double rate") + ", " + ordTxt + "). Install QTGMC in Video settings for the best quality.", Theme.Accent1));
                        double e2; string err2; string exe2, args2; Rtx.DriverPlan plan2;
                        BuildJobCommand(rjob, rs, outPath, out exe2, out args2, out e2, out err2, out plan2);
                        exe = exe2; args = args2; expected = e2; plan = plan2; err = err2;
                        if (args == null) { code = 3; lastErr = err2; }
                    }
                    else
                    {
                        string derr;
                        diStage = Qtgmc.Build(job, s, q, out derr);
                        if (diStage == null) { code = 3; lastErr = derr; }
                        else
                        {
                            try { Directory.CreateDirectory(diStage.TempDir); } catch { }
                            if (q) UI(() => Log("   Deinterlacing QTGMC (" + (s.Deint == 2 ? "same rate" : "double rate") + ", " + ordTxt + "): " + job.Width + "×" + job.Height + " — slow, best quality", Color.FromArgb(118, 185, 0)));
                            else UI(() => Log("   Deinterlacing: QTGMC is not installed — FFmpeg bwdif (" + (s.Deint == 2 ? "same rate" : "double rate") + ", " + ordTxt + "). Install QTGMC in Video settings for the best quality.", Theme.Accent1));
                            bool more = s.DnEnabled || s.CaiEnabled || s.RtxActive || (s.Vision != null && s.Vision.Enabled) || (s.Seed != null && s.Seed.Enabled);
                            double dShare = more ? 0.25 : 0.6;
                            try
                            {
                                code = Rtx.RunDriver(diStage.Plan,
                                    p =>
                                    {
                                        job.Progress = dShare * p;
                                        long now = jsw.ElapsedMilliseconds;
                                        if (now - lastUi < 120 && p < 1) return;
                                        lastUi = now;
                                        double ov = (index + job.Progress) / jobs.Count;
                                        UI(() => { overall.Value = ov; RefreshRow(job); });
                                    },
                                    line => UI(() => Log("   " + line, line.StartsWith("ForgeQTGMC") ? Color.FromArgb(118, 185, 0) : Color.FromArgb(200, 170, 110))),
                                    () => cancelRequested, out lastErr);
                            }
                            catch (Exception ex) { code = -2; lastErr = ex.Message; }
                            if (code == 0 && File.Exists(diStage.Intermediate))
                            {
                                rjob = new Job { Path = diStage.Intermediate };
                                Runner.Probe(rjob);
                                rs = s.Clone();
                                rs.TrimStart = -1; rs.TrimEnd = -1;
                                if (rs.Fps <= 0) rs.Fps = Qtgmc.ExactRate(rjob);   // MKV stores 59.94 as 19001/317: keep 60000/1001
                                double e2; string err2; string exe2, args2; Rtx.DriverPlan plan2;
                                BuildJobCommand(rjob, rs, outPath, out exe2, out args2, out e2, out err2, out plan2);
                                exe = exe2; args = args2; expected = e2; plan = plan2; err = err2;
                                pBase = dShare; pScale = 1 - dShare;
                                if (args == null) { code = 3; lastErr = err2; }
                            }
                            else if (code == 0) { code = 3; lastErr = "Deinterlacing did not write its result."; }
                        }
                    }
                }
                AiDenoise.Stage aiStage = null;
                bool aiWanted = s.DnEnabled && srcHasPicture && s.Format.Kind != MediaKind.Audio && job.Width > 0;
                if (aiWanted && !AiDenoise.ReadyFor(s))
                {
                    string why = AiDenoise.StatusFor(s.DnModel);
                    UI(() => Log("   Denoiser IA: " + why + " — using FFmpeg Forge's classic denoiser.", Theme.Accent1));
                }
                if (aiWanted && AiDenoise.ReadyFor(s) && code == 0 && !cancelRequested)
                {
                    string aerr;
                    aiStage = AiDenoise.Build(rjob, rs, s.Format.Kind == MediaKind.Image, out aerr);
                    if (aiStage != null)
                    {
                        try { Directory.CreateDirectory(aiStage.TempDir); } catch { }
                        if (pv)
                        {
                            aiStage.Plan.DecArgs = PreviewFrames.ForDecoder(aiStage.Plan.DecArgs, rs, aiStage.Seconds, rjob);
                            aiStage.Plan.EncArgs = PreviewFrames.ForEncoder(aiStage.Plan.EncArgs, rs, false, true, aiStage.Seconds, rjob);
                        }
                        var dnm = AiDenoise.FindModel(s.DnModel);
                        UI(() => Log("   Denoiser IA (" + (dnm.Key == "scunet" ? "SCUNet" : dnm.Arch + " · " + dnm.Key) + " + temporal stabilisation): " + job.Width + "×" + job.Height, Color.FromArgb(118, 185, 0)));
                        double aiStart = pBase, aiShare = pScale * (s.Format.Kind == MediaKind.Image ? 0.8 : 0.7);
                        try
                        {
                            code = Rtx.RunDriver(aiStage.Plan,
                                p =>
                                {
                                    job.Progress = aiStart + aiShare * p;
                                    long now = jsw.ElapsedMilliseconds;
                                    if (now - lastUi < 120 && p < 1) return;
                                    lastUi = now;
                                    double ov = (index + job.Progress) / jobs.Count;
                                    UI(() => { overall.Value = ov; RefreshRow(job); });
                                },
                                line => { if (pv && (line.Contains("deprecated pixel format") || line.Contains("Last message repeated"))) return; UI(() => Log("   " + line, line.StartsWith("ForgeAI") ? Color.FromArgb(118, 185, 0) : Color.FromArgb(200, 170, 110))); },
                                () => cancelRequested, out lastErr);
                        }
                        catch (Exception ex) { code = -2; lastErr = ex.Message; }
                        if (code == 0 && File.Exists(aiStage.Intermediate))
                        {
                            rjob = new Job { Path = aiStage.Intermediate };
                            Runner.Probe(rjob);
                            rs = rs.Clone();
                            rs.DnEnabled = false; rs.TrimStart = -1; rs.TrimEnd = -1;
                            double e2; string err2; string exe2, args2; Rtx.DriverPlan plan2;
                            BuildJobCommand(rjob, rs, outPath, out exe2, out args2, out e2, out err2, out plan2);
                            exe = exe2; args = args2; expected = e2; plan = plan2; err = err2;
                            pBase = aiStart + aiShare; pScale = 1 - pBase;
                            pv = false;     // keep the original / denoised comparison of the AI pass on screen
                            if (args == null) { code = 3; lastErr = err2; }
                        }
                        else if (code == 0) { code = 3; lastErr = "The AI Denoiser did not write its result."; }
                    }
                }
                AiDenoise.Stage caiStage = null;
                bool caiWanted = s.CaiEnabled && !string.IsNullOrEmpty(s.CaiModel) && srcHasPicture && s.Format.Kind != MediaKind.Audio && job.Width > 0;
                if (caiWanted && code == 0 && !cancelRequested)
                {
                    string cerr;
                    UI(() => Log("   Python / ONNX AI: loading " + s.CaiModel + " (" + CustomAi.Kind(s.CaiModel) + ")…", Color.FromArgb(118, 185, 0)));
                    caiStage = CustomAi.Build(rjob, rs, s.Format.Kind == MediaKind.Image, out cerr);
                    if (caiStage == null) { code = 3; lastErr = cerr; }
                    else
                    {
                        try { Directory.CreateDirectory(caiStage.TempDir); } catch { }
                        if (pv)
                        {
                            caiStage.Plan.DecArgs = PreviewFrames.ForDecoder(caiStage.Plan.DecArgs, rs, caiStage.Seconds, rjob);
                            caiStage.Plan.EncArgs = PreviewFrames.ForEncoder(caiStage.Plan.EncArgs, rs, false, true, caiStage.Seconds, rjob);
                        }
                        var cp = caiStage.Plan;
                        UI(() => Log("   Python / ONNX AI: " + s.CaiModel + "  ·  " + cp.W + "×" + cp.H + " → " + cp.OW + "×" + cp.OH + (s.CaiGpu ? "  ·  GPU" : "  ·  CPU"), Color.FromArgb(118, 185, 0)));
                        double cStart = pBase, cShare = pScale * (s.Format.Kind == MediaKind.Image ? 0.8 : 0.7);
                        try
                        {
                            code = Rtx.RunDriver(cp,
                                p =>
                                {
                                    job.Progress = cStart + cShare * p;
                                    long now = jsw.ElapsedMilliseconds;
                                    if (now - lastUi < 120 && p < 1) return;
                                    lastUi = now;
                                    double ov = (index + job.Progress) / jobs.Count;
                                    UI(() => { overall.Value = ov; RefreshRow(job); });
                                },
                                line => { if (pv && (line.Contains("deprecated pixel format") || line.Contains("Last message repeated"))) return; UI(() => Log("   " + line, line.StartsWith("ForgeAI") ? Color.FromArgb(118, 185, 0) : Color.FromArgb(200, 170, 110))); },
                                () => cancelRequested, out lastErr);
                        }
                        catch (Exception ex) { code = -2; lastErr = ex.Message; }
                        if (code == 0 && File.Exists(caiStage.Intermediate))
                        {
                            rjob = new Job { Path = caiStage.Intermediate };
                            Runner.Probe(rjob);
                            rs = rs.Clone();
                            rs.DnEnabled = false; rs.CaiEnabled = false; rs.TrimStart = -1; rs.TrimEnd = -1;
                            double e2; string err2; string exe2, args2; Rtx.DriverPlan plan2;
                            BuildJobCommand(rjob, rs, outPath, out exe2, out args2, out e2, out err2, out plan2);
                            exe = exe2; args = args2; expected = e2; plan = plan2; err = err2;
                            pBase = cStart + cShare; pScale = 1 - pBase;
                            pv = false;
                            if (args == null) { code = 3; lastErr = err2; }
                        }
                        else if (code == 0) { code = 3; lastErr = "The Python / ONNX model did not write its result."; }
                    }
                }
                if (code == 0 && !cancelRequested)
                try
                {
                    string dsdKey = rs.ACodec == "auto" ? rs.Format.ACodec : rs.ACodec;
                    if (s.Format.Kind == MediaKind.Audio && DsdEngine.IsDsd(dsdKey))
                    {
                        string derr;
                        var dp = DsdEngine.Build(rjob, rs, dsdKey, outPath, out derr);
                        if (dp.Requested != dp.Key) UI(() => Log("   ⚠ DSF holds up to 5.1 (Sony norm): this " + dp.Channels + "-channel mix is written as .dff (DSDIFF).", Theme.Accent1));
                        UI(() => Log("   DSD: " + (dp.Rate == 5644800 ? "DSD128 5.6448 MHz" : "DSD64 2.8224 MHz") + " · " + dp.Channels + " ch (" + string.Join(" ", dp.Names) + ") · 1-bit 7th-order delta-sigma → " + (dp.Dff ? "DFF (DSDIFF)" : "DSF"), Color.FromArgb(118, 185, 0)));
                        code = DsdEngine.Run(dp,
                            p =>
                            {
                                job.Progress = pBase + pScale * p;
                                long now = jsw.ElapsedMilliseconds;
                                if (now - lastUi < 120 && p < 1) return;
                                lastUi = now;
                                double ov = (index + job.Progress) / jobs.Count;
                                UI(() => { overall.Value = ov; RefreshRow(job); });
                            },
                            line => UI(() => Log("   " + line, Color.FromArgb(200, 170, 110))),
                            () => cancelRequested, out lastErr);
                    }
                    else if (s.Vision != null && s.Vision.Enabled && s.Format.Kind != MediaKind.Audio)
                    {
                        string verr;
                        var vp = VisionEngine.Build(rjob, rs, outPath, out verr);
                        Action<double> vprog = p =>
                        {
                            job.Progress = pBase + pScale * p;
                            long now = jsw.ElapsedMilliseconds;
                            if (now - lastUi < 120 && p < 1) return;
                            lastUi = now;
                            double ov = (index + job.Progress) / jobs.Count;
                            UI(() => { overall.Value = ov; RefreshRow(job); });
                        };
                        Action<string> vlog = line => { if (pv && (line.Contains("deprecated pixel format") || line.Contains("Last message repeated"))) return; UI(() => Log("   " + line, line.StartsWith("VisionNext ") || line.StartsWith("ForgeVN") || line.StartsWith("RazorNext ") || line.StartsWith("ForgeRN") ? Color.FromArgb(118, 185, 0) : Color.FromArgb(200, 170, 110))); };
                        if (vp.Driver != null)
                        {
                            // built-in engine: streamed decoder -> VOSR 2.0 -> final encoder (no picture files, one encode)
                            if (pv)
                            {
                                vp.Driver.DecArgs = PreviewFrames.ForDecoder(vp.Driver.DecArgs, s, vp.Expected, rjob);
                                vp.Driver.EncArgs = PreviewFrames.ForEncoder(vp.Driver.EncArgs, s, false, false, vp.Expected, rjob);
                            }
                            UI(() => Log("   Upscale " + (s.Vision.Razor ? "RazorNext (built-in RelayVSR, " : "VisionNext (built-in VOSR 2.0, ") + (s.Vision.UseGpu ? "GPU" : "CPU") + ", streamed) ×" + s.Vision.Scale + (s.Vision.Razor ? "" : " · tile " + s.Vision.Tile) + "  ·  " + vp.InW + "×" + vp.InH + " → " + vp.OutW + "×" + vp.OutH, Color.FromArgb(118, 185, 0)));
                            try { string od = Path.GetDirectoryName(outPath); if (!string.IsNullOrEmpty(od)) Directory.CreateDirectory(od); } catch { }
                            code = Rtx.RunDriver(vp.Driver, vprog, vlog, () => cancelRequested, out lastErr);
                        }
                        else
                        {
                            if (pv)
                            {
                                vp.EncArgs = PreviewFrames.ForEncoder(vp.EncArgs, s, srcHasPicture && job.Kind == MediaKind.Video, false, vp.Expected, rjob);
                                if (vp.Video && !vp.Streamed) { var vin = vp.VeIn; var vout = vp.VeOut; UI(() => { if (previewWin != null && !previewWin.IsDisposed) previewWin.SetFolders(vin, vout); }); }
                            }
                            UI(() => Log("   Upscale " + s.Vision.Name + " ×" + s.Vision.Scale + (s.Vision.Razor ? " · GPU memory " + s.Vision.MemBudget : " · tile " + s.Vision.Tile) + "  ·  " + vp.InW + "×" + vp.InH + " → " + vp.OutW + "×" + vp.OutH, Color.FromArgb(118, 185, 0)));
                            code = VisionEngine.Run(vp, vprog, vlog, () => cancelRequested, out lastErr);
                        }
                    }
                    else if (s.Seed != null && s.Seed.Enabled && s.Format.Kind != MediaKind.Audio)
                    {
                        string serr;
                        var sp = SeedEngine.Build(rjob, rs, outPath, out serr);
                        if (pv) sp.EncArgs = PreviewFrames.ForEncoder(sp.EncArgs, s, job.Kind == MediaKind.Video, false, sp.Expected, rjob);
                        UI(() => Log("   SeedVR2: " + SeedEngine.Short(sp.Script == null ? "" : s.Seed.Model) + "  ·  " + sp.InW + "×" + sp.InH + " → short side " + sp.OutShort, Color.FromArgb(118, 185, 0)));
                        code = SeedEngine.Run(sp,
                            p =>
                            {
                                job.Progress = pBase + pScale * p;
                                long now = jsw.ElapsedMilliseconds;
                                if (now - lastUi < 120 && p < 1) return;
                                lastUi = now;
                                double ov = (index + job.Progress) / jobs.Count;
                                UI(() => { overall.Value = ov; RefreshRow(job); });
                            },
                            line => { if (pv && (line.Contains("deprecated pixel format") || line.Contains("Last message repeated"))) return; UI(() => Log("   " + line, line.StartsWith("SeedVR2 ") ? Color.FromArgb(118, 185, 0) : Color.FromArgb(200, 170, 110))); },
                            () => cancelRequested, out lastErr);
                    }
                    else if (plan != null)
                    {
                        var pl = plan;
                        if (pv) plan.EncArgs = PreviewFrames.ForEncoder(plan.EncArgs, s, true, true, plan.Expected, rjob);
                        UI(() => Log("   RTX driver engine: " + pl.W + "×" + pl.H + " → " + pl.OW + "×" + pl.OH + (s.RtxHdr ? "  ·  HDR10" : ""), Theme.Muted));
                        code = Rtx.RunDriver(plan,
                            p =>
                            {
                                job.Progress = pBase + pScale * p;
                                long now = jsw.ElapsedMilliseconds;
                                if (now - lastUi < 120 && p < 1) return;
                                lastUi = now;
                                double ov = (index + job.Progress) / jobs.Count;
                                UI(() => { overall.Value = ov; RefreshRow(job); });
                            },
                            line => { if (pv && (line.Contains("deprecated pixel format") || line.Contains("Last message repeated"))) return; UI(() => Log("   " + line, line.StartsWith("ForgeRTX") ? Color.FromArgb(118, 185, 0) : Color.FromArgb(200, 170, 110))); },
                            () => cancelRequested, out lastErr);
                    }
                    else
                    {
                        bool withPv = pv && exe == Runner.FFmpeg;
                        string runArgs = withPv ? PreviewFrames.ForCommand(args, s, expected, rjob) : args;
                        Action<double> prog = p =>
                        {
                            job.Progress = pBase + pScale * p;
                            long now = jsw.ElapsedMilliseconds;
                            if (now - lastUi < 120 && p < 1) return;
                            lastUi = now;
                            double ov = (index + job.Progress) / jobs.Count;
                            UI(() => { overall.Value = ov; RefreshRow(job); });
                        };
                        Action<string> logLine = line => { if (line.Contains("Guessed Channel Layout") || line.Contains("before.jpg") || line.Contains("after.jpg") || (withPv && (line.Contains("deprecated pixel format") || line.Contains("Last message repeated")))) return; UI(() => Log("   " + line, Color.FromArgb(200, 170, 110))); };
                        code = Runner.Run(exe, runArgs, expected, prog, logLine, () => cancelRequested, out lastErr);
                        if (code != 0 && code != -1 && withPv && !cancelRequested)
                        {
                            // never let the preview break an export: run again without it
                            UI(() => Log("   (preview unavailable for this file — converting without it)", Theme.Muted));
                            code = Runner.Run(exe, args, expected, prog, logLine, () => cancelRequested, out lastErr);
                        }
                    }
                }
                catch (Exception ex) { code = -2; lastErr = ex.Message; }

                if (code == 0 && seqKind > 0 && !cancelRequested)
                {
                    // write the frames of the lossless render as numbered pictures
                    try
                    {
                        Directory.CreateDirectory(seqDir);
                        var tj = new Job { Path = seqTemp }; Runner.Probe(tj);
                        string fmtName = seqKind == 1 ? "PNG" : "JPG";
                        string sd = seqDir;
                        UI(() => Log("   Image sequence (" + fmtName + "): " + tj.Width + "×" + tj.Height + " → " + sd, Color.FromArgb(118, 185, 0)));
                        string vfx = "scale=in_color_matrix=auto:in_range=auto:out_range=pc:flags=bicubic+accurate_rnd+full_chroma_int,format=" +
                                     (seqKind == 1 ? (seqHi ? "rgb48be" : "rgb24") : "yuvj444p");
                        string sargs = "-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats -i \"" + seqTemp + "\" -map 0:v:0 -an -sn -dn -vf \"" + vfx + "\" " +
                                       (seqKind == 1 ? "-c:v png -pred mixed" : "-c:v mjpeg -q:v 2 -qmin 1 -huffman optimal") + " -start_number 1 -f image2 \"" + seqPattern + "\"";
                        UI(() => statusLbl.Text = "Writing " + fmtName + " frames…  —  " + Path.GetFileName(job.Path));
                        code = Runner.Run(Runner.FFmpeg, sargs, tj.Duration, p => { }, line => UI(() => Log("   " + line, Color.FromArgb(200, 170, 110))), () => cancelRequested, out lastErr);
                        if (code == 0)
                        {
                            var files = Directory.GetFiles(seqDir, "*." + (seqKind == 1 ? "png" : "jpg")).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray();
                            long bytes = files.Sum(f => new FileInfo(f).Length);
                            int nfr = files.Length;
                            UI(() => Log("   " + nfr + " " + fmtName + " frames written (" + FmtSize(bytes) + ")", Theme.Success));
                            if (nfr == 0) { code = 3; lastErr = "No frame was written."; }
                            else { outPath = files[0]; job.Output = files[0]; }
                        }
                    }
                    catch (Exception ex) { code = -2; lastErr = ex.Message; }
                }
                if (seqTemp != null) try { if (File.Exists(seqTemp)) File.Delete(seqTemp); } catch { }
                if (code == 0 && seqKind == 0 && !cancelRequested && s.SdrHdr == Hdr.DolbyVision81 && s.Format.Kind == MediaKind.Video && File.Exists(outPath))
                {
                    // Dolby Vision 8.1: dynamic metadata measured on the HDR10 render, RPU injected, dvcC written
                    UI(() => statusLbl.Text = "Writing Dolby Vision 8.1 metadata…  —  " + Path.GetFileName(job.Path));
                    string dverr;
                    bool dvok = Hdr.MakeDolbyVision(outPath, s.HdrPeak, m => UI(() => Log("   " + m, Color.FromArgb(118, 185, 0))), p => { }, () => cancelRequested, out dverr);
                    if (dvok) UI(() => Log("   Dolby Vision profile 8.1 written (HDR10 base layer + dynamic metadata).", Theme.Success));
                    else if (cancelRequested) code = -1;
                    else { code = 3; lastErr = "Dolby Vision: " + dverr + " (the HDR10 file is kept: " + Path.GetFileName(outPath) + ")"; }
                }
                if (code == 0 && seqKind == 0 && !cancelRequested && sUser.CoverMode > 0 && sUser.Format.Kind == MediaKind.Video && !seq)
                {
                    string cerr2, cext = Path.GetExtension(outPath).TrimStart('.').ToLowerInvariant();
                    if (!Cover.Supported(cext)) UI(() => Log("   Cover / thumbnail: not supported for ." + cext + " files (MP4, M4V and MKV only) — skipped.", Theme.Accent1));
                    else if (Cover.Apply(outPath, sUser, out cerr2)) UI(() => Log("   Cover / thumbnail added (" + (cext == "mkv" ? "cover.jpg attachment" : "attached picture") + ").", Theme.Success));
                    else UI(() => Log("   Cover / thumbnail could not be added: " + cerr2 + " (the video itself is fine).", Theme.Accent1));
                }
                if (code == 0 && seqKind == 0 && caiStage == null && aiStage == null && plan == null && exe == Runner.FFmpeg && s.Format.Kind == MediaKind.Video && (s.Vision == null || !s.Vision.Enabled) && (s.Seed == null || !s.Seed.Enabled))
                {
                    string vk = s.VCodec == "auto" || string.IsNullOrEmpty(s.VCodec) ? s.Format.VCodec : s.VCodec;
                    Compression.Learn(vk, s.PresetIdx, job.Width, job.Height, expected, jsw.Elapsed.TotalSeconds);
                }
                if (code == 0)
                {
                    job.Status = StDone; job.Progress = 1; ok++;
                    lastOutput = seq ? outPath.Replace("%05d", "00001") : outPath;
                    if (seq) job.Output = lastOutput;
                    string size = "";
                    try { if (File.Exists(job.Output)) size = "  (" + FmtSize(new FileInfo(job.Output).Length) + ")"; } catch { }
                    double secs = jsw.Elapsed.TotalSeconds;
                    UI(() => Log("✓ Done in " + secs.ToString("0.0", CultureInfo.InvariantCulture) + " s" + size, Theme.Success));
                }
                else if (code == -1 || cancelRequested)
                {
                    job.Status = StCancelled;
                    try { if (!seq && File.Exists(outPath)) File.Delete(outPath); } catch { }
                }
                else
                {
                    job.Status = StError; job.Error = lastErr; fail++;
                    try { if (!seq && File.Exists(outPath) && new FileInfo(outPath).Length == 0) File.Delete(outPath); } catch { }
                    string hint = Hint(lastErr);
                    UI(() =>
                    {
                        Log("✗ Failed (code " + code + ")" + (string.IsNullOrEmpty(lastErr) ? "" : ":"), Theme.Error);
                        if (!string.IsNullOrEmpty(lastErr)) foreach (var l in lastErr.Split('\n').Reverse().Take(5).Reverse()) Log("   " + l.TrimEnd(), Theme.Error);
                        if (hint != null) Log("   → " + hint, Theme.Accent1);
                    });
                }
                AiDenoise.Cleanup(aiStage);
                AiDenoise.Cleanup(caiStage);
                AiDenoise.Cleanup(diStage);
                if (usePreview) { int c2 = code; UI(() => { if (previewWin != null && !previewWin.IsDisposed) previewWin.SetStatus(c2 == 0 ? "done ✓" : (c2 == -1 ? "cancelled" : "failed")); }); }
                UI(() => RefreshRow(job));
            }
            double total = sw.Elapsed.TotalSeconds;
            // the preview window closes by itself when the export is finished
            UI(() =>
            {
                if (previewWin != null && !previewWin.IsDisposed && previewWin.Visible)
                {
                    var tm = new System.Windows.Forms.Timer { Interval = 1200 };
                    var pw = previewWin;
                    tm.Tick += (o2, e2) => { tm.Stop(); tm.Dispose(); try { if (!pw.IsDisposed) pw.Close(); } catch { } };
                    tm.Start();
                }
            });
            UI(() =>
            {
                running = false;
                btnConvert.Enabled = true; btnCancel.Enabled = false;
                overall.Value = cancelRequested ? overall.Value : 1;
                string msg = (cancelRequested ? "Conversion cancelled. " : "Finished! ") + ok + " succeeded" + (fail > 0 ? ", " + fail + " failed" : "") + " — " + FmtDur(total);
                statusLbl.Text = msg;
                Log(msg, fail > 0 ? Theme.Accent1 : Theme.Success);
                list.Invalidate();
                if (!cancelRequested) System.Media.SystemSounds.Asterisk.Play();
            });
        }

        static string Hint(string err)
        {
            if (string.IsNullOrEmpty(err)) return null;
            string e = err.ToLowerInvariant();
            if (e.Contains("tritonmissing") || e.Contains("working triton"))
                return "PyTorch tried to compile the model with Triton (not available on Windows): this FFmpeg Forge version runs VisionNext without it — retry the export.";
            if (e.Contains("forgevn") || e.Contains("forge_visionnext"))
                return null;
            if (e.Contains("forgern") || e.Contains("forge_razornext") || e.Contains("relayvsr"))
                return e.Contains("out of memory") || e.Contains("outofmemory")
                    ? "RazorNext ran out of GPU memory: its output is computed at ×4 — use a smaller source (720p / 1080p), or the Visual Enhancer engine with a lower GPU memory budget."
                    : e.Contains("checkpoint not found") || e.Contains("no module named") ? "The RazorNext installation is incomplete: click \"Install RazorNext model\" again." : null;
            if (e.Contains("razornext") && e.Contains("model") && (e.Contains("missing") || e.Contains("not installed")))
                return "Install the RazorNext model once in Visual Enhancer: Models → RazorNext → Download (or use the Built-in engine).";
            if (e.Contains("visionnext") && (e.Contains("model package is missing") || e.Contains("visionnext model is not installed")))
                return "Install the VisionNext model once in Visual Enhancer: Models → VisionNext → Download.";
            if (e.Contains("out of memory") || e.Contains("outofmemory"))
                return "SeedVR2 ran out of VRAM: pick a lighter model (FP8 / GGUF), a smaller batch size, raise BlockSwap or tick VAE tiling / Offload.";
            if (e.Contains("no module named") || e.Contains("modulenotfounderror"))
                return "The SeedVR2 Python environment is incomplete: click \"Install SeedVR2…\" again.";
            if (e.Contains("cuda") && (e.Contains("not available") || e.Contains("no cuda gpus") || e.Contains("no kernel image")))
                return "PyTorch cannot use your NVIDIA GPU: update the NVIDIA driver, then reinstall SeedVR2.";
            if (e.Contains("vulkan") || e.Contains("libplacebo") || e.Contains("failed creating vulkan") || e.Contains("device creation failed"))
                return "The GPU path (Vulkan / libplacebo) could not start. Update your graphics driver, or untick \"Optimized\" / pick a CPU scale type (xBR, Integer, Sharp bilinear, BCAS, Lanczos).";
            if (e.Contains("no such filter")) return "This filter is not included in your FFmpeg build. Pick it from the Filter library to see what is available.";
            if (e.Contains("error parsing") || e.Contains("error initializing filter") || e.Contains("option not found") || e.Contains("error applying option")) return "A filter parameter is invalid. Double-click the filter to edit it, or check its documentation.";
            if (e.Contains("cannot find a matching stream") || e.Contains("invalid stream specifier")) return "The filtergraph refers to a stream that does not exist (e.g. [1:v] without an extra input).";
            if (e.Contains("filtergraph") && e.Contains("was specified through")) return "A filter was used both in the chain and the filtergraph for the same stream. Use one or the other.";
            if (e.Contains("unknown encoder") || e.Contains("encoder not found")) return "This codec is not included in your FFmpeg build. Choose another codec (or use a \"full\" build).";
            if (e.Contains("nvenc") || e.Contains("cuda") || e.Contains("no capable devices")) return "NVIDIA GPU encoder unavailable: choose a CPU codec (H.264 / H.265).";
            if (e.Contains("qsv") || e.Contains("mfx")) return "Intel QuickSync is unavailable on this PC: choose a CPU codec.";
            if (e.Contains("amf")) return "AMD encoder unavailable on this PC: choose a CPU codec.";
            if (e.Contains("codec tag") || e.Contains("tag found for codec")) return "This encoder is not supported by this container. Pick a container that accepts it (MKV, MOV, NUT, AVI…) or its native format in the list.";
            if (e.Contains("could not find tag") || e.Contains("not currently supported in container") || e.Contains("codec not currently supported")) return "This codec is not compatible with this container. Set the codec back to “Auto”.";
            if (e.Contains("permission denied")) return "Access denied: check the destination folder or close the file if it is open.";
            if (e.Contains("does not contain any stream") || e.Contains("matches no streams")) return "The file does not contain the requested stream type.";
            return null;
        }

        void UI(Action a)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(a); } catch { }
        }

        void Log(string text, Color c)
        {
            log.Add(text, c);
        }

        static string FmtSize(long b)
        {
            if (b <= 0) return "";
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = b; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + u[i];
        }

        static string FmtDur(double s)
        {
            if (s <= 0) return "—";
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        // ---------- Upscale VisionNext ----------
        void BuildVisionSection(Control stack)
        {
            secVision = Section("AI UPSCALE  (VISIONNEXT · RAZORNEXT)");
            chkVision = Check("AI Upscale — VisionNext or RazorNext model (video and image)");
            AddRow(secVision, null, chkVision);
            // 2.2.0: model choice first — VisionNext (VOSR 2.0, per picture) or RazorNext (RelayVSR, temporal video SR)
            cbVisionModel = Combo(VisionEngine.Models.Cast<object>().ToArray());
            visionRows.Add(AddRow(secVision, "   Model", cbVisionModel)); visionRows.Add(cbVisionModel);
            tips.SetToolTip(cbVisionModel, "VisionNext: VOSR 2.0 (CVPR 2026), one picture at a time, tiles 256 / 512.\n" +
                "RazorNext: RelayVSR — real-world VIDEO super-resolution (native ×4): Wan keyframes + a fast decoder for the frames\n" +
                "in between, so detail stays stable from frame to frame (no flicker). Pictures work too (one-frame stream).");
            chkVisionGpu = Check("GPU (NVIDIA CUDA) — fast");
            chkVisionCpu = Check("CPU — slower, no NVIDIA card");
            visionRows.Add(AddRow(secVision, "   Processor", chkVisionGpu)); visionRows.Add(chkVisionGpu);
            visionRows.Add(AddRow(secVision, "", chkVisionCpu)); visionRows.Add(chkVisionCpu);
            cbVisionEngine = Combo("Built-in (no Visual Enhancer)", "Visual Enhancer (VE_CLI.exe)");
            cbVisionEngine.DropDownWidth = Theme.S(320);
            visionRows.Add(AddRow(secVision, "   Engine", cbVisionEngine)); visionRows.Add(cbVisionEngine);
            var bInst = Btn("Install VisionNext model…", 0); bInst.Dock = DockStyle.Fill; bInst.Height = Theme.S(30); bInst.Font = fSmall;
            bInst.Click += (s2, e2) => { if (cbVisionModel.SelectedIndex == 1) InstallRazorBuiltin(); else InstallVisionBuiltin(); };
            AddRow(secVision, null, bInst); visionRows.Add(bInst); visionInstallBtn = bInst;
            chkVisionGpu.Checked = true;
            chkVisionGpu.CheckedChanged += (s2, e2) => { if (chkVisionGpu.Checked) chkVisionCpu.Checked = false; else if (!chkVisionCpu.Checked) chkVisionCpu.Checked = true; };
            chkVisionCpu.CheckedChanged += (s2, e2) =>
            {
                if (chkVisionCpu.Checked) chkVisionGpu.Checked = false; else if (!chkVisionGpu.Checked) chkVisionGpu.Checked = true;
                // CPU mode is only possible with the built-in engine (Visual Enhancer always runs on the GPU)
                if (chkVisionCpu.Checked && cbVisionEngine.SelectedIndex != 0) cbVisionEngine.SelectedIndex = 0;
                UpdateVisionRows();
            };
            cbVisionEngine.SelectedIndexChanged += (s2, e2) => { if (cbVisionEngine.SelectedIndex != 0 && chkVisionCpu.Checked) chkVisionGpu.Checked = true; };
            tips.SetToolTip(chkVisionGpu, "Runs the VisionNext model on your NVIDIA graphics card (CUDA): by far the fastest.");
            tips.SetToolTip(chkVisionCpu, "Runs the VisionNext model on the processor. Works without an NVIDIA card,\nbut a 1.4-billion-parameter model is very slow on a CPU (minutes per frame).");
            tips.SetToolTip(cbVisionEngine, "Built-in: FFmpeg Forge runs VisionNext's model (VOSR 2.0, Apache-2.0) itself with Python / PyTorch on your NVIDIA GPU.\nThe model is installed in FFmpeg Forge's folder (VisionNext\\). Visual Enhancer is not needed.\nVisual Enhancer: uses your Visual Enhancer installation (VE_CLI.exe, DirectML).");
            var bDir = Btn("Visual Enhancer folder…", 0); bDir.Dock = DockStyle.Fill; bDir.Height = Theme.S(30); bDir.Font = fSmall;
            bDir.Click += (s2, e2) => ChooseVisionDir();
            var bApp = Btn("Open Visual Enhancer (Models)", 0); bApp.Dock = DockStyle.Fill; bApp.Height = Theme.S(30); bApp.Font = fSmall;
            bApp.Click += (s2, e2) =>
            {
                string a = VisionEngine.AppPath;
                if (a == null) { Log("Visual Enhancer.exe not found — choose its folder first.", Theme.Accent1); return; }
                try { Process.Start(new ProcessStartInfo(a) { WorkingDirectory = Path.GetDirectoryName(a), UseShellExecute = true }); } catch (Exception ex) { Log(ex.Message, Theme.Error); }
            };
            var bar = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            bar.Controls.Add(bDir, 0, 0); bar.Controls.Add(bApp, 1, 0);
            AddRow(secVision, null, bar); visionRows.Add(bar); visionVeBar = bar;
            lblVisionStatus = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)) };
            AddRow(secVision, null, lblVisionStatus); visionRows.Add(lblVisionStatus);
            cbVisionScale = Combo("×1 (same size, AI clean-up)", "×2", "×3", "×4", "×8");
            cbVisionScale.SelectedIndex = 1;
            visionRows.Add(AddRow(secVision, "   Upscale factor", cbVisionScale)); visionRows.Add(cbVisionScale);
            cbVisionTile = Combo("256", "512");
            cbVisionTile.SelectedIndex = 1;
            visionTileLbl = AddRow(secVision, "   Tile size", cbVisionTile);
            visionRows.Add(visionTileLbl); visionRows.Add(cbVisionTile);
            cbVisionMem = Combo("Automatic", "Unlimited", "12 GB", "8 GB", "6 GB");
            visionMemLbl = AddRow(secVision, "   GPU memory", cbVisionMem);
            visionRows.Add(visionMemLbl); visionRows.Add(cbVisionMem);
            tips.SetToolTip(cbVisionMem, "RazorNext with Visual Enhancer (--memory-budget): a lower budget uses less GPU memory\nbut takes longer and uses more system memory. The built-in engine manages memory itself.");
            lblVisionInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
            AddRow(secVision, null, lblVisionInfo); visionRows.Add(lblVisionInfo);
            tips.SetToolTip(chkVision, "AI upscaling with VisionNext (VOSR 2.0) or RazorNext (RelayVSR): built-in (FFmpeg Forge runs the model itself, no Visual Enhancer needed)\nor through your Visual Enhancer installation (VE_CLI.exe).\nYour format, codec, audio, trim, Denoiser and filters are kept.");
            tips.SetToolTip(cbVisionScale, "×1 keeps the size and only restores the picture; ×2 / ×3 / ×4 / ×8 multiply width and height.\nVisionNext: ×3, ×4 and ×8 need the Built-in engine and take much longer (the work grows with the output size).\nRazorNext: native ×4 (×1 / ×2 / ×3 are computed at ×4 then resized); ×8 needs Visual Enhancer, ×3 the Built-in engine.");
            tips.SetToolTip(cbVisionTile, "The picture is processed in tiles of this size.\n512: better quality, more VRAM.  256: less VRAM, a little slower.");
            EventHandler upd = (s2, e2) => UpdateVisionRows();
            chkVision.CheckedChanged += upd; cbVisionEngine.SelectedIndexChanged += upd; cbVisionScale.SelectedIndexChanged += upd; cbVisionTile.SelectedIndexChanged += upd;
            cbVisionModel.SelectedIndexChanged += upd; cbVisionMem.SelectedIndexChanged += upd;
            stack.Controls.Add(secVision);
            UpdateVisionRows();
        }

        // ---------- Python / ONNX AI ----------
        void BuildCustomAiSection(Control stack)
        {
            secCai = Section("PYTHON / ONNX AI  (YOUR OWN MODELS)");
            chkCai = Check("Use my AI model on the export (videos and images)");
            AddRow(secCai, null, chkCai);
            cbCaiModel = Combo();
            cbCaiModel.DropDownWidth = Theme.S(380);
            caiRows.Add(AddRow(secCai, "   Model", cbCaiModel)); caiRows.Add(cbCaiModel);
            var bImp = Btn("Import model…", 0); bImp.Primary = true;
            var bRem = Btn("Remove", 0);
            var bFol = Btn("Open models folder", 0);
            var bTpl = Btn("Python template", 0);
            var bar = new TableLayoutPanel { ColumnCount = 4, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, Theme.S(4), 0, Theme.S(4)), BackColor = Theme.Panel };
            for (int i = 0; i < 4; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            int bi = 0;
            foreach (var b in new[] { bImp, bRem, bFol, bTpl }) { b.Dock = DockStyle.Fill; b.Height = Theme.S(32); b.Font = fSmall; b.Margin = new Padding(0, 0, Theme.S(6), 0); bar.Controls.Add(b, bi++, 0); }
            AddRow(secCai, null, bar); caiRows.Add(bar);
            cbCaiSize = Combo("Model scale (upscalers enlarge the picture)", "Keep the original size");
            caiRows.Add(AddRow(secCai, "   Output size", cbCaiSize)); caiRows.Add(cbCaiSize);
            cbCaiProc = Combo("GPU (NVIDIA CUDA) — fast", "CPU — slower, no NVIDIA card");
            caiRows.Add(AddRow(secCai, "   Processor", cbCaiProc)); caiRows.Add(cbCaiProc);
            cbCaiPrec = Combo("Half precision fp16 (fast)", "Full precision fp32 (exact)");
            caiRows.Add(AddRow(secCai, "   Precision", cbCaiPrec)); caiRows.Add(cbCaiPrec);
            cbCaiTile = Combo("Auto (from GPU memory)", "256", "512", "1024", "Whole frame");
            caiRows.Add(AddRow(secCai, "   Tile size", cbCaiTile)); caiRows.Add(cbCaiTile);
            lblCaiRuntime = new Label { Text = "", Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(6), 0, Theme.S(2)) };
            AddRow(secCai, null, lblCaiRuntime); caiRows.Add(lblCaiRuntime);
            var bRt = Btn("Install AI runtime…  (PyTorch CUDA, ONNX Runtime GPU, spandrel)", 0); bRt.Dock = DockStyle.Fill; bRt.Height = Theme.S(32); bRt.Font = fSmall;
            AddRow(secCai, null, bRt); caiRuntimeBtn = bRt; caiRows.Add(bRt);
            lblCaiInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(6), 0, 0) };
            AddRow(secCai, null, lblCaiInfo); caiRows.Add(lblCaiInfo);
            tips.SetToolTip(chkCai, "Runs your model on every frame of the export, before Scale, RTX, SeedVR2 and VisionNext.\nYour format, codec, audio, trim and filters are kept.");
            tips.SetToolTip(bImp, "ONNX (.onnx), PyTorch (.pth .pt .ckpt .safetensors .bin — ESRGAN, SwinIR, HAT, DAT, SPAN, SCUNet, NAFNet, Restormer…)\nor a Python script (.py) with load(device) or process(img). The file is copied into FFmpeg Forge's models\\custom folder.");
            tips.SetToolTip(cbCaiTile, "Big pictures are processed in tiles that fit the GPU memory (blended, no seams).\nONNX models with a fixed input size always use their own size.");
            bImp.Click += (s2, e2) =>
            {
                using (var d = new OpenFileDialog { Title = "Import an AI model", Filter = "AI models (*.onnx;*.pth;*.pt;*.ckpt;*.safetensors;*.bin;*.py)|*.onnx;*.pth;*.pt;*.ckpt;*.safetensors;*.bin;*.py|All files (*.*)|*.*", Multiselect = true })
                {
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    string last = null;
                    foreach (var f in d.FileNames)
                        try { last = CustomAi.Import(f); Log("Python / ONNX AI: imported " + last + " (" + CustomAi.Kind(last) + ")", Theme.Success); }
                        catch (Exception ex) { Log("Could not import " + Path.GetFileName(f) + ": " + ex.Message, Theme.Error); }
                    RefreshCaiModels(last);
                    if (last != null) chkCai.Checked = true;
                }
            };
            bRem.Click += (s2, e2) =>
            {
                string m = cbCaiModel.SelectedItem as string;
                if (m == null) return;
                if (MessageBox.Show(this, "Remove " + m + " from FFmpeg Forge's model library?", "Python / ONNX AI", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                try { CustomAi.Remove(m); Log("Removed " + m, Theme.Muted); } catch (Exception ex) { Log(ex.Message, Theme.Error); }
                RefreshCaiModels(null);
            };
            bFol.Click += (s2, e2) => { try { Process.Start(new ProcessStartInfo(CustomAi.LibraryDir) { UseShellExecute = true }); } catch (Exception ex) { Log(ex.Message, Theme.Error); } };
            bTpl.Click += (s2, e2) =>
            {
                try
                {
                    string n = CustomAi.WriteTemplate();
                    RefreshCaiModels(n);
                    Log("Python template written: " + Path.Combine(CustomAi.LibraryDir, n) + " — edit it to plug in your own network.", Theme.Success);
                    try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Path.Combine(CustomAi.LibraryDir, n) + "\"") { UseShellExecute = true }); } catch { }
                }
                catch (Exception ex) { Log(ex.Message, Theme.Error); }
            };
            bRt.Click += (s2, e2) => InstallCaiRuntime();
            EventHandler upd = (s2, e2) => UpdateCaiRows();
            chkCai.CheckedChanged += upd; cbCaiModel.SelectedIndexChanged += upd; cbCaiSize.SelectedIndexChanged += upd; cbCaiProc.SelectedIndexChanged += upd;
            RefreshCaiModels(null);
            stack.Controls.Add(secCai);
        }

        void RefreshCaiModels(string select)
        {
            string cur = select ?? (cbCaiModel.SelectedItem as string);
            cbCaiModel.BeginUpdate();
            cbCaiModel.Items.Clear();
            foreach (var m in CustomAi.Models()) cbCaiModel.Items.Add(m);
            cbCaiModel.EndUpdate();
            int i = cur != null ? cbCaiModel.Items.IndexOf(cur) : -1;
            if (i < 0 && cbCaiModel.Items.Count > 0) i = 0;
            if (i >= 0) cbCaiModel.SelectedIndex = i;
            UpdateCaiRows();
        }

        void UpdateCaiRows()
        {
            using (UiLayoutBatch.Begin(secCai))
            {
            if (secCai == null) return;
            foreach (var c in caiRows) if (c != null) c.Visible = chkCai.Checked;
            if (!chkCai.Checked) return;
            string m = cbCaiModel.SelectedItem as string;
            string miss = m == null ? null : CustomAi.Missing(m);
            bool rtOk = AiDenoise.Python != null && CustomAi.HasOnnx && AiDenoise.HasSpandrel;
            lblCaiRuntime.Text = rtOk ? "AI runtime ready: PyTorch, ONNX Runtime and spandrel (" + AiDenoise.Root + ")."
                                      : (miss ?? "AI runtime not complete: click \"Install AI runtime…\" once (PyTorch CUDA, ONNX Runtime GPU, spandrel).");
            lblCaiRuntime.ForeColor = rtOk || (m != null && miss == null) ? Theme.Success : Theme.Accent1;
            ((Control)caiRuntimeBtn).Text = rtOk ? "Reinstall / repair AI runtime…" : "Install AI runtime…  (PyTorch CUDA, ONNX Runtime GPU, spandrel)";
            lblCaiInfo.Text = m == null
                ? "No model yet: click \"Import model…\". Supported: ONNX (.onnx), PyTorch (.pth .pt .ckpt .safetensors .bin — ESRGAN, Real-ESRGAN, SwinIR, HAT, DAT, SPAN, SCUNet, NAFNet, Restormer, OmniSR…) and Python scripts (.py)."
                : m + "  ·  " + CustomAi.Kind(m) + "\nRuns on every frame before the other effects, then your export settings apply (format, codec, audio, filters). " +
                  (cbCaiSize.SelectedIndex == 1 ? "The result is brought back to the source size." : "Upscaling models enlarge the picture (×2, ×4…), detected automatically.") +
                  "\nInputs are RGB 0..1 (NCHW or NHWC for ONNX). Models with a fixed input size are tiled automatically.";
            }
        }

        void UpdateDeintRows()
        {
            using (UiLayoutBatch.Begin(secVideo))
            {
            if (cbDeint == null) return;
            bool on = cbDeint.SelectedIndex > 0;
            cbDeintOrder.Visible = lblDeintOrder.Visible = on;
            bool ready = Qtgmc.Ready;
            btnQtgmcInstall.Visible = on && !ready;
            lblDeintInfo.Visible = on;
            if (!on) { lblDeintInfo.Text = ""; return; }
            string t = cbDeint.SelectedIndex == 1 ? "Every field becomes a frame: 25i → 50p, 29.97i → 59.94p." : "One frame per frame: 25i → 25p, 29.97i → 29.97p.";
            t += cbDeintOrder.SelectedIndex == 0 ? " Only videos flagged as interlaced are deinterlaced." : " Every video is deinterlaced with the forced field order.";
            t += ready ? "\nQTGMC ready (VapourSynth R81 + vsjetpack). Slow but the best quality: it runs before every other effect."
                       : "\nQTGMC is not installed yet: FFmpeg's bwdif deinterlacer is used until you install it (one time, about 150 MB).";
            lblDeintInfo.Text = t;
            lblDeintInfo.ForeColor = ready ? Theme.Muted : Theme.Accent1;
            }
        }

        void UpdateHdrRows()
        {
            using (UiLayoutBatch.Begin(secVideo))
            {
            if (cbHdr == null || cbHdrImg == null) return;
            int m = cbHdr.SelectedIndex;
            cbHdrPeak.Visible = lblHdrPeak.Visible = lblHdrInfo.Visible = m > 0;
            if (m > 0)
            {
                string pk = Hdr.Peaks[Math.Max(0, cbHdrPeak.SelectedIndex)] + " nits";
                lblHdrInfo.Text = (m == 1
                    ? "HDR10: H.265 10-bit (VP9 / AV1 for WebM), BT.2020 PQ, SDR white at 203 nits, highlights up to " + pk + "."
                    : "Dolby Vision 8.1: HDR10 base (H.265 10-bit) + dynamic metadata measured on every frame. MP4, MOV or MKV." + (Hdr.DoviTool == null ? "\ndovi_tool.exe is missing next to FFmpegForge.exe." : "")) +
                    "\nNeeds an HDR screen to look right; on SDR screens HDR files look dim or flat.";
                lblHdrInfo.ForeColor = m == 2 && Hdr.DoviTool == null ? Theme.Error : Theme.Muted;
            }
            int mi = cbHdrImg.SelectedIndex;
            cbHdrPeakImg.Visible = lblHdrPeakImg.Visible = lblHdrImgInfo.Visible = mi > 0;
            if (mi > 0) lblHdrImgInfo.Text = "Choose PNG (16-bit, cICP HDR tag) or AVIF (10-bit) as the output format.";
            }
        }

        void UpdateTracksInfo()
        {
            if (lblTracksInfo == null) return;
            int n = extraTracks.Count, m = cbExistingAudio.SelectedIndex;
            var parts = new List<string>();
            if (chkKeepVideo.Checked) parts.Add("Video copied as is (no re-encoding, same quality, very fast).");
            else if (n > 0 || m != 0) parts.Add("The video is re-encoded with the Video settings.");
            if (m == 0) parts.Add("Original tracks → converted to the codec of Audio settings (a track already in that codec is copied).");
            else if (m == 1) parts.Add("Original tracks kept as is.");
            else parts.Add("Original tracks removed" + (n == 0 ? ": the video will have no sound." : "."));
            if (n > 0) parts.Add(n + " added track(s), copied as is when the container accepts them (otherwise converted).");
            if (!chkKeepVideo.Checked && n == 0 && m == 0) parts.Clear();
            lblTracksInfo.Text = string.Join("\n", parts.ToArray());
            lblTracksInfo.Visible = parts.Count > 0;
            lblTracksInfo.ForeColor = m == 2 && n == 0 ? Theme.Accent1 : Theme.Muted;
        }

        void InstallQtgmc()
        {
            string root = Qtgmc.Root;
            if (MessageBox.Show(this,
                "Install QTGMC for deinterlacing?\n\nFFmpeg Forge downloads uv, Python 3.12, VapourSynth R81, vsjetpack (QTempGaussMC) and its plugins " +
                "(MVTools, znedi3, zsmooth, resize2, akarin, vszip, BestSource), about 150 MB in total, into:\n" + root +
                "\n\nA console window shows the progress.", "QTGMC", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                string bat = Qtgmc.WriteInstaller(root);
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = root, UseShellExecute = true });
                Log("QTGMC installation started in a console window (" + root + ").", Theme.Accent1);
                if (p != null)
                    new Thread(() => { try { p.WaitForExit(); } catch { } UI(() => { UpdateDeintRows(); Log(Qtgmc.Ready ? "QTGMC ready." : "QTGMC installation did not finish — see the console window.", Qtgmc.Ready ? Theme.Success : Theme.Error); }); }) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("Could not start the QTGMC installation: " + ex.Message, Theme.Error); }
        }

        void InstallCaiRuntime()
        {
            string root = AiDenoise.Root;
            bool full = !AiDenoise.Ready;
            if (MessageBox.Show(this,
                (full ? "Install the AI runtime now?\n\nFFmpeg Forge downloads Python 3.12, PyTorch CUDA (about 3 GB), ONNX Runtime GPU, spandrel and the SCUNet denoiser into:\n"
                      : "Install / repair the AI runtime (ONNX Runtime GPU, spandrel, torchvision) in:\n") + root +
                "\n\nA console window shows the progress.", "Python / ONNX AI", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                string bat = full ? AiDenoise.WriteInstaller(root) : AiDenoise.WriteModelSupportInstaller(root);
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = root, UseShellExecute = true });
                Log("AI runtime installation started in a console window (" + root + ").", Theme.Accent1);
                if (p != null)
                    new Thread(() => { try { p.WaitForExit(); } catch { } UI(() => { UpdateCaiRows(); UpdateDnRows(); Log(CustomAi.HasOnnx && AiDenoise.HasSpandrel ? "AI runtime ready." : "AI runtime installation did not finish — see the console window.", CustomAi.HasOnnx ? Theme.Success : Theme.Error); }); }) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("Could not start the installation: " + ex.Message, Theme.Error); }
        }

        void InstallVisionBuiltin()
        {
            string root = VisionBuiltin.Root;
            if (MessageBox.Show(this,
                "Install the VisionNext model in FFmpeg Forge's folder?\n\n" + root + "\n\n" +
                "VisionNext's model is VOSR 2.0 (CVPR 2026, Apache-2.0). FFmpeg Forge downloads Python 3.12, PyTorch CUDA (about 3 GB), " +
                "the official VOSR code (GitHub) and the VOSR 2.0 weights with their VAE and DINOv2 encoder (Hugging Face, several GB).\n" +
                "Visual Enhancer is not needed. A console window shows the progress. Continue?",
                "Install VisionNext", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            try
            {
                string bat = VisionBuiltin.WriteInstaller(root);
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = root, UseShellExecute = true });
                Log("VisionNext installation started in a console window (" + root + ").", Theme.Accent1);
                if (p != null)
                    new Thread(() => { try { p.WaitForExit(); } catch { } UI(() => { UpdateVisionRows(); Log(VisionBuiltin.Ready ? "VisionNext (built-in) is installed and ready." : "VisionNext installation did not finish — see the console window.", VisionBuiltin.Ready ? Theme.Success : Theme.Error); }); }) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("Could not start the installation: " + ex.Message, Theme.Error); }
        }

        void InstallRazorBuiltin()
        {
            string root = RazorBuiltin.Root;
            if (MessageBox.Show(this,
                "Install the RazorNext model in FFmpeg Forge's folder?\n\n" + root + "\n\n" +
                "RazorNext's model is RelayVSR (Apache-2.0), a real-world video super-resolution model (native ×4). FFmpeg Forge downloads " +
                "Python 3.12, PyTorch 2.10 CUDA (about 3 GB), the official RelayVSR code (GitHub) and its two checkpoints (Hugging Face, several GB).\n" +
                "Visual Enhancer is not needed. NVIDIA GPU with 16-24 GB VRAM recommended. A console window shows the progress. Continue?",
                "Install RazorNext", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            try
            {
                string bat = RazorBuiltin.WriteInstaller(root);
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = root, UseShellExecute = true });
                Log("RazorNext installation started in a console window (" + root + ").", Theme.Accent1);
                if (p != null)
                    new Thread(() => { try { p.WaitForExit(); } catch { } UI(() => { UpdateVisionRows(); Log(RazorBuiltin.Ready ? "RazorNext (built-in) is installed and ready." : "RazorNext installation did not finish — see the console window.", RazorBuiltin.Ready ? Theme.Success : Theme.Error); }); }) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("Could not start the installation: " + ex.Message, Theme.Error); }
        }

        void UpdateVisionRows()
        {
            using (UiLayoutBatch.Begin(secVision))
            {
            if (secVision == null || cbVisionModel == null || cbVisionMem == null || lblVisionInfo == null) return;
            bool on = chkVision.Checked;
            foreach (var c in visionRows) if (c != null) c.Visible = on;
            bool builtin = cbVisionEngine.SelectedIndex == 0;
            bool razor = cbVisionModel.SelectedIndex == 1;
            string nm = razor ? "RazorNext" : "VisionNext";
            visionVeBar.Visible = on && !builtin;
            visionInstallBtn.Visible = on && builtin;
            chkVisionGpu.Visible = chkVisionCpu.Visible = on;   // always shown when AI Upscale is ticked
            // tiles: VisionNext only. GPU memory budget: RazorNext with Visual Enhancer only.
            visionTileLbl.Visible = cbVisionTile.Visible = on && !razor;
            visionMemLbl.Visible = cbVisionMem.Visible = on && razor && !builtin;
            bool ready = razor ? RazorBuiltin.Ready : VisionBuiltin.Ready;
            ((Control)visionInstallBtn).Text = ready ? "Reinstall / repair " + nm + " model…" : "Install " + nm + " model…";
            if (builtin)
            {
                lblVisionStatus.Text = razor ? RazorBuiltin.Status() : VisionBuiltin.Status();
                lblVisionStatus.ForeColor = ready ? Theme.Success : Theme.Accent1;
            }
            else if (razor)
            {
                lblVisionStatus.Text = VisionEngine.RazorStatus();
                lblVisionStatus.ForeColor = VisionEngine.Root != null ? Theme.Success : Theme.Accent1;
            }
            else
            {
                lblVisionStatus.Text = VisionEngine.Status();
                lblVisionStatus.ForeColor = VisionEngine.Ready && VisionEngine.ModelInstalled != false ? Theme.Success : Theme.Accent1;
            }
            bool img = category == MediaKind.Image;
            int vsc = VisionScale();
            if (razor)
            {
                string warn = builtin && vsc == 8 ? "  ·  ⚠ ×8 needs the Visual Enhancer engine (built-in: ×1 – ×4)" :
                              !builtin && vsc == 3 ? "  ·  ⚠ ×3 needs the Built-in engine (Visual Enhancer: ×1, ×2, ×4, ×8)" :
                              vsc == 4 ? "  ·  native factor" : vsc < 4 ? "  ·  computed at ×4, then resized to ×" + vsc : "";
                lblVisionInfo.Text = "RazorNext ×" + vsc + warn + (builtin ? "" : "  ·  GPU memory " + cbVisionMem.Text) +
                    (img ? "" : builtin ? "\nVideos are streamed straight from the decoder to the final encoder, with temporal video super-resolution (stable detail, no flicker)." :
                                          "\nVisual Enhancer's RazorNext works per picture: the video is streamed to it in batches of frames (one single encode).") +
                    "\nScale / OptiScaler, SeedVR2 and RTX options must be off. " +
                    (builtin ? (chkVisionCpu.Checked ? "CPU mode: works without an NVIDIA card but is extremely slow." : "GPU mode: NVIDIA GPU (CUDA), 16-24 GB VRAM recommended; keep the source at 1080p or less (the output is 4× before resizing).") : "Needs a DirectX 12 GPU; RazorNext can use a lot of GPU and system memory.");
                return;
            }
            lblVisionInfo.Text = "VisionNext ×" + vsc + (vsc > 2 && !builtin ? "  ·  ⚠ ×" + vsc + " needs the Built-in engine" : vsc > 2 ? "  ·  ×" + vsc + ": about " + (vsc * vsc / 4.0).ToString("0.#", CultureInfo.InvariantCulture) + "× slower than ×2" : "") + "  ·  tile " + cbVisionTile.Text +
                                 (img ? "" : builtin ? "\nVideos are streamed straight from the decoder to the final encoder (no picture files, one single encode)." : "\nVideos are encoded directly: decoding, Visual Enhancer upscale and the final encode run at the same time (one single encode, no frame sequence saved).") +
                                 "\nScale / OptiScaler, SeedVR2 and RTX options must be off. " + (builtin ? (chkVisionCpu.Checked ? "CPU mode: works without an NVIDIA card but is very slow." : "GPU mode: NVIDIA GPU (CUDA), 24 GB VRAM recommended.") : "Needs a DirectX 12 GPU.");
            }
        }

        int VisionTile()
        {
            return cbVisionTile.SelectedIndex == 0 ? 256 : 512;
        }

        int VisionScale()
        {
            switch (cbVisionScale.SelectedIndex) { case 0: return 1; case 2: return 3; case 3: return 4; case 4: return 8; default: return 2; }
        }

        VisionSettings VisionSnapshot(MediaKind k)
        {
            return new VisionSettings
            {
                Enabled = chkVision.Checked && (k == MediaKind.Video || k == MediaKind.Image),
                Model = cbVisionModel.SelectedIndex == 1 ? "razornext" : "visionnext",
                MemBudget = VisionEngine.MemBudgets[Math.Max(0, Math.Min(VisionEngine.MemBudgets.Length - 1, cbVisionMem.SelectedIndex))],
                Scale = VisionScale(),
                Tile = VisionTile(),
                Engine = Math.Max(0, cbVisionEngine.SelectedIndex),
                UseGpu = !chkVisionCpu.Checked
            };
        }

        void LoadVisionSettings(Func<string, int, int> gi)
        {
            string v;
            if (settings.TryGetValue("vn_dir", out v)) VisionEngine.Dir = v;
            if (VisionEngine.Resolve(VisionEngine.Dir) == null)
                foreach (var cand in new[] { Path.Combine(AppPaths.AppDir, "Visual Enhancer") }.Concat(VisionEngine.DefaultDirs))
                    if (VisionEngine.Resolve(cand) != null) { VisionEngine.Dir = cand; break; }
            chkVision.Checked = gi("vn_on", 0) == 1;
            chkPreview.Checked = gi("preview", 1) == 1;
            { int vs = gi("vn_scale", 2); cbVisionScale.SelectedIndex = vs == 1 ? 0 : vs == 3 ? 2 : vs == 4 ? 3 : vs == 8 ? 4 : 1; }
            cbVisionEngine.SelectedIndex = Math.Max(0, Math.Min(1, gi("vn_engine", 0)));
            if (gi("vn_cpu", 0) == 1) chkVisionCpu.Checked = true; else chkVisionGpu.Checked = true;
            { string vb; if (settings.TryGetValue("vn_builtin_dir", out vb)) VisionBuiltin.Dir = vb; }
            cbVisionTile.SelectedIndex = gi("vn_tile", 512) == 256 ? 0 : 1;
            cbVisionModel.SelectedIndex = gi("vn_model", 0) == 1 ? 1 : 0;
            cbVisionMem.SelectedIndex = Math.Max(0, Math.Min(cbVisionMem.Items.Count - 1, gi("vn_mem", 0)));
            UpdateVisionRows();
        }

        void SaveVisionSettings()
        {
            settings["vn_dir"] = VisionEngine.Dir ?? "";
            settings["vn_on"] = chkVision.Checked ? "1" : "0";
            settings["preview"] = chkPreview.Checked ? "1" : "0";
            settings["vn_scale"] = VisionScale().ToString(CultureInfo.InvariantCulture);
            settings["vn_engine"] = cbVisionEngine.SelectedIndex.ToString();
            settings["vn_cpu"] = chkVisionCpu.Checked ? "1" : "0";
            settings["vn_builtin_dir"] = VisionBuiltin.Dir ?? "";
            settings["vn_tile"] = VisionTile().ToString(CultureInfo.InvariantCulture);
            settings["vn_model"] = cbVisionModel.SelectedIndex == 1 ? "1" : "0";
            settings["vn_mem"] = Math.Max(0, cbVisionMem.SelectedIndex).ToString(CultureInfo.InvariantCulture);
        }

        void ChooseVisionDir()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose your Visual Enhancer folder (the one that contains VE_CLI.exe)" })
            {
                if (!string.IsNullOrEmpty(VisionEngine.Dir) && Directory.Exists(VisionEngine.Dir)) d.SelectedPath = VisionEngine.Dir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                VisionEngine.Dir = d.SelectedPath;
                Log(VisionEngine.Root != null ? "Visual Enhancer: " + VisionEngine.ExePath : "VE_CLI.exe not found in " + d.SelectedPath, VisionEngine.Root != null ? Theme.Success : Theme.Error);
                SaveSettings();
                UpdateVisionRows();
            }
        }

        // ---------- SeedVR2 ----------
        void BuildSeedSection(Control stack)
        {
            secSeed = Section("SEEDVR2  (AI RESTORATION + UPSCALING)");
            chkSeed = Check("SeedVR2 — AI video and image restoration / upscaling");
            AddRow(secSeed, null, chkSeed);
            lblSeedStatus = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(2), 0, Theme.S(4)) };
            var bInst = Btn("Install SeedVR2…", 0); bInst.Dock = DockStyle.Fill; bInst.Height = Theme.S(30); bInst.Font = fSmall;
            bInst.Click += (s2, e2) => InstallSeed();
            var bDir = Btn("SeedVR2 folder…", 0); bDir.Dock = DockStyle.Fill; bDir.Height = Theme.S(30); bDir.Font = fSmall;
            bDir.Click += (s2, e2) => ChooseSeedDir();
            var bModels = Btn("Models folder", 0); bModels.Dock = DockStyle.Fill; bModels.Height = Theme.S(30); bModels.Font = fSmall;
            bModels.Click += (s2, e2) =>
            {
                string md = SeedEngine.ModelDir(SeedEngine.Script);
                if (md == null) { Log("Install SeedVR2 first.", Theme.Accent1); return; }
                try { Directory.CreateDirectory(md); Process.Start(new ProcessStartInfo(md) { UseShellExecute = true }); } catch (Exception ex) { Log(ex.Message, Theme.Error); }
            };
            var bar = new TableLayoutPanel { ColumnCount = 3, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0) };
            for (int i = 0; i < 3; i++) bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
            bar.Controls.Add(bInst, 0, 0); bar.Controls.Add(bDir, 1, 0); bar.Controls.Add(bModels, 2, 0);
            AddRow(secSeed, null, bar); seedRows.Add(bar);
            AddRow(secSeed, null, lblSeedStatus); seedRows.Add(lblSeedStatus);

            cbSeedModel = Combo(SeedEngine.Models.Cast<object>().ToArray());
            cbSeedModel.DropDownWidth = Theme.S(440);
            cbSeedModel.SelectedIndex = SeedEngine.Models.FindIndex(m => m.File == SeedEngine.DefaultModel);
            seedRows.Add(AddRow(secSeed, "   Model", cbSeedModel)); seedRows.Add(cbSeedModel);
            cbSeedSize = Combo(SeedEngine.Sizes.Cast<object>().ToArray());
            cbSeedSize.SelectedIndex = 1;
            seedRows.Add(AddRow(secSeed, "   Output size", cbSeedSize)); seedRows.Add(cbSeedSize);
            cbSeedBatch = Combo(SeedEngine.Batches.Select(b => (object)(b + " frames")).ToArray());
            cbSeedBatch.SelectedIndex = Array.IndexOf(SeedEngine.Batches, 13);
            seedRows.Add(AddRow(secSeed, "   Batch size", cbSeedBatch)); seedRows.Add(cbSeedBatch); seedVidRows.Add(cbSeedBatch);
            cbSeedColor = Combo(SeedEngine.ColorLabels.Cast<object>().ToArray());
            seedRows.Add(AddRow(secSeed, "   Colour correction", cbSeedColor)); seedRows.Add(cbSeedColor);
            Panel inHost, latHost;
            tbSeedInNoise = SliderRow(0, 100, 0, out lblSeedInNoise, out inHost);
            tbSeedLatNoise = SliderRow(0, 100, 0, out lblSeedLatNoise, out latHost);
            seedRows.Add(AddRow(secSeed, "   Input noise", inHost)); seedRows.Add(inHost);
            seedRows.Add(AddRow(secSeed, "   Latent noise", latHost)); seedRows.Add(latHost);
            nuSeedSeed = Num(0, 999999999, 42, 0);
            seedRows.Add(AddRow(secSeed, "   Seed", nuSeedSeed)); seedRows.Add(nuSeedSeed);
            nuSeedChunk = Num(0, 2000, 165, 0); nuSeedChunk.Increment = 15;
            seedRows.Add(AddRow(secSeed, "   Chunk (frames, 0 = all)", nuSeedChunk)); seedRows.Add(nuSeedChunk); seedVidRows.Add(nuSeedChunk);
            nuSeedOverlap = Num(0, 16, 3, 0);
            seedRows.Add(AddRow(secSeed, "   Temporal overlap", nuSeedOverlap)); seedRows.Add(nuSeedOverlap); seedVidRows.Add(nuSeedOverlap);
            nuSeedSwap = Num(0, 36, 0, 0);
            seedRows.Add(AddRow(secSeed, "   BlockSwap", nuSeedSwap)); seedRows.Add(nuSeedSwap);
            cbSeedAttn = Combo(SeedEngine.AttentionLabels.Cast<object>().ToArray());
            seedRows.Add(AddRow(secSeed, "   Attention", cbSeedAttn)); seedRows.Add(cbSeedAttn);
            txtSeedGpu = Txt("0");
            seedRows.Add(AddRow(secSeed, "   CUDA GPU", txtSeedGpu)); seedRows.Add(txtSeedGpu);
            chkSeedTiled = Check("VAE tiling (high resolutions with less VRAM)");
            AddRow(secSeed, null, chkSeedTiled); seedRows.Add(chkSeedTiled);
            chkSeedOffload = Check("Offload models to RAM between phases (less VRAM)");
            AddRow(secSeed, null, chkSeedOffload); seedRows.Add(chkSeedOffload);
            chkSeedCompile = Check("torch.compile (faster, needs Triton; slow first run)");
            AddRow(secSeed, null, chkSeedCompile); seedRows.Add(chkSeedCompile);
            lblSeedInfo = new Label { Text = "", ForeColor = Theme.Muted, Font = fSmall, AutoSize = true, MaximumSize = new Size(Theme.S(380), 0), Margin = new Padding(0, Theme.S(4), 0, 0) };
            AddRow(secSeed, null, lblSeedInfo); seedRows.Add(lblSeedInfo);

            tips.SetToolTip(chkSeed, "SeedVR2 (ByteDance-Seed): one-step diffusion model that restores and upscales videos and pictures —\nremoves blur, noise and compression artefacts and rebuilds fine detail, with temporal consistency.\nRuns locally on your NVIDIA GPU through the official SeedVR2 command line (Python / PyTorch).\nYour format, codec, audio, trim, Denoiser and filters are kept.");
            tips.SetToolTip(cbSeedModel, "3B: faster, less VRAM.  7B: better quality.  Sharp: more fine detail.\nFP16: full precision.  FP8 / GGUF: smaller and lighter (GGUF Q4 for 8–12 GB cards).\nThe chosen model downloads automatically on the first conversion (2 to 16 GB).");
            tips.SetToolTip(cbSeedSize, "Size of the SHORT side of the result (×2 on 1920×1080 → 3840×2160).\n\"Restore only\" keeps the size and only cleans / sharpens.");
            tips.SetToolTip(cbSeedBatch, "Frames processed together (4n+1). Larger = better temporal consistency, more VRAM.\n13–21 suits 24 GB with the 7B model at 1080p; lower it if you get \"out of memory\".");
            tips.SetToolTip(cbSeedColor, "Brings the colours of the result back to the source. LAB is recommended.");
            tips.SetToolTip(tbSeedInNoise, "Adds noise to the input before restoration: can reduce artefacts on very high-resolution sources. 0 = off.");
            tips.SetToolTip(tbSeedLatNoise, "Adds noise in the latent space: more generated detail, less faithful. 0 = off.");
            tips.SetToolTip(nuSeedChunk, "Long videos are processed in chunks of this many frames to limit RAM use. 0 = whole video at once.");
            tips.SetToolTip(nuSeedOverlap, "Frames shared between batches / chunks and blended, to hide seams.");
            tips.SetToolTip(nuSeedSwap, "Moves this many transformer blocks to RAM to save VRAM (0–32 for 3B, 0–36 for 7B). Slower. 0 = off.");
            tips.SetToolTip(cbSeedAttn, "Attention backend. SDPA always works; Flash / Sage are faster but must be installed in the SeedVR2 environment.");
            tips.SetToolTip(txtSeedGpu, "CUDA device index (0 = first GPU). Several GPUs: 0,1");

            EventHandler upd = (s2, e2) => UpdateSeedRows();
            chkSeed.CheckedChanged += upd; cbSeedModel.SelectedIndexChanged += upd; cbSeedSize.SelectedIndexChanged += upd;
            stack.Controls.Add(secSeed);
            UpdateSeedRows();
        }

        /// The CRF row is shown when the video codec is H.264 / H.265 NVENC (chosen, or the format's own codec).
        void UpdateNvCrfRow()
        {
            if (nuNvCrf == null) return;
            var f = CurrentFormat;
            string key = cbVCodec.SelectedItem is Choice ? ((Choice)cbVCodec.SelectedItem).Key : "auto";
            if (key == "auto" && f != null) key = f.VCodec;
            bool on = f != null && f.Kind == MediaKind.Video && CommandBuilder.IsNvencCrf(key) && cbVCodec.Enabled;
            nuNvCrf.Visible = lblNvCrf.Visible = lblNvCrfHint.Visible = on;
            if (!on) return;
            int v = (int)nuNvCrf.Value;
            bool h265 = key.Contains("265") || key.Contains("hevc");
            lblNvCrfHint.Text = nuVBitrate.Value > 0 ? "The bitrate above 0 is used instead of the CRF."
                              : v == 0 ? "Automatic: CQ set by Quality. Type a value (1–51) to choose it yourself."
                              : "CQ " + v + " — " + (v <= (h265 ? 20 : 18) ? "very high quality, large file" : v <= (h265 ? 26 : 23) ? "high quality (recommended range)" : v <= (h265 ? 32 : 28) ? "medium quality, smaller file" : "low quality, very small file");
            lblNvCrfHint.ForeColor = nuVBitrate.Value > 0 ? Theme.Accent1 : Theme.Muted;
        }

        void UpdateSeedRows()
        {
            using (UiLayoutBatch.Begin(secSeed))
            {
            if (secSeed == null) return;
            bool on = chkSeed.Checked;
            bool img = category == MediaKind.Image;
            foreach (var c in seedRows) if (c != null) c.Visible = on;
            foreach (var c in seedVidRows) if (c != null) c.Visible = on && !img;
            // also hide the labels of video-only rows
            foreach (var c in seedVidRows) { var p = c.Parent as TableLayoutPanel; if (p != null) { var l = p.GetControlFromPosition(0, p.GetRow(c)); if (l != null && l != c) l.Visible = on && !img; } }
            lblSeedStatus.Text = SeedEngine.Status();
            lblSeedStatus.ForeColor = SeedEngine.Ready ? Theme.Success : Theme.Accent1;
            var m = cbSeedModel.SelectedItem as SeedEngine.Model;
            bool have = m != null && SeedEngine.Downloaded().Contains(m.File);
            lblSeedInfo.Text = (m == null ? "" : (have ? "✓ " + SeedEngine.Short(m.File) + " is downloaded." : "↓ " + SeedEngine.Short(m.File) + " will download on the first conversion.")) +
                               "\nRuns after the Denoiser and filters, before encoding. Scale / OptiScaler and RTX options must be off." +
                               " Needs an NVIDIA GPU (CUDA); slow — minutes per minute of video with 7B.";
            }
        }

        SeedSettings SeedSnapshot(MediaKind k)
        {
            var v = new SeedSettings();
            v.Enabled = chkSeed.Checked && (k == MediaKind.Video || k == MediaKind.Image);
            var m = cbSeedModel.SelectedItem as SeedEngine.Model;
            v.Model = m != null ? m.File : SeedEngine.DefaultModel;
            v.SizeIdx = Math.Max(0, cbSeedSize.SelectedIndex);
            v.BatchSize = SeedEngine.Batches[Math.Max(0, cbSeedBatch.SelectedIndex)];
            v.Color = SeedEngine.Colors[Math.Max(0, cbSeedColor.SelectedIndex)];
            v.InputNoise = tbSeedInNoise.Value / 100.0;
            v.LatentNoise = tbSeedLatNoise.Value / 100.0;
            v.Seed = (int)nuSeedSeed.Value;
            v.ChunkSize = (int)nuSeedChunk.Value;
            v.Overlap = (int)nuSeedOverlap.Value;
            v.BlocksToSwap = (int)nuSeedSwap.Value;
            v.Attention = SeedEngine.Attentions[Math.Max(0, cbSeedAttn.SelectedIndex)];
            v.Gpu = txtSeedGpu.Text.Trim();
            v.VaeTiled = chkSeedTiled.Checked;
            v.Offload = chkSeedOffload.Checked;
            v.Compile = chkSeedCompile.Checked;
            return v;
        }

        void LoadSeedSettings(Func<string, int, int> gi)
        {
            string v;
            if (settings.TryGetValue("seed_dir", out v)) SeedEngine.Dir = v;
            if (SeedEngine.ScriptDir(SeedEngine.Dir) == null && SeedEngine.ScriptDir(SeedEngine.DefaultInstallDir) != null) SeedEngine.Dir = SeedEngine.DefaultInstallDir;
            Func<ComboBox, string, int, int> sel = (c, key, def) => c.SelectedIndex = Math.Max(0, Math.Min(c.Items.Count - 1, gi(key, def)));
            chkSeed.Checked = gi("seed_on", 0) == 1;
            nuNvCrf.Value = Math.Max(0, Math.Min(51, gi("nv_crf", 0)));
            if (settings.TryGetValue("seed_model", out v)) { int mi = SeedEngine.Models.FindIndex(x => x.File == v); if (mi >= 0) cbSeedModel.SelectedIndex = mi; }
            sel(cbSeedSize, "seed_size", 1);
            { int bi = Array.IndexOf(SeedEngine.Batches, gi("seed_batch", 13)); cbSeedBatch.SelectedIndex = bi >= 0 ? bi : Array.IndexOf(SeedEngine.Batches, 13); }
            sel(cbSeedColor, "seed_color", 0);
            tbSeedInNoise.Value = Math.Max(0, Math.Min(100, gi("seed_innoise", 0)));
            tbSeedLatNoise.Value = Math.Max(0, Math.Min(100, gi("seed_latnoise", 0)));
            nuSeedSeed.Value = Math.Max(0, Math.Min(999999999, gi("seed_seed", 42)));
            nuSeedChunk.Value = Math.Max(0, Math.Min(2000, gi("seed_chunk", 165)));
            nuSeedOverlap.Value = Math.Max(0, Math.Min(16, gi("seed_overlap", 3)));
            nuSeedSwap.Value = Math.Max(0, Math.Min(36, gi("seed_swap", 0)));
            sel(cbSeedAttn, "seed_attn", 0);
            if (settings.TryGetValue("seed_gpu", out v) && v.Length > 0) txtSeedGpu.Text = v;
            chkSeedTiled.Checked = gi("seed_tiled", 0) == 1;
            chkSeedOffload.Checked = gi("seed_offload", 0) == 1;
            chkSeedCompile.Checked = gi("seed_compile", 0) == 1;
            UpdateSeedRows();
        }

        void SaveSeedSettings()
        {
            settings["seed_dir"] = SeedEngine.Dir ?? "";
            settings["seed_on"] = chkSeed.Checked ? "1" : "0";
            settings["nv_crf"] = nuNvCrf.Value.ToString(CultureInfo.InvariantCulture);
            var m = cbSeedModel.SelectedItem as SeedEngine.Model;
            settings["seed_model"] = m != null ? m.File : SeedEngine.DefaultModel;
            settings["seed_size"] = cbSeedSize.SelectedIndex.ToString();
            settings["seed_batch"] = SeedEngine.Batches[Math.Max(0, cbSeedBatch.SelectedIndex)].ToString();
            settings["seed_color"] = cbSeedColor.SelectedIndex.ToString();
            settings["seed_innoise"] = tbSeedInNoise.Value.ToString();
            settings["seed_latnoise"] = tbSeedLatNoise.Value.ToString();
            settings["seed_seed"] = nuSeedSeed.Value.ToString(CultureInfo.InvariantCulture);
            settings["seed_chunk"] = nuSeedChunk.Value.ToString(CultureInfo.InvariantCulture);
            settings["seed_overlap"] = nuSeedOverlap.Value.ToString(CultureInfo.InvariantCulture);
            settings["seed_swap"] = nuSeedSwap.Value.ToString(CultureInfo.InvariantCulture);
            settings["seed_attn"] = cbSeedAttn.SelectedIndex.ToString();
            settings["seed_gpu"] = txtSeedGpu.Text.Trim();
            settings["seed_tiled"] = chkSeedTiled.Checked ? "1" : "0";
            settings["seed_offload"] = chkSeedOffload.Checked ? "1" : "0";
            settings["seed_compile"] = chkSeedCompile.Checked ? "1" : "0";
        }

        void ChooseSeedDir()
        {
            using (var d = new FolderBrowserDialog { Description = "Choose the SeedVR2 folder (FFmpeg Forge install, or the ComfyUI-SeedVR2_VideoUpscaler folder that contains inference_cli.py)" })
            {
                if (!string.IsNullOrEmpty(SeedEngine.Dir) && Directory.Exists(SeedEngine.Dir)) d.SelectedPath = SeedEngine.Dir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                SeedEngine.Dir = d.SelectedPath;
                string sc = SeedEngine.ScriptDir(d.SelectedPath);
                Log(sc != null ? "SeedVR2: " + sc + (SeedEngine.Python(sc) != null ? "  ·  Python: " + SeedEngine.Python(sc) : "  ·  no Python environment found") : "inference_cli.py not found in " + d.SelectedPath,
                    sc != null && SeedEngine.Python(sc) != null ? Theme.Success : Theme.Error);
                SaveSettings();
                UpdateSeedRows();
            }
        }

        void InstallSeed()
        {
            string root = SeedEngine.ScriptDir(SeedEngine.Dir) != null && SeedEngine.Python(SeedEngine.Script) == null ? SeedEngine.Dir : SeedEngine.DefaultInstallDir;
            if (MessageBox.Show(this,
                "FFmpeg Forge will install SeedVR2 in:\n" + root + "\n\n" +
                "It downloads (about 4 GB): the official SeedVR2 command line (github.com/numz/ComfyUI-SeedVR2_VideoUpscaler, Apache-2.0), " +
                "uv + Python 3.12 and PyTorch with CUDA 12.8. The models download later, on the first conversion.\n\n" +
                "A console window shows the progress. Continue?", "Install SeedVR2", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;
            try
            {
                string bat = SeedEngine.WriteInstaller(root);
                SeedEngine.Dir = root;
                SaveSettings();
                var p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { WorkingDirectory = root, UseShellExecute = true });
                Log("SeedVR2 installation started in a console window (" + root + ").", Theme.Accent1);
                if (p != null)
                    new Thread(() => { try { p.WaitForExit(); } catch { } UI(() => { UpdateSeedRows(); Log(SeedEngine.Ready ? "SeedVR2 is installed and ready." : "SeedVR2 installation did not finish — see the console window.", SeedEngine.Ready ? Theme.Success : Theme.Error); }); }) { IsBackground = true }.Start();
            }
            catch (Exception ex) { Log("Could not start the installation: " + ex.Message, Theme.Error); }
        }

        // =====================================================================
        //  Persistent settings
        // =====================================================================
        void LoadSettings()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;
                foreach (var line in File.ReadAllLines(settingsPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) settings[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }

        void ApplySettings()
        {
            string v;
            chkSameDir.Checked = !settings.TryGetValue("samedir", out v) || v == "1";
            txtOutDir.Text = settings.TryGetValue("outdir", out v) ? v : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "FFmpeg Forge");
            txtOutDir.Enabled = btnBrowseOut.Enabled = !chkSameDir.Checked;
            if (settings.TryGetValue("filters", out v) && v.Length > 0)
                foreach (var part in v.Split('\u001F')) { var fe = FilterEntry.Parse(part); if (fe != null) filters.Add(fe); }
            if (settings.TryGetValue("graph", out v)) txtGraph.Text = v.Replace("\u001D", "\r\n");
            if (settings.TryGetValue("extra", out v)) txtExtraInputs.Text = v;
            if (settings.TryGetValue("chlayout", out v))
                for (int ci = 0; ci < cbCh.Items.Count; ci++) if (((Choice)cbCh.Items[ci]).Key == v) cbCh.SelectedIndex = ci;
            Func<string, int, int> gi = (key, def) => { string x; int r; return settings.TryGetValue(key, out x) && int.TryParse(x, out r) ? r : def; };
            cbUpmix.SelectedIndex = Math.Max(0, Array.IndexOf(UpmixCodes, Math.Max(0, Math.Min(5, gi("upmix_style", gi("upmix", 0))))));
            chkScale.Checked = gi("scale_on", 0) == 1;
            if (settings.TryGetValue("scale_type", out v))
                for (int si = 0; si < cbScaleType.Items.Count; si++) if (((Scaling.ScaleType)cbScaleType.Items[si]).Key == v) cbScaleType.SelectedIndex = si;
            cbScaleFactor.SelectedIndex = Math.Max(0, Math.Min(5, gi("scale_factor", 1)));
            tbScaleSharp.Value = Math.Max(0, Math.Min(100, gi("scale_sharp", 100)));
            chkScaleOpt.Checked = gi("scale_opt", 1) == 1;
            chkScaleFid.Checked = gi("scale_fid", 1) == 1;
            chkScale444.Checked = gi("scale_444", 1) == 1;
            loadingSettings = true;
            chkDn.Checked = gi("dn", 0) == 1;
            { string ad; if (settings.TryGetValue("ai_dn_dir", out ad)) AiDenoise.Dir = ad; }
            cbSeqExport.SelectedIndex = Math.Max(0, Math.Min(2, gi("vseq", 0)));
            chkKeepVideo.Checked = gi("keepvideo", 0) == 1;
            cbHdr.SelectedIndex = Math.Max(0, Math.Min(2, gi("sdrhdr", 0)));
            cbHdrPeak.SelectedIndex = Math.Max(0, Math.Min(Hdr.Peaks.Length - 1, gi("hdrpeak", 1)));
            cbHdrImg.SelectedIndex = Math.Max(0, Math.Min(1, gi("sdrhdr_img", 0)));
            cbHdrPeakImg.SelectedIndex = Math.Max(0, Math.Min(Hdr.Peaks.Length - 1, gi("hdrpeak_img", 1)));
            UpdateHdrRows();
            cbExistingAudio.SelectedIndex = Math.Max(0, Math.Min(2, gi("existaudio", 0)));
            cbDeint.SelectedIndex = Math.Max(0, Math.Min(2, gi("deint", 0)));
            cbDeintOrder.SelectedIndex = Math.Max(0, Math.Min(2, gi("deint_order", 0)));
            UpdateDeintRows();
            chkSuggest.Checked = gi("suggest", 1) == 1;
            { string cp; if (settings.TryGetValue("cover_path", out cp) && cp.Length > 0) coverPath = cp; }
            { string ca; double cav; if (settings.TryGetValue("cover_at", out ca) && double.TryParse(ca, NumberStyles.Float, CultureInfo.InvariantCulture, out cav)) nuCoverAt.Value = (decimal)Math.Max(0, Math.Min(86400, cav)); }
            cbCover.SelectedIndex = Math.Max(0, Math.Min(2, gi("cover", 0)));
            {
                string sh;
                if (settings.TryGetValue("speedhist", out sh))
                    foreach (var kv in sh.Split(';'))
                    {
                        int eq = kv.LastIndexOf((char)61); double hv;
                        if (eq > 0 && double.TryParse(kv.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out hv) && hv > 0) Compression.History[kv.Substring(0, eq)] = hv;
                    }
            }
            chkCai.Checked = gi("cai_on", 0) == 1;
            { string cm; if (settings.TryGetValue("cai_model", out cm)) RefreshCaiModels(cm); }
            cbCaiSize.SelectedIndex = gi("cai_keep", 0) == 1 ? 1 : 0;
            cbCaiProc.SelectedIndex = gi("cai_cpu", 0) == 1 ? 1 : 0;
            cbCaiPrec.SelectedIndex = gi("cai_fp32", 0) == 1 ? 1 : 0;
            cbCaiTile.SelectedIndex = Math.Max(0, Math.Min(4, gi("cai_tile", 0)));
            { string dm; settings.TryGetValue("dn_model", out dm); int di = 0; for (int i = 0; i < AiDenoise.Models.Length; i++) if (AiDenoise.Models[i].Key == dm) di = i; cbDnModel.SelectedIndex = di; }
            UpdateDnRows();
            loadingSettings = false;
            chkOpti.Checked = gi("opti", 0) == 1;
            cbOptiMult.SelectedIndex = Math.Max(0, Math.Min(OptiMults.Length - 1, gi("opti_mult", 1)));
            cbOptiKernel.SelectedIndex = Math.Max(0, Math.Min(Scaling.OptiKernels.Length - 1, gi("opti_kernel", 2)));
            tbOptiSharp.Value = Math.Max(0, Math.Min(100, gi("opti_sharp", 40)));
            chkOptiContrast.Checked = gi("opti_contrast", 0) == 1;
            UpdateScaleRows(); UpdateOptiRows();
            UpdateUpmixRow();
            chkVsr.Checked = gi("rtx_vsr", 0) == 1; chkHdr.Checked = gi("rtx_hdr", 0) == 1; chkDlss.Checked = gi("rtx_dlss", 0) == 1;
            cbVsrQ.SelectedIndex = Math.Max(0, Math.Min(3, gi("vsr_q", 3)));
            cbVsrScale.SelectedIndex = Math.Max(0, Math.Min(2, gi("vsr_scale", 0)));
            cbDlssMode.SelectedIndex = Math.Max(0, Math.Min(4, gi("dlss_mode", 3)));
            cbRtxEngine.SelectedIndex = Math.Max(0, Math.Min(3, gi("rtx_engine2", 0)));
            LoadSeedSettings(gi);
            LoadVisionSettings(gi);
            nuHdrContrast.Value = Math.Max(0, Math.Min(200, gi("hdr_contrast", 125)));
            nuHdrSat.Value = Math.Max(0, Math.Min(200, gi("hdr_sat", 100)));
            nuHdrGray.Value = Math.Max(10, Math.Min(100, gi("hdr_gray", 25)));
            nuHdrLum.Value = Math.Max(400, Math.Min(10000, gi("hdr_lum", 1000)));
            if (settings.TryGetValue("vsr_expr", out v) && v.Length > 0) { vsrExpr = v; btnVsrParams.Text += "  (custom ✓)"; }
            if (settings.TryGetValue("hdr_expr", out v) && v.Length > 0) { hdrExpr = v; btnHdrParams.Text += "  (custom ✓)"; }
            if (settings.TryGetValue("dlss_expr", out v) && v.Length > 0) { dlssExpr = v; btnDlssParams.Text += "  (custom ✓)"; }
            UpdateRtxRows();
            RefreshFilterList(-1);
            MediaKind k = MediaKind.Video;
            if (settings.TryGetValue("category", out v)) { try { k = (MediaKind)Enum.Parse(typeof(MediaKind), v); } catch { } }
            if (k == MediaKind.Unknown) k = MediaKind.Video;
            cbQuality.SelectedIndex = 1;
            cbPreset.SelectedIndex = 3;
            SetCategory(k);
            // Upgrade old image-only RTX shortcut setting to the unified VSR option.
            if (k == MediaKind.Image && !chkVsr.Checked && gi("rtx_img", 0) > 0)
            {
                chkVsr.Checked = true;
                cbVsrScale.SelectedIndex = Math.Max(0, Math.Min(2, gi("rtx_img", 1) - 1));
            }
            UpdateScaleCategory();
            UpdateExtractLabel();
            UpdateEmpty();
        }

        void SaveSettings()
        {
            try
            {
                WindowPlacement.Save(this, settings, "ui_main");
                WindowPlacement.Save(scaleWindow, settings, "ui_scale");
                settings["samedir"] = chkSameDir.Checked ? "1" : "0";
                settings["outdir"] = txtOutDir.Text;
                settings["category"] = category.ToString();
                settings["filters"] = string.Join("\u001F", filters.Select(f => f.Serialize()).ToArray());
                settings["graph"] = txtGraph.Text.Replace("\r\n", "\u001D").Replace("\n", "\u001D");
                settings["extra"] = txtExtraInputs.Text;
                settings["chlayout"] = cbCh.SelectedItem is Choice ? ((Choice)cbCh.SelectedItem).Key : "";
                settings["upmix_style"] = UpmixCodes[Math.Max(0, cbUpmix.SelectedIndex)].ToString();
                settings["scale_on"] = chkScale.Checked ? "1" : "0";
                settings["scale_type"] = cbScaleType.SelectedItem is Scaling.ScaleType ? ((Scaling.ScaleType)cbScaleType.SelectedItem).Key : "fsr";
                settings["scale_factor"] = cbScaleFactor.SelectedIndex.ToString();
                settings["scale_sharp"] = tbScaleSharp.Value.ToString();
                settings["scale_opt"] = chkScaleOpt.Checked ? "1" : "0";
                settings["scale_fid"] = chkScaleFid.Checked ? "1" : "0";
                settings["scale_444"] = chkScale444.Checked ? "1" : "0";
                settings["dn"] = chkDn.Checked ? "1" : "0";
                settings["ai_dn_dir"] = AiDenoise.Dir ?? "";
                settings["dn_model"] = DnModelKey();
                settings["suggest"] = chkSuggest.Checked ? "1" : "0";
                settings["cover"] = cbCover.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["cover_at"] = ((double)nuCoverAt.Value).ToString(CultureInfo.InvariantCulture);
                settings["cover_path"] = coverPath ?? "";
                settings["speedhist"] = string.Join(";", Compression.History.Select(kv => kv.Key + "=" + kv.Value.ToString("0.####", CultureInfo.InvariantCulture)).ToArray());
                settings["vseq"] = cbSeqExport.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["keepvideo"] = chkKeepVideo.Checked ? "1" : "0";
                settings["sdrhdr"] = cbHdr.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["hdrpeak"] = cbHdrPeak.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["sdrhdr_img"] = cbHdrImg.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["hdrpeak_img"] = cbHdrPeakImg.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["existaudio"] = cbExistingAudio.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["deint"] = cbDeint.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["deint_order"] = cbDeintOrder.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["cai_on"] = chkCai.Checked ? "1" : "0";
                settings["cai_model"] = cbCaiModel.SelectedItem as string ?? "";
                settings["cai_keep"] = cbCaiSize.SelectedIndex == 1 ? "1" : "0";
                settings["cai_cpu"] = cbCaiProc.SelectedIndex == 1 ? "1" : "0";
                settings["cai_fp32"] = cbCaiPrec.SelectedIndex == 1 ? "1" : "0";
                settings["cai_tile"] = cbCaiTile.SelectedIndex.ToString(CultureInfo.InvariantCulture);
                settings["opti"] = chkOpti.Checked ? "1" : "0";
                settings["opti_mult"] = cbOptiMult.SelectedIndex.ToString();
                settings["opti_kernel"] = cbOptiKernel.SelectedIndex.ToString();
                settings["opti_sharp"] = tbOptiSharp.Value.ToString();
                settings["opti_contrast"] = chkOptiContrast.Checked ? "1" : "0";
                settings["rtx_vsr"] = chkVsr.Checked ? "1" : "0"; settings["rtx_hdr"] = chkHdr.Checked ? "1" : "0"; settings["rtx_dlss"] = chkDlss.Checked ? "1" : "0";
                settings["vsr_q"] = cbVsrQ.SelectedIndex.ToString(); settings["vsr_scale"] = cbVsrScale.SelectedIndex.ToString();
                settings["dlss_mode"] = cbDlssMode.SelectedIndex.ToString(); settings["rtx_engine2"] = cbRtxEngine.SelectedIndex.ToString();
                settings["hdr_contrast"] = nuHdrContrast.Value.ToString(); settings["hdr_sat"] = nuHdrSat.Value.ToString();
                settings["hdr_gray"] = nuHdrGray.Value.ToString(); settings["hdr_lum"] = nuHdrLum.Value.ToString();
                settings["vsr_expr"] = vsrExpr ?? ""; settings["hdr_expr"] = hdrExpr ?? ""; settings["dlss_expr"] = dlssExpr ?? "";
                if (!string.IsNullOrEmpty(Rtx.ProcessorPath)) settings["rtxproc"] = Rtx.ProcessorPath;
                SaveSeedSettings();
                SaveVisionSettings();
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                File.WriteAllLines(settingsPath, settings.Select(kv => kv.Key + "=" + kv.Value.Replace("\r", "").Replace("\n", "")).ToArray(), Encoding.UTF8);
            }
            catch { }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (running)
            {
                if (MessageBox.Show(this, "A conversion is running. Quit and cancel it?", "FFmpeg Forge", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                { e.Cancel = true; return; }
                cancelRequested = true;
                Thread.Sleep(400);
            }
            PreviewFrames.Cleanup();
            SaveSettings();
        }
    }
}
