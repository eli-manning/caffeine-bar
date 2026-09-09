using Microsoft.Win32;

namespace CaffeineBar;

/// The SMAppService equivalent. Per-user Run key, so toggling it never needs admin.
public static class LaunchAtLogin
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CaffeineBar";

    private static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string existing && existing.Trim('"')
                    .Equals(ExecutablePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void Toggle()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return;

            if (IsEnabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            Debug(ex);
        }
    }

    private static void Debug(Exception ex) =>
        System.Diagnostics.Debug.WriteLine($"Failed to toggle launch at login: {ex.Message}");
}
