import XCTest
@testable import SyxxKit

/// Offline tests for the cross-vantage TTL anomaly datapoint (Workstream H · F4).
final class TTLAnomalyProbeTests: XCTestCase {

    func testIndependentCachingWhenTTLsVary() {
        let r = TTLAnomalyProbe.classify(ttlsByResolver: ["1.1.1.1": 287, "8.8.8.8": 254, "9.9.9.9": 271])
        XCTAssertEqual(r.status, .independentCaching)
    }

    func testLockstepWhenThreeResolversAgreeExactly() {
        let r = TTLAnomalyProbe.classify(ttlsByResolver: ["1.1.1.1": 300, "8.8.8.8": 300, "9.9.9.9": 300])
        XCTAssertEqual(r.status, .lockstepTTL)
    }

    func testTwoIdenticalIsNotLockstep() {
        // Two coinciding is plausible; lockstep needs >= 3.
        let r = TTLAnomalyProbe.classify(ttlsByResolver: ["1.1.1.1": 300, "8.8.8.8": 300])
        XCTAssertEqual(r.status, .independentCaching)
    }

    func testExtremeTTLFlagged() {
        let r = TTLAnomalyProbe.classify(ttlsByResolver: ["1.1.1.1": 300, "8.8.8.8": 700000])  // > 7 days
        XCTAssertEqual(r.status, .extremeTTL)
    }

    func testInconclusiveWithOneAnswer() {
        XCTAssertEqual(TTLAnomalyProbe.classify(ttlsByResolver: ["1.1.1.1": 300]).status, .inconclusive)
        XCTAssertEqual(TTLAnomalyProbe.classify(ttlsByResolver: [:]).status, .inconclusive)
    }

    func testZeroTTLsAreNotCountedAsLockstep() {
        // All-zero (round-robin/dynamic) is common and not an injection signal.
        let r = TTLAnomalyProbe.classify(ttlsByResolver: ["1.1.1.1": 0, "8.8.8.8": 0, "9.9.9.9": 0])
        XCTAssertEqual(r.status, .independentCaching)
    }

    func testProbeViaMock() async {
        // Every resolver returns the same TTL → lockstep end to end.
        let query: TTLAnomalyProbe.QueryFn = { q in
            DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 1, ttl: 120, rdata: "1.2.3.4")])
        }
        let r = await TTLAnomalyProbe.probe(domain: "example.com", query: query)
        XCTAssertEqual(r.status, .lockstepTTL)
        XCTAssertEqual(r.ttlsByResolver.count, 3)
    }
}
