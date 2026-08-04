import XCTest
@testable import SyxxKit

/// Offline tests for the encrypted-transport datapoint (Workstream H · A8), including the B051
/// honesty fix: a failed DoH path is only `blocked` when a cleartext Do53 control PROVES the
/// network is up; otherwise (offline / provider down) it stays inconclusive, never a manufactured
/// "blocked". Mocks are injected so every case is deterministic and offline.
final class EncryptedTransportProbeTests: XCTestCase {

    /// A cleartext control that succeeds (network is provably up).
    private let controlUp: EncryptedTransportProbe.ControlQueryFn = { name, _ in
        DNSResponse(status: .success, records: [DNSRecord(name: name, type: 1, ttl: 300, rdata: "1.1.1.1")])
    }
    /// A cleartext control that also fails (host offline / no reachable Do53).
    private let controlDown: EncryptedTransportProbe.ControlQueryFn = { _, _ in DNSResponse(status: .timeout) }

    func testWorkingWhenDoHResolves() async {
        let query: EncryptedTransportProbe.DoHQueryFn = { name, _, _ in
            DNSResponse(status: .success, records: [DNSRecord(name: name, type: 1, ttl: 300, rdata: "104.16.1.1")])
        }
        // A healthy encrypted path resolves without ever consulting the control.
        let r = await EncryptedTransportProbe.probe(query: query, control: controlDown)
        XCTAssertEqual(r.status, .working)
    }

    // B051: DoH times out BUT the cleartext control succeeds → the network is up, so the encrypted
    // path is genuinely blocked/filtered. Mutation guard: revert classify to unconditional `.blocked`
    // and this still passes, but `testInconclusiveWhenDoHTimesOutAndControlAlsoDown` fails.
    func testBlockedWhenDoHTimesOutAndControlWorks() async {
        let query: EncryptedTransportProbe.DoHQueryFn = { _, _, _ in DNSResponse(status: .timeout) }
        let r = await EncryptedTransportProbe.probe(query: query, control: controlUp)
        XCTAssertEqual(r.status, .blocked)
        XCTAssertTrue(r.detail.contains("blocked") || r.detail.contains("unreachable"))
    }

    // B051 mutation-proven core: DoH fails AND the cleartext control also fails → OFFLINE, not blocked.
    // If classify ever regresses to mapping timeout/transportError straight to `.blocked`, this fails.
    func testInconclusiveWhenDoHTimesOutAndControlAlsoDown() async {
        let query: EncryptedTransportProbe.DoHQueryFn = { _, _, _ in DNSResponse(status: .timeout) }
        let r = await EncryptedTransportProbe.probe(query: query, control: controlDown)
        XCTAssertEqual(r.status, .inconclusive,
            "a DoH failure with no working cleartext control must be inconclusive, never a manufactured block")
    }

    func testBlockedOnTransportErrorWhenControlWorks() async {
        let query: EncryptedTransportProbe.DoHQueryFn = { _, _, _ in DNSResponse(status: .transportError) }
        let r = await EncryptedTransportProbe.probe(query: query, control: controlUp)
        XCTAssertEqual(r.status, .blocked)
    }

    func testInconclusiveOnTransportErrorWhenControlAlsoDown() async {
        let query: EncryptedTransportProbe.DoHQueryFn = { _, _, _ in DNSResponse(status: .transportError) }
        let r = await EncryptedTransportProbe.probe(query: query, control: controlDown)
        XCTAssertEqual(r.status, .inconclusive)
    }

    func testInconclusiveWhenReachedButEmpty() async {
        let query: EncryptedTransportProbe.DoHQueryFn = { _, _, _ in DNSResponse(status: .success, records: []) }
        let r = await EncryptedTransportProbe.probe(query: query, control: controlDown)
        XCTAssertEqual(r.status, .inconclusive)
    }

    // Direct classify vectors — both control outcomes across every DoH status.
    func testClassifyDirect() {
        XCTAssertEqual(EncryptedTransportProbe.classify(status: .success, recordCount: 2, controlSucceeded: false, endpoint: "x").status, .working)
        // B051: timeout/transportError split on the control.
        XCTAssertEqual(EncryptedTransportProbe.classify(status: .timeout, recordCount: 0, controlSucceeded: true, endpoint: "x").status, .blocked)
        XCTAssertEqual(EncryptedTransportProbe.classify(status: .timeout, recordCount: 0, controlSucceeded: false, endpoint: "x").status, .inconclusive)
        XCTAssertEqual(EncryptedTransportProbe.classify(status: .transportError, recordCount: 0, controlSucceeded: true, endpoint: "x").status, .blocked)
        XCTAssertEqual(EncryptedTransportProbe.classify(status: .transportError, recordCount: 0, controlSucceeded: false, endpoint: "x").status, .inconclusive)
        XCTAssertEqual(EncryptedTransportProbe.classify(status: .servFail, recordCount: 0, controlSucceeded: true, endpoint: "x").status, .inconclusive)
    }
}
