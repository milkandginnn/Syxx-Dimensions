import Foundation

// ROADMAP Portal · Open Resolver & Amplification Probe.
//
// Flags a resolver that recursively answers for names it isn't authoritative for (acts as an open
// recursive resolver) and measures its amplification factor — response bytes ÷ query bytes — the shape
// that makes a resolver abusable as a DDoS reflector. Single-shot, no hammering.
//
// SCOPE (enforced by the caller, never here): only resolvers the user owns or is authorized to assess —
// the configured system resolver and devices found by LAN cartography. This module never selects targets.
//
// HONESTY CAVEAT (baked into the wording): from the client's position we can only confirm the target is a
// recursive resolver reachable *from us* (on the LAN) and how much it amplifies. We CANNOT confirm it is
// reachable from the *internet* — the condition that actually makes it an "open resolver" / reflector.
// That requires the Dimension outside-in check (Workstream S). So this is an ADVISORY / exposure finding,
// never a decisive result, and its text says so.

public struct OpenResolverFinding: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case notRecursive     // does not recursively resolve external names for us — not an open-resolver concern
        case recursiveLowAmp  // recursive, amplification factor below the exposure threshold (advisory)
        case recursiveHighAmp // recursive + high amplification factor — reflector-shaped exposure (advisory)
        case untested         // could not complete the probe (never rendered clean)
    }

    public let status: Status
    public let target: String
    public let recursive: Bool
    public let amplificationFactor: Double   // response bytes ÷ query bytes for the amplification probe (0 if unmeasured)
    public let detail: String

    public init(status: Status, target: String = "", recursive: Bool = false,
                amplificationFactor: Double = 0, detail: String = "") {
        self.status = status
        self.target = target
        self.recursive = recursive
        self.amplificationFactor = amplificationFactor
        self.detail = detail
    }

    public static let untested = OpenResolverFinding(status: .untested, detail: "not run")
}

public enum OpenResolverProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeA: UInt16 = 1
    public static let typeANY: UInt16 = 255
    public static let typeDNSKEY: UInt16 = 48
    /// Amplification factor at or above which the exposure is called out prominently.
    public static let highAmpThreshold = 10.0
    /// An external name the target can't be authoritative for — resolving it proves recursive behaviour.
    public static let recursionProbeDomain = "example.com"
    /// A benchmark name whose DNSKEY/ANY response is large — used to gauge the amplification factor.
    public static let amplificationDomain = "cloudflare.com"

    /// Probe `target` (a resolver the CALLER owns / is authorized to assess — scope is enforced upstream).
    /// Single-shot: one recursion query, then one amplification-shaped query only if it's recursive.
    public static func probe(target: String,
                             recursionDomain: String = recursionProbeDomain,
                             amplificationDomain: String = amplificationDomain,
                             ampType: UInt16 = typeDNSKEY,
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> OpenResolverFinding {
        let rec = await query(DNSQuery(name: recursionDomain, type: typeA, server: target))
        if rec.didFail {
            return OpenResolverFinding(status: .untested, target: target,
                detail: "target did not answer (\(rec.status.rawValue)) — could not assess; unmeasured, not clean")
        }
        // Recursive behaviour = it resolved an external name it cannot be authoritative for.
        let recursive = rec.status == .success && !rec.records.isEmpty
        if !recursive {
            return OpenResolverFinding(status: .notRecursive, target: target, recursive: false,
                detail: "target did not recursively resolve an external name for us — not acting as an open recursive resolver")
        }
        // C068: send the amplification probe with EDNS0 (4096 buffer) so the resolver can fit
        // the DNSKEY answer in a single UDP datagram. Without EDNS0, a >512 byte answer sets
        // TC, the internal retry transparently falls back to TCP, and the TCP response size
        // is reported as a "UDP amplification factor" — but TCP responses cannot be reflected.
        let edns0UDPSize: UInt16 = 4096
        let queryBytes = (try? DNSResolver.makeQueryPacketWithID(name: amplificationDomain, type: ampType, edns0UDPSize: edns0UDPSize).0)?.count
        let amp = await query(DNSQuery(name: amplificationDomain, type: ampType, server: target, edns0UDPSize: edns0UDPSize))
        if amp.didFail {
            return OpenResolverFinding(status: .untested, target: target, recursive: true,
                detail: "amplification probe failed (\(amp.status.rawValue)) — factor unmeasured, not clean")
        }
        // C068: if the answer still arrived via TCP fallback (indicated by isTruncated on a
        // response that went through the internal retry), the byte count reflects a TCP
        // message, not a reflectable UDP datagram — report as unmeasured.
        if amp.isTruncated {
            return OpenResolverFinding(status: .untested, target: target, recursive: true,
                detail: "amplification response arrived via TCP fallback (UDP was truncated even with EDNS0 \(edns0UDPSize)) — UDP amplification factor unmeasured")
        }
        // B080: make factor optional — a nil factor means the measurement failed (missing byte
        // count), not that amplification is zero. Return untested, never recursiveLowAmp with ~0.0x.
        guard let factor = amplificationFactor(queryBytes: queryBytes, response: amp) else {
            return OpenResolverFinding(status: .untested, target: target, recursive: true,
                detail: "amplification factor could not be measured (missing response byte count) — unmeasured, not clean")
        }
        return classify(target: target, factor: factor)
    }

    /// response bytes ÷ query bytes; nil when either size is unknown (e.g. an injected/mock response
    /// or missing byte count). B080: nil means measurement failure, not zero amplification.
    public static func amplificationFactor(queryBytes: Int?, response: DNSResponse) -> Double? {
        guard let q = queryBytes, q > 0, let r = response.responseByteLength, r > 0 else { return nil }
        return Double(r) / Double(q)
    }

    /// Pure classification over a measured amplification factor for a target already shown to be recursive.
    public static func classify(target: String, factor: Double) -> OpenResolverFinding {
        if factor >= highAmpThreshold {
            return OpenResolverFinding(status: .recursiveHighAmp, target: target, recursive: true, amplificationFactor: factor,
                detail: String(format: "acts as a recursive resolver with a ~%.0f× amplification factor. If it is also reachable from the internet — the condition that makes it an open resolver / DDoS reflector, confirmable only from outside via Dimension — it is an exposure risk.", factor))
        }
        return OpenResolverFinding(status: .recursiveLowAmp, target: target, recursive: true, amplificationFactor: factor,
            detail: String(format: "acts as a recursive resolver (amplification factor ~%.1f×). Whether it is reachable from the internet — what would make it an open resolver — needs the Dimension outside-in check to confirm.", factor))
    }
}
