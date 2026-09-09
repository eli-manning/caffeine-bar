import AppKit

/// A short, self-contained walkthrough shown on first launch and re-openable
/// anytime from the icon's right-click menu.
///
/// Clicking, hovering and right-clicking all do different things here, and none
/// of that is discoverable from a menu bar icon on its own — so rather than hope
/// people find it, the app explains itself once and then gets out of the way.
///
/// Unlike CustomMenuWindow this is a real, focusable window: somewhere the user
/// reads and clicks through, not a transient panel that dismisses on the next click.
final class TutorialWindow: NSWindow {
    private struct Step {
        let heading: String
        let body: String
    }

    private static let steps: [Step] = [
        Step(
            heading: "Keep your Mac awake",
            body: """
            Caffeine Bar stops your screen from sleeping for as long as it's active, \
            so your session stays alive. Switch it off and your normal Energy Saver \
            settings take over again. It doesn't change anything permanently.
            """
        ),
        Step(
            heading: "Find the icon",
            body: """
            Caffeine Bar lives in the menu bar, up near the clock.

            If your menu bar is crowded it can get pushed out of sight, which happens \
            easily on a Mac with a notch. Hold Command and drag menu bar icons to \
            reorder them and pull it back where you can see it.
            """
        ),
        Step(
            heading: "Click to toggle",
            body: """
            Click the icon to switch it on and off.

            Filled means it's running: the can fills in and sprouts wings, or the cup \
            puffs steam, depending on the icon style you picked. An empty outline \
            means it's off.
            """
        ),
        Step(
            heading: "Hover for the menu",
            body: """
            Hover the icon to open the full panel. It shows how long you've been \
            active, the energy drink and coffee finders, and the icon style switch.

            Pick Coffee or Energy Drink under Icon Style to change which drink the \
            icon shows.
            """
        ),
        Step(
            heading: "Right-click for settings",
            body: """
            Right-click for Launch at Login, Prevent Sleep on Lid Close, this \
            tutorial, and Quit.

            Closing the lid puts the Mac to sleep in hardware, so that one needs an \
            admin password the first time. Caffeine Bar installs a narrow sudoers rule \
            covering only that command, then never has to ask again.
            """
        ),
    ]

    private var index = 0

    private let headingLabel = NSTextField(labelWithString: "")
    private let bodyLabel = NSTextField(wrappingLabelWithString: "")
    private let dotsView = DotsView(count: TutorialWindow.steps.count)
    private let backButton = PillButton(title: "Back", tint: NSColor.white.withAlphaComponent(0.55), filled: false)
    private let nextButton = PillButton(title: "Next", tint: TutorialWindow.energyTint, filled: true)
    private let skipButton = PillButton(title: "Skip", tint: NSColor.white.withAlphaComponent(0.4), filled: false)

    private static let energyTint = NSColor(calibratedRed: 0.98, green: 0.70, blue: 0.15, alpha: 1)
    private static let windowSize = NSSize(width: 460, height: 320)
    private static let pad: CGFloat = 26

    init() {
        super.init(
            contentRect: NSRect(origin: .zero, size: TutorialWindow.windowSize),
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        isOpaque = false
        backgroundColor = .clear
        hasShadow = true
        level = .floating
        isMovableByWindowBackground = true
        // Forced dark, to match the custom menu panel rather than following the system.
        appearance = NSAppearance(named: .darkAqua)

        let effect = NSVisualEffectView()
        effect.material = .hudWindow
        effect.state = .active
        effect.blendingMode = .behindWindow
        effect.wantsLayer = true
        effect.layer?.cornerRadius = 16
        effect.layer?.masksToBounds = true
        effect.layer?.borderWidth = 1
        effect.layer?.borderColor = NSColor.white.withAlphaComponent(0.1).cgColor
        contentView = effect

        let tint = NSView()
        tint.wantsLayer = true
        tint.layer?.backgroundColor = NSColor.black.withAlphaComponent(0.35).cgColor
        tint.translatesAutoresizingMaskIntoConstraints = false
        effect.addSubview(tint)

        let brandLabel = NSTextField(labelWithString: "CAFFEINE BAR")
        brandLabel.font = .systemFont(ofSize: 10, weight: .bold)
        brandLabel.textColor = TutorialWindow.energyTint.withAlphaComponent(0.85)
        brandLabel.translatesAutoresizingMaskIntoConstraints = false

        headingLabel.font = .systemFont(ofSize: 21, weight: .bold)
        headingLabel.textColor = .white
        headingLabel.translatesAutoresizingMaskIntoConstraints = false

        bodyLabel.font = .systemFont(ofSize: 13)
        bodyLabel.textColor = NSColor.white.withAlphaComponent(0.72)
        bodyLabel.translatesAutoresizingMaskIntoConstraints = false

        dotsView.translatesAutoresizingMaskIntoConstraints = false

        for view in [brandLabel, headingLabel, bodyLabel, dotsView, backButton, nextButton, skipButton] {
            effect.addSubview(view)
        }

        backButton.onClick = { [weak self] in self?.go(to: (self?.index ?? 1) - 1) }
        skipButton.onClick = { [weak self] in self?.finish() }
        nextButton.onClick = { [weak self] in
            guard let self else { return }
            if index == TutorialWindow.steps.count - 1 { finish() } else { go(to: index + 1) }
        }

        let pad = TutorialWindow.pad
        NSLayoutConstraint.activate([
            tint.topAnchor.constraint(equalTo: effect.topAnchor),
            tint.bottomAnchor.constraint(equalTo: effect.bottomAnchor),
            tint.leadingAnchor.constraint(equalTo: effect.leadingAnchor),
            tint.trailingAnchor.constraint(equalTo: effect.trailingAnchor),

            brandLabel.topAnchor.constraint(equalTo: effect.topAnchor, constant: pad),
            brandLabel.leadingAnchor.constraint(equalTo: effect.leadingAnchor, constant: pad),

            headingLabel.topAnchor.constraint(equalTo: brandLabel.bottomAnchor, constant: 8),
            headingLabel.leadingAnchor.constraint(equalTo: effect.leadingAnchor, constant: pad),
            headingLabel.trailingAnchor.constraint(equalTo: effect.trailingAnchor, constant: -pad),

            bodyLabel.topAnchor.constraint(equalTo: headingLabel.bottomAnchor, constant: 12),
            bodyLabel.leadingAnchor.constraint(equalTo: effect.leadingAnchor, constant: pad),
            bodyLabel.trailingAnchor.constraint(equalTo: effect.trailingAnchor, constant: -pad),

            backButton.leadingAnchor.constraint(equalTo: effect.leadingAnchor, constant: pad),
            backButton.bottomAnchor.constraint(equalTo: effect.bottomAnchor, constant: -pad),
            skipButton.leadingAnchor.constraint(equalTo: effect.leadingAnchor, constant: pad),
            skipButton.bottomAnchor.constraint(equalTo: effect.bottomAnchor, constant: -pad),
            nextButton.trailingAnchor.constraint(equalTo: effect.trailingAnchor, constant: -pad),
            nextButton.bottomAnchor.constraint(equalTo: effect.bottomAnchor, constant: -pad),

            dotsView.centerXAnchor.constraint(equalTo: effect.centerXAnchor),
            dotsView.centerYAnchor.constraint(equalTo: nextButton.centerYAnchor),
        ])

        go(to: 0)
    }

    override var canBecomeKey: Bool { true }

    /// Centers on whichever screen the pointer is on, so it lands where the user
    /// just clicked rather than always on the primary display.
    func present() {
        let screen = NSScreen.screens.first { NSMouseInRect(NSEvent.mouseLocation, $0.frame, false) }
            ?? NSScreen.main
        if let visible = screen?.visibleFrame {
            setFrameOrigin(NSPoint(
                x: visible.midX - frame.width / 2,
                y: visible.midY - frame.height / 2
            ))
        }
        NSApp.activate(ignoringOtherApps: true)
        makeKeyAndOrderFront(nil)
    }

    private func go(to newIndex: Int) {
        index = min(max(newIndex, 0), TutorialWindow.steps.count - 1)
        let step = TutorialWindow.steps[index]
        let isLast = index == TutorialWindow.steps.count - 1

        headingLabel.stringValue = step.heading
        bodyLabel.stringValue = step.body
        backButton.isHidden = index == 0
        skipButton.isHidden = index != 0
        nextButton.title = isLast ? "Done" : "Next"
        dotsView.current = index
    }

    private func finish() {
        orderOut(nil)
    }

    override func keyDown(with event: NSEvent) {
        switch event.keyCode {
        case 53: finish()                                   // Escape
        case 124, 36: nextButton.onClick?()                 // Right arrow, Return
        case 123 where index > 0: go(to: index - 1)         // Left arrow
        default: super.keyDown(with: event)
        }
    }

    /// The step indicator: one filled dot for the current step, dim for the rest.
    private final class DotsView: NSView {
        private let count: Int
        var current = 0 { didSet { needsDisplay = true } }

        init(count: Int) {
            self.count = count
            super.init(frame: .zero)
            let dot: CGFloat = 6, gap: CGFloat = 7
            widthAnchor.constraint(equalToConstant: CGFloat(count) * dot + CGFloat(count - 1) * gap).isActive = true
            heightAnchor.constraint(equalToConstant: dot).isActive = true
        }

        required init?(coder: NSCoder) { fatalError("not supported") }

        override func draw(_ dirtyRect: NSRect) {
            let dot: CGFloat = 6, gap: CGFloat = 7
            for i in 0..<count {
                let color = i == current ? TutorialWindow.energyTint : NSColor.white.withAlphaComponent(0.18)
                color.setFill()
                NSBezierPath(ovalIn: NSRect(x: CGFloat(i) * (dot + gap), y: 0, width: dot, height: dot)).fill()
            }
        }
    }

    /// A small rounded button — filled for the primary action, outlined otherwise.
    private final class PillButton: NSView {
        private let tint: NSColor
        private let filled: Bool
        private var isHovered = false { didSet { needsDisplay = true } }
        private var trackingAreaRef: NSTrackingArea?
        private var widthConstraint: NSLayoutConstraint?

        var onClick: (() -> Void)?

        var title: String {
            didSet {
                widthConstraint?.constant = PillButton.width(for: title)
                needsDisplay = true
            }
        }

        init(title: String, tint: NSColor, filled: Bool) {
            self.title = title
            self.tint = tint
            self.filled = filled
            super.init(frame: .zero)
            translatesAutoresizingMaskIntoConstraints = false
            heightAnchor.constraint(equalToConstant: 30).isActive = true
            let width = widthAnchor.constraint(equalToConstant: PillButton.width(for: title))
            width.isActive = true
            widthConstraint = width
        }

        required init?(coder: NSCoder) { fatalError("not supported") }

        private static let font = NSFont.systemFont(ofSize: 12, weight: .semibold)

        private static func width(for title: String) -> CGFloat {
            let size = (title as NSString).size(withAttributes: [.font: font])
            return max(78, ceil(size.width) + 32)
        }

        override func updateTrackingAreas() {
            super.updateTrackingAreas()
            if let trackingAreaRef { removeTrackingArea(trackingAreaRef) }
            let area = NSTrackingArea(
                rect: bounds,
                options: [.mouseEnteredAndExited, .activeAlways, .inVisibleRect],
                owner: self,
                userInfo: nil
            )
            addTrackingArea(area)
            trackingAreaRef = area
        }

        override func mouseEntered(with event: NSEvent) { isHovered = true }
        override func mouseExited(with event: NSEvent) { isHovered = false }
        override func mouseDown(with event: NSEvent) { onClick?() }

        override func resetCursorRects() {
            addCursorRect(bounds, cursor: .pointingHand)
        }

        override func draw(_ dirtyRect: NSRect) {
            let rect = bounds.insetBy(dx: 0.5, dy: 0.5)
            let path = NSBezierPath(roundedRect: rect, xRadius: rect.height / 2, yRadius: rect.height / 2)

            if filled {
                (isHovered ? tint : tint.withAlphaComponent(0.88)).setFill()
                path.fill()
            } else {
                tint.withAlphaComponent(isHovered ? 0.5 : 0.28).setStroke()
                path.lineWidth = 1
                path.stroke()
            }

            let color: NSColor = filled ? .black : tint.withAlphaComponent(isHovered ? 1.0 : 0.8)
            let attributes: [NSAttributedString.Key: Any] = [
                .font: PillButton.font,
                .foregroundColor: color,
            ]
            let size = (title as NSString).size(withAttributes: attributes)
            (title as NSString).draw(
                at: NSPoint(x: bounds.midX - size.width / 2, y: bounds.midY - size.height / 2),
                withAttributes: attributes
            )
        }
    }
}
