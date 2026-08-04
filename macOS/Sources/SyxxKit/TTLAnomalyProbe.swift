import Foundation

// ROADMAP Workstream H · datapoint F4 — cross-vantage TTL anomaly detection.
//
// TTLs are parsed everywhere but never compared across resolvers. Independent recursive resolvers
// that each cache a record hold DIFFERENT remaining TTLs (their caches filled at different instants),
// so the healthy signature is *variance*. This queries the same name at several independent resolvers
// and flags the anomalies:
//   • TTLs vary across resolvers                       → independentCaching (healthy).
//   • ≥3 independent resolvers return the EXACT same   → lockstepTTL: they are not caching
//     non-zero TTL                                        independently — a shared upstream, or an
//                                                          on-path device injecting one answer to all.
//   • a TTL is implausibly large (> 7 days)            → extremeTTL: a pinned/stale answer (replay).
//
// Honest v1 limits: lockstep is only flagged at ≥3 resolvers (two coinciding is plausible) and is a
// WEAK corroboration — a freshly-propagated record queried the instant every cache refreshed can
// briefly align. It never decides alone. A9 (AnswerStability) covers TTL behaviour *within* one
// resolver; this is the *across-resolver* complement.

public struct TTLAnomaly: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case independentCaching   // TTLs vary across resolvers — normal independent caching (benign)
        case lockstepTTL          // ≥3 resolvers return the identical non-zero TTL — shared upstream / injection
        case extremeTTL           // a TTL is implausibly large (pinned stale answer)
        case inconclusive         // fewer than two usable answers
        case untested
    }

    public let status: Status
    public let ttlsByResolver: [String: UInt32]
    public let detail: String

    public init(status: Status, ttlsByResolver: [String: UInt32] = [:], detail: String = "") {
        self.status = status
        self.ttlsByResolver = ttlsByResolver
        self.detail = detail
    }

    public static let untested = TTLAnomaly(status: .untested, detail: "not run")
}

public enum TTLAnomalyProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse
    public static let typeA: UInt16 = 1
    static let extremeSeconds: UInt32 = 604800   // 7 days — implausible for a routed A record

    /// Query `domain` at several independent resolvers and compare the answer's min-TTL across them.
    public static func probe(domain: String,
                             qtype: UInt16 = typeA,
                             resolvers: [String] = ["1.1.1.1", "8.8.8.8", "9.9.9.9"],
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> TTLAnomaly {
        var ttls: [String: UInt32] = [:]
        for server in resolvers {
            let resp = await query(DNSQuery(name: domain, type: qtype, server: server, port: 53))
            guard resp.status == .success else { continue }
            let answer = resp.records.filter { $0.type == qtype }
            guard let minTTL = answer.map({ $0.ttl }).min() else { continue }
            ttls[server] = minTTL
        }
        return classify(ttlsByResolver: ttls)
    }

    static func classify(ttlsByResolver: [String: UInt32]) -> TTLAnomaly {
        func make(_ s: TTLAnomaly.Status, _ d: String) -> TTLAnomaly {
            TTLAnomaly(status: s, ttlsByResolver: ttlsByResolver, detail: d)
        }
        let values = Array(ttlsByResolver.values)
        guard values.count >= 2 else {
            return make(.inconclusive, "only \(values.count) resolver(s) answered — cannot compare TTLs across vantages")
        }
        if let maxTTL = values.max(), maxTTL > extremeSeconds {
            return make(.extremeTTL, "a resolver reports TTL \(maxTTL)s (> 7 days) — implausibly pinned answer; possible stale/replay")
        }
        // B076: all-zero or all-equal-zero populations are not "varying" — branch to a
        // distinct inconclusive state with honest text, never "TTLs vary".
        let uniqueValues = Set(values)
        if uniqueValues.count == 1 {
            let only = values[0]
            if only == 0 {
                return make(.inconclusive, "all \(values.count) resolvers report TTL 0s — lockstep zero TTL (possible injection or CDN lockstep); cannot assess caching independence")
            } else if values.count < 3 {
                // B077: equal-but-underpowered — not enough samples for lockstep detection.
                return make(.inconclusive, "only \(values.count) resolver(s) report the same TTL \(only)s — insufficient samples to distinguish lockstep from independent caching")
            }
        }
        // Lockstep: 3+ resolvers agreeing on the exact same non-zero TTL is not independent caching.
        let nonZero = values.filter { $0 > 0 }
        if nonZero.count >= 3 && Set(nonZero).count == 1 {
            return make(.lockstepTTL, "\(nonZero.count) independent resolvers report the identical TTL \(nonZero[0])s — not independent caching (shared upstream or on-path injection); corroborate")
        }
        return make(.independentCaching, "TTLs vary across resolvers (\(values.sorted().map(String.init).joined(separator: ", "))s) — normal independent caching")
    }
}
