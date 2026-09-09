# Caffeine Bar ⚡

A tiny, free, open source menu bar app that keeps your display awake, styled like a drink bar living in your menu bar.

**On Windows?** There's a full port with the same features in [`windows/`](windows/README.md).

Click the icon to toggle it:

- **Filled icon**: active. Keeps your screen awake using the native macOS `caffeinate -d` command.
- **Empty icon**: inactive. Your normal system sleep settings apply.

No account, no telemetry, no background noise.

---

## Features

- **Two icon styles, switchable anytime**
  - **Energy Drink**: a can that fills with color and sprouts wings when activated, folding back in after a few seconds.
  - **Coffee**: a cup that fills with color and puffs steam wisps when activated.
- **A fully custom menu, not the native macOS one**: a hand-built, borderless dark panel with colored icon chips and hover-highlighted rows.
  - **Hover** the icon to open the full menu: status header, drink pickers, icon style switch, login toggle, and quit.
  - **Right-click** for a compact menu with Launch at Login, Prevent Sleep on Lid Close, Tutorial, and Quit.
- **"Get an Energy Drink"**: pick a brand (Red Bull, Monster Energy, Celsius, Bang Energy, Rockstar Energy), then jump to nearby stores on Apple or Google Maps, or search DoorDash, Instacart, and Uber Eats.
- **"Get a Coffee"**: same destination picker, aimed at nearby coffee shops instead.
- **Built-in tutorial**: a short walkthrough on first launch, reopenable anytime from the right-click menu.
- **Launch at login**: toggle auto-start from the menu.
- **Light and dark mode**: adapts to your macOS system theme (the menu panel itself stays dark either way).

---

## Installation

### Homebrew (recommended)

```sh
brew install eli-manning/tap/caffeine-bar
```

Caffeine Bar isn't notarized, so macOS will block it the first time you open it. Go to **System Settings > Privacy & Security**, scroll to the message about Caffeine Bar, and click **Open Anyway**.

### Build from source

Requires macOS 13.0+, Xcode Command Line Tools, and [xcodegen](https://github.com/yonaskolb/XcodeGen) (`brew install xcodegen`).

```sh
git clone https://github.com/eli-manning/caffeine-bar.git
cd caffeine-bar
./build.sh --install
```

---

## Windows

A Windows port lives in [`windows/`](windows/README.md) — same features, same menu, same icons, rebuilt on Win32 APIs (`SetThreadExecutionState` for the sleep assertion, a `NotifyIcon` tray item, and `powercfg` for the lid-close override).

Download **[Caffeine-Bar-Setup.exe](https://github.com/eli-manning/caffeine-bar/releases/latest)** and run it. One installer covers both Intel/AMD and Arm PCs, installs per-user with no admin prompt, and needs no .NET runtime. See the [Windows README](windows/README.md) for the rest, including a table mapping each macOS API to its Windows counterpart.

---

## Credits

Forked from [Ryan Stoffel's Caffeine](https://github.com/RyanStoffel/caffeine).

## License

[MIT](LICENSE)
