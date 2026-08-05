import SwiftUI
import AppKit
import WebKit

// MARK: - Geometry

/// Owner-approved fixed knob geometries for every Syxx scrollbar.
/// Values are logical points and scale normally on Retina.
///
/// - `fullPanel`: primary application surfaces (10 × 420)
/// - `compact`: nested cards, terminals, EULA text, detail panes (10 × 50)
///
/// Geometry is explicit per call site. A short track clamps knob length; it never
/// silently switches a full-panel surface to compact.
enum PurpleScrollbarGeometry: Equatable, CaseIterable, Sendable {
    case fullPanel
    case compact

    /// Knob thickness in points (both geometries).
    var knobWidth: CGFloat { 10 }

    /// Preferred knob length in points before track clamping.
    var preferredKnobLength: CGFloat {
        switch self {
        case .fullPanel: return 420
        case .compact: return 50
        }
    }

    /// Corner radius shared by both geometries.
    var cornerRadius: CGFloat { 4 }

    /// Clamp preferred length to the usable track. Never negative; never switches case.
    func clampedKnobLength(trackLength: CGFloat) -> CGFloat {
        let usable = max(0, trackLength)
        return min(preferredKnobLength, usable)
    }
}

// MARK: - Approved colors (single source for draw + tests)

enum PurpleScrollbarStyle {
    /// Knob fill: rgba(150, 116, 235, 0.35)
    static let knobFill = NSColor(srgbRed: 150 / 255.0, green: 116 / 255.0, blue: 235 / 255.0, alpha: 0.35)
    /// Glow: rgba(150, 116, 235, 0.55), blur 6pt, zero offset
    static let glowColor = NSColor(srgbRed: 150 / 255.0, green: 116 / 255.0, blue: 235 / 255.0, alpha: 0.55)
    static let glowBlurRadius: CGFloat = 6
    static let glowOffset = NSSize.zero
}

// MARK: - Scroll document (measured height + top pin)

/// Height of the hosted SwiftUI content, measured at the width the clip view actually gives it.
struct PurpleScrollContentHeight: PreferenceKey {
    static let defaultValue: CGFloat = 0
    static func reduce(value: inout CGFloat, nextValue: () -> CGFloat) {
        value = max(value, nextValue())
    }
}

/// The scroll view's document root.
///
/// Two guarantees, both needed:
///
///  • `Color.clear` is the only thing that SIZES this view, and the pane rides as an OVERLAY.
///    An overlay never grows its base, and `alignment: .topLeading` pins the pane's top-left
///    corner to the document view's top-left corner. Without this, SwiftUI CENTRES a root view
///    taller than the hosting view's frame, drawing half the excess ABOVE the document view's
///    top edge, where the clip view throws it away — the missing pane header and the empty
///    strip above it.
///  • The GeometryReader measures the pane at the width the clip view really gives it and
///    drives an explicit height constraint. `NSHostingView.sizingOptions = [.intrinsicContentSize]`
///    published a height measured at the root's IDEAL width instead, which was short by every
///    line the prose gains when it re-wraps narrower.
struct PurpleScrollDocument<Content: View>: View {
    let content: Content
    let onHeight: (CGFloat) -> Void

    var body: some View {
        Color.clear
            .frame(maxWidth: .infinity, maxHeight: .infinity)
            .overlay(alignment: .topLeading) {
                content
                    .frame(maxWidth: .infinity, alignment: .topLeading)
                    .background(
                        GeometryReader { proxy in
                            Color.clear.preference(
                                key: PurpleScrollContentHeight.self,
                                value: proxy.size.height
                            )
                        }
                    )
            }
            .onPreferenceChange(PurpleScrollContentHeight.self) { onHeight($0) }
    }
}

// MARK: - PurpleScrollView

/// App-wide vertical scroll container with the approved purple knob on a transparent track.
/// SwiftUI cannot recolor its scrollers, so this wraps an AppKit `NSScrollView` with
/// `PurpleScroller` and hosts SwiftUI content inside it.
///
/// Pass `scrollToBottom` any monotonically-increasing value (e.g. a log count) to follow
/// the bottom when the user is already pinned there. Reviewing older entries is never stolen.
struct PurpleScrollView<Content: View>: NSViewRepresentable {
    private let content: Content
    /// Explicit geometry. Defaults deliberately to `.fullPanel` for primary surfaces;
    /// nested terminals, EULA text, and detail panes must pass `.compact`.
    var geometry: PurpleScrollbarGeometry
    /// When this value changes the scroll view may animate to its bottom edge — only if the
    /// user is already following the bottom (or has not scrolled away). Pass `nil` (default)
    /// for containers that do not need auto-scroll-to-bottom behaviour.
    var scrollToBottom: Int?

    init(
        geometry: PurpleScrollbarGeometry = .fullPanel,
        scrollToBottom: Int? = nil,
        @ViewBuilder content: () -> Content
    ) {
        self.geometry = geometry
        self.scrollToBottom = scrollToBottom
        self.content = content()
    }

    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSScrollView()
        scroll.drawsBackground = false
        scroll.borderType = .noBorder
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = false
        scroll.autohidesScrollers = true
        scroll.scrollerStyle = .legacy
        scroll.horizontalScrollElasticity = .none
        scroll.verticalScrollElasticity = .allowed

        // The shell owns the pane origin (ContentView `paneOrigin` = 35). Zero the insets
        // explicitly so the anchor cannot move if the pane box is ever raised into the
        // titlebar band. Measured on macOS 26.6: with automatic adjustment left ON, the insets
        // are already (0,0,0,0), because the scroll view's frame never overlaps the title bar.
        // This is NOT what displaced the scroll panes — see PurpleScrollDocument.
        scroll.automaticallyAdjustsContentInsets = false
        scroll.contentInsets = NSEdgeInsetsZero

        let scroller = PurpleScroller(geometry: geometry)
        scroll.verticalScroller = scroller

        let coordinator = context.coordinator
        let host = NSHostingView(
            rootView: PurpleScrollDocument(content: content) { [weak coordinator] height in
                coordinator?.applyDocumentHeight(height)
            }
        )
        // NOT [.intrinsicContentSize]: see PurpleScrollDocument.
        host.sizingOptions = []
        host.translatesAutoresizingMaskIntoConstraints = false
        host.postsFrameChangedNotifications = true
        scroll.documentView = host

        // Pin top/leading/trailing + equal width so content fills the clip view and cannot
        // scroll horizontally. Height is the MEASURED content height, never shorter than the
        // viewport so a short pane still fills the box.
        let documentHeight = host.heightAnchor.constraint(equalToConstant: 1)
        documentHeight.priority = .defaultHigh
        coordinator.documentHeight = documentHeight
        NSLayoutConstraint.activate([
            host.topAnchor.constraint(equalTo: scroll.contentView.topAnchor),
            host.leadingAnchor.constraint(equalTo: scroll.contentView.leadingAnchor),
            host.trailingAnchor.constraint(equalTo: scroll.contentView.trailingAnchor),
            host.widthAnchor.constraint(equalTo: scroll.contentView.widthAnchor),
            host.heightAnchor.constraint(greaterThanOrEqualTo: scroll.contentView.heightAnchor),
            documentHeight,
        ])

        context.coordinator.host = host
        context.coordinator.scrollView = scroll
        context.coordinator.geometry = geometry
        context.coordinator.installObservers(on: scroll, host: host)

        // Deterministic load position. Nothing here previously set an initial scroll
        // offset, so where a fresh pane came up depended on AppKit's default for the
        // document view's coordinate system rather than on intent: content taller than
        // the viewport could load already scrolled, clipping the first line. Every pane
        // must open at its content origin. Runs after first layout, when the document
        // height is known.
        DispatchQueue.main.async { [weak scroll] in
            guard let scroll else { return }
            scroll.scrollToContentTop()
        }

        return scroll
    }

    func updateNSView(_ nsView: NSScrollView, context: Context) {
        let coordinator = context.coordinator
        coordinator.host?.rootView = PurpleScrollDocument(content: content) { [weak coordinator] height in
            coordinator?.applyDocumentHeight(height)
        }

        // Keep scroller geometry in sync if the call site changes it.
        if context.coordinator.geometry != geometry {
            context.coordinator.geometry = geometry
            if let purple = nsView.verticalScroller as? PurpleScroller {
                purple.geometry = geometry
            } else {
                nsView.verticalScroller = PurpleScroller(geometry: geometry)
            }
            nsView.tile()
        } else if let purple = nsView.verticalScroller as? PurpleScroller {
            purple.geometry = geometry
        } else {
            nsView.verticalScroller = PurpleScroller(geometry: geometry)
        }

        // Show the vertical scroller only when content overflows the viewport.
        context.coordinator.refreshOverflowState()

        // Auto-scroll to bottom when trigger advances — only if user is pinned to bottom.
        if let trigger = scrollToBottom, trigger != context.coordinator.lastTrigger {
            let previous = context.coordinator.lastTrigger
            context.coordinator.lastTrigger = trigger
            // First assignment arms the trigger without forcing a jump on initial appear
            // when the user has not interacted; still follow bottom if already pinned.
            if previous >= 0 || context.coordinator.userPinnedToBottom {
                context.coordinator.scrollToBottomIfPinned(animated: previous >= 0)
            }
        }
    }

    /// Tell SwiftUI this view occupies exactly the proposed size.
    ///
    /// Without this, AppKit's NSScrollView reports its CONTENT height as its preferred size
    /// along the scroll axis rather than capping to the host's proposal. The parent
    /// GeometryReader frame in `paneContent()` then receives a child taller than the frame,
    /// centers the overflow, and the pane header is pushed above the window's visible area.
    /// Returning the proposal exactly mirrors the behavior of a native SwiftUI view that
    /// fills available space and delegates scrolling to AppKit.
    func sizeThatFits(_ proposal: ProposedViewSize, nsView: NSScrollView, context: Context) -> CGSize? {
        guard let w = proposal.width, let h = proposal.height else { return nil }
        return CGSize(width: w, height: h)
    }

    func makeCoordinator() -> Coordinator { Coordinator() }

    /// All coordinator state is UI-bound and mutated exclusively from the main thread
    /// (updateNSView, notification callbacks with `queue: .main`, animation completion handlers).
    /// `@MainActor` makes this isolation explicit for Swift 6 strict concurrency.
    @MainActor
    final class Coordinator {
        var host: NSHostingView<PurpleScrollDocument<Content>>?
        /// Explicit document height, driven by the measured SwiftUI content height.
        var documentHeight: NSLayoutConstraint?
        weak var scrollView: NSScrollView?
        var geometry: PurpleScrollbarGeometry = .fullPanel
        var lastTrigger: Int = -1
        /// `true` while the user is at (or returns to) the lower edge. Live logs only
        /// auto-follow when this is true.
        var userPinnedToBottom: Bool = true
        var isProgrammaticScroll: Bool = false
        // `nonisolated(unsafe)`: these observers are only accessed from deinit (nonisolated)
        // and installObservers (main actor). NSObjectProtocol is not Sendable, so the properties
        // cannot be main-actor-isolated — deinit must read them to unregister.
        nonisolated(unsafe) private var boundsObserver: NSObjectProtocol?
        nonisolated(unsafe) private var frameObserver: NSObjectProtocol?

        deinit {
            if let boundsObserver {
                NotificationCenter.default.removeObserver(boundsObserver)
            }
            if let frameObserver {
                NotificationCenter.default.removeObserver(frameObserver)
            }
        }

        func installObservers(on scroll: NSScrollView, host: NSView) {
            scroll.contentView.postsBoundsChangedNotifications = true
            boundsObserver = NotificationCenter.default.addObserver(
                forName: NSView.boundsDidChangeNotification,
                object: scroll.contentView,
                queue: .main
            ) { [weak self] _ in
                self?.handleBoundsChanged()
            }
            frameObserver = NotificationCenter.default.addObserver(
                forName: NSView.frameDidChangeNotification,
                object: host,
                queue: .main
            ) { [weak self] _ in
                self?.refreshOverflowState()
            }
        }

        /// Adopt the height SwiftUI actually laid the content out at. A stale intrinsic size is
        /// what made the document view short enough to clip the pane's first lines.
        func applyDocumentHeight(_ height: CGFloat) {
            guard let documentHeight, height > 0,
                  abs(documentHeight.constant - height) > 0.5 else { return }
            documentHeight.constant = height
            refreshOverflowState()
        }

        func handleBoundsChanged() {
            guard !isProgrammaticScroll else { return }
            userPinnedToBottom = isNearBottom()
        }

        /// Distance from the bottom edge (points) that still counts as "following".
        private let bottomPinThreshold: CGFloat = 24

        func isNearBottom(threshold: CGFloat? = nil) -> Bool {
            let threshold = threshold ?? bottomPinThreshold
            guard let scroll = scrollView, let doc = scroll.documentView else { return true }
            let clip = scroll.contentView
            let viewHeight = clip.bounds.height
            let contentHeight = doc.frame.height
            let maxScrollY = max(0, contentHeight - viewHeight)
            guard maxScrollY > 0 else { return true }
            // Flipped document (NSHostingView): y grows downward; bottom is high y.
            let currentY = clip.bounds.origin.y
            return (maxScrollY - currentY) <= threshold
        }

        func refreshOverflowState() {
            guard let scroll = scrollView, let doc = scroll.documentView else { return }
            let overflows = doc.frame.height > scroll.contentView.bounds.height + 0.5
            if scroll.hasVerticalScroller != overflows {
                scroll.hasVerticalScroller = overflows
            }
            if let purple = scroll.verticalScroller as? PurpleScroller {
                purple.contentOverflows = overflows
                purple.needsDisplay = true
            }
        }

        /// Pin the pane to its content origin. Used on first appear so an overflowing
        /// pane never loads mid-content with its first line clipped.
        func scrollToTop() {
            guard let scroll = scrollView else { return }
            isProgrammaticScroll = true
            scroll.scrollToContentTop()
            isProgrammaticScroll = false
            userPinnedToBottom = false
            refreshOverflowState()
        }

        func scrollToBottomIfPinned(animated: Bool) {
            guard userPinnedToBottom else { return }
            guard let scroll = scrollView, let doc = scroll.documentView else { return }
            let targetY = max(0, doc.frame.height - scroll.contentView.bounds.height)
            let target = NSPoint(x: 0, y: targetY)
            isProgrammaticScroll = true
            let finish = { [weak self] in
                self?.isProgrammaticScroll = false
                self?.userPinnedToBottom = true
                self?.refreshOverflowState()
            }
            if animated {
                NSAnimationContext.runAnimationGroup({ ctx in
                    ctx.duration = 0.18
                    scroll.contentView.animator().scroll(to: target)
                    scroll.reflectScrolledClipView(scroll.contentView)
                }, completionHandler: finish)
            } else {
                scroll.contentView.scroll(to: target)
                scroll.reflectScrolledClipView(scroll.contentView)
                finish()
            }
        }
    }
}

// MARK: - PurpleScroller

/// Custom `NSScroller` matching the approved PurpleScrollView contract:
/// transparent track, fixed 10×420 or 10×50 knob, rgba(150,116,235,0.35) fill,
/// rgba(150,116,235,0.55) glow @ 6pt blur, 4px corner radius.
///
/// Drawing, hit testing, drag, paging, and accessibility all share `knobRect`.
final class PurpleScroller: NSScroller {
    var geometry: PurpleScrollbarGeometry = .fullPanel {
        didSet {
            if oldValue != geometry {
                needsDisplay = true
            }
        }
    }

    /// Set by the hosting scroll view; when false the knob is not drawn (no useless bar).
    var contentOverflows: Bool = true

    override class var isCompatibleWithOverlayScrollers: Bool { true }

    override class func scrollerWidth(for controlSize: NSControl.ControlSize,
                                      scrollerStyle: NSScroller.Style) -> CGFloat {
        // Both geometries share a 10pt thickness.
        10
    }

    convenience init(geometry: PurpleScrollbarGeometry) {
        self.init(frame: .zero)
        self.geometry = geometry
    }

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        commonInit()
    }

    required init?(coder: NSCoder) {
        super.init(coder: coder)
        commonInit()
    }

    private func commonInit() {
        // Transparent control chrome; we draw only the knob.
        wantsLayer = false
        setAccessibilityElement(true)
        setAccessibilityRole(.scrollBar)
        setAccessibilityLabel("Scroll bar")
    }

    // MARK: Shared knob geometry (draw / hit / drag / a11y)

    /// Single source of truth for the knob rectangle in scroller coordinates.
    /// Clamps to the track when the track is shorter than the preferred length.
    var knobRect: NSRect {
        Self.knobRect(
            geometry: geometry,
            bounds: bounds,
            slot: super.rect(for: .knobSlot),
            doubleValue: Double(doubleValue)
        )
    }

    /// Pure geometry helper used by production drawing and deterministic tests.
    static func knobRect(
        geometry: PurpleScrollbarGeometry,
        bounds: NSRect,
        slot: NSRect,
        doubleValue: Double
    ) -> NSRect {
        let track: NSRect = {
            if slot.width > 0.5 && slot.height > 0.5 { return slot }
            return bounds
        }()

        let vertical = bounds.height >= bounds.width
        let thickness = geometry.knobWidth
        let value = min(1, max(0, doubleValue))

        if vertical {
            let trackLen = track.height
            let knobLen = geometry.clampedKnobLength(trackLength: trackLen)
            guard knobLen > 0, trackLen > 0 else { return .zero }
            let travel = max(0, trackLen - knobLen)
            // NSScroller: doubleValue 0 places the knob at the top of a vertical scroller.
            let y = track.minY + CGFloat(value) * travel
            let x = track.midX - thickness / 2
            return NSRect(x: x, y: y, width: thickness, height: knobLen)
        } else {
            let trackLen = track.width
            let knobLen = geometry.clampedKnobLength(trackLength: trackLen)
            guard knobLen > 0, trackLen > 0 else { return .zero }
            let travel = max(0, trackLen - knobLen)
            let x = track.minX + CGFloat(value) * travel
            let y = track.midY - thickness / 2
            return NSRect(x: x, y: y, width: knobLen, height: thickness)
        }
    }

    override func rect(for partCode: NSScroller.Part) -> NSRect {
        switch partCode {
        case .knob:
            return knobRect
        default:
            return super.rect(for: partCode)
        }
    }

    override func testPart(_ point: NSPoint) -> NSScroller.Part {
        let slot = rect(for: .knobSlot)
        guard slot.contains(point) else { return .noPart }
        let knob = knobRect
        if knob.contains(point) {
            return .knob
        }
        // Page before / after the fixed knob (same geometry as drawing).
        // AppKit uses decrementPage (before knob) / incrementPage (after knob).
        let vertical = bounds.height >= bounds.width
        if vertical {
            if point.y < knob.minY { return .decrementPage }
            if point.y > knob.maxY { return .incrementPage }
        } else {
            if point.x < knob.minX { return .decrementPage }
            if point.x > knob.maxX { return .incrementPage }
        }
        return .knobSlot
    }

    // Transparent track — matches scrollbar-track { background: transparent }.
    override func drawKnobSlot(in slot: NSRect, highlight: Bool) {
        // Intentionally empty.
    }

    override func drawKnob() {
        guard contentOverflows else { return }
        let knob = knobRect
        guard knob.width > 0.5, knob.height > 0.5 else { return }

        NSGraphicsContext.saveGraphicsState()
        defer { NSGraphicsContext.restoreGraphicsState() }

        let path = NSBezierPath(
            roundedRect: knob,
            xRadius: geometry.cornerRadius,
            yRadius: geometry.cornerRadius
        )

        let shadow = NSShadow()
        shadow.shadowColor = PurpleScrollbarStyle.glowColor
        shadow.shadowBlurRadius = PurpleScrollbarStyle.glowBlurRadius
        shadow.shadowOffset = PurpleScrollbarStyle.glowOffset
        shadow.set()

        PurpleScrollbarStyle.knobFill.setFill()
        path.fill()
    }

    override func draw(_ dirtyRect: NSRect) {
        // Draw only our track (empty) + knob. Avoid default aqua chrome.
        drawKnobSlot(in: rect(for: .knobSlot), highlight: false)
        if contentOverflows {
            drawKnob()
        }
    }

    // MARK: Accessibility frame agrees with drawn knob

    override func accessibilityFrame() -> NSRect {
        guard let window else { return super.accessibilityFrame() }
        let rectInWindow = convert(knobRect, to: nil)
        return window.convertToScreen(rectInWindow)
    }

    override func isAccessibilityElement() -> Bool { true }

    override func accessibilityRole() -> NSAccessibility.Role? { .scrollBar }

    override func accessibilityValue() -> Any? { doubleValue }

    override func accessibilityMinValue() -> Any? { 0.0 }

    override func accessibilityMaxValue() -> Any? { 1.0 }
}

// MARK: - WKWebView bridge

enum PurpleWebScrollBridge {
    /// Locate the internal `NSScrollView` owned by a `WKWebView` (descendant, not enclosing).
    static func scrollView(in webView: WKWebView) -> NSScrollView? {
        if let inner = firstScrollView(in: webView) { return inner }
        return webView.enclosingScrollView
    }

    private static func firstScrollView(in view: NSView) -> NSScrollView? {
        for sub in view.subviews {
            if let scroll = sub as? NSScrollView { return scroll }
            if let found = firstScrollView(in: sub) { return found }
        }
        return nil
    }

    /// Install full-panel PurpleScroller on a web content scroll view without nesting wrappers.
    @discardableResult
    static func applyFullPanelScroller(to scrollView: NSScrollView) -> PurpleScroller {
        let scroller: PurpleScroller
        if let existing = scrollView.verticalScroller as? PurpleScroller {
            existing.geometry = .fullPanel
            scroller = existing
        } else {
            scroller = PurpleScroller(geometry: .fullPanel)
            scrollView.verticalScroller = scroller
        }
        scrollView.hasVerticalScroller = true
        scrollView.hasHorizontalScroller = false
        scrollView.scrollerStyle = .legacy
        scrollView.autohidesScrollers = true
        scrollView.automaticallyAdjustsContentInsets = false
        scrollView.contentInsets = NSEdgeInsetsZero
        scrollView.scrollerInsets = NSEdgeInsetsZero
        scrollView.tile()
        return scroller
    }

    /// Find and paint the internal WKWebView scroller with full-panel geometry.
    @discardableResult
    static func applyFullPanelScroller(toWebView webView: WKWebView) -> PurpleScroller? {
        guard let scroll = scrollView(in: webView) else { return nil }
        return applyFullPanelScroller(to: scroll)
    }
}

#if DEBUG
#Preview {
    PurpleScrollView(geometry: .fullPanel) {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(1..<18, id: \.self) { index in
                Text("Preview row \(index)")
                    .font(.ioMono(12))
                    .foregroundColor(.ioText)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
    .frame(width: 360, height: 180)
    .padding(24)
    .syxxPreviewPanel()
}
#endif

extension NSScrollView {
    /// Scroll to the first line of content, correct for either coordinate system.
    ///
    /// A flipped document view puts the origin at the top, so y = 0 is the first line.
    /// An unflipped one puts it at the bottom, so the first line is at
    /// `documentHeight - viewportHeight`. Scrolling blindly to `.zero` shows the TOP of
    /// flipped content and the BOTTOM of unflipped content, which is why the load
    /// position was not deterministic.
    func scrollToContentTop() {
        guard let doc = documentView else { return }
        let viewport = contentView.bounds.height
        let topY = doc.isFlipped ? 0 : max(0, doc.frame.height - viewport)
        contentView.scroll(to: NSPoint(x: 0, y: topY))
        reflectScrolledClipView(contentView)
    }
}
