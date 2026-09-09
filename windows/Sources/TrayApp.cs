using System.Diagnostics;
using System.Media;
using System.Reflection;

namespace CaffeineBar;

/// The tray-resident application. Windows counterpart to AppDelegate: owns the
/// tray icon, the sleep assertion, the custom menu, and the lid-close override.
public sealed class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly DrinkIconRenderer _renderer = new();
    private readonly System.Windows.Forms.Timer _frameTimer;
    private readonly System.Windows.Forms.Timer _hoverTimer;

    private DateTime? _activeStartDate;
    private Icon? _currentIcon;

    /// Brands shown under "Get an Energy Drink" — each opens its own destination picker.
    private static readonly string[] EnergyDrinkBrands =
        ["Red Bull", "Monster Energy", "Celsius", "Bang Energy", "Rockstar Energy"];

    private bool IsActive => _activeStartDate is not null;

    private IconStyle IconStyle
    {
        get => Settings.IconStyle;
        set
        {
            Settings.IconStyle = value;
            _renderer.Style = value;
            UpdateIcon(animated: false);
        }
    }

    /// When on, closing the lid won't sleep the machine while Caffeine Bar is active —
    /// a hardware-level sleep trigger the execution-state assertion doesn't cover.
    /// Applied via `powercfg`, which needs admin privileges.
    private bool PreventSleepOnLidClose
    {
        get => Settings.PreventSleepOnLidClose;
        set => Settings.PreventSleepOnLidClose = value;
    }

    /// Tracks whether we're the ones currently holding the lid-close override, so we
    /// only ever issue the matching restore we're responsible for.
    private bool _lidSleepDisabled;

    public TrayApp()
    {
        _renderer.Style = Settings.IconStyle;

        _notifyIcon = new NotifyIcon
        {
            Text = "Caffeine Bar",
            Visible = true,
        };
        _notifyIcon.MouseUp += OnTrayMouseUp;
        _notifyIcon.MouseMove += OnTrayMouseMove;

        _frameTimer = new System.Windows.Forms.Timer { Interval = 33 };
        _frameTimer.Tick += (_, _) =>
        {
            RefreshTrayIcon();
            if (!_renderer.IsAnimating) _frameTimer.Stop();
        };

        _hoverTimer = new System.Windows.Forms.Timer { Interval = 100 };
        _hoverTimer.Tick += (_, _) => PollHover();
        _hoverTimer.Start();

        ShellAppearanceWatcher.Init(OnAppearanceChanged);

        UpdateIcon(animated: false);

        // Windows hides newly-registered tray icons in the overflow flyout, so on a
        // first run the icon we just created may be nowhere the user can see it.
        // Open the walkthrough once to explain where it went and how to pin it.
        if (!Settings.HasSeenTutorial)
        {
            Settings.HasSeenTutorial = true;
            var firstRun = new System.Windows.Forms.Timer { Interval = 900 };
            firstRun.Tick += (_, _) =>
            {
                firstRun.Stop();
                firstRun.Dispose();
                ShowTutorial();
            };
            firstRun.Start();
        }
    }

    private TutorialWindow? _tutorialWindow;

    private void ShowTutorial()
    {
        if (_tutorialWindow is { IsDisposed: false })
        {
            _tutorialWindow.Activate();
            return;
        }

        _tutorialWindow = new TutorialWindow();
        _tutorialWindow.FormClosed += (_, _) => _tutorialWindow = null;
        _tutorialWindow.Show();
        _tutorialWindow.Activate();
    }

    // MARK: - Click handling

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            PresentMenu(CustomMenuWindow.PresentationMode.Compact, hoverPresented: false);
        }
        else if (e.Button == MouseButtons.Left)
        {
            if (IsActive) Stop(); else Start();
            UpdateIcon(animated: true);
        }
    }

    // MARK: - Hover-to-open

    private DateTime _lastTrayMove = DateTime.MinValue;
    private Point _lastTrayMovePoint;
    private DateTime? _showAt;
    private DateTime? _hideAt;
    private bool _wasHovering;

    private void OnTrayMouseMove(object? sender, MouseEventArgs e)
    {
        _lastTrayMove = DateTime.UtcNow;
        _lastTrayMovePoint = Cursor.Position;
    }

    /// The tray has no enter/exit events, so hover is polled: over the icon, or over
    /// the panel itself — the latter is what lets the cursor cross from the icon down
    /// into the menu without it closing underneath them.
    private void PollHover()
    {
        bool hovering = IsCursorOverTrayIcon() ||
            (_menuWindow is { Visible: true } menu && menu.Bounds.Contains(Cursor.Position));

        if (hovering != _wasHovering)
        {
            _wasHovering = hovering;
            if (hovering)
            {
                _hideAt = null;
                _showAt = DateTime.UtcNow.AddMilliseconds(150);
            }
            else
            {
                _showAt = null;
                if (MenuWindow.IsHoverPresented) _hideAt = DateTime.UtcNow.AddMilliseconds(200);
            }
        }

        if (_showAt is { } showAt && DateTime.UtcNow >= showAt)
        {
            _showAt = null;
            PresentMenu(CustomMenuWindow.PresentationMode.Full, hoverPresented: true);
        }

        if (_hideAt is { } hideAt && DateTime.UtcNow >= hideAt)
        {
            _hideAt = null;
            MenuWindow.Hide();
        }
    }

    private bool IsCursorOverTrayIcon()
    {
        if (TryGetTrayIconRect(out var rect)) return rect.Contains(Cursor.Position);

        // Fallback when the icon's rect isn't available: the shell sends a steady
        // stream of moves while the cursor is over the icon, and a perfectly still
        // cursor at the last known point still counts as hovering.
        var sinceMove = DateTime.UtcNow - _lastTrayMove;
        return sinceMove < TimeSpan.FromMilliseconds(400) ||
               (Cursor.Position == _lastTrayMovePoint && sinceMove < TimeSpan.FromSeconds(30));
    }

    /// NotifyIcon doesn't expose the window/id pair Shell_NotifyIconGetRect needs, so
    /// reach for them reflectively and degrade gracefully if the field names change.
    private bool TryGetTrayIconRect(out Rectangle rect)
    {
        rect = Rectangle.Empty;
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var type = typeof(NotifyIcon);
            var windowField = type.GetField("_window", flags) ?? type.GetField("window", flags);
            var idField = type.GetField("_id", flags) ?? type.GetField("id", flags);
            if (windowField?.GetValue(_notifyIcon) is not NativeWindow window ||
                idField?.GetValue(_notifyIcon) is not int id)
            {
                return false;
            }

            var identifier = new Native.NotifyIconIdentifier
            {
                cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.NotifyIconIdentifier>(),
                hWnd = window.Handle,
                uID = (uint)id,
            };

            if (Native.Shell_NotifyIconGetRect(ref identifier, out var native) != 0) return false;
            rect = native.ToRectangle();
            return !rect.IsEmpty;
        }
        catch
        {
            return false;
        }
    }

    // MARK: - The sleep assertion

    /// The `caffeinate -d` equivalent. ES_CONTINUOUS makes the request stick until
    /// cleared rather than being a one-shot nudge, and it lives on this thread — the
    /// UI thread, which outlives every other thread in the process.
    private void Start()
    {
        var result = Native.SetThreadExecutionState(
            Native.ExecutionState.Continuous |
            Native.ExecutionState.DisplayRequired |
            Native.ExecutionState.SystemRequired);

        _activeStartDate = result == 0 ? null : DateTime.UtcNow;
        ApplyLidSleepState();
    }

    private void Stop(bool synchronous = false)
    {
        Native.SetThreadExecutionState(Native.ExecutionState.Continuous);
        _activeStartDate = null;
        ApplyLidSleepState(synchronous);
    }

    /// Reconciles the lid-close override with whether it should currently be on
    /// (active + the setting enabled). Safe to call any time either input changes.
    ///
    /// `synchronous` is used on the way out: the restore has to actually finish
    /// before the process exits, or we'd leave the machine unable to sleep on lid
    /// close with no tray icon left to undo it from.
    private void ApplyLidSleepState(bool synchronous = false)
    {
        bool shouldDisable = IsActive && PreventSleepOnLidClose;
        if (shouldDisable == _lidSleepDisabled) return;
        _lidSleepDisabled = shouldDisable;

        if (synchronous)
        {
            Apply();
            return;
        }

        // The privileged call can block on a UAC prompt, so keep it off the UI thread.
        Task.Run(Apply);

        void Apply()
        {
            if (!LidSleepManager.SetLidSleepDisabled(shouldDisable))
            {
                // Declined or failed — don't leave our bookkeeping claiming an
                // override we don't actually hold.
                _lidSleepDisabled = !shouldDisable;
            }
        }
    }

    // MARK: - Icon

    private void UpdateIcon(bool animated)
    {
        if (_renderer.SetFilled(IsActive, animated))
        {
            _frameTimer.Start();
        }

        if (animated) PlayToggleSound(IsActive);

        _notifyIcon.Text = IsActive
            ? "Caffeine Bar: Active (display awake)"
            : "Caffeine Bar: Inactive";

        RefreshTrayIcon();
    }

    private void RefreshTrayIcon()
    {
        var icon = _renderer.RenderIcon();
        var previous = _currentIcon;
        _currentIcon = icon;
        _notifyIcon.Icon = icon;
        previous?.Dispose();
    }

    private void OnAppearanceChanged()
    {
        _renderer.RefreshAppearance();
        RefreshTrayIcon();
    }

    /// A single generic "charging" cue for turning on, regardless of icon style —
    /// no drink-specific jingle, just a quick power-up chime.
    private static void PlayToggleSound(bool on)
    {
        var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        var file = Path.Combine(media, on ? "Windows Notify System Generic.wav" : "Windows Balloon.wav");
        try
        {
            if (File.Exists(file))
            {
                var player = new SoundPlayer(file);
                player.Play(); // asynchronous
                return;
            }
        }
        catch
        {
            // Fall through to the system sound.
        }
        if (on) SystemSounds.Asterisk.Play(); else SystemSounds.Hand.Play();
    }

    // MARK: - Menu

    private CustomMenuWindow? _menuWindow;

    private CustomMenuWindow MenuWindow
    {
        get
        {
            if (_menuWindow is not null) return _menuWindow;

            var window = new CustomMenuWindow
            {
                EnergyDrinkBrands = EnergyDrinkBrands,
                DestinationsProvider = DrinkDestinations,
                OnOpenUrl = OpenUrl,
                OnQuit = QuitApp,
                OnShowTutorial = ShowTutorial,
            };
            window.OnSelectIconStyle = style =>
            {
                IconStyle = style;
                window.IconStyle = style;
            };
            window.OnToggleLaunchAtLogin = () =>
            {
                LaunchAtLogin.Toggle();
                window.SetLaunchAtLoginEnabled(LaunchAtLogin.IsEnabled);
            };
            window.OnTogglePreventSleepOnLidClose = () =>
            {
                PreventSleepOnLidClose = !PreventSleepOnLidClose;
                window.SetPreventSleepOnLidClose(PreventSleepOnLidClose);
                ApplyLidSleepState();
            };

            _menuWindow = window;
            return window;
        }
    }

    private void RefreshMenuState()
    {
        var window = MenuWindow;
        window.IsActive = IsActive;
        window.IconStyle = IconStyle;
        window.PreventSleepOnLidClose = PreventSleepOnLidClose;
        window.LaunchAtLoginEnabled = LaunchAtLogin.IsEnabled;
        window.ActiveDetail = IsActive && _activeStartDate is { } start
            ? FormatDuration((int)(DateTime.UtcNow - start).TotalSeconds)
            : "";
    }

    private void PresentMenu(CustomMenuWindow.PresentationMode mode, bool hoverPresented)
    {
        RefreshMenuState();
        TryGetTrayIconRect(out var rect);
        MenuWindow.Present(rect, mode, hoverPresented);
    }

    // MARK: - Get a Drink

    /// Search-only destinations — no checkout automation, no order placement.
    /// DoorDash/Instacart/Uber Eats don't publish a stable search API; these are
    /// best-effort URL patterns captured by hand and may break if any of these sites
    /// changes its URL structure. Uber Eats' search also has no location-free "near me"
    /// URL, so without a delivery address already set it'll prompt for one before
    /// showing results for `searchTerm`. Maps searches places, not products, so
    /// `mapQuery` targets the kind of store that carries the drink rather than the
    /// drink itself.
    private static IReadOnlyList<DrinkDestination> DrinkDestinations(string mapQuery, string searchTerm)
    {
        string Escape(string value) => Uri.EscapeDataString(value);

        // The Apple Maps stand-in: the bingmaps: protocol opens the built-in Windows
        // Maps app, and falls back to Bing Maps in a browser if it isn't installed.
        var windowsMaps = $"bingmaps:?q={Escape(mapQuery)}";
        var googleMaps = $"https://www.google.com/maps/search/?api=1&query={Escape(mapQuery + " near me")}";
        var doorDash = $"https://www.doordash.com/search/store/{Escape(searchTerm)}/";
        var instacart = $"https://www.instacart.com/store/s?k={Escape(searchTerm)}";
        var uberEats = $"https://www.ubereats.com/search?q={Escape(searchTerm)}";

        return
        [
            new DrinkDestination("Nearby Stores (Windows Maps)", "map.fill", windowsMaps),
            new DrinkDestination("Nearby Stores (Google Maps)", "map", googleMaps),
            new DrinkDestination("Find on DoorDash", "bag.fill", doorDash),
            new DrinkDestination("Find on Instacart", "cart", instacart),
            new DrinkDestination("Find on Uber Eats", "bag", uberEats),
        ];
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open {url}: {ex.Message}");
        }
    }

    private static string FormatDuration(int seconds)
    {
        if (seconds < 60) return $"{seconds}s";
        if (seconds < 3600) return $"{seconds / 60}m";
        return $"{seconds / 3600}h {seconds % 3600 / 60}m";
    }

    // MARK: - Teardown

    private void QuitApp()
    {
        // Release the assertion and the lid override before going away, so we never
        // leave the machine in a state the user can't undo from the tray. The lid
        // restore runs synchronously for exactly that reason.
        _menuWindow?.Hide();
        if (IsActive) Stop(synchronous: true);
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hoverTimer.Stop();
            _frameTimer.Stop();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _currentIcon?.Dispose();
            _renderer.Dispose();
            _menuWindow?.Dispose();
            _tutorialWindow?.Dispose();
            ShellAppearanceWatcher.Shutdown();
        }
        base.Dispose(disposing);
    }
}

/// Wraps the shell notifications the app cares about: taskbar theme changes and DPI
/// changes both alter how the tray icon should be drawn.
internal static class ShellAppearanceWatcher
{
    private static Microsoft.Win32.UserPreferenceChangedEventHandler? _handler;

    public static void Init(Action onChanged)
    {
        _handler = (_, _) => onChanged();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += _handler;
    }

    public static void Shutdown()
    {
        if (_handler is not null) Microsoft.Win32.SystemEvents.UserPreferenceChanged -= _handler;
        _handler = null;
    }
}
