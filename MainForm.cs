using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.IO.Pipes;
using System.Text.Json;

namespace PiPClickThrough;

internal sealed partial class MainForm : Form
{
    private readonly WindowController manager;
    private readonly AppSettings settings;
    private readonly System.Windows.Forms.Timer poller = new() { Interval = 900 };
    private readonly CancellationTokenSource cliCancellation = new();
    private readonly NotifyIcon tray;
    private readonly List<PiPWindow> empty = new();
    private List<PiPWindow> scan = new();
    private bool exiting, settingControls, uiReady;

    private readonly Panel sidebar = new(), header = new();
    private readonly SurfacePanel hero = new(), deviceCard = new(), settingsCard = new(), feedCard = new();
    private readonly Symbol logo = new("video", 43), heroSymbol = new("cursor", 52);
    private readonly CaptionControl minButton = new("minus"), maxButton = new("maximize"),
        closeButton = new("close") { Danger = true };
    private readonly Label brand = new(), brandSubtitle = new(), headerBadge = new();
    private readonly Label pageTitle = new(), pageDescription = new();
    private readonly Label sidebarSection = new(), sidebarNav = new(), sidebarCaption = new();
    private readonly Label sidebarFooter = new(), sidebarVersion = new();
    private readonly Label heroEyebrow = new(), heroTitle = new(), heroDescription = new();
    private readonly Label activeLabel = new(), deviceTitle = new(), deviceDescription = new();
    private readonly Label settingsTitle = new(), settingsDescription = new();
    private readonly Label autoTitle = new(), autoDescription = new();
    private readonly Label clickTitle = new(), clickDescription = new();
    private readonly Label topTitle = new(), topDescription = new();
    private readonly Label opacityTitle = new(), opacityValue = new();
    private readonly Label feedTitle = new(), feedText = new(), feedNote = new();
    private readonly WindowSelect windows = new();
    private readonly SwitchControl autoDetect = new(), autoEnable = new(), stayTop = new();
    private readonly SeekSlider alpha = new();
    private readonly ActionButton toggle = new("Switch to interactive", true) { Glyph = "cursor" };
    private readonly ActionButton restore = new("Restore all") { Glyph = "restore-all" };
    private readonly ActionButton capture = new("Capture window", true) { Glyph = "arrow" };
    private readonly ActionButton refresh = new("Rescan");
    private readonly ContextMenuStrip trayMenu = new();

    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd,
        int attribute, ref int value, int cbAttribute);

    public MainForm()
    {
        Text = "PiP ClickThrough";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        AutoScaleMode = AutoScaleMode.None;
        Size = new Size(1020, 746);
        MinimumSize = new Size(860, 730);
        BackColor = Palette.Background;
        ForeColor = Palette.White;
        Font = UiFonts.Create(10);
        ShowInTaskbar = true;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);

        settings = AppSettings.Load();
        manager = new WindowController(AppSettings.DataDirectory);
        manager.Status += WriteLog;
        manager.SetClickThrough(settings.AutoEnable);
        manager.SetAlwaysOnTop(settings.AlwaysOnTop);
        manager.SetOpacity(settings.Opacity);

        trayMenu.BackColor = Palette.Surface;
        trayMenu.ForeColor = Palette.White;
        trayMenu.Items.Add("Open control center", null, (_, _) => ShowWindow());
        trayMenu.Items.Add("Toggle click-through", null, (_, _) => Toggle());
        trayMenu.Items.Add("Restore all", null, (_, _) => RestoreAll());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit and restore windows", null, (_, _) => ExitApplication());
        tray = new NotifyIcon
        {
            Text = "PiP ClickThrough", Icon = SystemIcons.Application,
            Visible = true, ContextMenuStrip = trayMenu
        };
        tray.DoubleClick += (_, _) => ShowWindow();

        Compose();
        uiReady = true;
        LayoutInterface();
        ShapeWindow();
        poller.Tick += (_, _) => ScanAndUpdate();
        poller.Start();
        Shown += (_, _) => { ScanAndUpdate(); StartCliServer(); };
        WriteLog("Ready. Watching for Picture-in-Picture windows.");
        WriteLog("F10 toggles click-through. F12 restores all windows.");
    }

    private static void LabelStyle(Label l, string text, float size, Color color,
        bool bold = false, ContentAlignment alignment = ContentAlignment.MiddleLeft)
    {
        l.Text = text;
        l.Font = UiFonts.Create(size, bold);
        l.ForeColor = color;
        l.BackColor = Color.Transparent;
        l.TextAlign = alignment;
        l.AutoEllipsis = true;
    }

    private void Compose()
    {
        SuspendLayout();
        // Custom draggable header: none of the native titlebar or caption glyphs remain.
        header.BackColor = Palette.Background;
        sidebar.BackColor = Palette.Sidebar;
        Controls.Add(header);
        Controls.Add(sidebar);
        header.Controls.Add(logo);
        logo.BackColor = Palette.Background;
        LabelStyle(brand, "PiP / FLOW", 15.5f, Palette.White, true);
        LabelStyle(brandSubtitle, "WINDOW CONTROL STUDIO", 8.1f, Palette.Dim, true);
        LabelStyle(headerBadge, "NATIVE WINDOWS  ·  v1.1", 8.5f, Palette.Dim, true,
            ContentAlignment.MiddleRight);
        header.Controls.AddRange(new Control[] { brand, brandSubtitle, headerBadge,
            minButton, maxButton, closeButton });
        minButton.AccessibleName = "Minimize";
        maxButton.AccessibleName = "Maximize or restore";
        closeButton.AccessibleName = "Close to tray";
        minButton.Click += (_, _) => WindowState = FormWindowState.Minimized;
        maxButton.Click += (_, _) => ToggleMaximize();
        closeButton.Click += (_, _) => Close();
        header.MouseDown += (_, e) => BeginDrag(e);
        brand.MouseDown += (_, e) => BeginDrag(e);
        brandSubtitle.MouseDown += (_, e) => BeginDrag(e);
        logo.MouseDown += (_, e) => BeginDrag(e);
        header.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleMaximize(); };
        brand.MouseDoubleClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleMaximize(); };

        LabelStyle(sidebarSection, "WORKSPACE", 8.4f, Palette.Dim, true);
        LabelStyle(sidebarNav, "   ◈   Control center", 10, Palette.Cyan, true);
        LabelStyle(sidebarCaption, "   Video overlay management", 8.8f, Palette.Muted);
        LabelStyle(sidebarFooter, "SHORTCUTS", 8.4f, Palette.Dim, true);
        LabelStyle(sidebarVersion,
            "Ctrl + Shift + F9     Open\n\n" +
            "Ctrl + Shift + F10   Toggle\n\n" +
            "Ctrl + Shift + F12   Restore", 8.3f, Palette.Muted);
        sidebar.Controls.AddRange(new Control[] { sidebarSection, sidebarNav,
            sidebarCaption, sidebarFooter, sidebarVersion });

        LabelStyle(pageTitle, "Picture-in-Picture", 23, Palette.White, true);
        LabelStyle(pageDescription, "Make every window work around your workflow.",
            10, Palette.Muted);
        Controls.Add(pageTitle);
        Controls.Add(pageDescription);

        hero.Fill = Color.FromArgb(20, 46, 62);
        hero.GradientEnd = Color.FromArgb(24, 37, 70);
        hero.Outline = Color.FromArgb(45, 90, 110);
        hero.Gradient = true;
        hero.Radius = 22;
        Controls.Add(hero);
        heroSymbol.FillColor = Color.FromArgb(31, 81, 91);
        hero.Controls.Add(heroSymbol);
        LabelStyle(heroEyebrow, "OVERLAY STATUS", 8.5f, Palette.Cyan, true);
        LabelStyle(heroTitle, "Finding your PiP window", 15, Palette.White, true);
        LabelStyle(heroDescription, "Ready for seamless multitasking", 9.5f, Palette.Muted);
        hero.Controls.AddRange(new Control[] { heroEyebrow, heroTitle, heroDescription, toggle });

        deviceCard.Radius = 19;
        Controls.Add(deviceCard);
        LabelStyle(activeLabel, "01  /  ACTIVE SOURCE", 8.5f, Palette.Cyan, true);
        LabelStyle(deviceTitle, "Detected windows", 14, Palette.White, true);
        LabelStyle(deviceDescription, "Select your browser overlay, then capture it.",
            9, Palette.Muted);
        deviceCard.Controls.AddRange(new Control[] { activeLabel, deviceTitle, deviceDescription,
            windows, capture, refresh });
        windows.BackColor = Palette.Surface;
        windows.ForeColor = Palette.White;
        windows.Font = UiFonts.Create(10);
        capture.Click += (_, _) => CaptureSelected();
        refresh.Click += (_, _) => ScanAndUpdate();

        settingsCard.Radius = 19;
        Controls.Add(settingsCard);
        LabelStyle(settingsTitle, "02  /  PREFERENCES", 8.5f, Palette.Cyan, true);
        LabelStyle(settingsDescription, "Fine-tune the way your overlay behaves.", 9, Palette.Muted);
        LabelStyle(autoTitle, "Auto-detect PiP", 10, Palette.White, true);
        LabelStyle(autoDescription, "Watch for new video windows", 8.6f, Palette.Muted);
        LabelStyle(clickTitle, "Click-through by default", 10, Palette.White, true);
        LabelStyle(clickDescription, "Let clicks reach the app below", 8.6f, Palette.Muted);
        LabelStyle(topTitle, "Always on top", 10, Palette.White, true);
        LabelStyle(topDescription, "Keep the video visible", 8.6f, Palette.Muted);
        LabelStyle(opacityTitle, "Video opacity", 10, Palette.White, true);
        LabelStyle(opacityValue, "100%", 11, Palette.Cyan, true, ContentAlignment.MiddleRight);
        settingsCard.Controls.AddRange(new Control[] {
            settingsTitle, settingsDescription, autoTitle, autoDescription,
            clickTitle, clickDescription, topTitle, topDescription, opacityTitle, opacityValue,
            autoDetect, autoEnable, stayTop, alpha
        });
        autoDetect.BackColor = Palette.Surface;
        autoEnable.BackColor = Palette.Surface;
        stayTop.BackColor = Palette.Surface;
        alpha.BackColor = Palette.Surface;
        autoDetect.Checked = settings.AutoDetect;
        autoEnable.Checked = settings.AutoEnable;
        stayTop.Checked = settings.AlwaysOnTop;
        alpha.Value = settings.Opacity;
        opacityValue.Text = alpha.Value + "%";
        autoDetect.CheckedChanged += (_, _) => {
            if (settingControls) return;
            settings.AutoDetect = autoDetect.Checked; Save();
        };
        autoEnable.CheckedChanged += (_, _) => {
            if (settingControls) return;
            settings.AutoEnable = autoEnable.Checked;
            manager.SetClickThrough(settings.AutoEnable);
            Save(); UpdateStatus();
        };
        stayTop.CheckedChanged += (_, _) => {
            if (settingControls) return;
            settings.AlwaysOnTop = stayTop.Checked;
            manager.SetAlwaysOnTop(stayTop.Checked); Save();
        };
        alpha.ValueChanged += (_, _) => {
            if (settingControls) return;
            opacityValue.Text = alpha.Value + "%";
            settings.Opacity = alpha.Value;
            manager.SetOpacity(alpha.Value); Save();
        };

        feedCard.Radius = 16;
        feedCard.Fill = Palette.Sidebar;
        feedCard.Outline = Palette.Border;
        Controls.Add(feedCard);
        LabelStyle(feedTitle, "ACTIVITY", 8.5f, Palette.Cyan, true);
        LabelStyle(feedText, "Ready to connect", 9.5f, Palette.White, true);
        LabelStyle(feedNote, "LOCAL  ·  SECURE", 8.2f, Palette.Dim, true,
            ContentAlignment.MiddleRight);
        feedCard.Controls.AddRange(new Control[] { feedTitle, feedText, feedNote, restore });
        restore.Destructive = true;
        restore.Click += (_, _) => RestoreAll();
        toggle.Click += (_, _) => Toggle();

        ResumeLayout(false);
        UpdateStatus();
    }

    private void LayoutInterface()
    {
        if (!uiReady || ClientSize.Width < 600 || ClientSize.Height < 550) return;
        SuspendLayout();
        int width = ClientSize.Width, height = ClientSize.Height;
        int side = 212;
        int x = side + 28, right = 28;
        int contentW = width - x - right;
        header.SetBounds(0, 0, width, 70);
        sidebar.SetBounds(0, 70, side, height - 70);
        logo.SetBounds(24, 13, 43, 43);
        brand.SetBounds(80, 13, 300, 26);
        brandSubtitle.SetBounds(82, 40, 300, 16);
        closeButton.SetBounds(width - 57, 17, 42, 36);
        maxButton.SetBounds(width - 105, 17, 42, 36);
        minButton.SetBounds(width - 153, 17, 42, 36);
        headerBadge.SetBounds(width - 362, 22, 193, 25);
        sidebarSection.SetBounds(24, 37, 160, 22);
        sidebarNav.SetBounds(16, 77, side - 32, 43);
        sidebarCaption.SetBounds(20, 125, side - 28, 24);
        sidebarFooter.SetBounds(24, Math.Max(240, height - 280), side - 40, 24);
        sidebarVersion.SetBounds(24, Math.Max(265, height - 252), side - 34, 132);
        pageTitle.SetBounds(x, 87, contentW, 42);
        pageDescription.SetBounds(x + 2, 129, contentW, 27);

        int hHero = 113, yHero = 171;
        hero.SetBounds(x, yHero, contentW, hHero);
        heroSymbol.SetBounds(20, 31, 52, 52);
        heroEyebrow.SetBounds(89, 18, Math.Max(170, contentW - 370), 22);
        heroTitle.SetBounds(87, 41, Math.Max(180, contentW - 348), 29);
        heroDescription.SetBounds(89, 72, Math.Max(170, contentW - 355), 23);
        toggle.SetBounds(Math.Max(300, contentW - 247), 35, 226, 46);

        int yDev = 298, hDev = 153;
        deviceCard.SetBounds(x, yDev, contentW, hDev);
        activeLabel.SetBounds(23, 13, contentW - 46, 18);
        deviceTitle.SetBounds(23, 36, contentW - 46, 27);
        deviceDescription.SetBounds(23, 63, contentW - 46, 20);
        windows.SetBounds(22, 97, Math.Max(160, contentW - 323), 42);
        capture.SetBounds(contentW - 286, 97, 182, 42);
        refresh.SetBounds(contentW - 96, 97, 75, 42);

        int yPrefs = 465, hPrefs = Math.Max(184, height - 663);
        settingsCard.SetBounds(x, yPrefs, contentW, hPrefs);
        settingsTitle.SetBounds(23, 10, contentW - 46, 24);
        settingsDescription.SetBounds(23, 35, contentW - 46, 22);
        int leftW = (contentW - 60) / 2;
        int rightX = leftW + 28;
        autoTitle.SetBounds(23, 70, leftW - 75, 25);
        autoDescription.SetBounds(23, 94, leftW - 75, 20);
        autoDetect.SetBounds(leftW - 27, 73, 46, 26);
        clickTitle.SetBounds(23, 120, leftW - 75, 25);
        clickDescription.SetBounds(23, 144, leftW - 75, 20);
        autoEnable.SetBounds(leftW - 27, 123, 46, 26);
        topTitle.SetBounds(rightX, 70, contentW - rightX - 90, 25);
        topDescription.SetBounds(rightX, 94, contentW - rightX - 90, 20);
        stayTop.SetBounds(contentW - 72, 73, 46, 26);
        opacityTitle.SetBounds(rightX, 120, 136, 25);
        opacityValue.SetBounds(contentW - 90, 120, 65, 25);
        alpha.SetBounds(rightX + 140, 123, Math.Max(50, contentW - rightX - 240), 30);

        int yFeed = yPrefs + hPrefs + 14;
        int hFeed = Math.Max(60, height - yFeed - 20);
        feedCard.SetBounds(x, yFeed, contentW, hFeed);
        feedTitle.SetBounds(20, 12, 130, 20);
        feedText.SetBounds(20, 33, Math.Max(180, contentW - 220), 24);
        feedNote.SetBounds(Math.Max(160, contentW - 355), 12, 165, 22);
        restore.SetBounds(contentW - 173, 13, 154, 43);

        ResumeLayout(true);
        Invalidate();
    }

    private void ShapeWindow()
    {
        if (Width <= 1 || Height <= 1) return;
        var old = Region;
        if (WindowState == FormWindowState.Maximized)
            Region = null;
        else
        {
            using var shape = Draw.Rounded(new RectangleF(0, 0, Width, Height), 16.5f);
            Region = new Region(shape);
        }
        old?.Dispose();
        maxButton.Glyph = WindowState == FormWindowState.Maximized ? "restore" : "maximize";
        maxButton.Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (uiReady) { ShapeWindow(); LayoutInterface(); }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Palette.Background);
        e.Graphics.FillRectangle(brush, ClientRectangle);
        using var pen = new Pen(Palette.Border, 1);
        e.Graphics.DrawLine(pen, 0, 69, Width, 69);
        e.Graphics.DrawLine(pen, 211, 70, 211, Height);
        if (WindowState != FormWindowState.Maximized)
            Draw.RoundedOutline(e.Graphics, Color.FromArgb(54, 69, 93),
                new RectangleF(.5f,.5f,Width - 1.5f,Height - 1.5f), 16);
    }

    private void BeginDrag(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
    }
    private void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
            WindowState = FormWindowState.Normal;
        else
        {
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
            WindowState = FormWindowState.Maximized;
        }
        ShapeWindow();
        LayoutInterface();
    }

    private void WriteLog(string message)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => WriteLog(message)); return; }
        feedText.Text = message;
        if (uiReady) feedText.Invalidate();
    }
    private void Save() => settings.Save();

    private void UpdateStatus()
    {
        int count = manager.Handles.Count;
        heroTitle.Text = count == 0 ? "Waiting for your video" :
            manager.ClickThrough ? "Click-through is active" : "Interactive mode enabled";
        heroDescription.Text = count == 0
            ? "Open a Picture-in-Picture window to begin."
            : manager.ClickThrough
                ? "Keep watching while clicking through the video."
                : "Move, resize or control your video normally.";
        heroEyebrow.Text = count == 0 ? "READY TO CONNECT" :
            manager.ClickThrough ? "●  LIVE  /  PASS-THROUGH" : "●  LIVE  /  INTERACTIVE";
        toggle.Text = manager.ClickThrough ? "Interactive mode" : "Enable click-through";
        headerBadge.Text = count == 0 ? "NO WINDOW CONNECTED" :
            count + " WINDOW" + (count != 1 ? "S" : "") + " CONNECTED";
        toggle.Invalidate();
    }

    private void ScanAndUpdate()
    {
        if (!uiReady) return;
        try
        {
            manager.Enforce();
            scan = PiPDetector.Scan();
            windows.UpdateOptions(scan);
            if (settings.AutoDetect && settings.AutoEnable)
            {
                foreach (var target in scan.Where(w => w.StrictMatch && !manager.Handles.Contains(w.Handle)))
                {
                    try { manager.Capture(target); }
                    catch (Exception ex) { WriteLog("Auto capture failed: " + ex.Message); }
                }
            }
            UpdateStatus();
        }
        catch (Exception ex) { WriteLog("Detection error: " + ex.Message); }
    }


    private void CaptureSelected()
    {
        if (windows.SelectedWindow is not PiPWindow selected)
        {
            MessageBox.Show(this, "No browser PiP candidate selected.", Text);
            return;
        }
        if (!selected.StrictMatch)
        {
            var screen = Screen.FromHandle(selected.Hwnd).Bounds;
            if (selected.Width > screen.Width * .88 || selected.Height > screen.Height * .88)
            {
                MessageBox.Show(this, "Refusing a near-fullscreen browser window.", Text);
                return;
            }
            if (MessageBox.Show(this,
                "This window is not a strict PiP match.\n" + selected +
                "\n\nCapture anyway? Ctrl+Shift+F12 always restores.",
                "Confirm manual capture", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        try
        {
            if (!manager.ClickThrough) SetClickThrough(true);
            manager.Capture(selected); UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Capture failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
    private void SetClickThrough(bool next)
    {
        settingControls = true;
        autoEnable.Checked = next;
        settings.AutoEnable = next;
        settingControls = false;
        manager.SetClickThrough(next);
        Save(); UpdateStatus();
    }
    private void Toggle() => SetClickThrough(!manager.ClickThrough);
    private void RestoreAll()
    {
        SetClickThrough(false);
        manager.RestoreAll(); Save(); UpdateStatus();
    }
    private void ShowWindow()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }
    private void ExitApplication()
    {
        exiting = true; Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // DWM hints on Win11. The shape region remains an explicit fallback on Win10.
        try
        {
            int dark = 1, rounded = 2;
            int noBorder = unchecked((int)0xFFFFFFFE);
            DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            DwmSetWindowAttribute(Handle, 33, ref rounded, 4);
            DwmSetWindowAttribute(Handle, 34, ref noBorder, 4);
        }
        catch { /* On older Windows these hints can be unsupported. */ }
        uint mods = Native.MOD_CONTROL | Native.MOD_SHIFT | Native.MOD_NOREPEAT;
        var keys = new[] { Keys.F9, Keys.F10, Keys.Prior, Keys.Next, Keys.F12 };
        for (int i = 0; i < keys.Length; i++)
        {
            if (!Native.RegisterHotKey(Handle, i+1, mods, (uint)keys[i]))
                WriteLog("Hotkey Ctrl+Shift+" + keys[i] + " is unavailable.");
        }
    }
    protected override void OnHandleDestroyed(EventArgs e)
    {
        for (int i=1; i<=5; i++) Native.UnregisterHotKey(Handle,i);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0084 && WindowState == FormWindowState.Normal && uiReady)
        {
            base.WndProc(ref m);
            if (m.Result.ToInt64() != 1) return;
            long lp = m.LParam.ToInt64();
            int px = unchecked((short)(lp & 0xffff));
            int py = unchecked((short)((lp >> 16) & 0xffff));
            Point p = PointToClient(new Point(px,py));
            const int grip = 7;
            bool left = p.X <= grip, right = p.X >= Width-grip;
            bool top = p.Y <= grip, bottom = p.Y >= Height-grip;
            if (top && left) m.Result = (IntPtr)13;
            else if (top && right) m.Result = (IntPtr)14;
            else if (bottom && left) m.Result = (IntPtr)16;
            else if (bottom && right) m.Result = (IntPtr)17;
            else if (left) m.Result = (IntPtr)10;
            else if (right) m.Result = (IntPtr)11;
            else if (top) m.Result = (IntPtr)12;
            else if (bottom) m.Result = (IntPtr)15;
            else if (p.Y < 70 && p.X < Width-170) m.Result = (IntPtr)2;
            return;
        }
        if (m.Msg == Native.WM_HOTKEY)
        {
            switch (m.WParam.ToInt32())
            {
                case 1: ShowWindow(); break;
                case 2: Toggle(); break;
                case 3: alpha.Value = Math.Min(100, alpha.Value+10); break;
                case 4: alpha.Value = Math.Max(20, alpha.Value-10); break;
                case 5: RestoreAll(); break;
            }
        }
        base.WndProc(ref m);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && settings.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            tray.BalloonTipTitle = "PiP ClickThrough is still running";
            tray.BalloonTipText = "Open from the tray, or press Ctrl+Shift+F9.";
            tray.ShowBalloonTip(1400);
            return;
        }
        poller.Stop();
        cliCancellation.Cancel();
        manager.Dispose();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosing(e);
    }
}
