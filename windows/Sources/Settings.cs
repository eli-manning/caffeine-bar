using System.Text.Json;

namespace CaffeineBar;

/// Which drink the tray icon (and its fill animation) currently represents.
public enum IconStyle
{
    Coffee,
    EnergyDrink,
}

/// The UserDefaults equivalent: a small JSON file under %APPDATA%\CaffeineBar.
/// Written on every change so state survives a crash as well as a clean quit.
public static class Settings
{
    private sealed class Model
    {
        public string IconStyle { get; set; } = "energyDrink";
        public bool PreventSleepOnLidClose { get; set; }
        public bool HasSeenTutorial { get; set; }
        public int? SavedLidActionAc { get; set; }
        public int? SavedLidActionDc { get; set; }
    }

    private static readonly string Directory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CaffeineBar");

    private static readonly string FilePath = Path.Combine(Directory, "settings.json");

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private static Model _model = Load();

    private static Model Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<Model>(File.ReadAllText(FilePath)) ?? new Model();
            }
        }
        catch
        {
            // A corrupt or unreadable settings file falls back to defaults rather
            // than taking the app down on launch.
        }
        return new Model();
    }

    private static void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_model, SerializerOptions));
        }
        catch
        {
            // Non-fatal: the app keeps working for this session, it just won't remember.
        }
    }

    public static IconStyle IconStyle
    {
        get => _model.IconStyle == "coffee" ? IconStyle.Coffee : IconStyle.EnergyDrink;
        set
        {
            _model.IconStyle = value == IconStyle.Coffee ? "coffee" : "energyDrink";
            Save();
        }
    }

    public static bool PreventSleepOnLidClose
    {
        get => _model.PreventSleepOnLidClose;
        set
        {
            _model.PreventSleepOnLidClose = value;
            Save();
        }
    }

    /// Set once the first-run walkthrough has been shown, so it only auto-opens once.
    /// It stays reachable from the tray's right-click menu afterwards.
    public static bool HasSeenTutorial
    {
        get => _model.HasSeenTutorial;
        set
        {
            _model.HasSeenTutorial = value;
            Save();
        }
    }

    /// The user's own lid-close actions, captured before we first override them so
    /// "off" restores what they actually had rather than a hardcoded Windows default.
    public static (int? Ac, int? Dc) SavedLidAction
    {
        get => (_model.SavedLidActionAc, _model.SavedLidActionDc);
        set
        {
            _model.SavedLidActionAc = value.Ac;
            _model.SavedLidActionDc = value.Dc;
            Save();
        }
    }
}
