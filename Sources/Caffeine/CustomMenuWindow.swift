import AppKit

/// A destination link surfaced by the "Get an Energy Drink" / "Get a Coffee"
/// pickers — a nearby-stores map search or a delivery-app search.
struct DrinkDestination {
    let title: String
    let symbol: String
    let url: URL?
}

/// A fully custom replacement for NSMenu: a borderless, dark glassy panel
/// with its own hover-highlighted rows, colored icon chips, and an inline
/// accordion (rather than flyout submenus) for drilling into drink brands
/// and their destinations.
///
/// Trade-off, accepted deliberately: we lose native menu behavior for free
/// (arrow-key navigation, automatic screen-edge flipping, VoiceOver menu
/// semantics) in exchange for full control over look and layout.
final class CustomMenuWindow: NSPanel {
    /// `.full` is the hover-triggered drink/icon-style browser; `.compact` is
    /// the right-click fallback with just Launch at Login and Quit.
    enum PresentationMode {
        case full
        case compact
    }

    private let visualEffect = NSVisualEffectView()
    private let stack = NSStackView()

    private var expandedPath: [String] = []
    private var mode: PresentationMode = .full
    private(set) var isHoverPresented = false
    private let width: CGFloat = 264

    private let energyTint = NSColor(calibratedRed: 0.98, green: 0.70, blue: 0.15, alpha: 1)
    private let coffeeTint = NSColor(calibratedRed: 0.72, green: 0.52, blue: 0.32, alpha: 1)
    private let styleTint = NSColor(calibratedRed: 0.66, green: 0.52, blue: 0.98, alpha: 1)
    private let loginTint = NSColor(calibratedRed: 0.35, green: 0.65, blue: 0.98, alpha: 1)
    private let lidSleepTint = NSColor(calibratedRed: 0.40, green: 0.85, blue: 0.60, alpha: 1)

    var isActive = false
    var activeDetail = ""
    var iconStyle: IconStyle = .energyDrink
    var launchAtLoginEnabled = false
    var preventSleepOnLidClose = false
    var energyDrinkBrands: [String] = []
    var destinationsProvider: (_ mapQuery: String, _ searchTerm: String) -> [DrinkDestination] = { _, _ in [] }

    var onOpenURL: ((URL) -> Void)?
    var onSelectIconStyle: ((IconStyle) -> Void)?
    var onToggleLaunchAtLogin: (() -> Void)?
    var onTogglePreventSleepOnLidClose: (() -> Void)?
    var onShowTutorial: (() -> Void)?
    var onQuit: (() -> Void)?
    var onClose: (() -> Void)?

    /// Fired when the cursor enters/exits the panel itself, so a hover-triggered
    /// presentation can stay open while the mouse crosses from the status item
    /// down into the panel instead of closing the instant the cursor leaves the icon.
    var onHoverEnter: (() -> Void)?
    var onHoverExit: (() -> Void)?

    private var globalClickMonitor: Any?
    private var localClickMonitor: Any?
    private var keyMonitor: Any?
    private var hoverTrackingArea: NSTrackingArea?

    init() {
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: width, height: 10),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered,
            defer: false
        )
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        level = .popUpMenu
        isMovableByWindowBackground = false
        hidesOnDeactivate = false
        // Force a dark, branded look regardless of the system appearance,
        // rather than following light mode like a native menu would.
        appearance = NSAppearance(named: .darkAqua)

        visualEffect.material = .hudWindow
        visualEffect.state = .active
        visualEffect.blendingMode = .behindWindow
        visualEffect.wantsLayer = true
        visualEffect.layer?.cornerRadius = 16
        visualEffect.layer?.masksToBounds = true
        visualEffect.layer?.borderWidth = 1
        visualEffect.layer?.borderColor = NSColor.white.withAlphaComponent(0.1).cgColor
        visualEffect.translatesAutoresizingMaskIntoConstraints = false

        let tint = NSView()
        tint.wantsLayer = true
        tint.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.35).cgColor
        tint.translatesAutoresizingMaskIntoConstraints = false

        stack.orientation = .vertical
        stack.alignment = .leading
        stack.spacing = 0
        stack.translatesAutoresizingMaskIntoConstraints = false

        contentView = visualEffect
        visualEffect.addSubview(tint)
        visualEffect.addSubview(stack)
        NSLayoutConstraint.activate([
            tint.topAnchor.constraint(equalTo: visualEffect.topAnchor),
            tint.bottomAnchor.constraint(equalTo: visualEffect.bottomAnchor),
            tint.leadingAnchor.constraint(equalTo: visualEffect.leadingAnchor),
            tint.trailingAnchor.constraint(equalTo: visualEffect.trailingAnchor),

            stack.topAnchor.constraint(equalTo: visualEffect.topAnchor, constant: 8),
            stack.bottomAnchor.constraint(equalTo: visualEffect.bottomAnchor, constant: -8),
            stack.leadingAnchor.constraint(equalTo: visualEffect.leadingAnchor),
            stack.trailingAnchor.constraint(equalTo: visualEffect.trailingAnchor),
        ])

        let area = NSTrackingArea(
            rect: .zero,
            options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect],
            owner: self,
            userInfo: nil
        )
        visualEffect.addTrackingArea(area)
        hoverTrackingArea = area
    }

    required init?(coder: NSCoder) { fatalError("not supported") }

    override var canBecomeKey: Bool { true }

    override func mouseEntered(with event: NSEvent) {
        onHoverEnter?()
    }

    override func mouseExited(with event: NSEvent) {
        onHoverExit?()
    }

    // MARK: - Presentation

    /// - Parameter hoverPresented: whether this presentation should be treated
    ///   as a hover preview (see `onHoverExit`) rather than a click-invoked menu.
    func present(relativeTo button: NSStatusBarButton, mode: PresentationMode, hoverPresented: Bool) {
        if mode == .compact, isVisible, self.mode == .compact {
            hide()
            return
        }

        self.mode = mode
        isHoverPresented = hoverPresented
        expandedPath = []
        rebuild()

        guard let buttonWindow = button.window else { return }
        let screenFrame = buttonWindow.convertToScreen(button.convert(button.bounds, to: nil))
        visualEffect.layoutSubtreeIfNeeded()
        let height = max(visualEffect.fittingSize.height, 10)
        var origin = NSPoint(x: screenFrame.minX, y: screenFrame.minY - height - 6)

        if let screen = buttonWindow.screen ?? NSScreen.main {
            origin.x = min(origin.x, screen.visibleFrame.maxX - width - 4)
            origin.x = max(origin.x, screen.visibleFrame.minX + 4)
        }

        setFrame(NSRect(x: origin.x, y: origin.y, width: width, height: height), display: true)
        makeKeyAndOrderFront(nil)
        startMonitoringDismissal()
    }

    func hide() {
        guard isVisible else { return }
        stopMonitoringDismissal()
        isHoverPresented = false
        orderOut(nil)
        onClose?()
    }

    func setLaunchAtLoginEnabled(_ enabled: Bool) {
        launchAtLoginEnabled = enabled
        rebuild()
    }

    func setPreventSleepOnLidClose(_ enabled: Bool) {
        preventSleepOnLidClose = enabled
        rebuild()
    }

    override func resignKey() {
        super.resignKey()
        hide()
    }

    private func startMonitoringDismissal() {
        globalClickMonitor = NSEvent.addGlobalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] _ in
            self?.hide()
        }
        localClickMonitor = NSEvent.addLocalMonitorForEvents(matching: [.leftMouseDown, .rightMouseDown]) { [weak self] event in
            guard let self else { return event }
            if event.window !== self {
                self.hide()
            }
            return event
        }
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self else { return event }
            if event.keyCode == 53 { // Escape
                self.hide()
                return nil
            }
            return event
        }
    }

    private func stopMonitoringDismissal() {
        if let globalClickMonitor { NSEvent.removeMonitor(globalClickMonitor) }
        if let localClickMonitor { NSEvent.removeMonitor(localClickMonitor) }
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        globalClickMonitor = nil
        localClickMonitor = nil
        keyMonitor = nil
    }

    // MARK: - Content

    private func toggle(_ path: [String]) {
        expandedPath = expandedPath == path ? Array(path.dropLast()) : path
        rebuild()
        resize()
    }

    private func resize() {
        visualEffect.layoutSubtreeIfNeeded()
        let height = max(visualEffect.fittingSize.height, 10)
        let topY = frame.maxY
        setFrame(NSRect(x: frame.minX, y: topY - height, width: width, height: height), display: true)
    }

    private func rebuild() {
        stack.arrangedSubviews.forEach {
            stack.removeArrangedSubview($0)
            $0.removeFromSuperview()
        }
        for row in buildRows() {
            row.widthAnchor.constraint(equalToConstant: width).isActive = true
            stack.addArrangedSubview(row)
        }
    }

    private func buildRows() -> [NSView] {
        switch mode {
        case .full: return fullRows()
        case .compact: return compactRows()
        }
    }

    private func fullRows() -> [NSView] {
        var rows: [NSView] = []

        let header = StatusHeaderView()
        header.configure(active: isActive, duration: activeDetail)
        rows.append(header)
        rows.append(MenuSeparatorView())

        rows += energySectionRows()
        rows += coffeeSectionRows()
        rows.append(MenuSeparatorView())
        rows += iconStyleSectionRows()
        rows.append(MenuSeparatorView())
        rows += loginAndQuitRows()

        return rows
    }

    /// The right-click menu — just the two housekeeping actions, no drinks browsing.
    private func compactRows() -> [NSView] {
        loginAndQuitRows()
    }

    private func loginAndQuitRows() -> [NSView] {
        [
            MenuRowView(
                title: "Launch at Login",
                symbolName: "power",
                tint: loginTint,
                indent: 12,
                accessory: .checkmark(selected: launchAtLoginEnabled),
                onSelect: { [weak self] in self?.onToggleLaunchAtLogin?() }
            ),
            MenuRowView(
                title: "Prevent Sleep on Lid Close",
                symbolName: "laptopcomputer",
                tint: lidSleepTint,
                indent: 12,
                accessory: .checkmark(selected: preventSleepOnLidClose),
                onSelect: { [weak self] in self?.onTogglePreventSleepOnLidClose?() }
            ),
            MenuRowView(
                title: "Tutorial",
                symbolName: "questionmark.circle",
                tint: styleTint,
                indent: 12,
                onSelect: { [weak self] in
                    self?.onShowTutorial?()
                    self?.hide()
                }
            ),
            MenuSeparatorView(),
            MenuRowView(
                title: "Quit Caffeine Bar",
                symbolName: "xmark.circle.fill",
                tint: .systemRed,
                indent: 12,
                titleColor: NSColor.systemRed.withAlphaComponent(0.9),
                onSelect: { [weak self] in self?.onQuit?() }
            ),
        ]
    }

    private func energySectionRows() -> [NSView] {
        var rows: [NSView] = []
        let expanded = expandedPath.first == "energy"

        rows.append(MenuRowView(
            title: "Get an Energy Drink",
            symbolName: "bolt.fill",
            tint: energyTint,
            indent: 12,
            accessory: .chevron(expanded: expanded),
            emphasized: true,
            onSelect: { [weak self] in self?.toggle(["energy"]) }
        ))

        guard expanded else { return rows }

        for brand in energyDrinkBrands {
            let brandPath = ["energy", brand]
            let brandExpanded = expandedPath == brandPath
            rows.append(MenuRowView(
                title: brand,
                tint: energyTint,
                indent: 26,
                accessory: .chevron(expanded: brandExpanded),
                onSelect: { [weak self] in self?.toggle(brandPath) }
            ))
            if brandExpanded {
                rows += destinationRows(
                    mapQuery: "gas station OR convenience store OR grocery store",
                    searchTerm: brand.lowercased(),
                    indent: 36
                )
            }
        }
        return rows
    }

    private func coffeeSectionRows() -> [NSView] {
        var rows: [NSView] = []
        let expanded = expandedPath.first == "coffee"

        rows.append(MenuRowView(
            title: "Get a Coffee",
            symbolName: "cup.and.saucer.fill",
            tint: coffeeTint,
            indent: 12,
            accessory: .chevron(expanded: expanded),
            emphasized: true,
            onSelect: { [weak self] in self?.toggle(["coffee"]) }
        ))

        guard expanded else { return rows }
        rows += destinationRows(mapQuery: "coffee shop", searchTerm: "coffee", indent: 26)
        return rows
    }

    private func destinationRows(mapQuery: String, searchTerm: String, indent: CGFloat) -> [NSView] {
        var rows: [NSView] = []
        let destinations = destinationsProvider(mapQuery, searchTerm)
        for (index, destination) in destinations.enumerated() {
            rows.append(MenuRowView(
                title: destination.title,
                symbolName: destination.symbol,
                tint: .white.withAlphaComponent(0.7),
                indent: indent,
                isEnabled: destination.url != nil,
                onSelect: { [weak self] in
                    guard let url = destination.url else { return }
                    self?.onOpenURL?(url)
                    self?.hide()
                }
            ))
            if index == 1 {
                rows.append(MenuSeparatorView())
            }
        }
        return rows
    }

    private func iconStyleSectionRows() -> [NSView] {
        var rows: [NSView] = []
        let expanded = expandedPath.first == "iconStyle"

        rows.append(MenuRowView(
            title: "Icon Style",
            symbolName: "paintbrush.fill",
            tint: styleTint,
            indent: 12,
            accessory: .chevron(expanded: expanded),
            emphasized: true,
            onSelect: { [weak self] in self?.toggle(["iconStyle"]) }
        ))

        guard expanded else { return rows }

        rows.append(MenuRowView(
            title: "Coffee",
            symbolName: "cup.and.saucer.fill",
            tint: coffeeTint,
            indent: 26,
            accessory: .checkmark(selected: iconStyle == .coffee),
            onSelect: { [weak self] in self?.selectIconStyle(.coffee) }
        ))
        rows.append(MenuRowView(
            title: "Energy Drink",
            symbolName: "bolt.fill",
            tint: energyTint,
            indent: 26,
            accessory: .checkmark(selected: iconStyle == .energyDrink),
            onSelect: { [weak self] in self?.selectIconStyle(.energyDrink) }
        ))
        return rows
    }

    private func selectIconStyle(_ style: IconStyle) {
        iconStyle = style
        onSelectIconStyle?(style)
        rebuild()
    }
}
