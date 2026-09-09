# Caffeine Bar for Windows ⚡

A tiny, free, open source Windows system tray app that keeps your display awake,
styled like a drink bar living in your notification area.

This is a port of the [macOS Caffeine Bar](../README.md) — same features, same
menu, same icons, rebuilt on Windows APIs.

Click the icon to toggle it:

- **Filled icon**: active. Keeps your screen awake using the Windows
  `SetThreadExecutionState` display-required assertion.
- **Empty icon**: inactive. Your normal power plan applies.

No account, no telemetry, no background noise.

---

## Features

Everything the macOS build does:

- **Two icon styles, switchable anytime**
  - **Energy Drink**: a can that fills with colour and sprouts wings when
    activated, folding back in after a few seconds.
  - **Coffee**: a cup that fills with colour and puffs steam wisps when activated.
- **A fully custom menu, not the native Windows one**: a hand-built, borderless
  dark panel with coloured icon chips and hover-highlighted rows.
  - **Hover** the tray icon to open the full menu: status header, drink pickers,
    icon style switch, login toggle, and quit.
  - **Right-click** for a compact menu with Launch at Login, Prevent Sleep on
    Lid Close, Tutorial, and Quit.
- **"Get an Energy Drink"**: pick a brand (Red Bull, Monster Energy, Celsius,
  Bang Energy, Rockstar Energy), then jump to nearby stores on Windows Maps or
  Google Maps, or search DoorDash, Instacart, and Uber Eats.
- **"Get a Coffee"**: same destination picker, aimed at nearby coffee shops.
- **Built-in tutorial**: a short walkthrough on first launch, reopenable anytime
  from the right-click menu.
- **Launch at login**: toggle auto-start from the menu.
- **Prevent sleep on lid close**: opt-in, for laptops.
- **Light and dark mode**: the tray icon follows your taskbar theme (the menu
  panel itself stays dark either way, same as on macOS).

---

## Installation

Requires Windows 10 (1809+) or Windows 11.

### Build from source

Requires the [.NET SDK 8.0+](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/eli-manning/caffeine-bar.git
cd caffeine-bar\windows
.\build.ps1 -Install
```

On an ARM device (Surface Pro X and similar), pass the matching runtime:

```powershell
.\build.ps1 -Runtime win-arm64 -Install
```

`build.ps1` produces a self-contained single-file `CaffeineBar.exe`, so the
machine you run it on doesn't need .NET installed. `-Install` copies it to
`%LOCALAPPDATA%\Programs\Caffeine Bar` and starts it.

`-SingleFile` bundles it all into one `.exe` instead, which is easier to hand to
someone but more likely to be flagged by antivirus (see below).

### First run

Caffeine Bar opens a short walkthrough the first time it starts. You can reopen
it anytime from the tray icon's **right-click menu > Tutorial**.

**You will probably not see the icon at first.** Windows hides newly-registered
tray icons behind the **^** arrow next to the clock. To pin it so it's always
visible, open **Settings > Personalization > Taskbar > Other system tray icons**
and switch **Caffeine Bar** on. Dragging it out of the **^** flyout onto the
taskbar does the same thing.

---

## SmartScreen and antivirus

The executable isn't code-signed, so **SmartScreen will warn you the first time
you run it**: click **More info** > **Run anyway**. This is the Windows
equivalent of the macOS build's "Open Anyway" step, and it has the same cause —
signing needs a paid certificate this project doesn't have.

Antivirus is a separate matter, and it's worth knowing what actually triggers it:

- **Building from source barely trips anything.** A binary you compiled yourself,
  a moment ago, from source you can read is not the shape scanners are hunting
  for. This is the recommended path for that reason.
- **Single-file bundles are the main provoker.** `-SingleFile` produces a large
  self-contained blob that unpacks itself at startup, which is structurally what
  a packer or dropper looks like. Heuristic scanners flag that shape regularly,
  and it's why `build.ps1` no longer does it by default.
- **Downloading an unsigned `.exe` over plain HTTP is the worst case.** Scanners
  with web protection may delete it mid-download or quarantine it on arrival,
  sometimes silently truncating the file so it fails with a confusing error
  rather than an obvious block.

If your scanner does quarantine a build, the fix is an exclusion for the install
directory (`%LOCALAPPDATA%\Programs\Caffeine Bar`) — but only do that because
you built it and know what it is, not because a README told you to.

**Distributing to other people** genuinely requires an Authenticode code-signing
certificate (roughly $200-400/year for an OV cert). Without one, every recipient
sees the SmartScreen warning and some scanners will object. There is no free
workaround; reputation-based systems like SmartScreen are specifically designed
so that unsigned binaries from unknown publishers can't build trust cheaply.

To uninstall: quit from the tray menu, delete
`%LOCALAPPDATA%\Programs\Caffeine Bar`, and see "Prevent Sleep on Lid Close"
below if you ever enabled it.

---

## How it maps to the macOS build

| macOS | Windows |
| --- | --- |
| `caffeinate -d` child process | `SetThreadExecutionState(ES_CONTINUOUS \| ES_DISPLAY_REQUIRED \| ES_SYSTEM_REQUIRED)` |
| `pmset -a disablesleep` + a narrow `sudoers` rule | `powercfg ... SUB_BUTTONS LIDACTION` + two elevated Scheduled Tasks |
| `SMAppService` login item | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
| `NSStatusItem` in the menu bar | `NotifyIcon` in the notification area |
| `NSPanel` + `NSVisualEffectView` | Borderless no-activate tool window |
| Core Animation layer animations | Per-frame icon re-render on a 30fps timer |
| SF Symbols | Hand-drawn GDI+ vector icons (`VectorIcons.cs`) |
| Apple Maps (`maps://`) | Windows Maps (`bingmaps:`) |
| `UserDefaults` | `%APPDATA%\CaffeineBar\settings.json` |

### Prevent Sleep on Lid Close

Closing the lid is a hardware-level sleep trigger that the display assertion
doesn't cover. On macOS that needs `pmset disablesleep` as root; on Windows it
needs the power plan's lid-close action set to *Do nothing*, which needs admin.

Rather than prompting for admin on every toggle, the first time you enable it the
app asks for elevation **once** and registers two Scheduled Tasks under a
"Caffeine Bar" folder:

- **Disable Lid Sleep** — sets the lid action to *Do nothing*
- **Restore Lid Sleep** — puts your original setting back

Each task's only actions are three fixed `powercfg.exe` invocations with literal
arguments — no script file in the task action, so there's no writable
indirection that could be repointed at other elevated code. After that, toggling
runs `schtasks /run` with no further prompts. This mirrors the narrow
`/etc/sudoers.d` rule the macOS build installs.

Your original lid-close action is recorded in `settings.json` before the first
override, so turning the setting off restores what you actually had.

If you decline the elevation prompt, the toggle still works — it just falls back
to prompting for admin each time you switch it.

To remove the tasks later: `schtasks /delete /tn "Caffeine Bar\Disable Lid Sleep" /f`
and the same for `Restore Lid Sleep`.

### Known differences

- **The panel is solid, not translucent.** macOS gets vibrancy from
  `NSVisualEffectView` for free. The Windows equivalents (`DwmEnableBlurBehind`,
  acrylic via `SetWindowCompositionAttribute`) require making the window layered,
  which costs the drop shadow and softens text. The panel uses the same dark
  colour the macOS HUD material resolves to instead.
- **Apple Maps has no Windows equivalent**, so that row opens Windows Maps via
  the `bingmaps:` protocol, falling back to Bing Maps in a browser.
- **No arrow-key menu navigation.** Same trade-off the macOS build makes: the
  menu is hand-built rather than native, so it doesn't get keyboard navigation or
  screen-reader menu semantics for free. Escape closes it.

---

## License

[MIT](../LICENSE)
