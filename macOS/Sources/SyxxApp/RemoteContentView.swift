import SwiftUI
import WebKit
import SyxxKit

// MARK: - ZeroInsetContainerView

/// An NSView that hosts a WKWebView and aggressively enforces zero content insets
/// on the WKWebView's internal NSScrollView so the letter content touches the
/// window's top edge with zero gap.
///
/// macOS automatically adds title-bar–area insets to scroll views inside
/// transparent-title-bar windows.  `automaticallyAdjustsContentInsets = false`
/// alone is not enough — the insets may already be applied, and the system can
/// re-apply them on layout.  This view fights back on three fronts:
///   1. `viewDidMoveToWindow` — configures the scroll view as soon as the
///      hierarchy is attached to a window.
///   2. `layout()` — re-applies zero insets every layout pass.
///   3. `NSView.frameDidChangeNotification` — catches re-inset events that
///      happen outside of a full layout.
private final class ZeroInsetContainerView: NSView {
    private weak var trackedWebView: WKWebView?
    private var frameObserver: (any NSObjectProtocol)? {
        willSet { if let old = frameObserver { NotificationCenter.default.removeObserver(old) } }
    }

    /// Call this after adding the WKWebView as a subview.
    func track(_ webView: WKWebView) {
        trackedWebView = webView
        configureScrollView(of: webView)
    }

    // MARK: - NSView overrides

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        // The scroll view hierarchy is guaranteed to exist once the view is in a window.
        if let wv = trackedWebView {
            configureScrollView(of: wv)
        }
        // Observe frame changes to catch the system re-applying insets.
        if frameObserver == nil, let wv = trackedWebView {
            frameObserver = NotificationCenter.default.addObserver(
                forName: NSView.frameDidChangeNotification,
                object: wv,
                queue: .main
            ) { [weak self] _ in
                guard let self else { return }
                Task { @MainActor in self.zeroOutInsets() }
            }
        }
    }

    override func layout() {
        super.layout()
        zeroOutInsets()
    }

    // MARK: - Insets

    private func configureScrollView(of webView: WKWebView) {
        // Prefer the internal WKWebView scroll view (descendant). Apply full-panel
        // PurpleScroller without wrapping the web view in another scroll container.
        if let scrollView = PurpleWebScrollBridge.scrollView(in: webView) {
            PurpleWebScrollBridge.applyFullPanelScroller(to: scrollView)
        } else if let scrollView = webView.enclosingScrollView {
            scrollView.automaticallyAdjustsContentInsets = false
            scrollView.contentInsets = NSEdgeInsetsZero
        }
    }

    private func zeroOutInsets() {
        guard let wv = trackedWebView else { return }
        if let scrollView = PurpleWebScrollBridge.scrollView(in: wv) {
            PurpleWebScrollBridge.applyFullPanelScroller(to: scrollView)
        } else if let scrollView = wv.enclosingScrollView {
            scrollView.automaticallyAdjustsContentInsets = false
            scrollView.contentInsets = NSEdgeInsetsZero
        }
    }
}

// MARK: - RemoteContentView

/// A locked-down WKWebView that renders self-contained HTML from a remote URL.
///
/// Security properties (per handoff spec):
/// - JavaScript is OFF (`allowsContentJavaScript = false`)
/// - Navigation is restricted to the origin URL's host (or an allow-list); any link
///   click opens the system browser instead.
/// - The response is cached offline so the panel renders when Dimension is unreachable.
/// - No cookies, no autofill, no persistent data store.
/// - Inline CSS + data-URI images only (the server is expected to serve self-contained HTML).
///
/// Usage:
/// ```
/// RemoteContentView(url: "https://syxx.app/app/whats-new/macos")
/// ```
/// Remote-content surface mode. MOTD uses the shared five-second packaged fallback contract;
/// generic remote panels keep the existing offline-letter policy (`fallback-mac.html`).
enum RemoteContentMode: Equatable {
    case generic
    case motd
}

struct RemoteContentView: NSViewRepresentable {
    let url: URL
    /// Additional hosts whose links are allowed in-app (besides the URL's own host).
    var allowList: Set<String> = []
    /// `.motd` enforces the five-second remote deadline and `fallback-motd.html` only.
    var mode: RemoteContentMode = .generic

    /// One warm container NSView per URL, reused across sidebar switches. Rebuilding it on every switch
    /// (a fresh web-content process + reload) is what made entering/leaving "What's New" lag.
    ///
    /// A FAILED view is NEVER cached (see Coordinator.webView(_:didFail…)). It used to be: the view
    /// was inserted here unconditionally and `updateNSView` is a no-op, so one failed load meant a
    /// dead pane for the whole process lifetime — switching away and back returned the same corpse.
    /// That is how a server move on 2026-07-16 produced a permanently blank What's-New pane while
    /// the server was serving 200s the entire time.
    ///
    /// F024: NSCache automatically evicts under memory pressure and honours countLimit, so WKWebView
    /// instances no longer persist for the app's lifetime when the URL set grows.
    @MainActor private static let cache: NSCache<NSURL, NSView> = {
        let c = NSCache<NSURL, NSView>()
        c.countLimit = 4  // bounded: a few warm views, never unbounded growth
        return c
    }()

    /// Drop a URL's warm view so the next appearance builds and loads a fresh one.
    @MainActor static func invalidate(_ url: URL) {
        cache.removeObject(forKey: url as NSURL)
    }

    init(url: URL, allowList: Set<String> = [], mode: RemoteContentMode = .generic) {
        self.url = url
        self.allowList = allowList
        self.mode = mode
    }

    #if DEBUG
    private static let previewHTML = """
    <html>
    <head>
      <meta charset="utf-8">
      <style>
        body {
          margin: 0;
          min-height: 100vh;
          display: grid;
          place-items: center;
          background: #0b0518;
          color: #efeafe;
          font-family: -apple-system, BlinkMacSystemFont, sans-serif;
        }
        main {
          max-width: 680px;
          padding: 28px;
          border: 1px solid rgba(217,164,65,0.45);
          background: rgba(29,22,64,0.72);
          border-radius: 12px;
        }
        h1 { margin: 0 0 10px; font-size: 24px; }
        p { color: #c9c1e6; line-height: 1.5; }
      </style>
    </head>
    <body>
      <main>
        <h1>Syxx Preview Content</h1>
        <p>This local HTML is shown only in Xcode Canvas so the preview does not make a network request.</p>
      </main>
    </body>
    </html>
    """
    #endif

    func makeNSView(context: Context) -> NSView {
        // Reuse the already-built, already-loaded container for this URL — no new process, no reload.
        if let cached = Self.cache.object(forKey: url as NSURL) {
            // Re-attach the coordinator to the cached WKWebView subview
            if let webView = cached.subviews.first as? WKWebView {
                webView.navigationDelegate = context.coordinator
            }
            return cached
        }

        let container = ZeroInsetContainerView(frame: .zero)

        let config = WKWebViewConfiguration()
        // Ephemeral store — no cookies, no autofill
        config.websiteDataStore = WKWebsiteDataStore.nonPersistent()

        let preferences = WKWebpagePreferences()
        preferences.allowsContentJavaScript = false
        config.defaultWebpagePreferences = preferences

        // D039: install a WKContentRuleList that blocks external subresources (images, styles,
        // fonts, media). The server is expected to serve self-contained HTML with inline CSS and
        // data-URI images only. Without this rule, a redirected or misconfigured remote HTML
        // could load external resources despite the documented lock-down.
        // The rule blocks all resources EXCEPT data: URIs and the initial document load.
        let ruleListJSON = """
        [
          {
            "trigger": { "url-filter": ".*", "resource-type": ["image", "style-sheet", "font", "media", "svg-document"], "load-type": ["third-party"] },
            "action": { "type": "block" }
          },
          {
            "trigger": { "url-filter": "data:.*" },
            "action": { "type": "ignore-previous-rules" }
          }
        ]
        """
        // No WKUserScripts are added here.  allowsContentJavaScript is false, so
        // JavaScript is disabled entirely — user scripts injected via the content
        // controller never execute.  CSS injection is done server-side by fetching
        // the HTML ourselves and rewriting the <head> before loading (see
        // Coordinator.loadContent).

        let webView = WKWebView(frame: container.bounds, configuration: config)
        webView.navigationDelegate = context.coordinator
        webView.allowsBackForwardNavigationGestures = false
        webView.allowsMagnification = false

        // Transparent background so the webview blends with the app's dark canvas
        // instead of flashing white before the page paints.
        webView.setValue(false, forKey: "drawsBackground")
        if #available(macOS 14.0, *) {
            webView.underPageBackgroundColor = .clear
        }
        webView.autoresizingMask = [.width, .height]

        container.addSubview(webView)
        container.track(webView)

        #if DEBUG
        if ProcessInfo.processInfo.isSyxxXcodePreview {
            webView.loadHTMLString(Self.previewHTML, baseURL: nil)
            Self.cache.setObject(container, forKey: url as NSURL)
            return container
        }
        #endif

        // E048: defer loadContent until the WKContentRuleList compilation callback
        // fires. Previously, loadContent was called synchronously after kicking off
        // the async compile — the first HTTP request could race ahead of the rule
        // list being installed, letting external subresources through the gap.
        // Now the load is triggered from inside the completion handler, so the
        // rule list is guaranteed to be in place (or compilation has failed and
        // we proceed with CSP-only defense).
        WKContentRuleListStore.default().compileContentRuleList(
            forIdentifier: "SyxxBlockExternalResources",
            encodedContentRuleList: ruleListJSON
        ) { ruleList, error in
            if let ruleList {
                config.userContentController.add(ruleList)
            }
            // If compilation fails, the webview still works but external resources are not blocked.
            // This is a defense-in-depth layer; the server CSP header is the primary defense.
            // Fetch the HTML ourselves so we can inject a CSS reset before the page
            // renders.  The WKUserScript path is dead because allowsContentJavaScript
            // is false; this is the only way to strip html/body margins.
            DispatchQueue.main.async {
                context.coordinator.loadContent(into: webView, url: url)
            }
        }

        Self.cache.setObject(container, forKey: url as NSURL)
        return container
    }

    func updateNSView(_ nsView: NSView, context: Context) {
        // Keep the WKWebView sized to fill the container.
        if let webView = nsView.subviews.first as? WKWebView {
            // D058: defensively re-assert the navigation delegate on every SwiftUI update
            // pass. SwiftUI's coordinator lifetime normally matches the representable's,
            // but edge cases (identity change, deep view-tree rebuild) could detach it.
            // Re-assignment is idempotent and costs nothing when already correct.
            if webView.navigationDelegate !== context.coordinator {
                webView.navigationDelegate = context.coordinator
            }
            if webView.frame.size != nsView.bounds.size {
                webView.frame = nsView.bounds
            }
            // Aggressively zero out scroll view insets and re-apply full-panel
            // PurpleScroller on every update. ZeroInsetContainerView handles the
            // layout path; this catches SwiftUI resize / sidebar switch.
            if let scrollView = PurpleWebScrollBridge.scrollView(in: webView) {
                PurpleWebScrollBridge.applyFullPanelScroller(to: scrollView)
            } else {
                // Hierarchy may not be ready yet; defer one runloop cycle.
                DispatchQueue.main.async {
                    if let scrollView = PurpleWebScrollBridge.scrollView(in: webView) {
                        PurpleWebScrollBridge.applyFullPanelScroller(to: scrollView)
                    } else {
                        webView.enclosingScrollView?.automaticallyAdjustsContentInsets = false
                        webView.enclosingScrollView?.contentInsets = NSEdgeInsetsZero
                    }
                }
            }
        }
    }

    func makeCoordinator() -> Coordinator {
        Coordinator(url: url, allowList: allowList.union([url.host].compactMap { $0 }), mode: mode)
    }

    final class Coordinator: NSObject, WKNavigationDelegate {
        let allowedHosts: Set<String>
        let url: URL
        let mode: RemoteContentMode
        private var motd = MotdLoadController()
        private var dataTask: URLSessionDataTask?
        // nonisolated(unsafe): cancelled only from deinit / main-actor load paths; not MOTD logic.
        nonisolated(unsafe) private var deadlineWorkItem: DispatchWorkItem?
        private weak var activeWebView: WKWebView?

        init(url: URL, allowList: Set<String>, mode: RemoteContentMode) {
            // Include the API host for initial load, plus any addl allow-listed hosts
            self.url = url
            self.allowedHosts = allowList
            self.mode = mode
        }

        deinit {
            dataTask?.cancel()
            deadlineWorkItem?.cancel()
        }

        func webView(_ webView: WKWebView, decidePolicyFor navigationAction: WKNavigationAction,
                     decisionHandler: @escaping @MainActor @Sendable (WKNavigationActionPolicy) -> Void) {
            guard let targetURL = navigationAction.request.url else {
                decisionHandler(.cancel)
                return
            }

            // D033 + D087: full origin check — scheme, host, port, and userinfo.
            // A scheme downgrade (https→http) to the same host was previously .allow'd,
            // which is a live injection surface in a product whose thesis is that on-path
            // attackers rewrite DNS and traffic.
            if let host = targetURL.host, allowedHosts.contains(host),
               targetURL.scheme?.lowercased() == "https",
               targetURL.port == nil || targetURL.port == 443,
               targetURL.user == nil {
                decisionHandler(.allow)
                return
            }
            // For initial load (.other) of a non-allowed host, block it.
            if navigationAction.navigationType == .other {
                decisionHandler(.cancel)
                return
            }
            let eval = RemoteUrlPolicy.evaluate(targetURL.absoluteString)
            if eval.isAllowed {
                NSWorkspace.shared.open(targetURL)
            } else {
                let alert = NSAlert()
                alert.messageText = "Link Blocked"
                alert.informativeText = eval.reason ?? "This link is not authorized to be opened."
                alert.alertStyle = .warning
                alert.addButton(withTitle: "OK")
                alert.runModal()
            }
            decisionHandler(.cancel)
        }

        /// Fetch the HTML from `url`, inject a CSS reset into the <head>, and load the
        /// result into the webView.  On fetch failure, delegates to handleFailure for
        /// the offline/error fallback page.
        ///
        /// MOTD mode: starts a monotonic five-second deadline before the fetch. The first
        /// terminal state wins (remote success vs fallback). Late callbacks for a stale
        /// generation are ignored. Packaged fallback is always `fallback-motd.html`.
        ///
        /// This replaces the old WKUserScript-based CSS injection, which was dead code
        /// because `allowsContentJavaScript` is false — user scripts are JavaScript and
        /// never execute when JS is disabled.
        func loadContent(into webView: WKWebView, url: URL) {
            activeWebView = webView
            dataTask?.cancel()
            deadlineWorkItem?.cancel()

            let gen: UInt64
            let timeout: TimeInterval
            if mode == .motd {
                gen = motd.beginRemoteLoad()
                timeout = MotdLoadController.deadlineSeconds
                let work = DispatchWorkItem { [weak self] in
                    guard let self else { return }
                    self.activateMotdFallback(webView: webView, generation: gen)
                }
                deadlineWorkItem = work
                DispatchQueue.main.asyncAfter(deadline: .now() + MotdLoadController.deadlineSeconds, execute: work)
            } else {
                gen = 0
                timeout = 30
            }

            let request = URLRequest(url: url, cachePolicy: .reloadRevalidatingCacheData, timeoutInterval: timeout)
            let task = URLSession.shared.dataTask(with: request) { [weak self, weak webView] data, response, error in
                DispatchQueue.main.async {
                    guard let self, let webView else { return }
                    if self.mode == .motd {
                        guard self.motd.accepts(generation: gen) else { return }
                        guard self.motd.state == .loadingRemote || self.motd.state == .initializingRenderer else { return }
                    }
                    if let data = data, var html = String(data: data, encoding: .utf8),
                       self.isSuccessfulHTTP(response) {
                        // Inject a CSS reset before </head> so the letter container
                        // sits flush with the top of the WKWebView.
                        let cssReset = "<style>html,body{margin:0!important;padding:0!important;}</style>"
                        if let headEnd = html.range(of: "</head>") {
                            html.insert(contentsOf: cssReset, at: headEnd.lowerBound)
                        } else if let bodyStart = html.range(of: "<body") {
                            html.insert(contentsOf: "<head>\(cssReset)</head>",
                                        at: bodyStart.lowerBound)
                        } else {
                            html = "<html><head>\(cssReset)</head><body>\(html)</body></html>"
                        }
                        if self.mode == .motd {
                            guard self.motd.remoteSucceeded(generation: gen) else { return }
                            self.deadlineWorkItem?.cancel()
                            self.deadlineWorkItem = nil
                        }
                        webView.loadHTMLString(html, baseURL: url)
                    } else {
                        let nsError = error as NSError?
                            ?? NSError(domain: NSURLErrorDomain,
                                       code: NSURLErrorCannotLoadFromNetwork,
                                       userInfo: nil)
                        self.handleFailure(webView, nsError, generation: gen)
                    }
                }
            }
            dataTask = task
            task.resume()
        }

        private func isSuccessfulHTTP(_ response: URLResponse?) -> Bool {
            guard let http = response as? HTTPURLResponse else {
                // file:// and non-HTTP loads have no status; treat as success when body exists.
                return true
            }
            return (200..<300).contains(http.statusCode)
        }

        /// Load packaged `fallback-motd.html` into the same WebView only.
        private func activateMotdFallback(webView: WKWebView, generation gen: UInt64) {
            guard motd.activateFallback(generation: gen) else { return }
            dataTask?.cancel()
            deadlineWorkItem?.cancel()
            deadlineWorkItem = nil
            let u = url
            Task { @MainActor in RemoteContentView.invalidate(u) }

            guard let res = MotdLoadController.resolveFallbackURL(in: Bundle.module),
                  let html = try? String(contentsOf: res, encoding: .utf8) else {
                // Approved renderer-unavailable path when the packaged asset is missing.
                // Must not substitute any other local HTML resource.
                _ = motd.markRendererUnavailable(generation: gen)
                let page = Self.generatedFallback(offline: true,
                    error: NSError(domain: NSURLErrorDomain, code: NSURLErrorCannotLoadFromNetwork, userInfo: nil),
                    url: url)
                webView.loadHTMLString(page, baseURL: nil)
                return
            }
            // Base URL rooted at the packaged directory so local CSS/fonts/images resolve.
            webView.loadHTMLString(html, baseURL: res.deletingLastPathComponent())
        }

        /// EVERY failure must render something. This handler used to fire only for
        /// `NotConnectedToInternet` and `TimedOut`; every other error — `CannotFindHost`,
        /// `CannotConnectToHost`, TLS failures, 4xx/5xx-driven aborts — fell through the `if` and did
        /// NOTHING, leaving a blank pane in silence. Combined with the permanent view cache, one
        /// failed load meant a dead panel for the life of the process.
        ///
        /// That is this codebase's own cardinal sin wearing a different hat: a surface rendering
        /// "nothing is wrong" for a state it never measured. The delegate KNEW the load had failed and
        /// said nothing. A blank pane is not an honest report of a failure — it is the absence of one.
        func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!,
                     withError error: Error) {
            handleFailure(webView, error, generation: motd.generation)
        }

        /// The other half: a failure AFTER the response started arriving never had a handler at all.
        func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) {
            handleFailure(webView, error, generation: motd.generation)
        }

        func handleFailure(_ webView: WKWebView, _ error: Error, generation gen: UInt64 = 0) {
            let nsError = error as NSError

            // A cancelled navigation is not a failure — it is the allow-list opening a link in the
            // browser (decisionHandler(.cancel) above), or MOTD deadline cancelling the remote task.
            // Rendering an error page for it would be a lie in the opposite direction.
            if nsError.domain == NSURLErrorDomain && nsError.code == NSURLErrorCancelled { return }

            if mode == .motd {
                activateMotdFallback(webView: webView, generation: gen == 0 ? motd.generation : gen)
                return
            }

            // Drop the cached view so the next visit RETRIES instead of resurrecting this corpse.
            let u = url
            Task { @MainActor in RemoteContentView.invalidate(u) }

            // NetworkConnectionLost is deliberately NOT "offline" — see failurePage. The name lies:
            // it means the connection was ESTABLISHED and then killed, which is not what being
            // offline looks like.
            let offline = nsError.domain == NSURLErrorDomain
                && (nsError.code == NSURLErrorNotConnectedToInternet
                    || nsError.code == NSURLErrorTimedOut)
            // NEVER A WHITE SCREEN. Two layers on purpose: the designed letter, and — if the bundled
            // resource is somehow missing (a botched copy in bundle-app.sh, which uses `|| true` and
            // can silently ship an incomplete bundle) — a plain page built from a string literal that
            // cannot fail to exist. The failure path is the one place that must not have a failure path.
            // Generic remote-content keeps fallback-mac.html; MOTD never reaches this branch.
            let page = Self.bundledGenericFallback(error: nsError, url: url)
                ?? Self.generatedFallback(offline: offline, error: nsError, url: url)
            webView.loadHTMLString(page, baseURL: nil)
        }

        /// Generic (non-MOTD) offline letter. MOTD must not call this — it uses fallback-motd only.
        ///
        /// baseURL is nil, so the page must be entirely self-contained — no origin exists to resolve a
        /// relative URL against. That is why the fonts are data: URIs and why every Google Fonts link
        /// and the Cloudflare email-decode script were stripped: an offline page that fetches from the
        /// network is a contradiction, and a decoder script that never runs leaves the support address
        /// reading literally "[email protected]".
        private static func bundledGenericFallback(error: NSError, url: URL) -> String? {
            // Tolerate both bundle layouts: .copy("Resources") preserves the folder, but the icns and
            // fonts resolve without a subdirectory today. Cheap to accept either than to be wrong.
            let res = Bundle.module.url(forResource: "fallback-mac", withExtension: "html")
                ?? Bundle.module.url(forResource: "fallback-mac", withExtension: "html",
                                     subdirectory: "Resources")
            guard let res, let html = try? String(contentsOf: res, encoding: .utf8) else { return nil }

            let host = url.host ?? url.absoluteString
            let block = """
            <p class="diagnostic"><strong>What we saw:</strong> \(escape(detail(for: error, host: host)))
            <br><span class="mono">\(escape(error.domain)) \(error.code)</span></p>
            """
            // The Retry button was href="#" onclick="location.reload()" — inert, because JavaScript is
            // off in this webview. A real same-host href retries for free: the coordinator allows
            // same-host link clicks, so the click reloads the page that failed.
            return html
                .replacingOccurrences(of: "<!--SYXX_DIAGNOSTIC-->", with: block)
                .replacingOccurrences(of: "SYXX_RETRY_URL", with: escape(url.absoluteString))
        }

        /// The diagnosis, shared by BOTH layers so the designed letter and the last-resort page can
        /// never disagree about the cause. Two copies of this switch would eventually drift, which is
        /// the same defect as the three independent result→colour mappings in the Dashboards.
        private static func detail(for error: NSError, host: String) -> String {
            switch (error.domain, error.code) {
            case (NSURLErrorDomain, NSURLErrorNotConnectedToInternet):
                return "This Mac appears to be offline."
            case (NSURLErrorDomain, NSURLErrorTimedOut):
                return "\(host) did not respond in time."
            case (NSURLErrorDomain, NSURLErrorCannotFindHost):
                return "\(host) could not be resolved. If the rest of the internet works, "
                     + "something on this network may be interfering with DNS."
            case (NSURLErrorDomain, NSURLErrorCannotConnectToHost):
                return "\(host) resolved, but refused the connection."
            case (NSURLErrorDomain, NSURLErrorNetworkConnectionLost):
                // FIELD DIAGNOSIS, 2026-07-16: this was Little Snitch, and it cost an hour on the
                // author's own machine with full knowledge of the codebase. -1005 means the
                // connection was ESTABLISHED and then killed mid-flight — which is what a local
                // outbound filter does, and NOT what being offline looks like. Syxx self-selects for
                // exactly the people who run Little Snitch, LuLu, or a corporate agent, so this is a
                // high-frequency support ticket that answers itself if the copy names the cause.
                return "The connection to \(host) was interrupted after it opened. "
                     + "An outbound firewall (Little Snitch, LuLu, or a corporate agent) blocking "
                     + "Syxx is the usual cause — check whether it is allowed to reach \(host)."
            case (NSURLErrorDomain, NSURLErrorSecureConnectionFailed),
                 (NSURLErrorDomain, NSURLErrorServerCertificateUntrusted),
                 (NSURLErrorDomain, NSURLErrorServerCertificateHasBadDate),
                 (NSURLErrorDomain, NSURLErrorServerCertificateHasUnknownRoot):
                return "The secure connection to \(host) could not be verified."
            default:
                return "\(host) — \(error.localizedDescription)"
            }
        }

        /// The error text is server- and OS-supplied, and it is being interpolated into HTML.
        /// `localizedDescription` can carry a URL or hostname; a hostile one carrying markup must not
        /// become markup. JavaScript is off, so this is belt-and-braces — but the belt is free.
        private static func escape(_ s: String) -> String {
            s.replacingOccurrences(of: "&", with: "&amp;")
             .replacingOccurrences(of: "<", with: "&lt;")
             .replacingOccurrences(of: ">", with: "&gt;")
             .replacingOccurrences(of: "\"", with: "&quot;")
        }

        /// Last resort: a page built from a string literal, so it cannot fail to exist even if the
        /// bundled resource is missing. Deliberately plain — its only job is to never be blank.
        private static func generatedFallback(offline: Bool, error: NSError, url: URL) -> String {
            let host = url.host ?? url.absoluteString
            let headline = offline ? "No Connection" : "Couldn't Load What's New"
            return """
            <html><head><meta charset="utf-8">
            <style>
              body { font-family: -apple-system, sans-serif; background: #0f1629; color: #94a3bf;
                     display: flex; align-items: center; justify-content: center; height: 100vh;
                     margin: 0; text-align: center; padding: 32px; }
              h1 { color: #fff; font-size: 22px; margin-bottom: 8px; }
              p { font-size: 14px; line-height: 1.5; }
              code { color: #64748b; font-size: 11px; }
            </style></head><body>
            <div>
              <h1>\(escape(headline))</h1>
              <p>\(escape(detail(for: error, host: host)))</p>
              <p>Leave this page and come back to retry.</p>
              <p><code>\(escape(error.domain)) \(error.code)</code></p>
            </div>
            </body></html>
            """
        }
    }
}

#if DEBUG
#Preview {
    RemoteContentView(url: URL(string: "https://macmotd.syxx.app/")!)
        .syxxPreviewCanvas()
}
#endif
