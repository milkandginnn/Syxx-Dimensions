import Foundation

// ROADMAP Workstream H · datapoint A9 — answer stability over repeated queries.
//
// A single-shot lookup can't tell a genuinely unstable/poisoned answer from ordinary
// round-robin load-balancing, or spot a resolver that ignores TTLs. Repeating the query a
// few times exposes all three cheaply:
//   • identical answer set every time, TTL not rising  → stable (healthy).
//   • answer sets differ but share members             → rotating (benign round-robin / LB).
//   • answer sets change with NO shared member         → unstable (anomaly — corroborate).
//   • identical set but TTL rises across samples       → ttlAnomaly (cache/rewrite oddity).
//
// v1 limit: rapid successive queries can legitimately return an equal TTL (same wall-clock
// second), so only a TTL *increase* is flagged — detecting a never-decrementing fixed TTL
// would require samples spread over real time, a future refinement.

public struct AnswerStability: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case stable
        case rotating
        case unstable
        case ttlAnomaly
        case inconclusive
        case untested
    }

    public let status: Status
    public let sampleCount: Int          // total queries attempted
    public let usableCount: Int          // responses that carried a non-empty answer set
    public let distinctAnswerSets: Int
    public let ttlHonored: Bool          // min-TTL non-increasing across usable samples
    public let detail: String

    public init(status: Status, sampleCount: Int = 0, usableCount: Int = 0,
                distinctAnswerSets: Int = 0, ttlHonored: Bool = true, detail: String = "") {
        self.status = status
        self.sampleCount = sampleCount
        self.usableCount = usableCount
        self.distinctAnswerSets = distinctAnswerSets
        self.ttlHonored = ttlHonored
        self.detail = detail
    }

    public static let untested = AnswerStability(status: .untested, detail: "not run")
}

public enum AnswerStabilityProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse

    /// Query `domain` `samples` times through `server` and classify the answer's stability.
    public static func probe(domain: String,
                             qtype: UInt16 = 1,
                             server: String = "1.1.1.1",
                             samples: Int = 3,
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> AnswerStability {
        let count = max(2, min(samples, 8))
        var sets: [Set<String>] = []
        var minTTLs: [UInt32] = []
        for _ in 0..<count {
            let resp = await query(DNSQuery(name: domain, type: qtype, server: server, port: 53))
            guard resp.status == .success else { continue }
            let addrs = resp.records.filter { $0.type == qtype }
            let set = Set(addrs.map { $0.rdata.trimmingCharacters(in: .whitespaces).lowercased() })
            guard !set.isEmpty else { continue }
            sets.append(set)
            minTTLs.append(addrs.map { $0.ttl }.min() ?? 0)
        }
        return classify(sets: sets, minTTLs: minTTLs, attempted: count)
    }

    static func classify(sets: [Set<String>], minTTLs: [UInt32], attempted: Int) -> AnswerStability {
        func make(_ s: AnswerStability.Status, ttlHonored: Bool, _ d: String) -> AnswerStability {
            AnswerStability(status: s, sampleCount: attempted, usableCount: sets.count,
                            distinctAnswerSets: Set(sets.map { $0.sorted().joined(separator: ",") }).count,
                            ttlHonored: ttlHonored, detail: d)
        }
        guard sets.count >= 2 else {
            return make(.inconclusive, ttlHonored: true,
                        "only \(sets.count) usable answer(s) in \(attempted) samples — cannot assess stability")
        }
        // B078: require every adjacent pair to be non-increasing. Comparing only the endpoints
        // (last <= first) misses an impossible intermediate rise (e.g. 100, 500, 90).
        let ttlHonored: Bool = {
            guard minTTLs.count >= 2 else { return true }
            for i in 1..<minTTLs.count {
                if minTTLs[i] > minTTLs[i - 1] { return false }
            }
            return true
        }()
        let distinct = Set(sets.map { $0.sorted().joined(separator: ",") })

        if distinct.count == 1 {
            return ttlHonored
                ? make(.stable, ttlHonored: true, "identical answer set across \(sets.count) samples; TTL honored")
                : make(.ttlAnomaly, ttlHonored: false, "answer set stable but TTL rose across samples — cache/rewrite anomaly")
        }
        // Sets differ — shared membership means benign rotation; a fully changing set is an anomaly.
        let common = sets.dropFirst().reduce(sets[0]) { $0.intersection($1) }
        if !common.isEmpty {
            return make(.rotating, ttlHonored: ttlHonored,
                        "answer sets vary but share \(common.count) address(es) — round-robin / load-balancing")
        }
        return make(.unstable, ttlHonored: ttlHonored,
                    "answer sets change with no shared address across samples — disjoint rotation")
    }
}
