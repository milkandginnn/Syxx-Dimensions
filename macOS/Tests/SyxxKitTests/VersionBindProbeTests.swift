import XCTest
@testable import SyxxKit

/// Offline tests for the CHAOS version.bind datapoint (Workstream H · F2) and the QCLASS contract
/// change it rides on (a CHAOS-class question in the query builder).
final class VersionBindProbeTests: XCTestCase {

    /// Build the hex RDATA that `DNSResolver.formatRData` renders for a TXT record — one or more
    /// `<length><bytes>` character-strings.
    private func txtHex(_ strings: [String]) -> String {
        var bytes: [UInt8] = []
        for s in strings { bytes.append(UInt8(s.utf8.count)); bytes.append(contentsOf: Array(s.utf8)) }
        return bytes.map { String(format: "%02x", $0) }.joined()
    }

    private func txtResponse(_ strings: [String]) -> DNSResponse {
        DNSResponse(status: .success,
                    records: [DNSRecord(name: "version.bind", type: 16, ttl: 0, rdata: txtHex(strings))])
    }

    // MARK: - Resolver contract: CHAOS class encoding

    func testQueryPacketEncodesChaosClass() throws {
        let (packet, _) = try DNSResolver.makeQueryPacketWithID(name: "version.bind", type: 16, qclass: 3)
        let bytes = [UInt8](packet)
        // The question ends with TYPE (2 bytes) then CLASS (2 bytes). CLASS must be CHAOS = 0x0003.
        XCTAssertEqual(Array(bytes.suffix(2)), [0x00, 0x03])
        XCTAssertEqual(Array(bytes.suffix(4).prefix(2)), [0x00, 0x10])   // TYPE = TXT (16)
    }

    func testQueryPacketDefaultsToINClass() throws {
        let (packet, _) = try DNSResolver.makeQueryPacketWithID(name: "example.com", type: 1)
        XCTAssertEqual(Array([UInt8](packet).suffix(2)), [0x00, 0x01])   // CLASS = IN by default (unchanged)
    }

    // MARK: - TXT banner decoding

    func testDecodeTXTBannerSingleString() {
        XCTAssertEqual(VersionBindProbe.decodeTXTBanner(hexRData: txtHex(["dnsmasq-2.80"])), "dnsmasq-2.80")
    }

    func testDecodeTXTBannerMultipleStrings() {
        XCTAssertEqual(VersionBindProbe.decodeTXTBanner(hexRData: txtHex(["9.16.1", "Ubuntu"])), "9.16.1 Ubuntu")
    }

    func testDecodeTXTBannerEmptyIsNil() {
        XCTAssertNil(VersionBindProbe.decodeTXTBanner(hexRData: ""))
    }

    // MARK: - Classification

    func testForwarderBannerOnPublicResolverIsInterceptionHint() {
        let r = VersionBindProbe.classify(response: txtResponse(["dnsmasq-2.80"]),
                                          server: "8.8.8.8", expectedPublicResolver: true)
        XCTAssertEqual(r.status, .disclosed)
        XCTAssertEqual(r.banner, "dnsmasq-2.80")
        XCTAssertTrue(r.forwarderOnPublicPath)
    }

    func testForwarderBannerOnLocalRouterIsNotEscalated() {
        // Same forwarder banner, but the target is NOT expected to be a public recursive — no escalation.
        let r = VersionBindProbe.classify(response: txtResponse(["dnsmasq-2.80"]),
                                          server: "192.168.1.1", expectedPublicResolver: false)
        XCTAssertEqual(r.status, .disclosed)
        XCTAssertFalse(r.forwarderOnPublicPath)
    }

    func testRecursiveBannerIsDisclosedButNotAForwarderHint() {
        // A real upstream recursive (unbound) self-identifying is attribution only, never an interception hint.
        let r = VersionBindProbe.classify(response: txtResponse(["unbound 1.13.1"]),
                                          server: "8.8.8.8", expectedPublicResolver: true)
        XCTAssertEqual(r.status, .disclosed)
        XCTAssertFalse(r.forwarderOnPublicPath)
    }

    func testWithheldWhenNoTXTAnswer() {
        let r = VersionBindProbe.classify(response: DNSResponse(status: .noData),
                                          server: "1.1.1.1", expectedPublicResolver: true)
        XCTAssertEqual(r.status, .withheld)
        XCTAssertFalse(r.forwarderOnPublicPath)
    }

    func testUntestedWhenProbeFails() {
        // A failed probe must be UNTESTED — never collapsed into a clean/withheld state.
        let r = VersionBindProbe.classify(response: DNSResponse(status: .timeout),
                                          server: "1.1.1.1", expectedPublicResolver: true)
        XCTAssertEqual(r.status, .untested)
    }

    func testProbeInfersPublicResolverFromKnownIP() async {
        let hex = txtHex(["pi-hole"])   // captured by value (Sendable String), not `self`
        let query: VersionBindProbe.QueryFn = { _ in
            DNSResponse(status: .success, records: [DNSRecord(name: "version.bind", type: 16, ttl: 0, rdata: hex)])
        }
        let r = await VersionBindProbe.probe(server: "9.9.9.9", query: query)   // known public IP → inferred public
        XCTAssertTrue(r.forwarderOnPublicPath)
    }

    func testKnownPublicResolverMembership() {
        XCTAssertTrue(VersionBindProbe.isKnownPublicResolver("1.1.1.1"))
        XCTAssertFalse(VersionBindProbe.isKnownPublicResolver("192.168.1.1"))
    }
}
