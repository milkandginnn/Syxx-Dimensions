import XCTest
@testable import SyxxKit

/// Offline tests for the answer-stability datapoint (Workstream H · A9). classify() is pure,
/// and the probe() path is driven by a Sendable mock, so these run with no network.
final class AnswerStabilityProbeTests: XCTestCase {

    private func set(_ xs: String...) -> Set<String> { Set(xs) }

    func testStableWhenIdenticalSetsAndTTLHonored() {
        let r = AnswerStabilityProbe.classify(
            sets: [set("1.2.3.4", "5.6.7.8"), set("1.2.3.4", "5.6.7.8"), set("1.2.3.4", "5.6.7.8")],
            minTTLs: [300, 298, 295], attempted: 3)
        XCTAssertEqual(r.status, .stable)
        XCTAssertEqual(r.distinctAnswerSets, 1)
        XCTAssertTrue(r.ttlHonored)
    }

    func testTTLAnomalyWhenSetStableButTTLRises() {
        let r = AnswerStabilityProbe.classify(
            sets: [set("1.2.3.4"), set("1.2.3.4"), set("1.2.3.4")],
            minTTLs: [300, 300, 400], attempted: 3)
        XCTAssertEqual(r.status, .ttlAnomaly)
        XCTAssertFalse(r.ttlHonored)
    }

    func testRotatingWhenSetsShareAMember() {
        let r = AnswerStabilityProbe.classify(
            sets: [set("a", "b"), set("a", "c"), set("a", "d")],
            minTTLs: [60, 60, 60], attempted: 3)
        XCTAssertEqual(r.status, .rotating)
    }

    func testUnstableWhenSetsFullyChange() {
        let r = AnswerStabilityProbe.classify(
            sets: [set("a"), set("b"), set("c")],
            minTTLs: [60, 60, 60], attempted: 3)
        XCTAssertEqual(r.status, .unstable)
    }

    func testInconclusiveWithFewerThanTwoUsable() {
        let r = AnswerStabilityProbe.classify(sets: [set("a")], minTTLs: [60], attempted: 3)
        XCTAssertEqual(r.status, .inconclusive)
    }

    func testProbeStableViaMock() async {
        // Every query returns the same two-address set with decreasing TTL.
        let query: AnswerStabilityProbe.QueryFn = { q in
            DNSResponse(status: .success, records: [
                DNSRecord(name: q.name, type: 1, ttl: 120, rdata: "1.2.3.4"),
                DNSRecord(name: q.name, type: 1, ttl: 120, rdata: "5.6.7.8"),
            ])
        }
        let r = await AnswerStabilityProbe.probe(domain: "example.com", samples: 3, query: query)
        XCTAssertEqual(r.status, .stable)
        XCTAssertEqual(r.usableCount, 3)
    }

    func testProbeInconclusiveWhenLookupsFail() async {
        let query: AnswerStabilityProbe.QueryFn = { _ in DNSResponse(status: .timeout) }
        let r = await AnswerStabilityProbe.probe(domain: "example.com", samples: 3, query: query)
        XCTAssertEqual(r.status, .inconclusive)
        XCTAssertEqual(r.usableCount, 0)
    }
}
