import XCTest
@testable import SyxxKit

/// Offline tests for the TXID/source-port entropy datapoint (Workstream H · F3). It measures LOCAL
/// stub/OS randomization quality (spoofability posture) — never upstream cache poisoning.
final class StubEntropyProbeTests: XCTestCase {

    private func queries(count: Int, xid: (Int) -> UInt16, port: (Int) -> UInt16) -> [(xid: UInt16, sourcePort: UInt16)] {
        var q: [(xid: UInt16, sourcePort: UInt16)] = []
        for i in 0..<count { q.append((xid(i), port(i))) }
        return q
    }

    func testWellRandomized() {
        let q = queries(count: 12, xid: { UInt16($0 * 5000 + 123) }, port: { UInt16(40000 + $0 * 137) })
        XCTAssertEqual(StubEntropyProbe.audit(outboundQueries: q).status, .wellRandomized)
    }

    func testFixedSourcePortIsWeak() {
        let q = queries(count: 12, xid: { UInt16($0 * 4013 + 7) }, port: { _ in UInt16(53535) })
        let a = StubEntropyProbe.audit(outboundQueries: q)
        XCTAssertEqual(a.status, .weakRandomization)
        XCTAssertEqual(a.distinctSourcePorts, 1)
    }

    func testSequentialTransactionIDsAreWeak() {
        let q = queries(count: 12, xid: { UInt16(1000 + $0) }, port: { UInt16(40000 + $0 * 200) })
        XCTAssertEqual(StubEntropyProbe.audit(outboundQueries: q).status, .weakRandomization)
    }

    func testLowTransactionIDEntropyIsWeak() {
        let q = queries(count: 12, xid: { UInt16($0 % 2 == 0 ? 4242 : 4243) }, port: { UInt16(40000 + $0 * 211) })
        XCTAssertEqual(StubEntropyProbe.audit(outboundQueries: q).status, .weakRandomization)
    }

    func testInsufficientDataIsNeverClean() {
        let q = queries(count: 4, xid: { UInt16($0 * 5000 + 1) }, port: { UInt16(40000 + $0 * 300) })
        XCTAssertEqual(StubEntropyProbe.audit(outboundQueries: q).status, .insufficientData)
    }

    func testSequentialDetector() {
        XCTAssertTrue(StubEntropyProbe.isMostlySequential([10, 11, 12, 13, 14]))
        XCTAssertFalse(StubEntropyProbe.isMostlySequential([10, 900, 4000, 22222, 51000]))
    }
}
