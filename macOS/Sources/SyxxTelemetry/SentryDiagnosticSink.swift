import Foundation
import Sentry
import SyxxKit

/// The bridge that makes `SyxxDiagnostics` actually report.
///
/// WHY THIS FILE IS THE MISSING HALF. `SyxxKit` captures diagnostics through a vendor-neutral
/// protocol so the core does not link Sentry. That is the right shape, but until something
/// *implements* the protocol and installs it, `SyxxDiagnostics.capture` finds a nil sink and does
/// nothing — every carefully-placed capture in the engine drops on the floor. The core was
/// instrumented and the wire was never connected.
///
/// CONSENT. Installed only from `install(ifAllowed:)`, behind the same `TelemetryGate` that gates
/// `SentryBootstrap` and `OTelBootstrap`. One gate, three consumers — a second gate is a second
/// thing to forget.
///
/// REDACTION. `DiagnosticEvent.underlying` is documented as caller-redacted, but a hostname
/// leaking into a Sentry issue is a privacy incident for a product whose whole promise is DNS
/// privacy, so this sink re-checks rather than trusting the contract. Belt and braces is correct
/// here: the cost of a redundant check is nothing; the cost of a leak is the product's premise.
public struct SentryDiagnosticSink: DiagnosticSink {

    /// Install if and only if consent allows. Returns whether reporting is now live, so callers
    /// can surface the OBSERVED state rather than the toggle position.
    @discardableResult
    public static func install(ifAllowed: Bool = TelemetryGate.isAllowed) -> Bool {
        guard ifAllowed else { return false }
        SyxxDiagnostics.install(SentryDiagnosticSink())
        return true
    }

    /// Consent withdrawn: detach and drop. `SyxxDiagnostics.uninstall()` calls `discardBuffered()`.
    public static func remove() { SyxxDiagnostics.uninstall() }

    public func capture(_ event: DiagnosticEvent) {
        let e = Event(level: event.severity.sentryLevel)
        e.message = SentryMessage(formatted: "\(event.errorClass): \(event.component)")
        // Explicit fingerprint, always. Sentry's default stack-based grouping would merge the
        // thirteen distinct MatrixEngineError variants into one issue (they all throw from the
        // same line), while message-based grouping would shatter them into hundreds (the
        // descriptions interpolate diagnosis and indicator ids). Neither is usable.
        e.fingerprint = event.fingerprint

        var tags = event.tags
        tags["syxx.error_class"] = event.errorClass
        tags["syxx.component"]   = event.component
        tags["syxx.impact"]      = event.impact.rawValue
        tags["syxx.section"]     = event.section?.rawValue ?? "none"
        tags["syxx.surface"]     = "gui"
        tags["syxx.platform"]    = "macos"
        // Join key to the result-provenance event and to the OTel span, so one query returns the
        // whole causal chain: which failure, which section, which result.
        tags["syxx.run_id"]      = SyxxDiagnostics.run?.id ?? "none"
        e.tags = tags

        var extra: [String: Any] = event.extra
        if let u = event.underlying { extra["underlying"] = Self.redact(u) }
        e.extra = extra

        SentrySDK.capture(event: e)
    }

    public func breadcrumb(category: String, message: String,
                    level: DiagnosticSeverity, data: [String: String]) {
        let b = Breadcrumb(level: level.sentryLevel, category: category)
        b.message = Self.redact(message)
        b.data = data.mapValues { Self.redact($0) }
        SentrySDK.addBreadcrumb(b)
    }

    /// Consent withdrawal must DISCARD, never flush. Flushing here would transmit exactly the data
    /// the user just refused. `close()` alone is insufficient: sentry-cocoa persists unsent
    /// envelopes and would ship them on the next `start`, after the opt-out.
    public func discardBuffered() {
        SentrySDK.close()
        if let dir = SyxxConfig.telemetryCacheDirectory {
            try? FileManager.default.removeItem(at: dir)
        }
    }

    // MARK: - Redaction

    /// Last-line defence against user data reaching the issue tracker. A DNS query stream is a
    /// browsing history; resolver IPs identify an ISP or employer. Neither may leave the device.
    public static func redact(_ s: String) -> String {
        var out = s
        for (pattern, replacement) in Self.patterns {
            out = pattern.stringByReplacingMatches(
                in: out, range: NSRange(out.startIndex..., in: out), withTemplate: replacement)
        }
        return out
    }

    private static let patterns: [(NSRegularExpression, String)] = {
        func re(_ p: String) -> NSRegularExpression? {
            try? NSRegularExpression(pattern: p, options: [.caseInsensitive])
        }
        return [
            // IPv4 literal
            (re(#"\b(?:\d{1,3}\.){3}\d{1,3}\b"#), "<ip>"),
            // IPv6 literal (loose, deliberately over-matches rather than under-matches)
            (re(#"\b(?:[0-9a-f]{0,4}:){2,7}[0-9a-f]{0,4}\b"#), "<ip6>"),
            // Anything that looks like a registrable domain
            (re(#"\b[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9-]{1,63})+\b"#), "<domain>"),
            // Home directory paths
            (re(#"/Users/[^/\s]+"#), "/Users/<user>"),
        ].compactMap { pair in pair.0.map { ($0, pair.1) } }
    }()
}

private extension DiagnosticSeverity {
    var sentryLevel: SentryLevel {
        switch self {
        case .warning: return .warning
        case .error:   return .error
        case .fatal:   return .fatal
        }
    }
}
