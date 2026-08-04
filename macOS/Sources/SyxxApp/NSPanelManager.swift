import SwiftUI
import AppKit

/// Manages presentation of the app's system NSPanels (About, Update, EULA, Broadcast).
/// Each panel is created once and shown/hidden on demand (except broadcast, which is transient).
@MainActor
final class NSPanelManager {
    static let shared = NSPanelManager()

    private var aboutPanel: NSPanel?
    private var updatePanel: NSPanel?
    private var eulaPanel: NSPanel?
    private var settingsPanel: NSPanel?
    private var licensePanel: NSPanel?
    // D095: hold broadcast panels strongly. Under ARC, isReleasedWhenClosed = true on an
    // unretained window is an over-release / use-after-free risk. makePanel correctly sets
    // isReleasedWhenClosed = false; the old showBroadcast overrode that with true and kept
    // no reference, which is exactly the pattern Apple documents as unsafe.
    private var broadcastPanels: [NSPanel] = []

    private init() {}

    // MARK: - Broadcast (transient, created fresh so it can capture the dismiss closure)

    func showBroadcast(_ broadcast: Broadcast, onDismiss: @escaping () -> Void) {
        var panel: NSPanel?
        let createdPanel = makePanel(title: broadcast.title,
                                     view: BroadcastPanelView(broadcast: broadcast, onDismiss: { [weak self] in
                                         onDismiss()
                                         if let panel {
                                             self?.broadcastPanels.removeAll { $0 === panel }
                                         }
                                     }),
                                     style: [.closable, .titled])
        panel = createdPanel
        broadcastPanels.append(createdPanel)
        createdPanel.orderFront(nil)
    }

    // MARK: - Settings

    // E122: non-optional model — a missing model would silently produce a disconnected panel.
    func showSettings(model: AppModel) {
        if let panel = settingsPanel, panel.isVisible {
            panel.orderFront(nil)
            return
        }
        let panel = makePanel(title: "Settings", view: SettingsView().environmentObject(model), style: [.closable, .titled, .resizable])
        settingsPanel = panel
        panel.orderFront(nil)
    }

    // MARK: - License & Covenant

    // E122: non-optional model — a missing model would silently produce a disconnected panel.
    func showLicense(model: AppModel) {
        if let panel = licensePanel, panel.isVisible {
            panel.orderFront(nil)
            return
        }
        let panel = makePanel(title: "License & Pro Covenant", view: LicenseView().environmentObject(model), style: [.closable, .titled, .resizable])
        licensePanel = panel
        panel.orderFront(nil)
    }

    // MARK: - About Syxx

    func showAbout() {
        if let panel = aboutPanel, panel.isVisible {
            panel.orderFront(nil)
            return
        }
        let panel = makePanel(title: "About Syxx", view: AboutView(), style: [.closable, .titled])
        aboutPanel = panel
        panel.orderFront(nil)
    }

    // MARK: - Check for Updates

    func showUpdate() {
        if let panel = updatePanel, panel.isVisible {
            panel.orderFront(nil)
            return
        }
        let panel = makePanel(title: "Software Update", view: UpdateView())
        updatePanel = panel
        panel.orderFront(nil)
    }

    // MARK: - EULA

    func showEULA(menuPresented: Bool = true) {
        if let panel = eulaPanel, panel.isVisible {
            panel.orderFront(nil)
            return
        }
        let panel = makePanel(title: "License Agreement", view: EULAView(menuPresented: menuPresented))
        eulaPanel = panel
        panel.orderFront(nil)
    }

    // MARK: - Helpers

    private func makePanel<Content: View>(title: String, view: Content,
                                          style: NSWindow.StyleMask = [.closable, .titled]) -> NSPanel {
        let hosting = NSHostingController(rootView: view)
        let panel = NSPanel(contentViewController: hosting)
        panel.title = title
        panel.styleMask = style
        panel.isFloatingPanel = true
        panel.hidesOnDeactivate = false
        panel.isReleasedWhenClosed = false
        panel.becomesKeyOnlyIfNeeded = false
        panel.level = .modalPanel
        panel.setFrameAutosaveName(title)
        if NSApplication.shared.keyWindow != nil {
            panel.center()
        }
        return panel
    }
}
