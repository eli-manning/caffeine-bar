import AppKit
import ServiceManagement

final class AppDelegate: NSObject, NSApplicationDelegate {
    private var statusItem: NSStatusItem!
    private var iconView: CoffeeIconView!
    private var caffeinate: Process?
    private var activeStartDate: Date?

    private var isActive: Bool { caffeinate?.isRunning ?? false }

    private var iconStyle: IconStyle {
        get { IconStyle(rawValue: UserDefaults.standard.string(forKey: "iconStyle") ?? "") ?? .energyDrink }
        set {
            UserDefaults.standard.set(newValue.rawValue, forKey: "iconStyle")
            iconView.style = newValue
        }
    }

    /// When on, closing the lid won't sleep the Mac while Caffeine Bar is active — a
    /// hardware-level lid-close sleep assertion that `caffeinate -d` alone doesn't cover.
    /// Applied via `pmset -a disablesleep`, which needs admin privileges.
    private var preventSleepOnLidClose: Bool {
        get { UserDefaults.standard.bool(forKey: "preventSleepOnLidClose") }
        set { UserDefaults.standard.set(newValue, forKey: "preventSleepOnLidClose") }
    }

    /// Tracks whether we're the ones currently holding `disablesleep 1`, so we only
    /// ever issue the matching `disablesleep 0` we're responsible for.
    private var lidSleepDisabled = false

    func applicationDidFinishLaunching(_ notification: Notification) {
        statusItem = NSStatusBar.system.statusItem(withLength: 32)
        statusItem.button?.action = #selector(handleClick)
        statusItem.button?.target = self
        statusItem.button?.sendAction(on: [.leftMouseUp, .rightMouseUp])

        if let button = statusItem.button {
            iconView = CoffeeIconView(frame: button.bounds)
            iconView.autoresizingMask = [.width, .height]
            iconView.style = iconStyle
            iconView.onHoverEnter = { [weak self] in self?.handleHoverEnter() }
            iconView.onHoverExit = { [weak self] in self?.handleHoverExit() }
            button.addSubview(iconView)
        }
        updateIcon(animated: false)
    }

    func applicationWillTerminate(_ notification: Notification) {
        stop()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        isActive ? stop() : start()
        updateIcon(animated: true)
        return false
    }

    @objc private func handleClick() {
        if NSApp.currentEvent?.type == .rightMouseUp {
            presentCompactMenu()
        } else {
            isActive ? stop() : start()
            updateIcon(animated: true)
        }
    }

    // MARK: - Hover-to-open

    private var pendingHoverShow: DispatchWorkItem?
    private var pendingHoverHide: DispatchWorkItem?

    private func handleHoverEnter() {
        pendingHoverHide?.cancel()
        let work = DispatchWorkItem { [weak self] in self?.presentFullMenu(hoverPresented: true) }
        pendingHoverShow = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15, execute: work)
    }

    private func handleHoverExit() {
        pendingHoverShow?.cancel()
        scheduleHoverHide()
    }

    private func scheduleHoverHide() {
        guard menuWindow.isHoverPresented else { return }
        let work = DispatchWorkItem { [weak self] in self?.menuWindow.hide() }
        pendingHoverHide = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2, execute: work)
    }

    private func start() {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/caffeinate")
        process.arguments = ["-d"]
        process.terminationHandler = { [weak self] _ in
            DispatchQueue.main.async {
                self?.caffeinate = nil
                self?.activeStartDate = nil
                self?.updateIcon(animated: true)
            }
        }
        do {
            try process.run()
            caffeinate = process
            activeStartDate = Date()
        } catch {
            caffeinate = nil
            activeStartDate = nil
        }
        applyLidSleepState()
    }

    private func stop() {
        caffeinate?.terminate()
        caffeinate = nil
        activeStartDate = nil
        applyLidSleepState()
    }

    private let pmsetPath = "/usr/bin/pmset"
    private let sudoersRulePath = "/etc/sudoers.d/caffeinebar"

    /// Reconciles the `pmset disablesleep` assertion with whether it should currently
    /// be on (active + the setting enabled). Safe to call any time either input changes.
    private func applyLidSleepState() {
        let shouldDisable = isActive && preventSleepOnLidClose
        guard shouldDisable != lidSleepDisabled else { return }
        lidSleepDisabled = shouldDisable
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            guard let self else { return }
            let succeeded = self.setDisableSleep(shouldDisable)
            if !succeeded {
                DispatchQueue.main.async {
                    // The privileged command was cancelled or failed — don't leave our
                    // bookkeeping claiming an assertion we don't actually hold.
                    self.lidSleepDisabled = !shouldDisable
                }
            }
        }
    }

    /// Runs `pmset -a disablesleep <0|1>` as root. Tries passwordless `sudo` first (works
    /// once `installPasswordlessSudoRule` has run); on macOS a fresh `disablesleep` change
    /// otherwise triggers a password prompt from `do shell script ... with administrator
    /// privileges` every single time, since that authorization only caches for a few minutes.
    private func setDisableSleep(_ enable: Bool) -> Bool {
        let value = enable ? "1" : "0"
        let args = [pmsetPath, "-a", "disablesleep", value]

        if runProcess("/usr/bin/sudo", ["-n"] + args) {
            return true
        }
        if installPasswordlessSudoRule(), runProcess("/usr/bin/sudo", ["-n"] + args) {
            return true
        }
        // Setup was declined or failed — fall back to a one-off privileged prompt so the
        // toggle still works, just with a password prompt this time.
        let script = "do shell script \"\(pmsetPath) -a disablesleep \(value)\" with administrator privileges"
        return runProcess("/usr/bin/osascript", ["-e", script])
    }

    /// One-time setup: grants this user passwordless `sudo` for exactly the two
    /// `pmset -a disablesleep 0/1` invocations (nothing broader), so future toggles of
    /// "Prevent Sleep on Lid Close" don't prompt for a password. Shows a single admin
    /// password prompt to write the rule; validates it with `visudo -c` before installing
    /// so a malformed rule can never end up live in `/etc/sudoers.d`.
    private func installPasswordlessSudoRule() -> Bool {
        let username = NSUserName()
        let rule = "\(username) ALL=(root) NOPASSWD: \(pmsetPath) -a disablesleep 1, \(pmsetPath) -a disablesleep 0"
        let scriptContent = """
        #!/bin/bash
        set -e
        TMP=$(mktemp)
        echo "\(rule)" > "$TMP"
        visudo -c -f "$TMP"
        cp "$TMP" \(sudoersRulePath)
        chmod 440 \(sudoersRulePath)
        chown root:wheel \(sudoersRulePath)
        rm -f "$TMP"
        """
        let scriptURL = FileManager.default.temporaryDirectory
            .appendingPathComponent("caffeinebar-sudoers-setup-\(UUID().uuidString).sh")
        do {
            try scriptContent.write(to: scriptURL, atomically: true, encoding: .utf8)
            try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: scriptURL.path)
        } catch {
            return false
        }
        defer { try? FileManager.default.removeItem(at: scriptURL) }

        let appleScript = "do shell script \"/bin/bash '\(scriptURL.path)'\" with administrator privileges"
        return runProcess("/usr/bin/osascript", ["-e", appleScript])
    }

    @discardableResult
    private func runProcess(_ path: String, _ arguments: [String]) -> Bool {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = arguments
        do {
            try process.run()
            process.waitUntilExit()
            return process.terminationStatus == 0
        } catch {
            return false
        }
    }

    private func updateIcon(animated: Bool) {
        let description = isActive ? "Caffeine Bar: Active (display awake)" : "Caffeine Bar: Inactive"
        iconView.setFilled(isActive, animated: animated)
        statusItem.button?.setAccessibilityLabel(description)
        statusItem.button?.setAccessibilityHelp("Click to toggle display sleep assertion")
    }

    /// Brands shown under "Get an Energy Drink" — each opens its own destination picker.
    private let energyDrinkBrands = ["Red Bull", "Monster Energy", "Celsius", "Bang Energy", "Rockstar Energy"]

    private lazy var menuWindow: CustomMenuWindow = {
        let window = CustomMenuWindow()
        window.energyDrinkBrands = energyDrinkBrands
        window.destinationsProvider = { [weak self] mapQuery, searchTerm in
            self?.drinkDestinations(mapQuery: mapQuery, searchTerm: searchTerm) ?? []
        }
        window.onOpenURL = { url in NSWorkspace.shared.open(url) }
        window.onSelectIconStyle = { [weak self] style in
            self?.iconStyle = style
            self?.menuWindow.iconStyle = style
        }
        window.onToggleLaunchAtLogin = { [weak self] in
            self?.toggleLaunchAtLogin()
            if #available(macOS 13.0, *) {
                self?.menuWindow.setLaunchAtLoginEnabled(SMAppService.mainApp.status == .enabled)
            }
        }
        window.onTogglePreventSleepOnLidClose = { [weak self] in
            guard let self else { return }
            self.preventSleepOnLidClose.toggle()
            self.menuWindow.setPreventSleepOnLidClose(self.preventSleepOnLidClose)
            self.applyLidSleepState()
        }
        window.onQuit = { NSApp.terminate(nil) }
        window.onClose = { [weak self] in self?.statusItem.button?.highlight(false) }
        window.onHoverEnter = { [weak self] in self?.pendingHoverHide?.cancel() }
        window.onHoverExit = { [weak self] in self?.scheduleHoverHide() }
        return window
    }()

    private func refreshMenuState() {
        menuWindow.isActive = isActive
        menuWindow.iconStyle = iconStyle
        menuWindow.preventSleepOnLidClose = preventSleepOnLidClose
        if #available(macOS 13.0, *) {
            menuWindow.launchAtLoginEnabled = SMAppService.mainApp.status == .enabled
        }
        menuWindow.activeDetail = ""
        if isActive, let start = activeStartDate {
            menuWindow.activeDetail = formatDuration(Int(Date().timeIntervalSince(start)))
        }
    }

    private func presentFullMenu(hoverPresented: Bool) {
        guard let button = statusItem.button else { return }
        refreshMenuState()
        button.highlight(true)
        menuWindow.present(relativeTo: button, mode: .full, hoverPresented: hoverPresented)
    }

    private func presentCompactMenu() {
        guard let button = statusItem.button else { return }
        refreshMenuState()
        button.highlight(true)
        menuWindow.present(relativeTo: button, mode: .compact, hoverPresented: false)
    }

    // MARK: - Get a Drink

    /// Search-only destinations — no checkout automation, no order placement.
    /// DoorDash/Instacart/Uber Eats don't publish a stable search API; these are
    /// best-effort URL patterns captured by hand and may break if any of these
    /// sites changes its URL structure. Uber Eats' search also has no location-free
    /// "near me" URL, so without a delivery address already set it'll prompt for one
    /// before showing results for `searchTerm`. Maps searches places, not products, so
    /// `mapQuery` targets the kind of store that carries the drink rather than
    /// the drink itself.
    private func drinkDestinations(mapQuery: String, searchTerm: String) -> [DrinkDestination] {
        var appleMaps = URLComponents(string: "maps://")!
        appleMaps.queryItems = [URLQueryItem(name: "q", value: mapQuery)]

        var googleMaps = URLComponents(string: "https://www.google.com/maps/search/")!
        googleMaps.queryItems = [
            URLQueryItem(name: "api", value: "1"),
            URLQueryItem(name: "query", value: "\(mapQuery) near me"),
        ]

        var doorDash = URLComponents()
        doorDash.scheme = "https"
        doorDash.host = "www.doordash.com"
        doorDash.path = "/search/store/\(searchTerm)/"

        var instacart = URLComponents(string: "https://www.instacart.com/store/s")!
        instacart.queryItems = [URLQueryItem(name: "k", value: searchTerm)]

        var uberEats = URLComponents(string: "https://www.ubereats.com/search")!
        uberEats.queryItems = [URLQueryItem(name: "q", value: searchTerm)]

        return [
            DrinkDestination(title: "Nearby Stores (Apple Maps)", symbol: "map.fill", url: appleMaps.url),
            DrinkDestination(title: "Nearby Stores (Google Maps)", symbol: "map", url: googleMaps.url),
            DrinkDestination(title: "Find on DoorDash", symbol: "bag.fill", url: doorDash.url),
            DrinkDestination(title: "Find on Instacart", symbol: "cart", url: instacart.url),
            DrinkDestination(title: "Find on Uber Eats", symbol: "bag", url: uberEats.url),
        ]
    }

    @objc private func toggleLaunchAtLogin() {
        if #available(macOS 13.0, *) {
            do {
                if SMAppService.mainApp.status == .enabled {
                    try SMAppService.mainApp.unregister()
                } else {
                    try SMAppService.mainApp.register()
                }
            } catch {
                print("Failed to toggle launch at login: \(error)")
            }
        }
    }

    private func formatDuration(_ seconds: Int) -> String {
        if seconds < 60 {
            return "\(seconds)s"
        } else if seconds < 3600 {
            let mins = seconds / 60
            return "\(mins)m"
        } else {
            let hrs = seconds / 3600
            let mins = (seconds % 3600) / 60
            return "\(hrs)h \(mins)m"
        }
    }
}
