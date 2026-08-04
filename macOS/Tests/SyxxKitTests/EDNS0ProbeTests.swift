import XCTest
@testable import SyxxKit

/// Offline tests for the EDNS0 capability datapoint (Workstream H · F1) and the DNSResolver contract
/// change it rides on (OPT in the query, OPT parsed from the response).
final class EDNS0ProbeTests: XCTestCase {

    // MARK: - Resolver contract: query encodes OPT, parser reads it back

    func testQueryPacketCarriesOPTWhenEDNSRequested() throws {
        let (packet, _) = try DNSResolver.makeQueryPacketWithID(name: "cloudflare.com", type: 1, edns0UDPSize: 4096)
        let bytes = [UInt8](packet)
        XCTAssertEqual(bytes[10], 0x00)                       // ARCOUNT high
        XCTAssertEqual(bytes[11], 0x01)                       // ARCOUNT = 1 (the OPT record)
        // The packet ends with the OPT pseudo-record: root name, TYPE 41, CLASS 4096, TTL 0, RDLEN 0.
        XCTAssertEqual(Array(bytes.suffix(11)), [0x00, 0x00, 0x29, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00])
    }

    func testQueryPacketHasNoOPTByDefault() throws {
        let (packet, _) = try DNSResolver.makeQueryPacketWithID(name: "cloudflare.com", type: 1)
        XCTAssertEqual([UInt8](packet)[11], 0x00)             // ARCOUNT = 0
    }

    func testParserReadsOPTFromResponse() {
        // Response: QR=1, QD=1, AN=1, NS=0, AR=1 — an A answer plus an OPT record advertising 4096.
        func name(_ parts: [String]) -> [UInt8] { parts.flatMap { [UInt8($0.utf8.count)] + Array($0.utf8) } + [0x00] }
        var b: [UInt8] = [0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01]
        b += name(["example", "com"]) + [0x00, 0x01, 0x00, 0x01]                     // question A IN
        b += name(["example", "com"]) + [0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 93, 184, 216, 34]  // A answer
        b += [0x00, 0x00, 0x29, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]      // OPT: root, type 41, class 4096, ttl 0, rdlen 0

        let r = DNSResolver.parseResponsePacketDetailed(Data(b), requestedType: 1)
        XCTAssertEqual(r.status, .success)
        XCTAssertEqual(r.edns?.udpPayloadSize, 4096)
        XCTAssertEqual(r.edns?.version, 0)
    }

    func testParserReturnsNilEDNSWhenNoOPT() {
        func name(_ parts: [String]) -> [UInt8] { parts.flatMap { [UInt8($0.utf8.count)] + Array($0.utf8) } + [0x00] }
        var b: [UInt8] = [0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00]
        b += name(["example", "com"]) + [0x00, 0x01, 0x00, 0x01]
        b += name(["example", "com"]) + [0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 93, 184, 216, 34]
        let r = DNSResolver.parseResponsePacketDetailed(Data(b), requestedType: 1)
        XCTAssertEqual(r.status, .success)
        XCTAssertNil(r.edns)
    }

    // MARK: - Probe classification

    func testSupportedWhenResolverEchoesOPT() async {
        let query: EDNS0Probe.QueryFn = { _ in
            DNSResponse(status: .success, records: [DNSRecord(name: "x", type: 1, ttl: 60, rdata: "1.2.3.4")],
                        edns: EDNSResponseInfo(udpPayloadSize: 1232, version: 0, extendedRCode: 0))
        }
        let r = await EDNS0Probe.probe(server: "1.1.1.1", query: query)
        XCTAssertEqual(r.status, .supported)
        XCTAssertEqual(r.udpPayloadSize, 1232)
    }

    func testNotSupportedWhenAnswerHasNoOPT() async {
        let query: EDNS0Probe.QueryFn = { _ in
            DNSResponse(status: .success, records: [DNSRecord(name: "x", type: 1, ttl: 60, rdata: "1.2.3.4")])  // no edns
        }
        let r = await EDNS0Probe.probe(server: "192.168.1.1", query: query)
        XCTAssertEqual(r.status, .notSupported)
    }

    func testBrokenWhenEDNSFailsButPlainWorks() async {
        let query: EDNS0Probe.QueryFn = { q in
            q.edns0UDPSize != nil
                ? DNSResponse(status: .timeout)   // EDNS0 query dropped
                : DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 1, ttl: 60, rdata: "1.2.3.4")])
        }
        let r = await EDNS0Probe.probe(server: "192.168.1.1", query: query)
        XCTAssertEqual(r.status, .broken)
    }

    func testInconclusiveWhenBothFail() async {
        let query: EDNS0Probe.QueryFn = { _ in DNSResponse(status: .timeout) }
        let r = await EDNS0Probe.probe(server: "192.168.1.1", query: query)
        XCTAssertEqual(r.status, .inconclusive)
    }
}
