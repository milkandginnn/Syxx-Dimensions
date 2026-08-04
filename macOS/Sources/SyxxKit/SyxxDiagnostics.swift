import Foundation

/// Vendor-neutral diagnostic sink for the core library.
///
/// WHY THIS EXISTS: every honesty-critical failure site — the determinative engine, LicenseManager,
/// DNSCapture, ProbePlan — lives in SyxxKit, and SyxxKit does not (and should not) link Sentry.
/// A capture call written directly against a vendor SDK would not compile here. This indirection
/// is also the single consent gate: the sink is installed only when the user has agreed, so no
/// per-call-site consent check exists to forget.
///
/// THE RULE IT ENFORCES: a failure that changed what the product could conclude must never be
/// silent. A bare `catch { return nil }` on a measurement path is how a result goes missing with
/// nobody told — the same defect class as rendering a result before evidence, one layer down.

/// Severity of a diagnostic event.
public enum DiagnosticSeverity: String, Sendable {
    case warning, error, fatal
}

/// What this failure did to the measurement. An error is only interesting because of this.
/// Each case names the `SectionStatus` the adapter is expected to assign, so a mismatch between
/// what we report and what the engine records is itself detectable.
public enum MeasurementImpact: String, Sendable {
    /// Logged; the measurement is unaffected. Warning-level, never an issue.
    case none
    case degradedSection        // → SectionStatus.degraded
    case inconclusiveSection    // → SectionStatus.inconclusive
    case invalidatedSection     // → SectionStatus.invalidated
    case untestedSection        // → SectionStatus.untested
    case noDetermination        // → Conclusion.determination == nil
}

/// One diagnostic occurrence. Construct at the failure site, where the cause is known.
public struct DiagnosticEvent: Sendable {
    public let errorClass: String
    public let component: String
    public let section: Section?
    public let impact: MeasurementImpact
    public let severity: DiagnosticSeverity
    /// Error description. MUST NOT contain a hostname, an IP, a license key, or a path under the
    /// user's home directory — this product's users are people whose DNS is being surveilled, and
    /// their query names are the last thing that should leave the machine. Callers redact; the
    /// sink does not rescue them.
    public let underlying: String?
    /// Stable grouping key. Use closed-set discriminants (an error *variant*), never interpolated
    /// text — an interpolated id shards one bug into hundreds of issues.
    public let fingerprint: [String]
    public let tags: [String: String]
    public let extra: [String: String]

    public init(errorClass: String,
                component: String,
                section: Section? = nil,
                impact: MeasurementImpact,
                severity: DiagnosticSeverity,
                underlying: String? = nil,
                fingerprint: [String],
                tags: [String: String] = [:],
                extra: [String: String] = [:]) {
        self.errorClass = errorClass
        self.component = component
        self.section = section
        self.impact = impact
        self.severity = severity
        self.underlying = underlying
        self.fingerprint = fingerprint
        self.tags = tags
        self.extra = extra
    }
}

/// Implemented in the app layer, where a vendor SDK is linked. Absence of a sink is the OFF state.
public protocol DiagnosticSink: Sendable {
    func capture(_ event: DiagnosticEvent)
    func breadcrumb(category: String, message: String,
                    level: DiagnosticSeverity, data: [String: String])
    /// Drop everything not yet transmitted WITHOUT sending it. Called on consent withdrawal.
    /// Flushing here would transmit exactly the data the user just refused.
    func discardBuffered()
}

/// Per-run attribution ledger: which sections reported which failures during this scan.
/// Read once at synthesis to detect a degraded section that nobody explained.
public final class RunContext: @unchecked Sendable {
    public let id: String
    private let lock = NSLock()
    private var bySection: [Section: Set<String>] = [:]

    public init(id: String = UUID().uuidString) { self.id = id }

    func record(section: Section?, errorClass: String) {
        guard let section else { return }
        lock.lock(); defer { lock.unlock() }
        bySection[section, default: []].insert(errorClass)
    }

    public func errorClasses(for section: Section) -> Set<String> {
        lock.lock(); defer { lock.unlock() }
        return bySection[section] ?? []
    }

    public var allErrorClasses: Set<String> {
        lock.lock(); defer { lock.unlock() }
        return bySection.values.reduce(into: Set<String>()) { $0.formUnion($1) }
    }
}

public enum SyxxDiagnostics {
    /// Ambient run id, flowing across async boundaries so a failure deep in a probe can be joined
    /// to the result it produced.
    @TaskLocal public static var run: RunContext?

    private static let lock = NSLock()
    nonisolated(unsafe) private static var sink: DiagnosticSink?

    /// Install the platform sink. Call ONLY when consent has been affirmatively granted.
    /// Whether a sink is currently installed.
    ///
    /// Exists so surfaces can report the OBSERVED state instead of the toggle position. A settings
    /// screen that says "telemetry enabled" because a checkbox is ticked — while no sink is
    /// installed and nothing is being sent — is the same class of falsehood as a green result
    /// with no evidence behind it, and it is how the current inert toggle came to ship.
    public static var isReporting: Bool {
        lock.lock(); defer { lock.unlock() }
        return sink != nil
    }

    public static func install(_ newSink: DiagnosticSink) {
        lock.lock(); sink = newSink; lock.unlock()
    }

    /// Detach and discard. Order matters: clear the sink under the lock FIRST so a capture racing
    /// in from a scan thread finds nil, THEN discard whatever the vendor buffered.
    public static func uninstall() {
        lock.lock(); let old = sink; sink = nil; lock.unlock()
        old?.discardBuffered()
    }

    /// True when a sink is installed. For honest UI: report observed state, never the toggle.
    public static var isActive: Bool {
        lock.lock(); defer { lock.unlock() }
        return sink != nil
    }

    public static func capture(_ event: DiagnosticEvent) {
        // Record attribution even with no sink: the unattributed-degradation check is a local
        // honesty invariant and must hold whether or not the user shares telemetry.
        run?.record(section: event.section, errorClass: event.errorClass)
        lock.lock(); let current = sink; lock.unlock()
        current?.capture(event)
    }

    public static func breadcrumb(category: String, message: String,
                                  level: DiagnosticSeverity = .warning,
                                  data: [String: String] = [:]) {
        lock.lock(); let current = sink; lock.unlock()
        current?.breadcrumb(category: category, message: message, level: level, data: data)
    }
}

/// Telemetry redaction. ONE list, applied to every channel — spans, breadcrumbs, and Sentry
/// events alike. If it lived in only one place the leak would simply move channels.
///
/// THE RULE: a DNS query stream IS a browsing history. Syxx's users are people whose DNS is being
/// surveilled; exporting the names they look up to any endpoint — ours included — would make this
/// product a second surveillance channel. Domains are therefore never emitted verbatim unless they
/// are Syxx's OWN constants, which carry no user information.
public enum TelemetryRedaction {
    /// Domains Syxx itself chose. Safe to name exactly: they say nothing about the user.
    /// Canary/probe constants are added here as they are introduced.
    static let syxxOwnedDomains: Set<String> = ["github.com"]

    /// Classification of a queried name, safe to export in place of the name itself.
    public enum TargetClass: String, Sendable {
        case syxxOwned      // a Syxx constant (default target, canary) — no user information
        case userSupplied   // the user's own lookup — NEVER emitted verbatim
    }

    public static func classify(domain: String) -> TargetClass {
        syxxOwnedDomains.contains(domain.lowercased()) ? .syxxOwned : .userSupplied
    }

    /// Attributes describing a queried name WITHOUT disclosing it. Shape is enough to diagnose
    /// malformed-name and long-name failures; the name itself is not.
    public static func attributes(forDomain domain: String) -> [String: String] {
        let cls = classify(domain: domain)
        var out: [String: String] = ["dns.target_class": cls.rawValue]
        if cls == .syxxOwned {
            out["dns.target"] = domain          // our own constant — safe verbatim
        } else {
            out["dns.name.labels"] = String(domain.split(separator: ".").count)
            out["dns.name.length"] = String(domain.count)
            out["dns.name.is_idn"] = String(domain.contains("xn--") || !domain.allSatisfy(\.isASCII))
        }
        return out
    }

    /// Resolver addresses: public resolvers are a meaningful, non-identifying dimension; a user's
    /// ISP or corporate resolver is identifying and is reduced to a class.
    static let knownPublicResolvers: Set<String> = [
        "8.8.8.8", "8.8.4.4", "1.1.1.1", "1.0.0.1", "9.9.9.9", "149.112.112.112",
        "208.67.222.222", "208.67.220.220", "94.140.14.14", "76.76.2.0"
    ]

    /// `dns.server` verbatim only when it is a known public resolver; otherwise a class token.
    public static func attributes(forResolver address: String) -> [String: String] {
        if knownPublicResolvers.contains(address) {
            return ["dns.server": address, "dns.server_class": "public"]
        }
        let cls: String
        if address.hasPrefix("127.") || address == "::1" { cls = "loopback" }
        else if address.hasPrefix("10.") || address.hasPrefix("192.168.")
                 || address.hasPrefix("172.16.") || address.hasPrefix("169.254.") { cls = "private" }
        else { cls = "unknown" }
        return ["dns.server_class": cls]
    }
}

/// The single consent gate for ALL telemetry channels — Sentry, OTel, and breadcrumbs.
///
/// WHY IT IS TRI-STATE: `telemetryEnabled` is a `Bool` that defaults to `true`, so "no answer"
/// and "yes" were the same value. A user who opted out and then had their settings file
/// truncated was silently opted back in. `unknown` exists so that missing data can behave as
/// `denied` — the same rule the result engine applies to evidence, applied to consent.
public enum TelemetryConsent: String, Sendable {
    case granted   // user affirmatively enabled
    case denied    // user affirmatively disabled
    case unknown   // never asked, OR settings exist but will not decode

    /// The only question callers should ask. `unknown` is NOT consent.
    public var allowsTelemetry: Bool { self == .granted }
}

public enum TelemetryGate {
    /// Resolve consent from persisted settings and the environment override.
    ///
    /// `SYXX_TELEMETRY=0` always wins: a CI run or a scripted invocation must be able to
    /// guarantee silence without touching the user's settings file.
    public static func currentConsent(
        settings: SyxxSettings = SyxxSettingsStore.load(),
        environment: [String: String] = ProcessInfo.processInfo.environment
    ) -> TelemetryConsent {
        if environment["SYXX_TELEMETRY"] == "0" { return .denied }
        return settings.telemetryEnabled ? .granted : .denied
    }

    /// Whether any exporter may be registered right now.
    public static var isAllowed: Bool { currentConsent().allowsTelemetry }
}
