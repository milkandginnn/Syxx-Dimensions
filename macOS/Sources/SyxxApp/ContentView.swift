import SwiftUI
import AppKit
import ImageIO
import SyxxKit

private struct WindowChromeConfigurator: NSViewRepresentable {
    func makeNSView(context: Context) -> NSView {
        let v = NSView()
        DispatchQueue.main.async { configure(v.window) }
        return v
    }
    func updateNSView(_ nsView: NSView, context: Context) {
        // Window chrome only needs configuring once per window lifecycle.
        // SwiftUI calls updateNSView on every diff; skip redundant dispatches.
    }
    private func configure(_ window: NSWindow?) {
        guard let window else { return }
        // Issue 1: panes run full-height, unobstructed from the window's bottom edge to
        // its top. `titlebarAppearsTransparent` alone only makes the titlebar SEE-THROUGH;
        // without `.fullSizeContentView` the content view is still laid out BELOW it, so
        // the window reserves a band at the top that the pane can neither occupy nor
        // scroll into. That band is what concealed the first lines of pane content.
        window.styleMask.insert(.fullSizeContentView)
        window.titlebarAppearsTransparent = true
        window.titleVisibility = .hidden
        // Issue 2: macOS automatically inserts an NSToolbar into WindowGroup scenes even
        // when no toolbar items are defined. That toolbar renders as an opaque system-
        // material band directly above the pane content, breaking the 35pt top origin.
        // Removing it collapses the band entirely; the only chrome above the pane area
        // is the transparent titlebar (traffic lights), which the canvas already paints
        // behind via .ignoresSafeArea().
        window.toolbar = nil
        // Traffic lights must be explicitly visible. With titleVisibility = .hidden and
        // no toolbar, the buttons are present but can be occluded by full-bleed content
        // (the sidebar gradient paints under the titlebar via .ignoresSafeArea()). Force
        // them on so the user always sees close/minimize/maximize.
        window.standardWindowButton(.closeButton)?.isHidden = false
        window.standardWindowButton(.miniaturizeButton)?.isHidden = false
        window.standardWindowButton(.zoomButton)?.isHidden = false
        guard let screen = window.screen ?? NSScreen.main else { return }
        let vis = screen.visibleFrame
        var f = window.frame
        var moved = false
        if f.maxY > vis.maxY { f.origin.y = vis.maxY - f.height; moved = true }
        if f.minY < vis.minY { f.origin.y = vis.minY; moved = true }
        if f.maxX > vis.maxX { f.origin.x = vis.maxX - f.width; moved = true }
        if f.minX < vis.minX { f.origin.x = vis.minX; moved = true }
        if moved { window.setFrame(f, display: true) }
    }
}

/// TEMPORARY (2026-08-02 pane-anchoring investigation). Dumps the real window/pane geometry to
/// stderr when SYXX_GEOM_DEBUG=1 so the origin is measured, not inferred. Costs nothing when the
/// variable is unset.
private struct PaneGeometryProbe: NSViewRepresentable {
    let label: String

    final class ProbeView: NSView {
        var label = ""
        override func layout() {
            super.layout()
            guard ProcessInfo.processInfo.environment["SYXX_GEOM_DEBUG"] == "1",
                  let window else { return }
            let inWindow = convert(bounds, to: nil)
            let insets = window.contentView?.safeAreaInsets ?? NSEdgeInsets()
            let message = "[geom] pane=\(label)"
                + " window=\(Self.text(window.frame))"
                + " contentLayoutRect=\(Self.text(window.contentLayoutRect))"
                + " contentView=\(Self.text(window.contentView?.frame ?? .zero))"
                + String(format: " safeArea(t,l,b,r)=(%.1f,%.1f,%.1f,%.1f)",
                         insets.top, insets.left, insets.bottom, insets.right)
                + " fullSizeContentView=\(window.styleMask.contains(.fullSizeContentView))"
                + " paneBoxInWindow=\(Self.text(inWindow))"
                + String(format: " gapWindowTopToPaneTop=%.1f", window.frame.height - inWindow.maxY)
                + String(format: " gapWindowLeftToPaneLeft=%.1f", inWindow.minX)
            FileHandle.standardError.write(Data((message + "\n").utf8))
            Self.appendToLog(message)
        }

        /// stderr is not reliably reachable when the bundled app is launched detached, so the
        /// same line is also appended to `~/.syxx/geom.log`. Debug-only; gated on the same
        /// SYXX_GEOM_DEBUG check as the caller.
        private static func appendToLog(_ message: String) {
            let dir = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".syxx")
            try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)
            let url = dir.appendingPathComponent("geom.log")
            guard let data = (message + "\n").data(using: .utf8) else { return }
            if let handle = try? FileHandle(forWritingTo: url) {
                defer { try? handle.close() }
                _ = try? handle.seekToEnd()
                try? handle.write(contentsOf: data)
            } else {
                try? data.write(to: url)
            }
        }

        private static func text(_ rect: NSRect) -> String {
            String(format: "(%.1f,%.1f %.1fx%.1f)", rect.origin.x, rect.origin.y, rect.width, rect.height)
        }
    }

    func makeNSView(context: Context) -> ProbeView {
        let view = ProbeView()
        view.label = label
        return view
    }

    func updateNSView(_ nsView: ProbeView, context: Context) {
        nsView.label = label
        nsView.needsLayout = true
    }
}

struct ContentView: View {
    @EnvironmentObject var model: AppModel
    @Environment(\.colorScheme) private var colorScheme
    @State private var localDarkMode = true

    /// The shell owns the pane origin. Child panes must not add another outer inset.
    private static let paneOrigin: CGFloat = 35

    var body: some View {
        HStack(alignment: .top, spacing: 0) {
            WardenSidebar(localDarkMode: $localDarkMode)
                .frame(width: 236)
                .frame(maxHeight: .infinity)

            Divider()
                .background(Color.ioBorderSubtle)
                .frame(maxHeight: .infinity)

            ZStack(alignment: .top) {
                    Color.clear
                        .ioCanvas()
                        .ignoresSafeArea()

                    Group {
                        switch model.selection {
                        case .beta:
                            BetaView()
                                .ignoresSafeArea()
                        case .dashboard:
                            paneContent(DashboardView())
                        case .prophet:
                            paneContent(ProphetView())
                        case .mirror:
                            paneContent(MirrorView())
                        case .portal:
                            paneContent(PortalView())
                        case .glutton:
                            paneContent(GluttonView())
                        case .cage:
                            paneContent(CageView())
                        case .witness:
                            paneContent(WitnessView())
                        case .conclusions:
                            paneContent(ConclusionsView())
                        case .settings:
                            paneContent(SettingsView())
                        case .license:
                            paneContent(LicenseView())
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                }
                .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topLeading)
                // The content column anchors to the WINDOW's top edge, not the safe-area
                // top, so `paneOrigin` measures the owner's 35pt from the window itself.
                // Safe to do here and NOT on the sidebar: the traffic lights sit over the
                // 236pt sidebar (x < 236), never over this column, so nothing can be
                // occluded by running full-bleed.
                .ignoresSafeArea()
        }
        // Full-bleed belongs to the BACKGROUNDS, not to the layout. Applying
        // `.ignoresSafeArea()` to this HStack expanded the whole shell — sidebar included —
        // up past the window's content area, which carried `WardenSidebar`'s own
        // `.padding(.top, 46)` above the window's top edge and clipped the "Syxx" wordmark
        // off entirely (owner report 2026-08-02: "the sidebar is outside the window").
        // The canvas fill (line ~100), the MOTD pane, and the sidebar gradient each ignore
        // the safe area individually, so the window still paints edge to edge with no
        // titlebar band — while the CONTENT stays clear of the traffic lights.
        .preferredColorScheme(localDarkMode ? .dark : .light)
        .background(WindowChromeConfigurator())
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onAppear {
            #if DEBUG
            guard !ProcessInfo.processInfo.isSyxxXcodePreview else { return }
            #endif
            model.checkStartup()
        }
        .onChange(of: model.pendingBroadcast) { _, newValue in
            guard let b = newValue else { return }
            NSPanelManager.shared.showBroadcast(b) { model.dismissBroadcast(b.id) }
        }
    }

    /// Apply the single 35-point content origin to every non-MOTD pane.
    /// The MOTD remains full-bleed so its hero image and fallback surface are untouched.
    ///
    /// The GeometryReader + FIXED frame is the overflow clamp. A flexible
    /// `.frame(maxHeight: .infinity)` does NOT clamp to the proposal — it reports
    /// `max(proposal, childHeight)`, so a pane whose content outgrew the window (Prophet
    /// post-scan) grew the whole shell and the hosting view CENTRED the excess, throwing
    /// half of it (the pane header) above the window's top edge. A GeometryReader always
    /// reports exactly the proposal, and a fixed `.frame(width:height:)` top-anchors an
    /// oversized child, so overflow can only spill off the BOTTOM. A no-op for the
    /// scroll-wrapped panes: an NSScrollView already reports exactly the proposal.
    private func paneContent<Content: View>(_ content: Content) -> some View {
        GeometryReader { geo in
            content
                .background(PaneGeometryProbe(label: String(describing: model.selection)))
                .padding(.horizontal, Self.paneOrigin)
                .padding(.top, Self.paneOrigin)
                .frame(width: geo.size.width, height: geo.size.height, alignment: .topLeading)
        }
    }
}

#if DEBUG
#Preview {
    ContentView()
        .environmentObject(SyxxPreviewSupport.appModel(selection: .dashboard))
        .syxxPreviewCanvas()
}

#Preview {
    WardenSidebar(localDarkMode: .constant(true))
        .environmentObject(SyxxPreviewSupport.appModel(selection: .prophet))
        .frame(width: 236, height: 613)
        .background(LinearGradient.ioSidebarFill)
        .preferredColorScheme(.dark)
}
#endif

// MARK: - Warden Sidebar (236px, rune nav, gold dividers)

struct WardenSidebar: View {
    @EnvironmentObject var model: AppModel
    @State private var confirmingReset = false
    @Binding var localDarkMode: Bool

    private let goldColor = Color(red: 224/255, green: 180/255, blue: 90/255)

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            // Logo block. Owner directive 2026-08-02, all three parts measured rather than
            // eyeballed:
            //  • "DIMENSIONS" is all caps at 13pt;
            //  • the mark's total height equals the two stacked wordmarks plus the margin
            //    between them, measured ink to ink — the cap top of "Syxx" down to the
            //    "DIMENSIONS" baseline (see `wordmarkInkHeight`). The previous 65x65 frame was
            //    10pt taller than the 55pt wordmark line-box block and hung below it;
            //  • the mark's lowest point sits ON the "DIMENSIONS" baseline: the row aligns on
            //    the last text baseline and the logo offers its own bottom edge as that guide,
            //    so no hand-tuned offset is involved;
            //  • the group's leftmost point is the mark's INK, not the transparent canvas the
            //    PNG ships on (`trimmedLogo` crops it), and the 13pt leading inset puts that
            //    ink on the rune column of the rows below: sidebar 15 + row 13 = 28pt.
            HStack(alignment: .lastTextBaseline, spacing: 9) {
                logoMark()
                    .alignmentGuide(.lastTextBaseline) { $0[.bottom] }

                VStack(alignment: .leading, spacing: 0) {
                    Text("Syxx")
                        .font(.custom("Cinzel-Bold", size: Self.wordmarkTitleSize))
                        .foregroundStyle(LinearGradient.ioWordmark)

                    // Mockup: subtitle = "The Six Wardens" (Story) / "DNS Investigation" (General)
                    // system-ui 9.5px, .22em tracking, #8f83b8, weight 600
                    Text(SyxxSettingsStore.load().experienceMode == "general" ? "DNS INVESTIGATION" : "THE SIX WARDENS")
                        .font(.system(size: 9.5, weight: .semibold))
                        .tracking(2.1)
                        .foregroundColor(Color.ioEyebrowMuted)
                }
                .fixedSize()
            }
            .padding(.leading, 13)

            // Hairline under logo — mockup: rgba(148,120,246,.16)
            Rectangle()
                .fill(Color.ioVioletDivider.opacity(0.16))
                .frame(height: 1)
                .padding(.horizontal, 4)
                .padding(.top, 15)
                .padding(.bottom, 11)

            // Navigation list — fixed-height rows prevent selection and hover wobble.
            ScrollView(.vertical, showsIndicators: false) {
                VStack(alignment: .leading, spacing: 2) {
                // What's New
                SidebarItem(
                    label: "What's New",
                    rune: "✦",
                    selected: model.selection == .beta,
                    onClick: { model.selection = .beta }
                )

                // Dashboard
                SidebarItem(
                    label: "Dashboard",
                    rune: "◧",
                    selected: model.selection == .dashboard,
                    onClick: { model.selection = .dashboard },
                    shortcut: "1"
                )

                WardenGoldDivider()

                // Prophet. Owner directive 2026-08-02: the selected row carries Prophet's own
                // module colour (#00b4ff), the way the pane tints its rune tile and its primary
                // button. The label stays white in the selected state (SidebarItem).
                SidebarItem(
                    label: "Prophet",
                    rune: "\u{16A8}", // ᚨ
                    selected: model.selection == .prophet,
                    onClick: { model.selection = .prophet },
                    shortcut: "2",
                    accent: .ioProphetBlue
                )

                // Mirror (Pro-locked)
                SidebarItem(
                    label: "Mirror",
                    rune: "\u{16DE}", // ᛞ
                    selected: model.selection == .mirror,
                    onClick: { model.selection = .mirror },
                    isLocked: !model.isPro,
                    shortcut: "3"
                )

                // Portal
                SidebarItem(
                    label: "Portal",
                    rune: "\u{16DC}", // ᛜ
                    selected: model.selection == .portal,
                    onClick: { model.selection = .portal },
                    shortcut: "4"
                )

                // Glutton (Pro-locked)
                SidebarItem(
                    label: "Glutton",
                    rune: "\u{16A6}", // ᚦ
                    selected: model.selection == .glutton,
                    onClick: { model.selection = .glutton },
                    isLocked: !model.isPro,
                    shortcut: "5"
                )

                // Cage
                SidebarItem(
                    label: "Cage",
                    rune: "\u{16BE}", // ᚾ
                    selected: model.selection == .cage,
                    onClick: { model.selection = .cage },
                    shortcut: "6"
                )

                // Witness (Pro-locked)
                SidebarItem(
                    label: "Witness",
                    rune: "\u{16C9}", // ᛉ
                    selected: model.selection == .witness,
                    onClick: { model.selection = .witness },
                    isLocked: !model.isPro,
                    shortcut: "7"
                )

                WardenGoldDivider()

                // Conclusion
                SidebarItem(
                    label: "Conclusion",
                    rune: "✒",
                    selected: model.selection == .conclusions,
                    onClick: { model.selection = .conclusions },
                    shortcut: "8"
                )

                WardenGoldDivider()

                // Dimensions (gold, external)
                SidebarItem(
                    label: "Dimensions",
                    rune: "⊕",
                    selected: false,
                    onClick: {
                        if let url = URL(string: "https://syxx.app") {
                            NSWorkspace.shared.open(url)
                        }
                    },
                    isGold: true,
                    showExternal: true
                )
                }
            }
            .frame(maxHeight: .infinity, alignment: .top)

            // Footer pill: "Start your story anew" — destructive, so it confirms first (plan §9.5)
            Button(action: { confirmingReset = true }) {
                HStack(spacing: 8) {
                    Text("⟲")
                        .font(.system(size: 13))
                    Text("Start your story anew")
                        .font(.uiLabel(13))
                }
                .foregroundColor(goldColor)
                .padding(.horizontal, 14)
                .padding(.vertical, 8)
                .cornerRadius(18)
                .overlay(
                    RoundedRectangle(cornerRadius: 18)
                        .stroke(goldColor.opacity(0.35), lineWidth: 1)
                )
            }
            .buttonStyle(.plain)
            .padding(.top, 12)
            .confirmationDialog("Start your story anew?", isPresented: $confirmingReset) {
                Button("Erase session results", role: .destructive) { model.resetAll() }
                Button("Cancel", role: .cancel) {}
            } message: {
                Text("This clears every result and collected piece of evidence from this session.")
            }
        }
        .padding(.top, 46)
        .padding(.horizontal, 15)
        .padding(.bottom, 16)
        .frame(width: 236)
        .frame(maxHeight: .infinity)
        // The gradient ignores the safe area so the pane still paints under the traffic
        // lights and no titlebar band is visible; the CONTENT above keeps its 46pt top
        // inset so the "Syxx" wordmark clears them instead of being clipped by the
        // window edge.
        .background(
            LinearGradient(
                colors: [
                    Color(red: 29/255, green: 18/255, blue: 64/255),   // #1d1240
                    Color(red: 22/255, green: 12/255, blue: 48/255)    // #160c30
                ],
                startPoint: .top, endPoint: .bottom
            )
            .ignoresSafeArea()
        )
        .overlay(
            Rectangle()
                .fill(Color(white: 1, opacity: 0.08))
                .frame(width: 1),
            alignment: .trailing
        )
    }

    // MARK: - Logo mark (ink-cropped, sized from the wordmark's own metrics)

    /// Type sizes of the two wordmark lines. The mark is sized from these, so changing the
    /// wordmark re-sizes the mark instead of leaving a hand-tuned constant behind.
    private static let wordmarkTitleSize: CGFloat = 27
    private static let wordmarkSubtitleSize: CGFloat = 13

    /// Ink height of the two-line wordmark: the cap top of "Syxx" down to the baseline of
    /// "DIMENSIONS", which is the block the owner asked the mark to match (both wordmarks plus
    /// the margin between them). Read from the live font metrics; the fallback only applies if
    /// a face fails to register, in which case the whole sidebar is already rendering wrong.
    @MainActor private static var wordmarkInkHeight: CGFloat {
        guard let title = NSFont(name: "Cinzel-Bold", size: wordmarkTitleSize),
              let subtitle = NSFont(name: "Cinzel-SemiBold", size: wordmarkSubtitleSize) else {
            return 42
        }
        let titleLineBox = ceil(title.ascender - title.descender + title.leading)
        // Distance from the "Syxx" baseline to the "DIMENSIONS" baseline (VStack spacing 0).
        let baselineGap = (titleLineBox - title.ascender) + subtitle.ascender
        return baselineGap + title.capHeight
    }

    /// The bundled mark cropped to its opaque pixels, plus that crop's aspect ratio.
    private struct TrimmedLogo {
        let image: Image
        /// width / height of the cropped mark, so a height-driven frame keeps its proportions.
        let aspectRatio: CGFloat
    }

    /// Cropped once, at first use. The shipped PNG is a 1254x1254 canvas whose mark occupies
    /// only the middle ~71% horizontally and ~89% vertically, so a frame around the raw image
    /// is not the mark's visible box and cannot be aligned to a text edge. Cropping to the
    /// alpha bounding box makes frame and ink the same rectangle.
    @MainActor private static let trimmedLogo: TrimmedLogo? = makeTrimmedLogo()

    private static func makeTrimmedLogo() -> TrimmedLogo? {
        guard let url = Bundle.module.url(forResource: "Syxx-logo", withExtension: "png"),
              let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let cgImage = CGImageSourceCreateImageAtIndex(source, 0, nil),
              let ink = opaqueBounds(of: cgImage),
              ink.width > 0, ink.height > 0,
              let cropped = cgImage.cropping(to: ink) else {
            return nil
        }
        let nsImage = NSImage(cgImage: cropped, size: NSSize(width: ink.width, height: ink.height))
        return TrimmedLogo(image: Image(nsImage: nsImage), aspectRatio: ink.width / ink.height)
    }

    /// Alpha bounding box of `cgImage`, returned in the image's own top-left-origin coordinates
    /// (what `cropping(to:)` expects). Returns nil when the image is fully transparent.
    private static func opaqueBounds(of cgImage: CGImage) -> CGRect? {
        let width = cgImage.width, height = cgImage.height
        guard width > 0, height > 0 else { return nil }
        var alpha = [UInt8](repeating: 0, count: width * height)
        guard let context = CGContext(data: &alpha,
                                      width: width, height: height,
                                      bitsPerComponent: 8, bytesPerRow: width,
                                      space: CGColorSpaceCreateDeviceGray(),
                                      bitmapInfo: CGImageAlphaInfo.alphaOnly.rawValue) else {
            return nil
        }
        context.draw(cgImage, in: CGRect(x: 0, y: 0, width: width, height: height))

        // Row 0 of the buffer is the BOTTOM row of the image (CGContext is y-up).
        var minX = width, maxX = -1, minRow = height, maxRow = -1
        alpha.withUnsafeBufferPointer { buffer in
            for row in 0..<height {
                let base = row * width
                var first = -1, last = -1
                for column in 0..<width where buffer[base + column] > 8 {
                    if first < 0 { first = column }
                    last = column
                }
                guard first >= 0 else { continue }
                if row < minRow { minRow = row }
                maxRow = row
                if first < minX { minX = first }
                if last > maxX { maxX = last }
            }
        }
        guard maxRow >= 0, maxX >= 0 else { return nil }
        return CGRect(x: CGFloat(minX),
                      y: CGFloat(height - 1 - maxRow),
                      width: CGFloat(maxX - minX + 1),
                      height: CGFloat(maxRow - minRow + 1))
    }

    /// The mark, cropped to its ink and sized to the wordmark's ink height.
    private func logoMark() -> some View {
        let height = Self.wordmarkInkHeight
        let trimmed = Self.trimmedLogo
        return (trimmed?.image ?? bundledLogoImage())
            .resizable()
            .aspectRatio(contentMode: .fit)
            .frame(width: height * (trimmed?.aspectRatio ?? 1), height: height)
            .shadow(color: Color(red: 255/255, green: 45/255, blue: 120/255).opacity(0.3), radius: 10)
    }

    /// Resolve the shipped PNG directly from the SwiftPM resource bundle. The explicit URL
    /// avoids AppKit's main-bundle lookup when the app is launched from a packaged bundle.
    /// Only used if the ink crop above fails.
    private func bundledLogoImage() -> Image {
        guard let url = Bundle.module.url(forResource: "Syxx-logo", withExtension: "png"),
              let image = NSImage(contentsOf: url) else {
            return Image("Syxx-logo", bundle: .module)
        }
        return Image(nsImage: image)
    }
}
