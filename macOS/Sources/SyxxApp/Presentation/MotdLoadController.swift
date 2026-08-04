import Foundation

/// Terminal states for one MOTD load attempt. The first valid terminal wins; later callbacks
/// for the same generation are ignored.
enum MotdLoadState: Equatable {
    case initializingRenderer
    case loadingRemote
    case showingRemote
    case showingFallback
    case rendererUnavailable
}

/// Pure generation-token race for the five-second remote MOTD contract.
///
/// - Remote success before the deadline cancels the deadline and keeps remote content.
/// - Deadline or definitive failure activates packaged `fallback-motd.html` only.
/// - Late remote callbacks after fallback must never replace it.
/// - A new attempt (retry) mints a new generation.
struct MotdLoadController: Equatable {
    /// Exact shared MOTD deadline (seconds). Monotonic; never wall-clock arithmetic.
    static let deadlineSeconds: TimeInterval = 5.0

    /// Resource name for the packaged MOTD timeout fallback. No other name is allowed.
    static let fallbackResourceName = "fallback-motd"
    static let fallbackResourceExtension = "html"
    static let contentRoleMarker = "motd-timeout-fallback"

    /// Forbidden MOTD timeout substitutes (source-guard / resource-selection).
    static let forbiddenFallbackNames: Set<String> = ["motd-local", "fallback-mac"]

    private(set) var generation: UInt64
    private(set) var state: MotdLoadState

    init(generation: UInt64 = 1, state: MotdLoadState = .initializingRenderer) {
        self.generation = generation
        self.state = state
    }

    /// Begin a remote-load attempt. Returns the generation that owns this attempt.
    mutating func beginRemoteLoad() -> UInt64 {
        switch state {
        case .showingRemote, .showingFallback, .rendererUnavailable, .initializingRenderer, .loadingRemote:
            generation &+= 1
            state = .loadingRemote
            return generation
        }
    }

    /// Remote top-level load completed successfully for `gen`.
    /// Returns true if the transition to `.showingRemote` was accepted.
    @discardableResult
    mutating func remoteSucceeded(generation gen: UInt64) -> Bool {
        guard gen == generation else { return false }
        guard state == .loadingRemote || state == .initializingRenderer else { return false }
        state = .showingRemote
        return true
    }

    /// Deadline fired or a definitive remote failure for `gen`.
    /// Returns true if the transition to `.showingFallback` was accepted.
    @discardableResult
    mutating func activateFallback(generation gen: UInt64) -> Bool {
        guard gen == generation else { return false }
        switch state {
        case .showingRemote, .showingFallback, .rendererUnavailable:
            return false
        case .initializingRenderer, .loadingRemote:
            state = .showingFallback
            return true
        }
    }

    /// Renderer itself is unavailable (no WebView). Distinct from content fallback.
    @discardableResult
    mutating func markRendererUnavailable(generation gen: UInt64) -> Bool {
        guard gen == generation else { return false }
        guard state != .showingRemote && state != .showingFallback else { return false }
        state = .rendererUnavailable
        return true
    }

    /// Whether a callback from `gen` may still mutate the surface.
    func accepts(generation gen: UInt64) -> Bool {
        gen == generation
    }

    /// Resolve the packaged MOTD fallback URL. Never returns motd-local or fallback-mac.
    static func resolveFallbackURL(in bundle: Bundle) -> URL? {
        let url = bundle.url(forResource: fallbackResourceName, withExtension: fallbackResourceExtension)
            ?? bundle.url(forResource: fallbackResourceName, withExtension: fallbackResourceExtension,
                          subdirectory: "Resources")
        guard let url else { return nil }
        let base = url.deletingPathExtension().lastPathComponent
        // Defense: refuse if the resolved name is a forbidden substitute.
        if forbiddenFallbackNames.contains(base) { return nil }
        return url
    }

    /// True when HTML carries the nonvisual MOTD timeout identity marker.
    static func htmlIsTimeoutFallback(_ html: String) -> Bool {
        html.contains("syxx-content-role") && html.contains(contentRoleMarker)
    }
}
