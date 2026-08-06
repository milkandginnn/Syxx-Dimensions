import SwiftUI
import AppKit
import OSLog
import SyxxKit
import UserNotifications
import SyxxTelemetry

/// Sets the running app's Dock/Cmd-Tab icon from the bundled `Syxx.icns`. When the binary
/// is wrapped into `Syxx.app` (see `bundle-app.sh`), `CFBundleIconFile` already supplies the
/// Finder icon; this makes the icon appear even when running the bare SwiftPM binary in dev.
final class SyxxAppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        // Run as a proper foreground app even when launched unbundled (the dev binary has no Info.plist):
        // .regular gives a Dock icon, normal window layering, AND a window that can become KEY — which is
        // what lets text fields receive keyboard input. NOT always-on-top (that is window level, unchanged).
        NSApplication.shared.setActivationPolicy(.regular)

        if let url = Bundle.module.url(forResource: "Syxx", withExtension: "icns"),
           let icon = NSImage(contentsOf: url) {
            NSApplication.shared.applicationIconImage = icon
        }
        
        // Register the bundled typography system (every face in SyxxTypography.bundledFonts).
        // The CFError is NOT discarded: a font that fails to register renders as the system
        // font with no error anywhere, so the only symptom is "the app looks slightly wrong" —
        // a silent-fallback defect that survived a full release cycle. Fail loud instead.
        Self.registerTypographyOrComplainLoudly()

        // Start the window borderless — FirstRunWindowConfigurator (in the SwiftUI
        // hierarchy) transitions to standard chrome when first-run completes. For
        // returning users (state already .ready), the configurator adds chrome on
        // its first layout pass (~1 frame). For first-run users, the borderless
        // window shows only the EULA or Setup card with no macOS chrome.
        DispatchQueue.main.async {
            if let window = NSApplication.shared.windows.first {
                window.styleMask = .borderless
                window.setFrame(NSRect(x: 0, y: 0, width: 640, height: 660),
                                display: true, animate: false)
                window.center()
            }
        }
    }

    private static let typographyLog = Logger(subsystem: "com.milkandgin.syxx", category: "typography")

    /// Registers the bundled faces and makes any shortfall impossible to miss.
    ///
    /// Escalation is tri-state, matching `FontRegistrationOutcome`:
    ///   • allRegistered      → one debug line, nothing user-visible.
    ///   • partiallyRegistered→ `Logger.fault` + stderr, one line per failing face.
    ///   • noneRegistered     → the above, plus `assertionFailure` so a debug build traps at
    ///                          launch instead of shipping a release that renders in Helvetica.
    ///
    /// No GUI copy is emitted here on purpose: user-visible strings need sign-off, and a
    /// console fault is already loud enough for the packaging bug this guards against.
    @discardableResult
    static func registerTypographyOrComplainLoudly() -> SyxxTypography.FontRegistrationReport {
        let report = SyxxTypography.registerBundledFonts()
        let declared = SyxxTypography.bundledFonts.count

        switch report.outcome {
        case .allRegistered:
            typographyLog.debug("Typography: registered all \(declared, privacy: .public) bundled faces.")

        case .partiallyRegistered, .noneRegistered:
            let headline = report.outcome == .noneRegistered
                ? "TYPOGRAPHY REGISTRATION FAILED: 0 of \(declared) bundled faces resolved."
                : "TYPOGRAPHY DEGRADED: only \(report.registered.count) of \(declared) bundled faces resolved."
            typographyLog.fault("\(headline, privacy: .public)")
            FileHandle.standardError.write(Data(("[syxx] " + headline + "\n").utf8))

            for failure in report.failures {
                typographyLog.fault("Typography: \(failure.diagnostic, privacy: .public)")
                FileHandle.standardError.write(Data(("[syxx]   ✗ " + failure.diagnostic + "\n").utf8))
            }

            if report.outcome == .noneRegistered {
                let hint = "Every Font.custom(...) will silently fall back to the system font. "
                         + "Check that the flat .ttf files are at Sources/SyxxApp/Resources/ root "
                         + "and that bundle-app.sh copied them into Contents/Resources."
                typographyLog.fault("\(hint, privacy: .public)")
                FileHandle.standardError.write(Data(("[syxx] " + hint + "\n").utf8))
                assertionFailure(headline + " " + hint)
            }
        }

        return report
    }

    // F014: Bring window to front when clicking the Dock icon.
    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag {
            for window in sender.windows {
                if window.isKeyWindow || window.canBecomeKey {
                    window.makeKeyAndOrderFront(nil)
                    break
                }
            }
            sender.activate(ignoringOtherApps: true)
        }
        return true
    }

    // F011: Ensure the app activates (becomes frontmost) when launched, so the window
    // receives keyboard focus instead of staying behind other apps.
    func applicationDidBecomeActive(_ notification: Notification) {
        NSApp.activate(ignoringOtherApps: true)
    }

    // Flush in-flight OTel spans before the process exits so no telemetry is lost.
    func applicationWillTerminate(_ notification: Notification) {
        OTelBootstrap.flush()
    }
}

/// Deliberately minimal macOS GUI: a single window with a sidebar of tools and, for each,
/// the input fields it needs plus an output pane. It wraps the same `SyxxKit` the CLIs use,
/// so the GUI and CLI can never drift apart. Not a designed product surface — just enough
/// I/O to drive every feature from a window.
public struct SyxxApp: App {
    @NSApplicationDelegateAdaptor(SyxxAppDelegate.self) private var appDelegate
    @StateObject private var model = AppModel()
    @StateObject private var firstRun = FirstRunCoordinator.live()

    public init() {}

    public var body: some Scene {
        WindowGroup("Syxx \(SyxxConfig.version)") {
            Group {
                switch firstRun.state {
                case .ready:
                    ReadyApplicationView(model: model, settings: firstRun.settings)
                default:
                    GuidedSetup(coordinator: firstRun)
                }
            }
            .background(FirstRunWindowConfigurator(state: firstRun.state))
        }
        .commands {
            CommandGroup(replacing: .appInfo) {
                Button("About Syxx") { NSPanelManager.shared.showAbout() }
                    .disabled(firstRun.state != .ready)
            }

            CommandGroup(after: .appSettings) {
                Button("Settings…") { NSPanelManager.shared.showSettings(model: model) }
                    .keyboardShortcut(",", modifiers: [.command])
                    .disabled(firstRun.state != .ready)
                Button("License & Covenant…") { NSPanelManager.shared.showLicense(model: model) }
                    .disabled(firstRun.state != .ready)
            }

            CommandGroup(after: .newItem) {
                Button("Check for Updates…") { NSPanelManager.shared.showUpdate() }
                    .keyboardShortcut("U", modifiers: [.command])
                    .disabled(firstRun.state != .ready)
            }

            CommandMenu("Help") {
                Button("License Agreement") {
                    NSPanelManager.shared.showEULA(menuPresented: true)
                }
                .disabled(firstRun.state != .ready)
                Button("License & Pro Covenant") {
                    NSPanelManager.shared.showLicense(model: model)
                }
                .disabled(firstRun.state != .ready)
                Divider()
                Button("Syxx Help") {
                    NSWorkspace.shared.open(URL(string: "https://syxx.app/help")!)
                }
                .keyboardShortcut("?", modifiers: [.command])
                .disabled(firstRun.state != .ready)
            }
        }
        .windowStyle(.hiddenTitleBar)

        MenuBarExtra(isInserted: Binding(
            get: { firstRun.state == .ready },
            set: { _ in }
        )) {
            MenuBarContent().environmentObject(model)
        } label: {
            let ind = TrayIndicators.For(model.conclusion)
            MenuBarLabel(severity: ind.severity, isEncrypted: model.isEncrypted)
        }
    }
}

// MARK: - First-run window chrome management

/// Manages the main window's chrome and size based on the first-run state. During first-run
/// (`.agreementRequired`, `.setupRequired`, etc.), the window is borderless — no titlebar,
/// no traffic lights, no resize handle — and sized to the current card. When setup completes
/// (`.ready`), the window transitions to standard chrome at 1040×613 with the content's
/// flexible min-size floor.
///
/// Placed as a `.background()` on the WindowGroup root so it runs for every state, including
/// the `.ready` transition. The `WindowChromeConfigurator` in ContentView handles ongoing
/// chrome tweaks (fullSizeContentView, toolbar removal) once the main view is visible.
private struct FirstRunWindowConfigurator: NSViewRepresentable {
    let state: OnboardingState

    func makeNSView(context: Context) -> NSView {
        let v = NSView()
        DispatchQueue.main.async { Self.configure(v.window, state: state) }
        return v
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        DispatchQueue.main.async { Self.configure(nsView.window, state: state) }
    }

    static func configure(_ window: NSWindow?, state: OnboardingState) {
        guard let window else { return }

        switch state {
        case .ready:
            // Transition from borderless to standard chrome. Guard on .borderless to
            // avoid re-applying chrome on every SwiftUI invalidation (the ready path
            // is hot — ContentView redraws frequently).
            guard window.styleMask.contains(.borderless) else { return }
            window.styleMask = [.titled, .closable, .miniaturizable, .resizable, .fullSizeContentView]
            window.titlebarAppearsTransparent = true
            window.titleVisibility = .hidden
            window.toolbar = nil
            window.isMovableByWindowBackground = true
            window.hasShadow = true
            // Unlock resizing — the ready state allows the user to grow the window
            // beyond 1040×613 (ContentView uses maxWidth/maxHeight .infinity).
            window.contentMinSize = NSSize(width: 1040, height: 613)
            window.contentMaxSize = NSSize(width: CGFloat.greatestFiniteMagnitude,
                                           height: CGFloat.greatestFiniteMagnitude)
            var f = window.frame
            f.size = NSSize(width: 1040, height: 613)
            window.setFrame(f, display: true, animate: true)
            window.center()

        default:
            // First-run: borderless, sized to the current card. Lock min==max to
            // prevent SwiftUI's .frame(minWidth: 1050) on GuidedSetup from growing
            // the window behind our back.
            if !window.styleMask.contains(.borderless) {
                window.styleMask = .borderless
            }
            window.toolbar = nil
            window.isMovableByWindowBackground = true
            window.hasShadow = true

            let cardSize = Self.cardSize(for: state)
            window.contentMinSize = cardSize
            window.contentMaxSize = cardSize

            // Only reframe if the current size doesn't match — avoids animation
            // jitter on every SwiftUI layout pass.
            if abs(window.frame.width - cardSize.width) > 1
                || abs(window.frame.height - cardSize.height) > 1 {
                window.setFrame(NSRect(origin: .zero, size: cardSize),
                                display: true, animate: true)
                window.center()
            }
        }
    }

    /// Window size per first-run state. Each card has its own backdrop fill; the window
    /// is sized tightly around it so no empty space shows beyond the card's shadow.
    private static func cardSize(for state: OnboardingState) -> NSSize {
        switch state {
        case .loading:
            return NSSize(width: 300, height: 200)
        case .agreementRequired:
            // EULA card is 600 wide; add 20px horizontal breathing room for the shadow.
            return NSSize(width: 640, height: 660)
        case .setupRequired:
            // Setup card is 640×585; add 20px horizontal, 20px vertical for the shadow.
            return NSSize(width: 680, height: 625)
        case .persistenceFailure, .stateUnknown:
            return NSSize(width: 560, height: 340)
        case .ready:
            return NSSize(width: 1040, height: 613)
        }
    }
}

private struct ReadyApplicationView: View {
    @ObservedObject var model: AppModel
    let settings: SyxxSettings

    var body: some View {
        ContentView()
            .environmentObject(model)
            .frame(minWidth: 1040, maxWidth: .infinity, minHeight: 613, maxHeight: .infinity)
            .task {
                MacStartupServices.start(settings: settings, model: model)
            }
    }
}

@MainActor
private enum MacStartupServices {
    private static var started = false

    static func start(settings: SyxxSettings, model: AppModel) {
        guard !started else { return }
        started = true

        if settings.crashReportingEnabled {
            SentryBootstrap.setup(ifAllowed: true)
        }
        if settings.telemetryEnabled {
            OTelBootstrap.setup(serviceName: "syxx-gui-macos", ifAllowed: true)
        }
        if settings.notificationsEnabled {
            // B247: capture the authorization result so the UI can reflect whether alerting
            // is possible. A denied authorization means monitoring can run but cannot alert.
            UNUserNotificationCenter.current().requestAuthorization(
                options: [.alert, .sound]
            ) { granted, error in
                Task { @MainActor in
                    if let error = error {
                        model.appendDashboardFeedback("[notifications] authorization request failed: \(error.localizedDescription)")
                        model.notificationAuthorized = nil   // undetermined
                    } else {
                        model.notificationAuthorized = granted
                    }
                }
            }
        }
    }
}

// MARK: - A6 menu-bar mark

/// Namespace for the menu-bar mark. Trust STATE is carried by the severity dot in `MenuBarLabel`
/// (Color.ioSeverity), not by the glyph: the glyph is the constant white Syxx brand mark.
enum TrayGlyph {

}

// MARK: - A6 menu-bar label (white Syxx mark + severity dot)

/// The composed menu-bar label: the Syxx logo with a small severity-colored status dot. The dot is
/// the state channel; the logo is brand presence. When DNS is encrypted, the glyph animates through
/// white → pink (#ff2d78) → purple (#7c5cff) over a 7-second cycle to signal active encryption.
/// When DNS is not encrypted, the glyph is a macOS-tinted template image (white on dark bar, dark
/// on light). The dot color is `Color.ioSeverity`, the SAME mapping the grade and result badge use,
/// so the three can never disagree — and `.unknown` is grey, never the green of a measured-clean
/// network.
struct MenuBarLabel: View {
    let severity: Severity
    let isEncrypted: Bool

    /// The three colors in the encryption animation cycle.
    private static let cycleColors: [(r: Double, g: Double, b: Double)] = [
        (1.0, 1.0, 1.0),          // White
        (1.0, 0.176, 0.471),      // Pink #ff2d78
        (0.486, 0.361, 1.0)       // Purple #7c5cff
    ]

    var body: some View {
        ZStack(alignment: .bottomTrailing) {
            if isEncrypted {
                TimelineView(.animation) { context in
                    let elapsed = context.date.timeIntervalSinceReferenceDate
                    let cyclePosition = elapsed.truncatingRemainder(dividingBy: 7.0) / 7.0
                    let phase = cyclePosition * 3.0
                    let fromIndex = Int(phase) % 3
                    let toIndex = (fromIndex + 1) % 3
                    let t = phase - Double(Int(phase))

                    let from = Self.cycleColors[fromIndex]
                    let to = Self.cycleColors[toIndex]
                    let color = Color(
                        red: from.r + (to.r - from.r) * t,
                        green: from.g + (to.g - from.g) * t,
                        blue: from.b + (to.b - from.b) * t
                    )

                    Image(nsImage: TrayGlyph.syxxColoredMark)
                        .resizable()
                        .aspectRatio(contentMode: .fit)
                        .frame(width: 17, height: 17)
                        .colorMultiply(color)
                }
            } else {
                Image(nsImage: TrayGlyph.syxxTemplateMark)
                    .resizable()
                    .aspectRatio(contentMode: .fit)
                    .frame(width: 17, height: 17)
            }

            // The one spot of color. A thin dark ring keeps the dot legible on both light and dark
            // menu bars without depending on the bar's color.
            Circle()
                .fill(Color.ioSeverity(severity))
                .frame(width: 6, height: 6)
                .overlay(Circle().stroke(Color.black.opacity(0.35), lineWidth: 0.5))
                .offset(x: 1, y: 1)
        }
        .accessibilityLabel(isEncrypted
            ? "Syxx network trust — DNS encrypted"
            : "Syxx network trust")
    }
}

extension TrayGlyph {
    /// The Syxx mark as a TEMPLATE image: every opaque pixel of the logo becomes a single tint the
    /// menu bar controls, so it reads white on a dark bar and dark on a light one. Built once from
    /// the bundled logo; falls back to a shield SF Symbol if the resource is somehow missing (an
    /// empty menu-bar item would be worse than a generic mark).
    ///
    /// `nonisolated(unsafe)`: NSImage is not Sendable, but this image is built once at launch on
    /// the main thread and never mutated — safe to share across actors.
    nonisolated(unsafe) static let syxxTemplateMark: NSImage = {
        let fallback = NSImage(systemSymbolName: "shield.fill", accessibilityDescription: "Syxx")
            ?? NSImage()
        guard let url = Bundle.module.url(forResource: "Syxx-logo", withExtension: "png"),
              let logo = NSImage(contentsOf: url) else {
            fallback.isTemplate = true
            return fallback
        }
        // Flatten to a white silhouette (opaque → white, keep the alpha), then mark it a template so
        // AppKit re-tints it per the bar's appearance.
        let out = NSImage(size: logo.size)
        out.lockFocus()
        logo.draw(at: .zero, from: .zero, operation: .sourceOver, fraction: 1)
        NSColor.white.set()
        NSRect(origin: .zero, size: logo.size).fill(using: .sourceAtop)
        out.unlockFocus()
        out.isTemplate = true
        return out
    }()

    /// The Syxx mark as a NON-TEMPLATE image: same white silhouette as `syxxTemplateMark` but with
    /// `isTemplate = false`, so AppKit does NOT auto-tint it. This is the base layer for the
    /// encryption animation — `.colorMultiply()` tints the white pixels through the three-color
    /// cycle while transparent pixels stay transparent.
    ///
    /// `nonisolated(unsafe)`: NSImage is not Sendable, but this image is built once at launch on
    /// the main thread and never mutated — safe to share across actors.
    nonisolated(unsafe) static let syxxColoredMark: NSImage = {
        let fallback = NSImage(systemSymbolName: "shield.fill", accessibilityDescription: "Syxx")
            ?? NSImage()
        guard let url = Bundle.module.url(forResource: "Syxx-logo", withExtension: "png"),
              let logo = NSImage(contentsOf: url) else {
            // Fallback: NOT a template, so colorMultiply still works.
            return fallback
        }
        let out = NSImage(size: logo.size)
        out.lockFocus()
        logo.draw(at: .zero, from: .zero, operation: .sourceOver, fraction: 1)
        NSColor.white.set()
        NSRect(origin: .zero, size: logo.size).fill(using: .sourceAtop)
        out.unlockFocus()
        // Deliberately NOT a template — colorMultiply handles tinting.
        return out
    }()
}

// MARK: - A6 menu-bar dropdown

/// The menu shown when the indicator is clicked. Reads the SAME TrayIndicator the label does, so the
/// tooltip text and the icon can never describe different states.
struct MenuBarContent: View {
    @EnvironmentObject var model: AppModel

    var body: some View {
        let ind = TrayIndicators.For(model.conclusion)
        Text(ind.tooltip)

        // A5 — the monitoring state, latched. A change stays visible here until acknowledged, so the
        // user cannot miss "your DNS changed" by not having the window open when it happened.
        if let change = model.lastMonitorChange, change.state == .changed {
            Divider()
            Text("⚠︎ \(change.summary)")
            Button("Acknowledge") { model.acknowledgeMonitorChange() }
        } else if model.monitoringOn, let last = model.lastMonitorTick {
            Divider()
            Text("Monitoring — \(last.summary)")
        }

        Divider()
        Toggle("Watch this network", isOn: Binding(
            get: { model.monitoringOn },
            set: { model.setMonitoring($0) }
        ))
        // B247: when monitoring is on but notifications are denied, alert the user that
        // monitoring cannot deliver alerts. Never present monitoring as armed when it cannot alert.
        if model.monitoringOn, model.notificationAuthorized == false {
            Text("⚠︎ Notifications denied — monitoring cannot alert you. Enable notifications in System Settings.")
                .font(.caption)
                .foregroundColor(.orange)
        }
        Button("Open Settings…") {
            NSPanelManager.shared.showSettings(model: model)
        }
        Button("License & Covenant…") {
            NSPanelManager.shared.showLicense(model: model)
        }
        Button("Open Syxx") {
            NSApp.activate(ignoringOtherApps: true)
            for window in NSApp.windows where window.canBecomeMain {
                window.makeKeyAndOrderFront(nil)
            }
        }
        Button("Quit Syxx") { NSApp.terminate(nil) }
    }
}

#if DEBUG
#Preview {
    MenuBarLabel(severity: .unknown, isEncrypted: false)
        .padding(24)
        .syxxPreviewPanel()
}

#Preview("MenuBarLabel — encrypted") {
    MenuBarLabel(severity: .unknown, isEncrypted: true)
        .padding(24)
        .syxxPreviewPanel()
}

#Preview {
    VStack(alignment: .leading, spacing: 8) {
        MenuBarContent()
            .environmentObject(SyxxPreviewSupport.appModel(selection: .dashboard))
    }
    .padding(20)
    .frame(width: 320, alignment: .leading)
    .syxxPreviewPanel()
}
#endif
