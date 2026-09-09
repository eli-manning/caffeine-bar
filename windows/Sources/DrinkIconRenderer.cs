using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.Win32;

namespace CaffeineBar;

/// Draws the tray icon for the current style — a coffee cup with rising steam, or
/// a can that sprouts wings — and animates the transition between the empty
/// (inactive) and filled (active) states.
///
/// macOS animates Core Animation layers inside the status item's view. The Windows
/// tray has no such thing: an icon is a static bitmap, so animation here means
/// re-rendering a frame on a timer and handing the shell a new icon each tick.
/// Timings and easing below are ported from the CoffeeIconView layer animations so
/// the two builds feel the same.
public sealed class DrinkIconRenderer : IDisposable
{
    // Ported timings (seconds).
    private const double CrossfadeDuration = 0.35;
    private const double PopSettleDuration = 0.60;
    private const double WingGrowDuration = 0.30;
    private const double WingFlapDuration = 1.10;
    private const double WingsHoldDuration = 1.90;
    private const double WingShrinkDuration = 0.32;
    private const double SteamDuration = 1.30;
    private const double SteamStagger = 0.35;
    private const int SteamRepeats = 2;

    private static readonly double WingCollapseDelay =
        PopSettleDuration + WingFlapDuration + WingsHoldDuration;

    private readonly Stopwatch _clock = new();
    private bool _animating;

    private IconStyle _style = IconStyle.EnergyDrink;
    private Color _tint = Color.White;
    private int _size = 16;

    /// Cached base artwork (outline + fill) so a frame only composites two bitmaps
    /// plus the live wing/steam overlay instead of redrawing the whole can.
    private Bitmap? _outlineArt;
    private Bitmap? _fillArt;
    private bool _artDirty = true;

    public bool IsFilled { get; private set; }

    public IconStyle Style
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            _artDirty = true;
            // Style changed mid-flight: drop any wings/steam belonging to the old
            // style rather than leaving a stale overlay on the new artwork.
            _animating = false;
            _clock.Reset();
        }
    }

    /// True while a frame timer still needs to tick.
    public bool IsAnimating => _animating;

    public DrinkIconRenderer()
    {
        RefreshAppearance();
    }

    /// Re-reads the taskbar theme (the NSColor.labelColor equivalent) and the DPI
    /// scaled tray icon size. Call on theme or DPI changes.
    public void RefreshAppearance()
    {
        var size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        var tint = TaskbarUsesLightTheme() ? Color.FromArgb(28, 28, 30) : Color.White;

        if (size != _size || tint != _tint)
        {
            _size = size;
            _tint = tint;
            _artDirty = true;
        }
    }

    private static bool TaskbarUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

    /// Mirrors setFilled(_:animated:). Returns true if a frame timer should run.
    public bool SetFilled(bool filled, bool animated)
    {
        if (filled == IsFilled && animated) return _animating;

        IsFilled = filled;
        if (animated)
        {
            _animating = true;
            _clock.Restart();
        }
        else
        {
            _animating = false;
            _clock.Reset();
        }
        return _animating;
    }

    /// Total wall-clock length of the current transition, after which the icon is static.
    private double AnimationLength
    {
        get
        {
            if (!IsFilled) return PopSettleDuration;
            return _style switch
            {
                IconStyle.EnergyDrink => WingCollapseDelay + WingShrinkDuration,
                _ => SteamStagger + SteamDuration * SteamRepeats,
            };
        }
    }

    /// Renders the current frame. The caller owns the returned icon and must destroy
    /// its handle once the shell has been handed a replacement.
    public Icon RenderIcon()
    {
        EnsureArt();

        var t = _animating ? _clock.Elapsed.TotalSeconds : double.MaxValue;
        if (_animating && t >= AnimationLength)
        {
            _animating = false;
            _clock.Reset();
            t = double.MaxValue;
        }

        using var frame = new Bitmap(_size, _size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(frame))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Color.Transparent);

            DrawFrame(g, t);
        }

        var handle = frame.GetHicon();
        try
        {
            // Clone off the handle-backed icon so the caller holds a managed copy and
            // we can free the GDI handle right away rather than leaking one per frame.
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            Native.DestroyIcon(handle);
        }
    }

    private void DrawFrame(Graphics g, double t)
    {
        var animating = t != double.MaxValue;

        // Crossfade between the outline and filled artwork.
        double progress = animating ? Clamp01(t / CrossfadeDuration) : 1.0;
        double fillAlpha = IsFilled ? progress : 1 - progress;
        double outlineAlpha = 1 - fillAlpha;

        // The container "pop" spring both states share.
        double popScale = 1.0;
        if (animating && t < PopSettleDuration)
        {
            double from = IsFilled ? 0.82 : 1.12;
            popScale = from + (1.0 - from) * Spring(t / PopSettleDuration);
        }

        var state = g.Save();
        g.TranslateTransform(_size / 2f, _size / 2f);
        g.ScaleTransform((float)popScale, (float)popScale);
        g.TranslateTransform(-_size / 2f, -_size / 2f);

        if (outlineAlpha > 0.001 && _outlineArt is not null) DrawWithAlpha(g, _outlineArt, outlineAlpha);
        if (fillAlpha > 0.001 && _fillArt is not null) DrawWithAlpha(g, _fillArt, fillAlpha);

        if (animating && IsFilled)
        {
            if (_style == IconStyle.EnergyDrink) DrawWings(g, t);
            else DrawSteam(g, t);
        }

        g.Restore(state);
    }

    private static void DrawWithAlpha(Graphics g, Bitmap image, double alpha)
    {
        if (alpha >= 0.999)
        {
            g.DrawImage(image, 0, 0, image.Width, image.Height);
            return;
        }

        var matrix = new ColorMatrix { Matrix33 = (float)alpha };
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height),
            0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }

    // MARK: - Wings (energy drink)

    private void DrawWings(Graphics g, double t)
    {
        double opacity;
        double scale;

        if (t < WingCollapseDelay)
        {
            opacity = Clamp01(t / WingGrowDuration);
            scale = t < PopSettleDuration ? 0.001 + 0.999 * Spring(t / PopSettleDuration) : 1.0;
        }
        else
        {
            double p = Clamp01((t - WingCollapseDelay) / WingShrinkDuration);
            opacity = 1 - p;
            scale = Math.Max(0.001, 1.0 - p);
        }
        if (opacity <= 0.001 || scale <= 0.002) return;

        // One long stroke once the pop-in has settled, then hold still.
        double rotation = 0;
        if (t >= PopSettleDuration && t < PopSettleDuration + WingFlapDuration)
        {
            double p = (t - PopSettleDuration) / WingFlapDuration;
            const double flapAngle = 13.0;
            double[] values = [0, flapAngle, -flapAngle * 0.25, 0];
            double[] keyTimes = [0, 0.45, 0.8, 1.0];
            for (int i = 1; i < keyTimes.Length; i++)
            {
                if (p > keyTimes[i]) continue;
                double segment = (p - keyTimes[i - 1]) / (keyTimes[i] - keyTimes[i - 1]);
                rotation = values[i - 1] + (values[i] - values[i - 1]) * EaseInOut(segment);
                break;
            }
        }

        // The can artwork is aspect-fit from a 16x20 canvas, so find where it actually
        // lands before lining the wings up with the body's edges.
        var image = CanvasRect();
        float leftEdgeX = image.X + image.Width * 0.1875f;
        float rightEdgeX = image.X + image.Width * 0.8125f;
        // Canvas coordinates are bottom-up; the bitmap is top-down.
        float anchorY = image.Bottom - image.Height * 0.54f;

        float w = image.Width * 0.55f;
        float h = image.Height * 0.36f;

        using var brush = new SolidBrush(Color.FromArgb((int)(0.95 * 255 * opacity), 255, 255, 255));
        using var pen = new Pen(Color.FromArgb((int)(0.6 * 255 * opacity), 153, 153, 153), 0.4f);

        DrawWing(g, brush, pen, new PointF(leftEdgeX, anchorY), w, h, scale, rotation, flipped: false);
        DrawWing(g, brush, pen, new PointF(rightEdgeX, anchorY), w, h, scale, rotation, flipped: true);
    }

    private static void DrawWing(Graphics g, Brush brush, Pen pen, PointF position,
        float w, float h, double scale, double rotationDegrees, bool flipped)
    {
        var state = g.Save();
        g.TranslateTransform(position.X, position.Y);
        g.RotateTransform((float)rotationDegrees);
        g.ScaleTransform((float)scale, (float)scale);
        // Anchor: trailing edge for the left wing, leading edge for the right one.
        g.TranslateTransform(flipped ? 0 : -w, -h / 2f);

        using var path = WingPath(w, h, flipped);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
        g.Restore(state);
    }

    /// A single swept-back wing, tip at the trailing edge. Ported from wingPath(in:flipped:),
    /// with the y terms mirrored for the top-down bitmap coordinate space.
    private static GraphicsPath WingPath(float w, float h, bool flipped)
    {
        float Y(float fraction) => h - fraction * h;

        var path = new GraphicsPath();
        var root = new PointF(flipped ? 0 : w, Y(0.5f));
        var tip = new PointF(flipped ? w : 0, Y(0.08f));

        path.AddBezier(
            root,
            new PointF(flipped ? w * 0.35f : w * 0.65f, Y(0.95f)),
            new PointF(flipped ? w * 0.85f : w * 0.15f, Y(0.55f)),
            tip);
        path.AddBezier(
            tip,
            new PointF(w * 0.5f, Y(0.0f)),
            new PointF(flipped ? w * 0.15f : w * 0.85f, Y(0.18f)),
            new PointF(root.X, Y(0.35f)));
        path.CloseFigure();
        return path;
    }

    // MARK: - Steam (coffee)

    private void DrawSteam(Graphics g, double t)
    {
        float lineWidth = Math.Max(1f, _size * 0.075f);

        for (int index = 0; index < 2; index++)
        {
            double local = t - index * SteamStagger;
            if (local < 0 || local >= SteamDuration * SteamRepeats) continue;

            double p = local % SteamDuration / SteamDuration;

            double[] values = [0, 0.65, 0.4, 0];
            double[] keyTimes = [0, 0.25, 0.7, 1.0];
            double opacity = 0;
            for (int i = 1; i < keyTimes.Length; i++)
            {
                if (p > keyTimes[i]) continue;
                double segment = (p - keyTimes[i - 1]) / (keyTimes[i] - keyTimes[i - 1]);
                opacity = values[i - 1] + (values[i] - values[i - 1]) * EaseInOut(segment);
                break;
            }
            if (opacity <= 0.004) continue;

            // Rises as it fades, same as the transform.translation.y animation.
            float rise = (float)(EaseInOut(p) * _size * 0.18);

            float x = index == 0 ? _size * 0.42f : _size * 0.58f;
            float startY = _size * 0.26f - rise;
            float dx = (index == 0 ? -1f : 1f) * _size * 0.047f;

            using var pen = new Pen(Color.FromArgb((int)(opacity * 255), _tint), lineWidth)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            using var path = new GraphicsPath();
            path.AddBezier(
                new PointF(x, startY),
                new PointF(x + dx * 1.5f, startY - _size * 0.082f),
                new PointF(x, startY - _size * 0.159f),
                new PointF(x + dx, startY - _size * 0.227f));
            g.DrawPath(pen, path);
        }
    }

    // MARK: - Base artwork

    /// Where the 16x20 can canvas lands inside the square icon, aspect-fit.
    private RectangleF CanvasRect()
    {
        const float canvasAspect = 16f / 20f;
        float width = _size * canvasAspect;
        return new RectangleF((_size - width) / 2f, 0, width, _size);
    }

    private void EnsureArt()
    {
        RefreshAppearance();
        if (!_artDirty && _outlineArt is not null && _fillArt is not null) return;

        _outlineArt?.Dispose();
        _fillArt?.Dispose();
        _outlineArt = RenderArt(filled: false);
        _fillArt = RenderArt(filled: true);
        _artDirty = false;
    }

    private Bitmap RenderArt(bool filled)
    {
        var bitmap = new Bitmap(_size, _size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);

        if (_style == IconStyle.Coffee)
        {
            VectorIcons.Draw(g, filled ? "cup.and.saucer.fill" : "cup.and.saucer",
                new RectangleF(_size * 0.08f, _size * 0.08f, _size * 0.84f, _size * 0.84f),
                _tint, Color.Transparent);
        }
        else
        {
            DrawCan(g, filled);
        }
        return bitmap;
    }

    /// A drink-can silhouette — original artwork, not a reproduction of any real can's
    /// logo or wordmark. The filled state uses a silver body with a single diagonal blue
    /// stripe and a small abstract red/yellow colour-block accent (no figurative logo);
    /// the outline state is a plain monochrome template shape.
    private void DrawCan(Graphics g, bool filled)
    {
        var image = CanvasRect();
        var state = g.Save();

        // Port the 16x20 bottom-up canvas coordinates verbatim by flipping into it.
        g.TranslateTransform(image.X, image.Bottom);
        g.ScaleTransform(image.Width / 16f, -image.Height / 20f);

        const float bodyInset = 3.0f;
        var bodyRect = new RectangleF(bodyInset, 0.8f, 16f - bodyInset * 2, 20f - 3.4f);
        using var body = VectorIcons.RoundedRect(bodyRect, 1.1f);

        var lidRect = new RectangleF(bodyInset - 0.3f, bodyRect.Bottom - 0.55f, bodyRect.Width + 0.6f, 1.8f);
        var tabRect = new RectangleF(8f - 1.3f, lidRect.Bottom - 0.75f - 0.55f, 2.6f, 1.1f);

        if (filled)
        {
            var clip = g.Save();
            g.SetClip(body);

            using (var silver = new SolidBrush(Color.FromArgb(237, 237, 237)))
            {
                g.FillPath(silver, body);
            }

            // Single diagonal blue stripe sweeping across the silver body.
            var stripeState = g.Save();
            float cx = bodyRect.X + bodyRect.Width / 2f;
            float cy = bodyRect.Y + bodyRect.Height / 2f;
            g.TranslateTransform(cx, cy);
            g.RotateTransform(22f);
            g.TranslateTransform(-cx, -cy);
            using (var blue = new SolidBrush(Color.FromArgb(18, 43, 117)))
            {
                g.FillRectangle(blue,
                    cx - bodyRect.Width * 0.42f,
                    bodyRect.Y - bodyRect.Height * 0.3f,
                    bodyRect.Width * 0.84f,
                    bodyRect.Height * 1.6f);
            }
            g.Restore(stripeState);

            // Small abstract red/yellow colour-block accent, not a figurative logo.
            float emblemY = bodyRect.Y + bodyRect.Height * 0.47f;
            float yellowRadius = bodyRect.Width * 0.24f;
            using (var yellow = new SolidBrush(Color.FromArgb(250, 199, 26)))
            {
                g.FillEllipse(yellow, cx - yellowRadius, emblemY - yellowRadius * 0.6f,
                    yellowRadius * 2, yellowRadius * 1.2f);
            }
            using (var red = new SolidBrush(Color.FromArgb(209, 33, 33)))
            {
                float redRadius = bodyRect.Width * 0.19f;
                foreach (float dx in new[] { -1f, 1f })
                {
                    float ex = cx + dx * yellowRadius * 0.8f;
                    g.FillEllipse(red, ex - redRadius, emblemY - redRadius * 0.5f,
                        redRadius * 2, redRadius * 1.0f);
                }
            }

            g.Restore(clip);

            using (var lidBrush = new SolidBrush(Color.FromArgb(219, 219, 219)))
            {
                g.FillEllipse(lidBrush, lidRect);
            }
            using (var tabBrush = new SolidBrush(Color.FromArgb(140, 140, 140)))
            {
                g.FillEllipse(tabBrush, tabRect);
            }
        }
        else
        {
            using var pen = new Pen(_tint, 1.1f);
            g.DrawPath(pen, body);
            pen.Width = 1.0f;
            g.DrawEllipse(pen, lidRect);
            pen.Width = 0.9f;
            g.DrawEllipse(pen, tabRect);
        }

        g.Restore(state);
    }

    // MARK: - Easing

    /// Stand-in for CASpringAnimation: settles on 1.0 with a small overshoot.
    private static double Spring(double p)
    {
        if (p >= 1) return 1;
        if (p <= 0) return 0;
        return 1 - Math.Pow(2, -10 * p) * Math.Cos(p * Math.PI * 3.0);
    }

    private static double EaseInOut(double p) =>
        p <= 0 ? 0 : p >= 1 ? 1 : p < 0.5 ? 2 * p * p : 1 - Math.Pow(-2 * p + 2, 2) / 2;

    private static double Clamp01(double value) => value < 0 ? 0 : value > 1 ? 1 : value;

    public void Dispose()
    {
        _outlineArt?.Dispose();
        _fillArt?.Dispose();
    }
}
