using System.Drawing.Drawing2D;

namespace CaffeineBar;

/// A short, self-contained walkthrough shown on first launch and re-openable
/// anytime from the tray's right-click menu.
///
/// Windows tucks newly-registered tray icons into the overflow flyout, so a
/// first-time user can easily install this and never see the icon at all. Step
/// two exists specifically to solve that; the rest explain the three
/// interactions (click, hover, right-click) that aren't discoverable on their own.
///
/// Unlike CustomMenuWindow this is a normal, focusable window — it's a
/// destination the user reads and clicks through, not a transient panel.
public sealed class TutorialWindow : Form
{
    private sealed record Step(string Heading, string Body);

    private static readonly Step[] Steps =
    [
        new("Keep your PC awake",
            "Caffeine Bar holds a display-awake request for as long as it's active, so your "
            + "screen won't sleep and your session stays alive. Switch it off and your normal "
            + "power plan applies again — nothing is changed permanently."),

        new("Find the icon",
            "Windows hides newly-installed tray icons, so Caffeine Bar starts out behind the "
            + "˄ arrow next to the clock.\n\nTo keep it visible, open Settings › "
            + "Personalization › Taskbar › Other system tray icons and switch "
            + "Caffeine Bar on. Dragging it out of the ˄ flyout does the same thing."),

        new("Click to toggle",
            "Click the icon to switch it on and off.\n\nFilled means active — the can fills in "
            + "and sprouts wings, or the cup puffs steam, depending on your icon style. An "
            + "empty outline means it's off."),

        new("Hover for the menu",
            "Hover the icon to open the full panel: how long you've been active, the energy "
            + "drink and coffee finders, and the icon style switch.\n\nPick Coffee or Energy "
            + "Drink under Icon Style to change which drink the icon shows."),

        new("Right-click for settings",
            "Right-click for Launch at Login, Prevent Sleep on Lid Close, this tutorial, and Quit."
            + "\n\nLid close is a hardware sleep trigger, so it needs admin rights once. Caffeine "
            + "Bar registers two scheduled tasks that first time and never has to ask again."),
    ];

    private int _index;
    private readonly PillButton _backButton;
    private readonly PillButton _nextButton;
    private readonly PillButton _closeButton;

    private const int WindowWidth = 460;
    private const int WindowHeight = 320;
    private const int Pad = 26;
    private const int CornerRadius = 16;

    public TutorialWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        Text = "Caffeine Bar Tutorial";
        BackColor = Palette.Panel;
        ClientSize = new Size(WindowWidth, WindowHeight);
        DoubleBuffered = true;
        KeyPreview = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

        _backButton = new PillButton("Back", Palette.White(0.55), filled: false)
        {
            Left = Pad,
            Top = WindowHeight - Pad - PillButton.ButtonHeight,
        };
        _backButton.Clicked += () => Go(_index - 1);

        _nextButton = new PillButton("Next", Palette.Energy, filled: true)
        {
            Top = WindowHeight - Pad - PillButton.ButtonHeight,
        };
        _nextButton.Clicked += () =>
        {
            if (_index == Steps.Length - 1) Close();
            else Go(_index + 1);
        };

        _closeButton = new PillButton("Skip", Palette.White(0.4), filled: false)
        {
            Left = Pad,
            Top = WindowHeight - Pad - PillButton.ButtonHeight,
        };
        _closeButton.Clicked += Close;

        Controls.Add(_backButton);
        Controls.Add(_nextButton);
        Controls.Add(_closeButton);
        Go(0);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int preference = Native.DWMWCP_ROUND;
        if (Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, sizeof(int)) != 0)
        {
            var region = Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1,
                CornerRadius, CornerRadius);
            Native.SetWindowRgn(Handle, region, true);
        }
    }

    private void Go(int index)
    {
        _index = Math.Clamp(index, 0, Steps.Length - 1);
        bool isLast = _index == Steps.Length - 1;

        _backButton.Visible = _index > 0;
        _closeButton.Visible = _index == 0;
        _nextButton.Caption = isLast ? "Done" : "Next";
        _nextButton.Left = WindowWidth - Pad - _nextButton.Width;

        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Escape: Close(); break;
            case Keys.Right or Keys.Enter: _nextButton.PerformClick(); break;
            case Keys.Left when _index > 0: Go(_index - 1); break;
        }
    }

    // Borderless, so dragging the body has to stand in for a title bar.
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Native.ReleaseCapture();
        Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Palette.Panel);

        var step = Steps[_index];

        TextRenderer.DrawText(g, "CAFFEINE BAR", MenuFonts.Pill,
            new Point(Pad, Pad), Palette.Blend(Palette.Energy, 0.85),
            TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, step.Heading, MenuFonts.Heading,
            new Rectangle(Pad, Pad + 20, WindowWidth - Pad * 2, 30),
            Color.White, TextFormatFlags.Left | TextFormatFlags.NoPadding);

        TextRenderer.DrawText(g, step.Body, MenuFonts.Body,
            new Rectangle(Pad, Pad + 56, WindowWidth - Pad * 2, 150),
            Palette.White(0.72),
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        DrawDots(g);

        using var border = new Pen(Palette.White(0.10), 1);
        using var path = VectorIcons.RoundedRect(
            new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), CornerRadius);
        g.DrawPath(border, path);
    }

    private void DrawDots(Graphics g)
    {
        const int dot = 6;
        const int gap = 7;
        int total = Steps.Length * dot + (Steps.Length - 1) * gap;
        int x = (WindowWidth - total) / 2;
        int y = WindowHeight - Pad - PillButton.ButtonHeight / 2 - dot / 2;

        for (int i = 0; i < Steps.Length; i++)
        {
            using var brush = new SolidBrush(i == _index ? Palette.Energy : Palette.White(0.18));
            g.FillEllipse(brush, x + i * (dot + gap), y, dot, dot);
        }
    }

    /// A small rounded button — filled for the primary action, outlined otherwise.
    private sealed class PillButton : Control
    {
        public const int ButtonHeight = 30;

        private readonly Color _tint;
        private readonly bool _filled;
        private bool _hover;

        public event Action? Clicked;

        public string Caption
        {
            get => Text;
            set { Text = value; UpdateWidth(); Invalidate(); }
        }

        public PillButton(string caption, Color tint, bool filled)
        {
            _tint = tint;
            _filled = filled;
            Height = ButtonHeight;
            Cursor = Cursors.Hand;
            BackColor = Palette.Panel;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Caption = caption;
        }

        public void PerformClick() => Clicked?.Invoke();

        private void UpdateWidth()
        {
            var size = TextRenderer.MeasureText(Text, MenuFonts.Button, Size.Empty,
                TextFormatFlags.NoPadding);
            Width = Math.Max(78, size.Width + 32);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Clicked?.Invoke();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Palette.Panel);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            using var path = VectorIcons.RoundedRect(rect, ButtonHeight / 2f);

            if (_filled)
            {
                using var fill = new SolidBrush(_hover ? _tint : Palette.Blend(_tint, 0.88));
                g.FillPath(fill, path);
            }
            else
            {
                using var outline = new Pen(Palette.Blend(_tint, _hover ? 0.5 : 0.28), 1);
                g.DrawPath(outline, path);
            }

            var textColor = _filled ? Color.Black : Palette.Blend(_tint, _hover ? 1.0 : 0.8);
            TextRenderer.DrawText(g, Text, MenuFonts.Button, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.NoPadding);
        }
    }
}
