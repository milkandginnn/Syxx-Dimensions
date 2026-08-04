import XCTest
@testable import SyxxKit

/// Offline tests for the authenticated/authoritative denial-of-existence datapoint (H · A6).
final class DenialOfExistenceProbeTests: XCTestCase {

    private func soa(_ owner: String) -> DNSRecord { DNSRecord(name: owner, type: 6, ttl: 3600, rdata: "aabbccdd") }
    private func nsec(_ owner: String) -> DNSRecord { DNSRecord(name: owner, type: 47, ttl: 3600, rdata: "00") }
    private func a(_ owner: String) -> DNSRecord { DNSRecord(name: owner, type: 1, ttl: 60, rdata: "10.0.0.1") }

    func testSoaBackedNegativeIsBenign() {
        let r = DenialOfExistenceProbe.classify(name: "nope.example.com", status: .nxDomain,
                                                answers: [], authority: [soa("example.com")])
        XCTAssertEqual(r.status, .soaBacked)
        XCTAssertEqual(r.soaOwner, "example.com")
    }

    func testNsecProvenNegativeIsBenign() {
        let r = DenialOfExistenceProbe.classify(name: "nope.example.com", status: .nxDomain,
                                                answers: [], authority: [soa("example.com"), nsec("example.com")])
        XCTAssertEqual(r.status, .nsecProven)
        XCTAssertTrue(r.hasNSEC)
    }

    func testBareDenialIsFlagged() {
        // NXDOMAIN with an empty authority section — the injected-block signature.
        let r = DenialOfExistenceProbe.classify(name: "nope.example.com", status: .nxDomain,
                                                answers: [], authority: [])
        XCTAssertEqual(r.status, .bareDenial)
    }

    func testContradictoryWhenNegativeCarriesAnswers() {
        let r = DenialOfExistenceProbe.classify(name: "nope.example.com", status: .nxDomain,
                                                answers: [a("nope.example.com")], authority: [soa("example.com")])
        XCTAssertEqual(r.status, .contradictory)
    }

    func testContradictoryWhenSoaIsWrongZone() {
        // SOA for blockpage.isp.net on a denial of nope.example.com → denial from the wrong zone.
        let r = DenialOfExistenceProbe.classify(name: "nope.example.com", status: .nxDomain,
                                                answers: [], authority: [soa("blockpage.isp.net")])
        XCTAssertEqual(r.status, .contradictory)
    }

    func testResolvedNameIsNotADenial() {
        let r = DenialOfExistenceProbe.classify(name: "example.com", status: .success,
                                                answers: [a("example.com")], authority: [])
        XCTAssertEqual(r.status, .notADenial)
    }

    func testTransportFailureIsInconclusive() {
        let r = DenialOfExistenceProbe.classify(name: "example.com", status: .timeout, answers: [], authority: [])
        XCTAssertEqual(r.status, .inconclusive)
    }

    func testNoDataWithSoaIsBenign() {
        // NODATA (name exists, no record of this type) with a proper SOA is a legit negative.
        let r = DenialOfExistenceProbe.classify(name: "example.com", status: .noData,
                                                answers: [], authority: [soa("example.com")])
        XCTAssertEqual(r.status, .soaBacked)
    }

    func testAncestorMatching() {
        XCTAssertTrue(DenialOfExistenceProbe.isAncestor(zone: "example.com", of: "a.b.example.com"))
        XCTAssertTrue(DenialOfExistenceProbe.isAncestor(zone: "example.com.", of: "example.com"))
        XCTAssertFalse(DenialOfExistenceProbe.isAncestor(zone: "example.com", of: "notexample.com"))
        XCTAssertFalse(DenialOfExistenceProbe.isAncestor(zone: "evil.net", of: "example.com"))
    }

    func testProbeGradesBareDenialViaMock() async {
        let query: DenialOfExistenceProbe.QueryFn = { _ in DNSResponse(status: .nxDomain) }   // bare NXDOMAIN
        let r = await DenialOfExistenceProbe.probe(name: "nope.example.com", server: "192.168.1.1", query: query)
        XCTAssertEqual(r.status, .bareDenial)
    }
}
