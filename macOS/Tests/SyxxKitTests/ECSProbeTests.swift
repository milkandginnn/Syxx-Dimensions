import XCTest
@testable import SyxxKit

/// Offline tests for the EDNS Client Subnet leak datapoint (Workstream H · F10) and the ECS
/// encode/decode contract extension on DNSResolver.
final class ECSProbeTests: XCTestCase {

    // MARK: - Contract: query encodes an ECS option; parser reads the echoed scope

    func testQueryEncodesECSOption() throws {
        let (packet, _) = try DNSResolver.makeQueryPacketWithID(name: "example.com", type: 1,
                                                                edns0UDPSize: 4096, edns0ClientSubnetV4: "203.0.113.0")
        let bytes = [UInt8](packet)
        XCTAssertEqual(bytes[11], 0x01)   // ARCOUNT = 1 (OPT)
        // OPT RDATA ends with the ECS option: code 8, len 7, family 1, /24, scope 0, 203.0.113.
        XCTAssertEqual(Array(bytes.suffix(11)), [0x00, 0x08, 0x00, 0x07, 0x00, 0x01, 0x18, 0x00, 203, 0, 113])
    }

    func testParserReadsEchoedECSScope() {
        func name(_ p: [String]) -> [UInt8] { p.flatMap { [UInt8($0.utf8.count)] + Array($0.utf8) } + [0x00] }
        // Response with an OPT record whose RDATA carries an ECS option echoed with scope /24.
        let ecsOption: [UInt8] = [0x00, 0x08, 0x00, 0x07, 0x00, 0x01, 0x18, 0x18, 203, 0, 113]  // scope byte = 0x18 (24)
        var b: [UInt8] = [0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01]
        b += name(["example", "com"]) + [0x00, 0x01, 0x00, 0x01]
        b += name(["example", "com"]) + [0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 93, 184, 216, 34]
        b += [0x00, 0x00, 0x29, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00]  // OPT: root, type 41, class 4096, ttl 0
        b += [UInt8((ecsOption.count >> 8) & 0xff), UInt8(ecsOption.count & 0xff)] + ecsOption  // RDLENGTH + ECS option

        let r = DNSResolver.parseResponsePacketDetailed(Data(b), requestedType: 1)
        XCTAssertEqual(r.status, .success)
        XCTAssertEqual(r.edns?.ecsScopePrefixLength, 24)
    }

    func testParserReadsNilECSWhenOPTHasNoECS() {
        func name(_ p: [String]) -> [UInt8] { p.flatMap { [UInt8($0.utf8.count)] + Array($0.utf8) } + [0x00] }
        var b: [UInt8] = [0x12, 0x34, 0x81, 0x80, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01]
        b += name(["example", "com"]) + [0x00, 0x01, 0x00, 0x01]
        b += name(["example", "com"]) + [0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C, 0x00, 0x04, 93, 184, 216, 34]
        b += [0x00, 0x00, 0x29, 0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]  // OPT with RDLENGTH 0 (no options)
        let r = DNSResolver.parseResponsePacketDetailed(Data(b), requestedType: 1)
        XCTAssertNotNil(r.edns)
        XCTAssertNil(r.edns?.ecsScopePrefixLength)
    }

    // MARK: - Probe classification

    func testForwardedWhenScopeNonZero() async {
        let query: ECSProbe.QueryFn = { _ in
            DNSResponse(status: .success, records: [DNSRecord(name: "x", type: 1, ttl: 60, rdata: "1.2.3.4")],
                        edns: EDNSResponseInfo(udpPayloadSize: 4096, version: 0, extendedRCode: 0, ecsScopePrefixLength: 24))
        }
        let r = await ECSProbe.probe(server: "1.1.1.1", query: query)
        XCTAssertEqual(r.status, .forwarded)
        XCTAssertEqual(r.scopePrefixLength, 24)
    }

    func testNotForwardedWhenNoECSScope() async {
        let query: ECSProbe.QueryFn = { _ in
            DNSResponse(status: .success, records: [DNSRecord(name: "x", type: 1, ttl: 60, rdata: "1.2.3.4")],
                        edns: EDNSResponseInfo(udpPayloadSize: 4096, version: 0, extendedRCode: 0))  // no ECS scope
        }
        let r = await ECSProbe.probe(server: "9.9.9.9", query: query)
        XCTAssertEqual(r.status, .notForwarded)
    }

    func testInconclusiveWhenNoEDNS() async {
        let query: ECSProbe.QueryFn = { _ in
            DNSResponse(status: .success, records: [DNSRecord(name: "x", type: 1, ttl: 60, rdata: "1.2.3.4")])  // no edns
        }
        let r = await ECSProbe.probe(server: "8.8.8.8", query: query)
        XCTAssertEqual(r.status, .inconclusive)
    }

    func testInconclusiveOnTransportFailure() async {
        let query: ECSProbe.QueryFn = { _ in DNSResponse(status: .timeout) }
        let r = await ECSProbe.probe(server: "8.8.8.8", query: query)
        XCTAssertEqual(r.status, .inconclusive)
    }
}
