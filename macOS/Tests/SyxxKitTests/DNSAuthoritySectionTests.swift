import XCTest
@testable import SyxxKit

/// The A6 contract: `parseResponsePacketDetailed` now preserves the authority section (SOA / NSEC /
/// NS), so a genuine authenticated negative answer can be told apart from an injected block-page
/// NXDOMAIN. These build synthetic packets by hand and assert the authority records survive — and
/// that the answer-section behaviour and Codable round-trip are unchanged.
final class DNSAuthoritySectionTests: XCTestCase {

    private func label(_ s: String) -> [UInt8] { [UInt8(s.utf8.count)] + Array(s.utf8) }
    private func name(_ parts: [String]) -> [UInt8] { parts.flatMap(label) + [0x00] }

    /// Minimal DNS response: header + one question (nope.example.com A IN), an optional A answer,
    /// and an optional authority SOA for example.com (opaque rdata — the parser hex-dumps type 6).
    private func packet(rcode: UInt8, aa: Bool, answerRDATA: [UInt8]? = nil, authoritySOA: Bool) -> Data {
        var b: [UInt8] = [0x12, 0x34]                       // ID
        var flags: UInt16 = 0x8000                          // QR
        if aa { flags |= 0x0400 }                           // AA
        flags |= UInt16(rcode)
        b += [UInt8(flags >> 8), UInt8(flags & 0xff)]
        let an: UInt16 = answerRDATA == nil ? 0 : 1
        let ns: UInt16 = authoritySOA ? 1 : 0
        b += [0x00, 0x01, UInt8(an >> 8), UInt8(an & 0xff), UInt8(ns >> 8), UInt8(ns & 0xff), 0x00, 0x00]
        b += name(["nope", "example", "com"]) + [0x00, 0x01, 0x00, 0x01]   // question A IN
        if let rd = answerRDATA {
            b += name(["nope", "example", "com"]) + [0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0x00, 0x3C]
            b += [UInt8(rd.count >> 8), UInt8(rd.count & 0xff)] + rd
        }
        if authoritySOA {
            b += name(["example", "com"]) + [0x00, 0x06, 0x00, 0x01, 0x00, 0x00, 0x0E, 0x10]
            let soa: [UInt8] = [0xAA, 0xBB, 0xCC, 0xDD]
            b += [UInt8(soa.count >> 8), UInt8(soa.count & 0xff)] + soa
        }
        return Data(b)
    }

    func testNXDomainCarriesAuthoritySOA() {
        let r = DNSResolver.parseResponsePacketDetailed(packet(rcode: 3, aa: true, authoritySOA: true), requestedType: 1)
        XCTAssertEqual(r.status, .nxDomain)
        XCTAssertTrue(r.isAuthoritative)
        XCTAssertEqual(r.authorityRecords.count, 1)
        XCTAssertEqual(r.authorityRecords.first?.type, 6)              // SOA
        XCTAssertEqual(r.authorityRecords.first?.name, "example.com")
    }

    func testBareNXDomainHasEmptyAuthority() {
        // No SOA in authority — the signature of an injected/synthesised block-page NXDOMAIN.
        let r = DNSResolver.parseResponsePacketDetailed(packet(rcode: 3, aa: false, authoritySOA: false), requestedType: 1)
        XCTAssertEqual(r.status, .nxDomain)
        XCTAssertTrue(r.authorityRecords.isEmpty)
    }

    func testNoDataCarriesAuthoritySOA() {
        let r = DNSResolver.parseResponsePacketDetailed(packet(rcode: 0, aa: true, answerRDATA: nil, authoritySOA: true), requestedType: 1)
        XCTAssertEqual(r.status, .noData)
        XCTAssertEqual(r.authorityRecords.count, 1)
        XCTAssertEqual(r.authorityRecords.first?.type, 6)
    }

    func testSuccessAnswerParsingUnaffected() {
        // A normal A answer with no authority — records still parse exactly, authority empty.
        let r = DNSResolver.parseResponsePacketDetailed(packet(rcode: 0, aa: false, answerRDATA: [93, 184, 216, 34], authoritySOA: false), requestedType: 1)
        XCTAssertEqual(r.status, .success)
        XCTAssertEqual(r.records.first?.rdata, "93.184.216.34")
        XCTAssertTrue(r.authorityRecords.isEmpty)
    }

    func testSuccessAnswerAlsoCapturesAuthority() {
        let r = DNSResolver.parseResponsePacketDetailed(packet(rcode: 0, aa: false, answerRDATA: [93, 184, 216, 34], authoritySOA: true), requestedType: 1)
        XCTAssertEqual(r.status, .success)
        XCTAssertEqual(r.records.first?.rdata, "93.184.216.34")
        XCTAssertEqual(r.authorityRecords.count, 1)
        XCTAssertEqual(r.authorityRecords.first?.type, 6)
    }

    func testCodableToleratesMissingAuthorityRecords() throws {
        // A response encoded before `authorityRecords` existed must still decode (→ empty), not throw.
        let legacy = #"{"status":"nxDomain","records":[],"offTypeAnswerCount":0,"responseCode":3,"isAuthoritative":false,"isTruncated":false,"recursionAvailable":true,"authenticatedData":false}"#
        let decoded = try JSONDecoder().decode(DNSResponse.self, from: Data(legacy.utf8))
        XCTAssertEqual(decoded.status, .nxDomain)
        XCTAssertTrue(decoded.authorityRecords.isEmpty)
    }

    func testCodableRoundTripPreservesAuthority() throws {
        let original = DNSResponse(status: .nxDomain,
                                   authorityRecords: [DNSRecord(name: "example.com", type: 6, ttl: 3600, rdata: "aabbccdd")],
                                   responseCode: 3, isAuthoritative: true)
        let round = try JSONDecoder().decode(DNSResponse.self, from: JSONEncoder().encode(original))
        XCTAssertEqual(round.authorityRecords.count, 1)
        XCTAssertEqual(round.authorityRecords.first?.name, "example.com")
    }
}
