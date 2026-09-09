using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace CaffeineBar;

/// Windows counterpart to the macOS `pmset -a disablesleep` handling.
///
/// Closing the lid is a hardware-level sleep trigger that the display/system
/// execution-state assertion doesn't cover, so it needs the power scheme's
/// "lid close action" set to Do Nothing (index 0) — which requires admin.
///
/// To avoid a UAC prompt on every single toggle, a one-time elevated setup
/// registers exactly two Scheduled Tasks (one to disable, one to restore) that
/// run `powercfg.exe` with fixed, literal arguments and nothing else. Once
/// registered, `schtasks /run` triggers them elevated with no further prompts —
/// the same bargain the macOS build makes with its narrow sudoers rule.
public static class LidSleepManager
{
    private const string TaskFolder = "Caffeine Bar";
    private const string DisableTask = TaskFolder + @"\Disable Lid Sleep";
    private const string RestoreTask = TaskFolder + @"\Restore Lid Sleep";

    private static readonly string PowerCfg =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "powercfg.exe");

    private static readonly string SchTasks =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    /// Lid close action index meaning "Do nothing".
    private const int LidActionDoNothing = 0;

    /// Applies (or lifts) the lid-close override. Returns false when the privileged
    /// step was declined or failed, so the caller can avoid recording an assertion
    /// it doesn't actually hold.
    public static bool SetLidSleepDisabled(bool disable)
    {
        if (disable)
        {
            // Capture what the user had before we override it, so restoring puts
            // their own setting back instead of guessing at a default.
            if (Settings.SavedLidAction.Ac is null)
            {
                Settings.SavedLidAction = QueryCurrentLidAction();
            }
            return RunPrivileged(DisableTask, LidActionDoNothing, LidActionDoNothing);
        }

        var (ac, dc) = Settings.SavedLidAction;
        // 1 == Sleep, the Windows default, used only if we never managed to read theirs.
        return RunPrivileged(RestoreTask, ac ?? 1, dc ?? 1);
    }

    /// Reads the active scheme's current lid-close action. Needs no elevation.
    private static (int? Ac, int? Dc) QueryCurrentLidAction()
    {
        var output = RunCapture(PowerCfg, "/query SCHEME_CURRENT SUB_BUTTONS LIDACTION");
        if (output is null) return (null, null);

        int? Parse(string label)
        {
            var match = Regex.Match(output, label + @"\s*Power Setting Index:\s*0x([0-9a-fA-F]+)");
            return match.Success && int.TryParse(match.Groups[1].Value,
                System.Globalization.NumberStyles.HexNumber, null, out var value) ? value : null;
        }

        return (Parse("Current AC"), Parse("Current DC"));
    }

    /// Triggers the named task, registering both tasks first if they're missing or stale.
    /// Falls back to a single elevated powercfg run (one UAC prompt, this time only) if
    /// the setup is declined — so the toggle still works, just less smoothly.
    private static bool RunPrivileged(string taskName, int acValue, int dcValue)
    {
        if (TryRunTask(taskName)) return true;

        if (InstallTasks() && TryRunTask(taskName)) return true;

        return RunElevatedDirectly(acValue, dcValue);
    }

    private static bool TryRunTask(string taskName)
    {
        var output = RunCapture(SchTasks, $"/run /tn \"{taskName}\"");
        if (output is null) return false;

        return WaitForTask(taskName);
    }

    /// `schtasks /run` returns as soon as the task is queued, so poll until it stops
    /// running and report whether its last exit code was success.
    private static bool WaitForTask(string taskName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var query = RunCapture(SchTasks, $"/query /tn \"{taskName}\" /fo LIST /v");
            if (query is null) return false;

            if (!query.Contains("Running", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(query, @"Last Result:\s*(-?\d+)");
                return match.Success && match.Groups[1].Value == "0";
            }
            Thread.Sleep(150);
        }
        return false;
    }

    /// The one-time elevated setup: writes both task definitions plus a registration
    /// script to a temp folder, runs the script once through UAC, then cleans up.
    private static bool InstallTasks()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "CaffeineBar-lid-setup-" + Guid.NewGuid().ToString("N"));
        try
        {
            System.IO.Directory.CreateDirectory(scratch);

            var (ac, dc) = Settings.SavedLidAction;
            var disableXml = Path.Combine(scratch, "disable.xml");
            var restoreXml = Path.Combine(scratch, "restore.xml");

            // schtasks /xml requires UTF-16.
            File.WriteAllText(disableXml,
                BuildTaskXml("Caffeine Bar: keep the machine awake when the lid closes",
                    LidActionDoNothing, LidActionDoNothing), Encoding.Unicode);
            File.WriteAllText(restoreXml,
                BuildTaskXml("Caffeine Bar: restore the previous lid close action",
                    ac ?? 1, dc ?? 1), Encoding.Unicode);

            var script = Path.Combine(scratch, "install.cmd");
            File.WriteAllText(script, $"""
                @echo off
                schtasks /create /tn "{DisableTask}" /xml "{disableXml}" /f || exit /b 1
                schtasks /create /tn "{RestoreTask}" /xml "{restoreXml}" /f || exit /b 1
                exit /b 0
                """, Encoding.Default);

            var info = new ProcessStartInfo
            {
                FileName = script,
                UseShellExecute = true,
                Verb = "runas", // triggers the single UAC prompt
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(info);
            if (process is null) return false;
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            // User dismissed the UAC prompt.
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { System.IO.Directory.Delete(scratch, recursive: true); } catch { }
        }
    }

    /// Fallback path: prompt for elevation and apply the change directly. Used only
    /// when the task-based setup isn't available, and prompts on every toggle.
    private static bool RunElevatedDirectly(int acValue, int dcValue)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "CaffeineBar-lid-" + Guid.NewGuid().ToString("N") + ".cmd");
        try
        {
            File.WriteAllText(scratch, $"""
                @echo off
                "{PowerCfg}" /setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {acValue} || exit /b 1
                "{PowerCfg}" /setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {dcValue} || exit /b 1
                "{PowerCfg}" /setactive SCHEME_CURRENT || exit /b 1
                exit /b 0
                """, Encoding.Default);

            var info = new ProcessStartInfo
            {
                FileName = scratch,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            using var process = Process.Start(info);
            if (process is null) return false;
            process.WaitForExit(30_000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { File.Delete(scratch); } catch { }
        }
    }

    /// A task whose only actions are three fixed `powercfg.exe` invocations — no
    /// script file in the action, so there's no writable indirection an attacker
    /// could repoint at arbitrary elevated code.
    private static string BuildTaskXml(string description, int acValue, int dcValue)
    {
        var user = $"{Environment.UserDomainName}\\{Environment.UserName}";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{description}</Description>
                <URI>\{TaskFolder}\CaffeineBar</URI>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{System.Security.SecurityElement.Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{PowerCfg}</Command>
                  <Arguments>/setacvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {acValue}</Arguments>
                </Exec>
                <Exec>
                  <Command>{PowerCfg}</Command>
                  <Arguments>/setdcvalueindex SCHEME_CURRENT SUB_BUTTONS LIDACTION {dcValue}</Arguments>
                </Exec>
                <Exec>
                  <Command>{PowerCfg}</Command>
                  <Arguments>/setactive SCHEME_CURRENT</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string? RunCapture(string fileName, string arguments)
    {
        try
        {
            var info = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var process = Process.Start(info);
            if (process is null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15_000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
