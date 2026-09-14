using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace AutoHotspot;

/// <summary>
/// A whole-row on/off toggle: click anywhere on it (or press Space/Enter) to flip it.
/// Draws a green check when on and a grey cross when off, with a title and a subtitle line.
/// </summary>
[DefaultEvent(nameof(CheckedChanged))]
internal sealed class ToggleRow : Control
{
    private static readonly Color OnColor = Color.FromArgb(34, 139, 34);
    private static readonly Color OffColor = Color.FromArgb(150, 150, 150);

    private bool isChecked;
    private bool hovering;

    [Category("Appearance")]
    public string OnTitle { get; set; } = "Enabled";

    [Category("Appearance")]
    public string OnSubtitle { get; set; } = "";

    [Category("Appearance")]
    public string OffTitle { get; set; } = "Disabled";

    [Category("Appearance")]
    public string OffSubtitle { get; set; } = "";

    [Category("Behavior")]
    [DefaultValue(false)]
    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value)
                return;
            isChecked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [Category("Behavior")]
    public event EventHandler? CheckedChanged;

    public ToggleRow()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Focus();
        Checked = !Checked;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Checked = !Checked;
            e.Handled = true;
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Enter || base.IsInputKey(keyData);

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovering = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovering = false; Invalidate(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);

        Color accent = isChecked ? OnColor : OffColor;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        Color fill = isChecked
            ? Color.FromArgb(hovering ? 48 : 28, OnColor)
            : Color.FromArgb(hovering ? 40 : 20, Color.Black);

        using (GraphicsPath rowPath = RoundedRect(bounds, Scale(8)))
        using (var fillBrush = new SolidBrush(fill))
        using (var borderPen = new Pen(Focused ? SystemColors.Highlight : Color.FromArgb(90, accent), Focused ? 2 : 1))
        {
            g.FillPath(fillBrush, rowPath);
            g.DrawPath(borderPen, rowPath);
        }

        int iconSize = Scale(30);
        int padding = Scale(14);
        var iconRect = new Rectangle(padding, (Height - iconSize) / 2, iconSize, iconSize);
        using (var iconBrush = new SolidBrush(accent))
            g.FillEllipse(iconBrush, iconRect);

        using (var glyphPen = new Pen(Color.White, Math.Max(2f, iconSize / 9f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            float x = iconRect.X, y = iconRect.Y, s = iconSize;
            if (isChecked)
            {
                g.DrawLines(glyphPen, new[]
                {
                    new PointF(x + s * 0.28f, y + s * 0.52f),
                    new PointF(x + s * 0.44f, y + s * 0.68f),
                    new PointF(x + s * 0.73f, y + s * 0.36f),
                });
            }
            else
            {
                g.DrawLine(glyphPen, x + s * 0.34f, y + s * 0.34f, x + s * 0.66f, y + s * 0.66f);
                g.DrawLine(glyphPen, x + s * 0.66f, y + s * 0.34f, x + s * 0.34f, y + s * 0.66f);
            }
        }

        int textLeft = iconRect.Right + Scale(12);
        var textWidth = Math.Max(1, Width - textLeft - padding);
        string title = isChecked ? OnTitle : OffTitle;
        string subtitle = isChecked ? OnSubtitle : OffSubtitle;

        using var titleFont = new Font(Font.FontFamily, Font.Size * 1.2f, FontStyle.Bold);
        Size titleSize = TextRenderer.MeasureText(g, title, titleFont);
        Size subtitleSize = TextRenderer.MeasureText(g, subtitle, Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak);
        int blockHeight = titleSize.Height + (subtitle.Length > 0 ? subtitleSize.Height : 0);
        int top = (Height - blockHeight) / 2;

        TextRenderer.DrawText(g, title, titleFont, new Point(textLeft, top), isChecked ? OnColor : ForeColor);
        if (subtitle.Length > 0)
        {
            TextRenderer.DrawText(g, subtitle, Font, new Rectangle(textLeft, top + titleSize.Height, textWidth, subtitleSize.Height),
                ForeColor, TextFormatFlags.WordBreak);
        }
    }

    private int Scale(int logicalPixels) => (int)Math.Round(logicalPixels * DeviceDpi / 96.0);

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
