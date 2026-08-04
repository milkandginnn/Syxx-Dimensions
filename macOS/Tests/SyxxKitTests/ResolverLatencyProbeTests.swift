import XCTest
@testable import SyxxKit

/// Offline tests for the resolver-latency datapoint (Workstream H · A1). classify() is a pure
/// function of latency samples; probe() is exercised via a Sendable mock for the count/status paths.
final class ResolverLatencyProbeTests: XCTestCase {

    func testFastWhenMedianLow() {
        let r = ResolverLatencyProbe.classify(latenciesMs: [8, 12, 10, 9], attempted: 4)
        XCTAssertEqual(r.status, .fast)
        XCTAssertEqual(r.successCount, 4)
    }

    func testModerateBand() {
        let r = ResolverLatencyProbe.classify(latenciesMs: [90, 120, 100], attempted: 3)
        XCTAssertEqual(r.status, .moderate)
    }

    func testSlowWhenMedianHigh() {
        let r = ResolverLatencyProbe.classify(latenciesMs: [200, 210, 190], attempted: 3)
        XCTAssertEqual(r.status, .slow)
    }

    /// Band boundaries: <50 fast, [50,150) moderate, >=150 slow. The in-band cases above can't catch
    /// an off-by-one flip of the `<` comparisons — these exact-threshold cases do.
    func testLatencyBandBoundaries() {
        XCTAssertEqual(ResolverLatencyProbe.classify(latenciesMs: [49.9], attempted: 1).status, .fast)
        XCTAssertEqual(ResolverLatencyProbe.classify(latenciesMs: [50.0], attempted: 1).status, .moderate,
            "50 ms is not < 50 — moderate, not fast")
        XCTAssertEqual(ResolverLatencyProbe.classify(latenciesMs: [149.9], attempted: 1).status, .moderate)
        XCTAssertEqual(ResolverLatencyProbe.classify(latenciesMs: [150.0], attempted: 1).status, .slow,
            "150 ms is >= 150 — slow, not moderate")
    }

    func testDegradedWhenSomeQueriesLost() {
        // 2 of 4 answered → degraded regardless of how fast the survivors were.
        let r = ResolverLatencyProbe.classify(latenciesMs: [10, 12], attempted: 4)
        XCTAssertEqual(r.status, .degraded)
        XCTAssertEqual(r.successCount, 2)
        XCTAssertEqual(r.sampleCount, 4)
    }

    func testUnreachableWhenNoResponses() {
        let r = ResolverLatencyProbe.classify(latenciesMs: [], attempted: 4)
        XCTAssertEqual(r.status, .unreachable)
        XCTAssertEqual(r.successCount, 0)
    }

    func testProbeCountsResponsesViaMock() async {
        // Instant mock responses → tiny RTTs → fast, and every sample counts as answered.
        let query: ResolverLatencyProbe.QueryFn = { q in
            DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 1, ttl: 60, rdata: "1.2.3.4")])
        }
        let r = await ResolverLatencyProbe.probe(domain: "example.com", samples: 5, query: query)
        XCTAssertEqual(r.successCount, 5)
        XCTAssertEqual(r.sampleCount, 5)
        XCTAssertNotEqual(r.status, .unreachable)
    }

    func testProbeUnreachableWhenAllTimeout() async {
        let query: ResolverLatencyProbe.QueryFn = { _ in DNSResponse(status: .timeout) }
        let r = await ResolverLatencyProbe.probe(domain: "example.com", samples: 3, query: query)
        XCTAssertEqual(r.status, .unreachable)
    }
}
