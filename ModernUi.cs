using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace PiPClickThrough;

internal static class Palette
{
    internal static readonly Color Background = Color.FromArgb(11, 16, 28);
    internal static readonly Color Sidebar = Color.FromArgb(15, 22, 37);
    internal static readonly Color Surface = Color.FromArgb(20, 29, 46);
    internal static readonly Color Elevated = Color.FromArgb(26, 38, 58);
    internal static readonly Color Border = Color.FromArgb(47, 62, 83);
    internal static readonly Color White = Color.FromArgb(246, 249, 255);
    internal static readonly Color Muted = Color.FromArgb(155, 169, 190);
    internal static readonly Color Dim = Color.FromArgb(104, 125, 151);
    internal static readonly Color Cyan = Color.FromArgb(78, 218, 217);
    internal static readonly Color Blue = Color.FromArgb(96, 155, 251);
    internal static readonly Color Green = Color.FromArgb(99, 228, 173);
    internal static readonly Color Red = Color.FromArgb(247, 117, 136);
    internal static Color Blend(Color a, Color b, float t) =>
        Color.FromArgb((int)(a.R * (1 - t) + b.R * t),
            (int)(a.G * (1 - t) + b.G * t), (int)(a.B * (1 - t) + b.B * t));
}

internal static class Draw
{
    internal static GraphicsPath Rounded(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= .01f) { path.AddRectangle(r); return path; }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    internal static void RoundedFill(Graphics g, Color color, RectangleF rect, float radius)
    {
        using var path = Rounded(rect, radius);
        using var brush = new SolidBrush(color);
        g.FillPath(brush, path);
    }

    internal static void RoundedOutline(Graphics g, Color color, RectangleF rect, float radius, float width = 1)
    {
        using var path = Rounded(rect, radius);
        using var pen = new Pen(color, width);
        g.DrawPath(pen, path);
    }

    internal static void Icon(Graphics g, string icon, RectangleF rect, Color color, float stroke = 1.8f)
    {
        IconAssets.Draw(g, icon, rect, color);
    }
}

internal sealed class SurfacePanel : Panel
{
    public int Radius { get; set; } = 18;
    public Color Fill { get; set; } = Palette.Surface;
    public Color Outline { get; set; } = Palette.Border;
    public bool Gradient { get; set; }
    public Color GradientEnd { get; set; } = Color.FromArgb(19, 48, 67);

    public SurfacePanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Palette.Background;
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var bg = new SolidBrush(BackColor);
        e.Graphics.FillRectangle(bg, ClientRectangle);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1, 1, Width - 2, Height - 2);
        using var path = Draw.Rounded(r, Radius);
        if (Gradient)
        {
            using var brush = new LinearGradientBrush(r, Fill, GradientEnd, LinearGradientMode.Horizontal);
            e.Graphics.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(Fill);
            e.Graphics.FillPath(brush, path);
        }
        Draw.RoundedOutline(e.Graphics, Outline, r, Radius);
    }
}

internal sealed class CaptionControl : Control
{
    private bool hover, down;
    public string Glyph { get; set; } = "minus";
    public bool Danger { get; set; }
    public CaptionControl(string glyph)
    {
        Glyph = glyph;
        Size = new Size(42, 36);
        Cursor = Cursors.Hand;
        BackColor = Palette.Background;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = glyph;
    }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        var rect = new RectangleF(2, 2, Width - 4, Height - 4);
        if (hover || down)
            Draw.RoundedFill(e.Graphics,
                down ? (Danger ? Color.FromArgb(170, 48, 69) : Palette.Elevated) :
                Danger ? Color.FromArgb(108, 41, 59) : Color.FromArgb(36, 49, 70), rect, 10);
        Draw.Icon(e.Graphics, Glyph, new RectangleF((Width - 24) / 2f,
            (Height - 24) / 2f, 24, 24), hover && Danger ? Palette.Red : Palette.Muted, 1.7f);
    }
}

internal sealed class ActionButton : Control
{
    private bool hovered, pressed;
    public bool Primary { get; set; }
    public bool Destructive { get; set; }
    public string Glyph { get; set; } = "";
    public ActionButton(string text, bool primary = false)
    {
        Text = text; Primary = primary;
        Font = UiFonts.Create(10, true);
        Height = 45;
        Cursor = Cursors.Hand;
        BackColor = Palette.Surface;
        ForeColor = Palette.White;
        AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new RectangleF(1, 1, Width - 2, Height - 2);
        Color c = Primary ? Palette.Cyan : Destructive ? Color.FromArgb(63, 38, 50) : Palette.Elevated;
        if (hovered) c = Palette.Blend(c, Palette.White, Primary ? .13f : .08f);
        if (pressed) c = Palette.Blend(c, Color.Black, .14f);
        Draw.RoundedFill(e.Graphics, c, r, 13);
        if (!Primary)
            Draw.RoundedOutline(e.Graphics, Destructive ? Color.FromArgb(103, 55, 68) : Palette.Border, r, 13);
        Color textColor = Primary ? Palette.Background : Destructive ? Color.FromArgb(251, 169, 177) : Palette.White;
        using var brush = new SolidBrush(textColor);
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        float offset = !string.IsNullOrEmpty(Glyph) ? 11 : 0;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        e.Graphics.DrawString(Text, Font, brush, new RectangleF(6 + offset, 0, Width - 12 - offset, Height), sf);
        if (!string.IsNullOrEmpty(Glyph))
            Draw.Icon(e.Graphics, Glyph, new RectangleF(15, (Height - 22)/2f, 22, 22), textColor, 1.8f);
    }
}

internal sealed class SwitchControl : Control
{
    private bool checkedValue, hovering;
    public bool Checked
    {
        get => checkedValue;
        set { if (checkedValue == value) return; checkedValue = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? CheckedChanged;
    public SwitchControl()
    {
        Size = new Size(46, 26);
        BackColor = Palette.Surface;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnMouseEnter(EventArgs e) { hovering=true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovering=false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bg = Checked ? Palette.Cyan : Color.FromArgb(60, 76, 96);
        if (hovering) bg = Palette.Blend(bg, Palette.White, .12f);
        Draw.RoundedFill(e.Graphics, bg, new RectangleF(0, 1, Width, Height - 2), 13);
        float x = Checked ? Width - 23 : 3;
        using var brush = new SolidBrush(Checked ? Palette.Background : Palette.White);
        e.Graphics.FillEllipse(brush, x, 4, 19, 19);
    }
}

internal sealed class SeekSlider : Control
{
    private int level = 100;
    private bool dragging;
    public int Value
    {
        get => level;
        set { int v = Math.Clamp(value, 20, 100); if (v == level) return; level = v; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    public event EventHandler? ValueChanged;
    public SeekSlider()
    {
        Height = 31; Cursor = Cursors.Hand; BackColor = Palette.Surface;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    private void SetFromMouse(int x) => Value = 20 + (int)Math.Round(Math.Clamp((x - 9d) / Math.Max(1, Width - 18), 0, 1) * 80);
    protected override void OnMouseDown(MouseEventArgs e) { dragging=true; Capture=true; SetFromMouse(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (dragging) SetFromMouse(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { dragging=false; Capture=false; base.OnMouseUp(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Draw.RoundedFill(e.Graphics, Color.FromArgb(58, 74, 94),
            new RectangleF(9, Height/2f-3, Width-18, 6), 3);
        float t = (level - 20) / 80f;
        float cx = 9 + (Width - 18)*t;
        Draw.RoundedFill(e.Graphics, Palette.Cyan, new RectangleF(9, Height/2f-3,
            Math.Max(1, cx - 9), 6), 3);
        using var glow = new SolidBrush(Color.FromArgb(38, 78, 218, 217));
        e.Graphics.FillEllipse(glow, cx - 12, Height/2f-12, 24, 24);
        using var circle = new SolidBrush(Palette.White);
        e.Graphics.FillEllipse(circle, cx - 7, Height/2f-7, 14, 14);
    }
}

internal sealed class Symbol : Control
{
    public string Glyph { get; set; } = "video";
    public Color SymbolColor { get; set; } = Palette.Cyan;
    public Color FillColor { get; set; } = Color.FromArgb(29, 61, 74);
    public Symbol(string glyph, int size = 44)
    {
        Glyph = glyph; Size = new Size(size, size);
        BackColor = Palette.Background;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Parent is SurfacePanel parent && parent.Gradient)
        {
            using var gradient = new LinearGradientBrush(
                new RectangleF(-Left + 1, -Top + 1, parent.Width-2, parent.Height-2),
                parent.Fill, parent.GradientEnd, LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(gradient, ClientRectangle);
        }
        else e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Draw.RoundedFill(e.Graphics, FillColor, new RectangleF(1,1,Width-2,Height-2), 13);
        Draw.Icon(e.Graphics, Glyph, new RectangleF((Width-28)/2f,(Height-28)/2f,28,28),SymbolColor,1.8f);
    }
}

