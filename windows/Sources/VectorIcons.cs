using System.Drawing.Drawing2D;

namespace CaffeineBar;

/// Windows has no SF Symbols, and the Segoe icon fonts differ enough between
/// Windows 10 and 11 (and across their glyph revisions) to make the menu look
/// inconsistent. So every glyph the menu uses is drawn here as vector art in a
/// normalized 0..1 box, then scaled into whatever rect the caller wants.
///
/// Names deliberately mirror the SF Symbol names used by the macOS build so the
/// two menu definitions stay line-for-line comparable.
public static class VectorIcons
{
    /// Draws `name` centered in `rect`, tinted `color`. `knockout` fills the
    /// cut-out parts of "filled" glyphs that punch through to the background.
    public static void Draw(Graphics g, string name, RectangleF rect, Color color, Color knockout)
    {
        var previousMode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var state = g.Save();
        g.TranslateTransform(rect.X, rect.Y);
        g.ScaleTransform(rect.Width, rect.Height);

        using var brush = new SolidBrush(color);
        // Pen widths are in the normalized space, so they scale with the glyph.
        using var pen = new Pen(color, 0.10f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        switch (name)
        {
            case "bolt.fill": Bolt(g, brush); break;
            case "cup.and.saucer.fill": Cup(g, brush, pen, filled: true); break;
            case "cup.and.saucer": Cup(g, brush, pen, filled: false); break;
            case "map.fill": Map(g, brush, pen, filled: true); break;
            case "map": Map(g, brush, pen, filled: false); break;
            case "bag.fill": Bag(g, brush, pen, filled: true); break;
            case "bag": Bag(g, brush, pen, filled: false); break;
            case "cart": Cart(g, brush, pen); break;
            case "paintbrush.fill": Paintbrush(g, brush, pen); break;
            case "power": Power(g, pen); break;
            case "laptopcomputer": Laptop(g, brush, pen); break;
            case "xmark.circle.fill": XmarkCircle(g, brush, knockout); break;
            case "questionmark.circle": QuestionCircle(g, pen); break;
            case "checkmark": Checkmark(g, pen); break;
            case "chevron.right": Chevron(g, pen, down: false); break;
            case "chevron.down": Chevron(g, pen, down: true); break;
        }

        g.Restore(state);
        g.SmoothingMode = previousMode;
    }

    private static void Bolt(Graphics g, Brush brush)
    {
        PointF[] points =
        [
            new(0.60f, 0.02f), new(0.20f, 0.56f), new(0.44f, 0.56f),
            new(0.38f, 0.98f), new(0.80f, 0.42f), new(0.54f, 0.42f),
        ];
        g.FillPolygon(brush, points);
    }

    private static void Cup(Graphics g, Brush brush, Pen pen, bool filled)
    {
        using var body = new GraphicsPath();
        body.AddPolygon(new PointF[]
        {
            new(0.16f, 0.28f), new(0.64f, 0.28f), new(0.57f, 0.68f), new(0.23f, 0.68f),
        });

        var handle = new RectangleF(0.60f, 0.34f, 0.26f, 0.26f);
        var saucer = new RectangleF(0.06f, 0.74f, 0.76f, 0.10f);

        if (filled)
        {
            g.FillPath(brush, body);
            using var saucerPath = new GraphicsPath();
            saucerPath.AddEllipse(saucer);
            g.FillPath(brush, saucerPath);
        }
        else
        {
            g.DrawPath(pen, body);
            g.DrawEllipse(pen, saucer);
        }
        // The handle reads better as a stroke in both variants.
        g.DrawArc(pen, handle, -70f, 160f);
    }

    private static void Map(Graphics g, Brush brush, Pen pen, bool filled)
    {
        PointF[] outline =
        [
            new(0.04f, 0.22f), new(0.35f, 0.09f), new(0.65f, 0.22f), new(0.96f, 0.09f),
            new(0.96f, 0.78f), new(0.65f, 0.91f), new(0.35f, 0.78f), new(0.04f, 0.91f),
        ];

        if (filled)
        {
            g.FillPolygon(brush, outline);
        }
        else
        {
            g.DrawPolygon(pen, outline);
            using var fold = new Pen(pen.Color, 0.07f);
            g.DrawLine(fold, 0.35f, 0.09f, 0.35f, 0.78f);
            g.DrawLine(fold, 0.65f, 0.22f, 0.65f, 0.91f);
        }
    }

    private static void Bag(Graphics g, Brush brush, Pen pen, bool filled)
    {
        var body = new RectangleF(0.12f, 0.34f, 0.76f, 0.58f);
        using var path = RoundedRect(body, 0.12f);

        if (filled) g.FillPath(brush, path);
        else g.DrawPath(pen, path);

        // Handle arc rising out of the top of the bag, stroked in both variants.
        using var handlePen = new Pen(pen.Color, 0.09f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
        };
        g.DrawArc(handlePen, new RectangleF(0.30f, 0.12f, 0.40f, 0.40f), 180f, 180f);
    }

    private static void Cart(Graphics g, Brush brush, Pen pen)
    {
        g.DrawLine(pen, 0.04f, 0.16f, 0.20f, 0.16f);
        using var basket = new GraphicsPath();
        basket.AddPolygon(new PointF[]
        {
            new(0.22f, 0.22f), new(0.96f, 0.32f), new(0.84f, 0.62f), new(0.34f, 0.62f),
        });
        g.DrawPath(pen, basket);
        g.DrawLine(pen, 0.20f, 0.16f, 0.30f, 0.55f);
        g.FillEllipse(brush, 0.34f, 0.74f, 0.17f, 0.17f);
        g.FillEllipse(brush, 0.68f, 0.74f, 0.17f, 0.17f);
    }

    private static void Paintbrush(Graphics g, Brush brush, Pen pen)
    {
        using var handle = new Pen(pen.Color, 0.16f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(handle, 0.92f, 0.10f, 0.50f, 0.52f);

        using var bristles = new GraphicsPath();
        bristles.AddPolygon(new PointF[]
        {
            new(0.44f, 0.40f), new(0.62f, 0.58f), new(0.24f, 0.94f), new(0.06f, 0.76f),
        });
        g.FillPath(brush, bristles);
    }

    private static void Power(Graphics g, Pen pen)
    {
        g.DrawArc(pen, new RectangleF(0.14f, 0.20f, 0.72f, 0.72f), -60f, 300f);
        g.DrawLine(pen, 0.50f, 0.04f, 0.50f, 0.46f);
    }

    private static void Laptop(Graphics g, Brush brush, Pen pen)
    {
        using var screen = RoundedRect(new RectangleF(0.16f, 0.16f, 0.68f, 0.48f), 0.06f);
        g.DrawPath(pen, screen);
        using var basePath = new GraphicsPath();
        basePath.AddPolygon(new PointF[]
        {
            new(0.06f, 0.72f), new(0.94f, 0.72f), new(1.00f, 0.84f), new(0.00f, 0.84f),
        });
        g.FillPath(brush, basePath);
    }

    private static void XmarkCircle(Graphics g, Brush brush, Color knockout)
    {
        g.FillEllipse(brush, 0.02f, 0.02f, 0.96f, 0.96f);
        using var cross = new Pen(knockout, 0.13f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(cross, 0.32f, 0.32f, 0.68f, 0.68f);
        g.DrawLine(cross, 0.68f, 0.32f, 0.32f, 0.68f);
    }

    private static void QuestionCircle(Graphics g, Pen pen)
    {
        using var ring = new Pen(pen.Color, 0.09f);
        g.DrawEllipse(ring, 0.06f, 0.06f, 0.88f, 0.88f);

        using var stroke = new Pen(pen.Color, 0.11f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        // Hook of the question mark, then the dot below it.
        using var hook = new GraphicsPath();
        hook.AddBezier(
            new PointF(0.34f, 0.36f), new PointF(0.36f, 0.20f),
            new PointF(0.66f, 0.20f), new PointF(0.62f, 0.40f));
        hook.AddBezier(
            new PointF(0.62f, 0.40f), new PointF(0.59f, 0.50f),
            new PointF(0.50f, 0.50f), new PointF(0.50f, 0.62f));
        g.DrawPath(stroke, hook);

        using var dot = new SolidBrush(pen.Color);
        g.FillEllipse(dot, 0.44f, 0.71f, 0.12f, 0.12f);
    }

    private static void Checkmark(Graphics g, Pen pen)
    {
        using var thick = new Pen(pen.Color, 0.17f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        g.DrawLines(thick, new PointF[] { new(0.16f, 0.52f), new(0.40f, 0.78f), new(0.86f, 0.20f) });
    }

    private static void Chevron(Graphics g, Pen pen, bool down)
    {
        using var thin = new Pen(pen.Color, 0.14f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        PointF[] points = down
            ? [new(0.14f, 0.34f), new(0.50f, 0.68f), new(0.86f, 0.34f)]
            : [new(0.34f, 0.14f), new(0.68f, 0.50f), new(0.34f, 0.86f)];
        g.DrawLines(thin, points);
    }

    /// Rounded rectangle helper. `radius` is in the same normalized units as `rect`.
    public static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
