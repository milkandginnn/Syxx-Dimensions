import Foundation

// ROADMAP Workstream H · datapoint A1 — per-query resolver latency (RTT).
//
// Resolution is otherwise recorded as a boolean (success / no-data / timeout) with no timing, so
// "DNS is slow" is invisible. Repeating the query and timing each round-trip separates a degraded
// resolver from a healthy one, and — because a query that never answers is excluded — surfaces
// intermittent packet loss as a `degraded` result rather than a clean "fast".
//
// Additive: wraps the committed `DNSResolver.queryUDPDetailed`; no edits to the resolver.

public struct ResolverLatency: Codable, Sendable, Equatable {
    public enum Status: String, Codable, Sendable {
        case fast          // median < 50 ms
        case moderate      // median < 150 ms
        case slow          // median >= 150 ms
        case degraded      // some queries answered, some timed out (packet loss / flaky path)
        case unreachable   // no query answered at all
        case untested
    }

    public let status: Status
    public let sampleCount: Int      // queries attempted
    public let successCount: Int     // queries that got any response (incl. NODATA/NXDOMAIN)
    public let minMs: Double
    public let medianMs: Double
    public let maxMs: Double
    public let detail: String

    public init(status: Status, sampleCount: Int = 0, successCount: Int = 0,
                minMs: Double = 0, medianMs: Double = 0, maxMs: Double = 0, detail: String = "") {
        self.status = status
        self.sampleCount = sampleCount
        self.successCount = successCount
        self.minMs = minMs
        self.medianMs = medianMs
        self.maxMs = maxMs
        self.detail = detail
    }

    public static let untested = ResolverLatency(status: .untested, detail: "not run")
}

public enum ResolverLatencyProbe {
    public typealias QueryFn = @Sendable (DNSQuery) async -> DNSResponse

    /// Query `server` `samples` times, timing each round-trip that gets a response.
    public static func probe(domain: String,
                             qtype: UInt16 = 1,
                             server: String = "1.1.1.1",
                             samples: Int = 4,
                             query: QueryFn = { await DNSResolver.queryUDPDetailed($0) }) async -> ResolverLatency {
        let count = max(1, min(samples, 10))
        var latenciesMs: [Double] = []
        for _ in 0..<count {
            let t0 = DispatchTime.now().uptimeNanoseconds
            let resp = await query(DNSQuery(name: domain, type: qtype, server: server, port: 53))
            let t1 = DispatchTime.now().uptimeNanoseconds
            // A response (even NODATA/NXDOMAIN) means the resolver answered → a real RTT sample.
            // A timeout/transport failure is NOT a latency sample; it counts toward "degraded".
            // C070: a retried query (attemptCount > 1) includes a ~2 s timeout delay and must
            // not be treated as a clean RTT sample — count it as a lost query instead.
            if resp.attemptCount > 1 {
                // Retried: the elapsed time includes a transport timeout, not a real RTT.
                continue
            }
            switch resp.status {
            // C069: .rcodeOther is a real resolver answer (FORMERR, NOTIMP, BADVERS, etc.) —
            // the resolver DID respond, so it is a valid RTT sample.
            case .success, .noData, .nxDomain, .servFail, .refused, .rcodeOther:
                latenciesMs.append(Double(t1 &- t0) / 1_000_000.0)
            default:
                break
            }
        }
        return classify(latenciesMs: latenciesMs, attempted: count)
    }

    static func classify(latenciesMs: [Double], attempted: Int) -> ResolverLatency {
        guard !latenciesMs.isEmpty else {
            return ResolverLatency(status: .unreachable, sampleCount: attempted, successCount: 0,
                                   detail: "no response in \(attempted) queries — resolver unreachable")
        }
        let sorted = latenciesMs.sorted()
        let median = sorted[sorted.count / 2]
        let minMs = sorted.first ?? 0
        let maxMs = sorted.last ?? 0
        let success = latenciesMs.count
        let degraded = success < attempted

        let byMedian: ResolverLatency.Status = median < 50 ? .fast : (median < 150 ? .moderate : .slow)
        let status: ResolverLatency.Status = degraded ? .degraded : byMedian
        let detail = degraded
            ? "\(success)/\(attempted) queries answered (\(attempted - success) lost); median \(rounded(median)) ms — degraded/flaky path"
            : "median \(rounded(median)) ms (min \(rounded(minMs)) / max \(rounded(maxMs))) over \(success) samples"

        return ResolverLatency(status: status, sampleCount: attempted, successCount: success,
                               minMs: minMs, medianMs: median, maxMs: maxMs, detail: detail)
    }

    private static func rounded(_ ms: Double) -> Double { (ms * 10).rounded() / 10 }
}
