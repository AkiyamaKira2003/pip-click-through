using System.Drawing.Drawing2D;

namespace PiPClickThrough;

/// <summary>
/// Fully custom dropdown, not a WinForms ComboBox or ContextMenuStrip.
/// Each item is a rich, keyboard-selectable card in its own rounded popup.
/// </summary>
internal sealed class WindowSelect : Control
{
    private List<PiPWindow> options = new();
    private bool hovered;
    private WindowPickerPopup? popup;
    public PiPWindow? SelectedWindow { get; private set; }

    public WindowSelect()
    {
        Height = 42;
        BackColor = Palette.Surface;
        Font = UiFonts.Create(10);
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ComboBox;
        AccessibleName = "Picture-in-Picture source selector";
        TabStop = true;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    public void UpdateOptions(List<PiPWindow> list)
    {
        long last = SelectedWindow?.Handle ?? 0;
        options = list;
        SelectedWindow = list.FirstOrDefault(w => w.Handle == last) ??
                         list.FirstOrDefault(w => w.StrictMatch) ?? list.FirstOrDefault();
        Invalidate();
    }

    protected override void OnMouseEnter(EventArgs e)
    { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e)
    { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnClick(EventArgs e)
    { OpenPopup(); base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space or Keys.Down or Keys.F4)
        { OpenPopup(); e.Handled = true; }
        base.OnKeyDown(e);
    }

    private void OpenPopup()
    {
        if (options.Count == 0 || popup is { IsDisposed: false }) return;
        var owner = FindForm();
        if (owner == null) return;
        var snapshot = options.ToArray();
        popup = new WindowPickerPopup(snapshot, SelectedWindow?.Handle,
            selected =>
            {
                SelectedWindow = selected;
                Invalidate();
            });
        var screen = Screen.FromControl(this).WorkingArea;
        int width = Math.Min(Math.Max(Width, 360), screen.Width - 24);
        int height = popup.PreferredHeight;
        var point = PointToScreen(new Point(0, Height + 7));
        point.X = Math.Clamp(point.X, screen.Left + 12, screen.Right - width - 12);
        if (point.Y + height > screen.Bottom - 8)
            point.Y = PointToScreen(Point.Empty).Y - height - 7;
        popup.Bounds = new Rectangle(point, new Size(width, height));
        popup.FormClosed += (_, _) => { popup = null; Focus(); Invalidate(); };
        popup.Show(owner);
        Invalidate();
    }

    internal static string BrowserName(PiPWindow? window) =>
        window?.ProcessName.ToLowerInvariant() switch
        {
            "chrome" => "Chrome",
            "msedge" => "Edge",
            "brave" => "Brave",
            "vivaldi" => "Vivaldi",
            "opera" => "Opera",
            "chromium" => "Chromium",
            _ => "Browser"
        };

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var rect = new RectangleF(1, 1, Width - 2, Height - 2);
        bool open = popup is { IsDisposed: false };
        Draw.RoundedFill(e.Graphics, open || hovered
            ? Color.FromArgb(39, 59, 76) : Palette.Elevated, rect, 12);
        Draw.RoundedOutline(e.Graphics, open ? Palette.Cyan :
            hovered ? Color.FromArgb(77, 114, 140) : Palette.Border, rect, 12);
        Draw.Icon(e.Graphics, "picture", new RectangleF(13, 10, 23, 23),
            Palette.Cyan);
        if (SelectedWindow is { } window)
        {
            string browser = BrowserName(window);
            using var brandFont = UiFonts.Create(9.1f, true);
            using var primaryFont = UiFonts.Create(9.8f);
            using var sizeFont = UiFonts.Create(8.8f);
            using var brandBrush = new SolidBrush(Palette.Cyan);
            using var mainBrush = new SolidBrush(Palette.White);
            using var mutedBrush = new SolidBrush(Palette.Muted);
            using var centered = new StringFormat { LineAlignment = StringAlignment.Center,
                Alignment = StringAlignment.Center };
            using var left = new StringFormat { LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap };
            using var right = new StringFormat { LineAlignment = StringAlignment.Center,
                Alignment = StringAlignment.Far };
            float brandW = Math.Min(89, Math.Max(64,
                e.Graphics.MeasureString(browser, brandFont).Width + 20));
            Draw.RoundedFill(e.Graphics, Color.FromArgb(30, 70, 81),
                new RectangleF(44, 9, brandW, 25), 8);
            e.Graphics.DrawString(browser, brandFont, brandBrush,
                new RectangleF(44, 9, brandW, 25), centered);
            float separator = brandW + 54;
            using (var line = new Pen(Color.FromArgb(65, 85, 107)))
                e.Graphics.DrawLine(line, separator, 12, separator, Height - 12);
            string title = string.IsNullOrWhiteSpace(window.Title)
                ? "Picture in picture" : window.Title;
            string resolution = window.Width + " × " + window.Height;
            float titleLeft = separator + 12;
            bool compact = Width < 390;
            float rightPadding = compact ? 43 : 112;
            e.Graphics.DrawString(title, primaryFont, mainBrush,
                new RectangleF(titleLeft, 0, Math.Max(24, Width - titleLeft - rightPadding),
                    Height), left);
            if (!compact)
                e.Graphics.DrawString(resolution, sizeFont, mutedBrush,
                    new RectangleF(Math.Max(titleLeft + 18, Width - 118), 0, 85, Height), right);
        }
        else
        {
            using var emptyBrush = new SolidBrush(Palette.Muted);
            using var emptyFormat = new StringFormat { LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString("No PiP window detected", Font, emptyBrush,
                new RectangleF(46, 0, Width - 89, Height), emptyFormat);
        }
        Draw.Icon(e.Graphics, "chevron", new RectangleF(Width - 33, 11, 20, 20),
            open ? Palette.Cyan : Palette.Muted);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && popup is { IsDisposed: false }) popup.Close();
        base.Dispose(disposing);
    }
}

internal sealed class WindowPickerPopup : Form
{
    private readonly PiPWindow[] options;
    private readonly Action<PiPWindow> selection;
    private readonly Panel scroll = new();
    private readonly List<WindowOptionRow> rows = new();
    private int currentIndex;
    private bool deliberateClose;
    public int PreferredHeight => Math.Min(6, options.Length) * 64 + 53;

    public WindowPickerPopup(PiPWindow[] items, long? current, Action<PiPWindow> onSelected)
    {
        options = items; selection = onSelected;
        currentIndex = Math.Max(0, Array.FindIndex(options, x => x.Handle == current));
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Palette.Surface;
        ForeColor = Palette.White;
        KeyPreview = true;
        DoubleBuffered = true;
        Font = UiFonts.Create(9.4f);

        scroll.BackColor = Palette.Surface;
        scroll.AutoScroll = true;
        Controls.Add(scroll);
        for (int index = 0; index < items.Length; index++)
        {
            int i = index;
            var row = new WindowOptionRow(items[index]) { Selected = i == currentIndex };
            row.Click += (_, _) => Commit(i);
            scroll.Controls.Add(row);
            rows.Add(row);
        }
    }

    private void Commit(int index)
    {
        if (index < 0 || index >= options.Length) return;
        deliberateClose = true;
        selection(options[index]);
        Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate();
        UpdateRows();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!deliberateClose && !IsDisposed) BeginInvoke(new Action(Close));
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Down or Keys.Up)
        {
            int change = e.KeyCode == Keys.Down ? 1 : -1;
            currentIndex = (currentIndex + options.Length + change) % options.Length;
            UpdateRows();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            Commit(currentIndex);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void UpdateRows()
    {
        for (int i = 0; i < rows.Count; i++)
            rows[i].Selected = i == currentIndex;
        if (rows.Count > currentIndex)
            scroll.ScrollControlIntoView(rows[currentIndex]);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (Width < 2 || Height < 2) return;
        scroll.SetBounds(1, 37, Width - 2, Height - 45);
        int innerW = Math.Max(100, scroll.ClientSize.Width - 16);
        for (int i = 0; i < rows.Count; i++)
            rows[i].SetBounds(8, i * 64 + 4, innerW, 59);
        using var round = Draw.Rounded(new RectangleF(0,0,Width,Height), 16);
        var old = Region;
        Region = new Region(round);
        old?.Dispose();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Palette.Surface);
        using var brush = new SolidBrush(Palette.Muted);
        using var font = UiFonts.Create(8.2f, true);
        e.Graphics.DrawString("AVAILABLE OVERLAYS", font, brush, 17, 12);
        using var countBrush = new SolidBrush(Palette.Cyan);
        using var sf = new StringFormat { Alignment = StringAlignment.Far };
        e.Graphics.DrawString(options.Length + " FOUND", font, countBrush,
            new RectangleF(Width - 121, 12, 100, 20), sf);
        using var rule = new Pen(Palette.Border, 1);
        e.Graphics.DrawLine(rule, 13, 36, Width - 13, 36);
        Draw.RoundedOutline(e.Graphics, Color.FromArgb(70, 89, 112),
            new RectangleF(.5f,.5f,Width - 1,Height - 1), 16);
    }
}

internal sealed class WindowOptionRow : Control
{
    private bool hovered, selected;
    public PiPWindow Window { get; }
    public bool Selected { get => selected; set { selected = value; Invalidate(); } }

    public WindowOptionRow(PiPWindow w)
    {
        Window = w;
        BackColor = Palette.Surface;
        Cursor = Cursors.Hand;
        Height = 59;
        AccessibleRole = AccessibleRole.ListItem;
        AccessibleName = w.ToString();
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e)
    { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e)
    { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var r = new RectangleF(1,1,Width - 2,Height - 2);
        Color bg = Selected ? Color.FromArgb(29, 61, 73) :
                   hovered ? Color.FromArgb(39, 52, 71) : Palette.Elevated;
        Draw.RoundedFill(e.Graphics, bg, r, 11);
        if (Selected) Draw.RoundedOutline(e.Graphics,
            Color.FromArgb(79, 158, 158), r, 11);
        Draw.RoundedFill(e.Graphics, Color.FromArgb(37, 78, 85),
            new RectangleF(11, 12, 35, 35), 10);
        Draw.Icon(e.Graphics, "picture",
            new RectangleF(18, 18, 21, 21), Palette.Cyan);
        using var titleFont = UiFonts.Create(10.1f, true);
        using var infoFont = UiFonts.Create(9.1f);
        using var foreground = new SolidBrush(Palette.White);
        using var secondary = new SolidBrush(Palette.Muted);
        using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap };
        string title = string.IsNullOrEmpty(Window.Title) ? "Untitled window" : Window.Title;
        e.Graphics.DrawString(title, titleFont, foreground,
            new RectangleF(60, 9, Width - 118, 23), fmt);
        string sub = WindowSelect.BrowserName(Window) + "   ·   " +
            Window.Width + " × " + Window.Height +
            (Window.StrictMatch ? "   ·   PiP detected" : "   ·   Manual selection");
        e.Graphics.DrawString(sub, infoFont, secondary,
            new RectangleF(60, 32, Width - 104, 18), fmt);
        if (Selected) Draw.Icon(e.Graphics, "circle-check",
            new RectangleF(Width - 36, 17, 23, 23), Palette.Green);
    }
}
