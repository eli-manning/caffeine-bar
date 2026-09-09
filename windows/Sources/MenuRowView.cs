using System.Drawing.Drawing2D;

namespace CaffeineBar;

/// What a row shows on its trailing edge.
public abstract record MenuRowAccessory
{
    public sealed record None : MenuRowAccessory;
    public sealed record Chevron(bool Expanded) : MenuRowAccessory;
    public sealed record Checkmark(bool Selected) : MenuRowAccessory;

    public static readonly MenuRowAccessory Empty = new None();
}

/// Colours ported from CustomMenuWindow so both builds read identically.
public static class Palette
{
    public static readonly Color Energy = Color.FromArgb(250, 179, 38);
    public static readonly Color Coffee = Color.FromArgb(184, 133, 82);
    public static readonly Color Style = Color.FromArgb(168, 133, 250);
    public static readonly Color Login = Color.FromArgb(89, 166, 250);
    public static readonly Color LidSleep = Color.FromArgb(102, 217, 153);
    public static readonly Color Red = Color.FromArgb(255, 69, 58);
    public static readonly Color ActiveGreen = Color.FromArgb(77, 217, 115);

    /// The panel body — the dark HUD material the macOS panel forces regardless of theme.
    public static readonly Color Panel = Color.FromArgb(32, 32, 34);

    public static Color White(double alpha) => Color.FromArgb((int)(alpha * 255), 255, 255, 255);

    public static Color Blend(Color color, double alpha) =>
        Color.FromArgb((int)(alpha * 255), color.R, color.G, color.B);
}

/// A single row in the custom menu. Rows aren't native menu items, so hover
/// highlighting, the icon chip, and the accessory glyph are all hand-rolled here.
public sealed class MenuRowView : Control
{
    public const int RowHeight = 30;

    private readonly string _title;
    private readonly string? _symbolName;
    private readonly Color _tint;
    private readonly int _indent;
    private readonly MenuRowAccessory _accessory;
    private readonly Color _titleColor;
    private readonly bool _emphasized;
    private readonly bool _isEnabled;
    private readonly Action? _onSelect;

    private bool _isHighlighted;

    public MenuRowView(
        string title,
        string? symbolName = null,
        Color? tint = null,
        int indent = 14,
        MenuRowAccessory? accessory = null,
        Color? titleColor = null,
        bool emphasized = false,
        bool isEnabled = true,
        Action? onSelect = null)
    {
        _title = title;
        _symbolName = symbolName;
        _tint = tint ?? Color.White;
        _indent = indent;
        _accessory = accessory ?? MenuRowAccessory.Empty;
        _emphasized = emphasized;
        _isEnabled = isEnabled && onSelect is not null;
        _onSelect = onSelect;
        _titleColor = _isEnabled ? titleColor ?? Palette.White(0.92) : Palette.White(0.30);

        Height = RowHeight;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Palette.Panel;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        if (!_isEnabled) return;
        _isHighlighted = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _isHighlighted = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_isEnabled || e.Button != MouseButtons.Left || _onSelect is null) return;

        // Selecting usually rebuilds the menu, which disposes this very row. Let the
        // current event unwind first so we're not destroyed mid-dispatch.
        var form = FindForm();
        if (form is not null) form.BeginInvoke(_onSelect);
        else _onSelect();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Panel);

        if (_isHighlighted)
        {
            using (var background = new SolidBrush(Palette.Blend(_tint, 0.12)))
            using (var path = VectorIcons.RoundedRect(
                new RectangleF(6, 1, Width - 12, Height - 2), 8))
            {
                g.FillPath(background, path);
            }

            using var bar = new SolidBrush(_tint);
            using var barPath = VectorIcons.RoundedRect(new RectangleF(6, 5, 3, Height - 10), 1.5f);
            g.FillPath(bar, barPath);
        }

        int textLeft = _indent;

        if (_symbolName is not null)
        {
            var chipColor = _isEnabled ? _tint : Palette.White(0.25);
            var chipRect = new RectangleF(_indent, (Height - 22) / 2f, 22, 22);
            using (var chipBrush = new SolidBrush(Palette.Blend(chipColor, 0.22)))
            using (var chipPath = VectorIcons.RoundedRect(chipRect, 7))
            {
                g.FillPath(chipBrush, chipPath);
            }

            var glyphRect = new RectangleF(chipRect.X + 5f, chipRect.Y + 5f, 12f, 12f);
            VectorIcons.Draw(g, _symbolName, glyphRect, chipColor, Palette.Panel);

            textLeft = (int)chipRect.Right + 8;
        }

        var font = MenuFonts.Row(_emphasized);
        TextRenderer.DrawText(g, _title, font,
            new Rectangle(textLeft, 0, Width - textLeft - 30, Height),
            _titleColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis);

        DrawAccessory(g);
    }

    private void DrawAccessory(Graphics g)
    {
        switch (_accessory)
        {
            case MenuRowAccessory.Chevron chevron:
            {
                var rect = new RectangleF(Width - 13 - 10, (Height - 10) / 2f, 10, 10);
                VectorIcons.Draw(g, chevron.Expanded ? "chevron.down" : "chevron.right",
                    rect, Palette.White(0.35), Palette.Panel);
                break;
            }

            case MenuRowAccessory.Checkmark { Selected: true }:
            {
                var badge = new RectangleF(Width - 11 - 14, (Height - 14) / 2f, 14, 14);
                using (var brush = new SolidBrush(_tint))
                using (var path = VectorIcons.RoundedRect(badge, 7))
                {
                    g.FillPath(brush, path);
                }
                VectorIcons.Draw(g, "checkmark",
                    new RectangleF(badge.X + 3.5f, badge.Y + 3.5f, 7, 7),
                    Color.Black, _tint);
                break;
            }
        }
    }
}

/// A thin inset divider, standing in for a native menu separator.
public sealed class MenuSeparatorView : Control
{
    public const int SeparatorHeight = 9;

    public MenuSeparatorView()
    {
        Height = SeparatorHeight;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Palette.Panel;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Palette.Panel);
        using var brush = new SolidBrush(Palette.White(0.08));
        e.Graphics.FillRectangle(brush, 14, Height / 2f, Width - 28, 1);
    }
}

/// Header row: a bold brand title plus a coloured status pill (e.g. "ACTIVE · 12m").
public sealed class StatusHeaderView : Control
{
    public const int HeaderHeight = 44;

    private bool _active;
    private string _duration = "";

    public StatusHeaderView()
    {
        Height = HeaderHeight;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Palette.Panel;
    }

    /// `duration` (e.g. "12m") is shown as-is, lowercase unit and all — only the
    /// ACTIVE/INACTIVE word itself is a badge-style caps word.
    public void Configure(bool active, string duration)
    {
        _active = active;
        _duration = duration;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Panel);

        TextRenderer.DrawText(g, "Caffeine Bar", MenuFonts.Title,
            new Point(14, 7), Color.White, TextFormatFlags.NoPadding);

        var color = _active ? Palette.ActiveGreen : Color.White;
        var text = _active ? $"ACTIVE · {_duration}" : "INACTIVE";

        var textSize = TextRenderer.MeasureText(g, text, MenuFonts.Pill, Size.Empty,
            TextFormatFlags.NoPadding);
        var pill = new RectangleF(14, 27, textSize.Width + 14, 16);

        using (var brush = new SolidBrush(Palette.Blend(color, _active ? 0.18 : 0.10)))
        using (var path = VectorIcons.RoundedRect(pill, 8))
        {
            g.FillPath(brush, path);
        }

        TextRenderer.DrawText(g, text, MenuFonts.Pill,
            new Rectangle((int)pill.X + 7, (int)pill.Y, textSize.Width, (int)pill.Height),
            _active ? color : Palette.White(0.5),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// Shared fonts, sized in pixels so they match the macOS point sizes one-for-one.
public static class MenuFonts
{
    private static readonly Font RowRegular = new("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    private static readonly Font RowSemibold = new("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Pixel);

    public static readonly Font Title = new("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Pill = new("Segoe UI", 10f, FontStyle.Bold, GraphicsUnit.Pixel);

    // Tutorial window
    public static readonly Font Heading = new("Segoe UI", 21f, FontStyle.Bold, GraphicsUnit.Pixel);
    public static readonly Font Body = new("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    public static readonly Font Button = new("Segoe UI Semibold", 12f, FontStyle.Regular, GraphicsUnit.Pixel);

    public static Font Row(bool emphasized) => emphasized ? RowSemibold : RowRegular;
}
