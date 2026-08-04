import XCTest
@testable import SyxxKit

/// C2 — filtering verification. Every test names its fail state; the mutation matrix in the
/// changelog demonstrates each one actually reds when the corresponding defect is introduced.
final class FilteringProbeTests: XCTestCase {

    private func resp(_ status: DNSResponseStatus, ips: [String] = []) -> DNSResponse {
        DNSResponse(status: status,
                    records: ips.map { DNSRecord(name: "doubleclick.net", type: 1, ttl: 60, rdata: $0) })
    }

    // ── classify ─────────────────────────────────────────────────────────────

    /// The acceptance bar: a known-blocked domain RESOLVING publicly must read `resolved`
    /// (the filter did not act). Fail state: classify public answers as sinkholed and the
    /// probe would vouch for a filter that let the domain through.
    func testPublicAnswerMeansFilterDidNotAct() {
        XCTAssertEqual(FilteringProbe.classify(resp(.success, ips: ["142.250.72.14"])), .resolved)
    }

    /// NXDOMAIN and null/loopback/private answers are all sinkhole styles.
    /// Fail state: treat 0.0.0.0 as resolved → a working Pi-hole reads "not filtering".
    func testSinkholeStylesAllReadSinkholed() {
        XCTAssertEqual(FilteringProbe.classify(resp(.nxDomain)), .sinkholed)
        XCTAssertEqual(FilteringProbe.classify(resp(.success, ips: ["0.0.0.0"])), .sinkholed)
        XCTAssertEqual(FilteringProbe.classify(resp(.success, ips: ["127.0.0.1"])), .sinkholed)
        XCTAssertEqual(FilteringProbe.classify(resp(.success, ips: ["192.168.1.5"])), .sinkholed)
        XCTAssertEqual(FilteringProbe.classify(resp(.noData)), .sinkholed)
    }

    /// One public address among sinkhole addresses means the domain ESCAPED — any public
    /// answer is an escape. Fail state: require all-public before `resolved` and a partial
    /// escape hides behind the sinkhole entries.
    func testMixedAnswerWithAnyPublicReadsResolved() {
        XCTAssertEqual(FilteringProbe.classify(resp(.success, ips: ["0.0.0.0", "142.250.72.14"])), .resolved)
    }

    /// A timeout is NO READING. Fail state: map timeout to sinkholed and a dead network
    /// fabricates "your filter is working" (Protocol §1 false-secure).
    func testTimeoutIsUnknownNeverSinkholed() {
        XCTAssertEqual(FilteringProbe.classify(resp(.timeout)), .unknown)
        XCTAssertEqual(FilteringProbe.classify(resp(.transportError)), .unknown)
    }

    // ── aggregate ────────────────────────────────────────────────────────────

    func testAllSinkholedIsActive() {
        XCTAssertEqual(FilteringProbe.aggregate([.sinkholed, .sinkholed, .sinkholed]), .active)
    }

    func testAllResolvedIsInactive() {
        XCTAssertEqual(FilteringProbe.aggregate([.resolved, .resolved, .resolved]), .inactive)
    }

    /// A mix is a LEAKY blocklist, distinct from active and inactive.
    func testMixIsPartial() {
        XCTAssertEqual(FilteringProbe.aggregate([.sinkholed, .resolved, .sinkholed]), .partial)
    }

    /// All-unreadable must be inconclusive. Fail state: default the empty case to `.active`
    /// and an offline machine reads "filter verified" with zero evidence (§6 zero-data).
    func testAllUnknownIsInconclusiveNeverActive() {
        XCTAssertEqual(FilteringProbe.aggregate([.unknown, .unknown, .unknown]), .inconclusive)
        XCTAssertEqual(FilteringProbe.aggregate([]), .inconclusive)
    }

    /// Unreadable probes drop out of the denominator; the readable remainder still decides.
    func testUnknownsDropOutOfDenominator() {
        XCTAssertEqual(FilteringProbe.aggregate([.unknown, .sinkholed]), .active)
        XCTAssertEqual(FilteringProbe.aggregate([.unknown, .resolved]), .inactive)
    }
}
