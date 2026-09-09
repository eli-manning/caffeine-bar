namespace CaffeineBar;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // A second copy would fight the first for the tray slot and the sleep
        // assertion, so only one instance runs at a time.
        using var singleInstance = new Mutex(initiallyOwned: true,
            @"Local\CaffeineBar.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance) return;

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // No taskbar button and no main window — the tray icon is the whole UI,
        // the same shape as the macOS build's LSUIElement/accessory activation policy.
        Application.Run(new TrayApp());
    }
}
