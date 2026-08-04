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

        // Force window to 1040×613 on launch — SwiftUI's .frame() is a suggestion,
        // the real size must be set on the NSWindow itself.
        DispatchQueue.main.async {
            if let window = NSApplication.shared.windows.first {
                let desired = NSRect(x: window.frame.origin.x, y: window.frame.origin.y,
                                     width: 1040, height: 613)
                window.setFrame(desired, display: true, animate: false)
                // 1040×613 is the design floor, not a hard lock (plan §9.5 unlocks resizing;
                // supersedes the 2026-07-21 min==max jiggle mitigation). The jiggle came from
                // each view carrying a FIXED ideal frame, so `.contentMinSize` re-sized the
                // window on every tool switch. ContentView now uses a flexible frame
                // (min 1040×613, max .infinity), which keeps the user's chosen size stable
                // across view switches without capping growth.
                window.contentMinSize = NSSize(width: 1040, height: 613)
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
            switch firstRun.state {
            case .ready:
                ReadyApplicationView(model: model, settings: firstRun.settings)
            default:
                GuidedSetup(coordinator: firstRun)
            }
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
        .windowResizability(.contentMinSize)
        .windowStyle(.hiddenTitleBar)

        MenuBarExtra(isInserted: Binding(
            get: { firstRun.state == .ready },
            set: { _ in }
        )) {
            MenuBarContent().environmentObject(model)
        } label: {
            let ind = TrayIndicators.For(model.conclusion)
            MenuBarLabel(severity: ind.severity)
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

/// The composed menu-bar label: the white Syxx logo (template-tinted by macOS) with a small
/// severity-colored status dot. The dot is the state channel; the logo is brand presence. The dot
/// color is `Color.ioSeverity`, the SAME mapping the grade and result badge use, so the three can
/// never disagree — and `.unknown` is grey, never the green of a measured-clean network.
struct MenuBarLabel: View {
    let severity: Severity

    var body: some View {
        ZStack(alignment: .bottomTrailing) {
            Image(nsImage: TrayGlyph.syxxTemplateMark)
                .resizable()
                .aspectRatio(contentMode: .fit)
                .frame(width: 17, height: 17)
            // The one spot of color. A thin dark ring keeps the dot legible on both light and dark
            // menu bars without depending on the bar's color.
            Circle()
                .fill(Color.ioSeverity(severity))
                .frame(width: 6, height: 6)
                .overlay(Circle().stroke(Color.black.opacity(0.35), lineWidth: 0.5))
                .offset(x: 1, y: 1)
        }
        // A tray icon is decorative chrome; the tooltip and dropdown carry the words.
        .accessibilityLabel("Syxx network trust")
    }
}

extension TrayGlyph {
    /// The Syxx mark as a TEMPLATE image: every opaque pixel of the logo becomes a single tint the
    /// menu bar controls, so it reads white on a dark bar and dark on a light one. Built once from
    /// the bundled logo; falls back to a shield SF Symbol if the resource is somehow missing (an
    /// empty menu-bar item would be worse than a generic mark).
    static let syxxTemplateMark: NSImage = {
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
    MenuBarLabel(severity: .unknown)
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
