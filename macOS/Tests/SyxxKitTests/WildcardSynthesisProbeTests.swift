import XCTest
@testable import SyxxKit

/// Offline tests for the cross-vantage NXDOMAIN-hijack / wildcard-synthesis datapoint (H · F5).
final class WildcardSynthesisProbeTests: XCTestCase {

    private func obs(label: String = "x.example.com", test: [String], testHas: Bool, refHas: Bool, refDenies: Bool)
        -> WildcardSynthesisProbe.LabelObservation {
        .init(label: label, testAddrs: test, testHasAnswer: testHas, refHasAnswer: refHas, refDenies: refDenies)
    }

    func testNxHijackWhenReferenceDeniesButTestInvents() {
        // Trusted resolver: NXDOMAIN. Resolver-under-test: hands back a block-page IP.
        let r = WildcardSynthesisProbe.classify([
            obs(test: ["10.0.0.1"], testHas: true, refHas: false, refDenies: true),
        ])
        XCTAssertEqual(r.status, .nxHijack)
        XCTAssertEqual(r.syntheticAddresses, ["10.0.0.1"])
    }

    func testWildcardZoneWhenBothResolve() {
        let r = WildcardSynthesisProbe.classify([
            obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),
            obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),
        ])
        XCTAssertEqual(r.status, .wildcardZone)
        XCTAssertTrue(r.syntheticAddresses.isEmpty)   // resolving at both vantages is benign, not synthetic
    }

    func testNoSynthesisWhenBothDeny() {
        let r = WildcardSynthesisProbe.classify([
            obs(test: [], testHas: false, refHas: false, refDenies: true),
            obs(test: [], testHas: false, refHas: false, refDenies: true),
        ])
        XCTAssertEqual(r.status, .noSynthesis)
    }

    func testInconclusiveWhenNoUsableLabels() {
        XCTAssertEqual(WildcardSynthesisProbe.classify([]).status, .inconclusive)
    }

    func testInconclusiveOnMixedOutcomes() {
        let r = WildcardSynthesisProbe.classify([
            obs(test: [], testHas: false, refHas: false, refDenies: true),   // both deny
            obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),  // both resolve
        ])
        XCTAssertEqual(r.status, .inconclusive)
    }

    func testAnySingleHijackWins() {
        // One honest label + one hijack label → the hijack is what matters.
        let r = WildcardSynthesisProbe.classify([
            obs(test: [], testHas: false, refHas: false, refDenies: true),
            obs(test: ["10.0.0.1"], testHas: true, refHas: false, refDenies: true),
        ])
        XCTAssertEqual(r.status, .nxHijack)
    }

    // probe() via a Sendable mock — reference denies, test synthesizes → hijack end to end.
    func testProbeDetectsHijackViaMock() async {
        let query: WildcardSynthesisProbe.QueryFn = { q in
            if q.server == "1.1.1.1" { return DNSResponse(status: .nxDomain) }   // trusted reference: does not exist
            return DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 1, ttl: 30, rdata: "10.0.0.1")])
        }
        let r = await WildcardSynthesisProbe.probe(domain: "example.com", resolverUnderTest: "192.168.1.1", count: 2, query: query)
        XCTAssertEqual(r.status, .nxHijack)
        XCTAssertEqual(r.syntheticAddresses, ["10.0.0.1"])
    }

    // A flaky reference (timeout) must yield inconclusive, never a false hijack.
    func testProbeInconclusiveWhenReferenceUnreachable() async {
        let query: WildcardSynthesisProbe.QueryFn = { q in
            if q.server == "1.1.1.1" { return DNSResponse(status: .timeout) }    // reference unusable
            return DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 1, ttl: 30, rdata: "10.0.0.1")])
        }
        let r = await WildcardSynthesisProbe.probe(domain: "example.com", resolverUnderTest: "192.168.1.1", count: 2, query: query)
        XCTAssertEqual(r.status, .inconclusive)
    }
}
