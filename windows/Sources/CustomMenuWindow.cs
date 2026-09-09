using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CaffeineBar;

/// A destination link surfaced by the "Get an Energy Drink" / "Get a Coffee"
/// pickers — a nearby-stores map search or a delivery-app search.
public sealed record DrinkDestination(string Title, string Symbol, string? Url);

/// A fully custom replacement for a context menu: a borderless dark panel with its
/// own hover-highlighted rows, coloured icon chips, and an inline accordion (rather
/// than flyout submenus) for drilling into drink brands and their destinations.
///
/// Trade-off, accepted deliberately: we lose native menu behaviour for free
/// (arrow-key navigation, automatic screen-edge flipping, screen-reader menu
/// semantics) in exchange for full control over look and layout.
public sealed class CustomMenuWindow : Form
{
    /// `Full` is the hover-triggered drink/icon-style browser; `Compact` is the
    /// right-click fallback with just Launch at Login, lid sleep, and Quit.
    public enum PresentationMode
    {
        Full,
        Compact,
    }

    private const int PanelWidth = 264;
    private const int VerticalPadding = 8;
    private const int CornerRadius = 16;

    private List<string> _expandedPath = [];
    private PresentationMode _mode = PresentationMode.Full;

    public bool IsHoverPresented { get; private set; }

    public bool IsActive { get; set; }
    public string ActiveDetail { get; set; } = "";
    public IconStyle IconStyle { get; set; } = IconStyle.EnergyDrink;
    public bool LaunchAtLoginEnabled { get; set; }
    public bool PreventSleepOnLidClose { get; set; }
    public IReadOnlyList<string> EnergyDrinkBrands { get; set; } = [];
    public Func<string, string, IReadOnlyList<DrinkDestination>> DestinationsProvider { get; set; }
        = (_, _) => [];

    public Action<string>? OnOpenUrl { get; set; }
    public Action<IconStyle>? OnSelectIconStyle { get; set; }
    public Action? OnToggleLaunchAtLogin { get; set; }
    public Action? OnTogglePreventSleepOnLidClose { get; set; }
    public Action? OnShowTutorial { get; set; }
    public Action? OnQuit { get; set; }
    public Action? OnClose { get; set; }

    // Global monitors that dismiss the panel, mirroring the AppKit event monitors.
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private Native.HookProc? _mouseProc;
    private Native.HookProc? _keyboardProc;

    public CustomMenuWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Palette.Panel;
        Width = PanelWidth;
        Height = 10;
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    /// A no-activate tool window with a drop shadow: it can be clicked without
    /// stealing focus from whatever the user was actually working in.
    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
            parameters.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return parameters;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyRoundedCorners();
    }

    private void ApplyRoundedCorners()
    {
        // Windows 11 rounds the window itself, shadow and all.
        int preference = Native.DWMWCP_ROUND;
        var result = Native.DwmSetWindowAttribute(
            Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));

        if (result != 0)
        {
            // Windows 10: clip to a rounded region instead.
            var region = Native.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, CornerRadius, CornerRadius);
            Native.SetWindowRgn(Handle, region, true);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Palette.White(0.10), 1);
        using var path = VectorIcons.RoundedRect(
            new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), CornerRadius);
        e.Graphics.DrawPath(pen, path);
    }

    // MARK: - Presentation

    /// - Parameter hoverPresented: whether this presentation should be treated as a
    ///   hover preview rather than a click-invoked menu.
    public void Present(Rectangle iconRect, PresentationMode mode, bool hoverPresented)
    {
        if (mode == PresentationMode.Compact && Visible && _mode == PresentationMode.Compact)
        {
            Hide();
            return;
        }

        _mode = mode;
        IsHoverPresented = hoverPresented;
        _expandedPath = [];
        Rebuild();

        var screen = Screen.FromRectangle(iconRect.IsEmpty ? new Rectangle(Cursor.Position, Size.Empty) : iconRect);
        var working = screen.WorkingArea;
        var anchor = iconRect.IsEmpty ? new Rectangle(Cursor.Position, new Size(1, 1)) : iconRect;

        // The taskbar is usually at the bottom, so unlike the macOS menu bar the
        // panel normally opens upward. Flip if the tray is actually at the top.
        bool openUpward = anchor.Top > working.Top + working.Height / 2;
        int y = openUpward ? anchor.Top - Height - 6 : anchor.Bottom + 6;

        int x = Math.Min(anchor.Left, working.Right - PanelWidth - 4);
        x = Math.Max(x, working.Left + 4);
        y = Math.Max(working.Top + 4, Math.Min(y, working.Bottom - Height - 4));

        Location = new Point(x, y);
        ApplyRoundedCorners();
        Show();
        StartMonitoringDismissal();
    }

    public new void Hide()
    {
        if (!Visible) return;
        StopMonitoringDismissal();
        IsHoverPresented = false;
        base.Hide();
        OnClose?.Invoke();
    }

    public void SetLaunchAtLoginEnabled(bool enabled)
    {
        LaunchAtLoginEnabled = enabled;
        Rebuild();
    }

    public void SetPreventSleepOnLidClose(bool enabled)
    {
        PreventSleepOnLidClose = enabled;
        Rebuild();
    }

    private void StartMonitoringDismissal()
    {
        if (_mouseHook != IntPtr.Zero) return;

        var module = Native.GetModuleHandle(null);

        _mouseProc = (code, wParam, lParam) =>
        {
            if (code >= 0)
            {
                int message = (int)wParam;
                if (message is Native.WM_LBUTTONDOWN or Native.WM_RBUTTONDOWN or Native.WM_MBUTTONDOWN)
                {
                    var data = Marshal.PtrToStructure<Native.MouseLowLevelHookStruct>(lParam);
                    if (!Bounds.Contains(data.Point))
                    {
                        // Never call back into UI work from inside a hook.
                        BeginInvoke(Hide);
                    }
                }
            }
            return Native.CallNextHookEx(_mouseHook, code, wParam, lParam);
        };
        _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, module, 0);

        _keyboardProc = (code, wParam, lParam) =>
        {
            if (code >= 0 && (int)wParam is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
            {
                var data = Marshal.PtrToStructure<Native.KeyboardLowLevelHookStruct>(lParam);
                if (data.VirtualKeyCode == (uint)Keys.Escape) BeginInvoke(Hide);
            }
            return Native.CallNextHookEx(_keyboardHook, code, wParam, lParam);
        };
        _keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _keyboardProc, module, 0);
    }

    private void StopMonitoringDismissal()
    {
        if (_mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_mouseHook);
        if (_keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(_keyboardHook);
        _mouseHook = IntPtr.Zero;
        _keyboardHook = IntPtr.Zero;
        _mouseProc = null;
        _keyboardProc = null;
    }

    // MARK: - Content

    private void Toggle(List<string> path)
    {
        _expandedPath = _expandedPath.SequenceEqual(path)
            ? path.Take(path.Count - 1).ToList()
            : path;
        ResizeAroundAnchor();
    }

    /// Rebuilds, then grows or shrinks around the panel's anchored edge so an
    /// expanding accordion doesn't walk the panel across the screen.
    private void ResizeAroundAnchor()
    {
        var screen = Screen.FromControl(this).WorkingArea;
        bool anchoredToBottom = Top > screen.Top + screen.Height / 2;
        int previousBottom = Bottom;

        Rebuild();
        if (anchoredToBottom)
        {
            Top = Math.Max(screen.Top + 4, previousBottom - Height);
        }
        else
        {
            Top = Math.Min(Top, screen.Bottom - Height - 4);
        }
        ApplyRoundedCorners();
    }

    private void Rebuild()
    {
        SuspendLayout();
        foreach (Control control in Controls.Cast<Control>().ToList())
        {
            Controls.Remove(control);
            control.Dispose();
        }

        int y = VerticalPadding;
        foreach (var row in BuildRows())
        {
            row.Left = 0;
            row.Top = y;
            row.Width = PanelWidth;
            Controls.Add(row);
            y += row.Height;
        }

        Height = y + VerticalPadding;
        ResumeLayout();
        Invalidate();
    }

    private List<Control> BuildRows() =>
        _mode == PresentationMode.Full ? FullRows() : CompactRows();

    private List<Control> FullRows()
    {
        var rows = new List<Control>();

        var header = new StatusHeaderView();
        header.Configure(IsActive, ActiveDetail);
        rows.Add(header);
        rows.Add(new MenuSeparatorView());

        rows.AddRange(EnergySectionRows());
        rows.AddRange(CoffeeSectionRows());
        rows.Add(new MenuSeparatorView());
        rows.AddRange(IconStyleSectionRows());
        rows.Add(new MenuSeparatorView());
        rows.AddRange(LoginAndQuitRows());

        return rows;
    }

    /// The right-click menu — just the housekeeping actions, no drinks browsing.
    private List<Control> CompactRows() => LoginAndQuitRows();

    private List<Control> LoginAndQuitRows() =>
    [
        new MenuRowView(
            title: "Launch at Login",
            symbolName: "power",
            tint: Palette.Login,
            indent: 12,
            accessory: new MenuRowAccessory.Checkmark(LaunchAtLoginEnabled),
            onSelect: () => OnToggleLaunchAtLogin?.Invoke()),
        new MenuRowView(
            title: "Prevent Sleep on Lid Close",
            symbolName: "laptopcomputer",
            tint: Palette.LidSleep,
            indent: 12,
            accessory: new MenuRowAccessory.Checkmark(PreventSleepOnLidClose),
            onSelect: () => OnTogglePreventSleepOnLidClose?.Invoke()),
        new MenuRowView(
            title: "Tutorial",
            symbolName: "questionmark.circle",
            tint: Palette.Style,
            indent: 12,
            onSelect: () =>
            {
                OnShowTutorial?.Invoke();
                Hide();
            }),
        new MenuSeparatorView(),
        new MenuRowView(
            title: "Quit Caffeine Bar",
            symbolName: "xmark.circle.fill",
            tint: Palette.Red,
            indent: 12,
            titleColor: Palette.Blend(Palette.Red, 0.9),
            onSelect: () => OnQuit?.Invoke()),
    ];

    private List<Control> EnergySectionRows()
    {
        var rows = new List<Control>();
        bool expanded = _expandedPath.FirstOrDefault() == "energy";

        rows.Add(new MenuRowView(
            title: "Get an Energy Drink",
            symbolName: "bolt.fill",
            tint: Palette.Energy,
            indent: 12,
            accessory: new MenuRowAccessory.Chevron(expanded),
            emphasized: true,
            onSelect: () => Toggle(["energy"])));

        if (!expanded) return rows;

        foreach (var brand in EnergyDrinkBrands)
        {
            var brandPath = new List<string> { "energy", brand };
            bool brandExpanded = _expandedPath.SequenceEqual(brandPath);
            rows.Add(new MenuRowView(
                title: brand,
                tint: Palette.Energy,
                indent: 26,
                accessory: new MenuRowAccessory.Chevron(brandExpanded),
                onSelect: () => Toggle(brandPath)));

            if (brandExpanded)
            {
                rows.AddRange(DestinationRows(
                    mapQuery: "gas station OR convenience store OR grocery store",
                    searchTerm: brand.ToLowerInvariant(),
                    indent: 36));
            }
        }
        return rows;
    }

    private List<Control> CoffeeSectionRows()
    {
        var rows = new List<Control>();
        bool expanded = _expandedPath.FirstOrDefault() == "coffee";

        rows.Add(new MenuRowView(
            title: "Get a Coffee",
            symbolName: "cup.and.saucer.fill",
            tint: Palette.Coffee,
            indent: 12,
            accessory: new MenuRowAccessory.Chevron(expanded),
            emphasized: true,
            onSelect: () => Toggle(["coffee"])));

        if (!expanded) return rows;
        rows.AddRange(DestinationRows(mapQuery: "coffee shop", searchTerm: "coffee", indent: 26));
        return rows;
    }

    private List<Control> DestinationRows(string mapQuery, string searchTerm, int indent)
    {
        var rows = new List<Control>();
        var destinations = DestinationsProvider(mapQuery, searchTerm);

        for (int index = 0; index < destinations.Count; index++)
        {
            var destination = destinations[index];
            rows.Add(new MenuRowView(
                title: destination.Title,
                symbolName: destination.Symbol,
                tint: Palette.White(0.7),
                indent: indent,
                isEnabled: destination.Url is not null,
                onSelect: () =>
                {
                    if (destination.Url is null) return;
                    OnOpenUrl?.Invoke(destination.Url);
                    Hide();
                }));

            if (index == 1) rows.Add(new MenuSeparatorView());
        }
        return rows;
    }

    private List<Control> IconStyleSectionRows()
    {
        var rows = new List<Control>();
        bool expanded = _expandedPath.FirstOrDefault() == "iconStyle";

        rows.Add(new MenuRowView(
            title: "Icon Style",
            symbolName: "paintbrush.fill",
            tint: Palette.Style,
            indent: 12,
            accessory: new MenuRowAccessory.Chevron(expanded),
            emphasized: true,
            onSelect: () => Toggle(["iconStyle"])));

        if (!expanded) return rows;

        rows.Add(new MenuRowView(
            title: "Coffee",
            symbolName: "cup.and.saucer.fill",
            tint: Palette.Coffee,
            indent: 26,
            accessory: new MenuRowAccessory.Checkmark(IconStyle == IconStyle.Coffee),
            onSelect: () => SelectIconStyle(IconStyle.Coffee)));
        rows.Add(new MenuRowView(
            title: "Energy Drink",
            symbolName: "bolt.fill",
            tint: Palette.Energy,
            indent: 26,
            accessory: new MenuRowAccessory.Checkmark(IconStyle == IconStyle.EnergyDrink),
            onSelect: () => SelectIconStyle(IconStyle.EnergyDrink)));
        return rows;
    }

    private void SelectIconStyle(IconStyle style)
    {
        IconStyle = style;
        OnSelectIconStyle?.Invoke(style);
        Rebuild();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) StopMonitoringDismissal();
        base.Dispose(disposing);
    }
}
