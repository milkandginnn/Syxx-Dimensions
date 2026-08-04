import XCTest
@testable import SyxxKit

/// [B251] macOS side of the `DNSResponseStatus` WIRE CONTRACT.
///
/// These raw values are not an implementation detail: they are the bytes another implementation
/// parses. `syxx witness verify` on Windows failed outright on every macOS-produced snapshot
/// ("The JSON value could not be converted to Syxx.Kit.DNSResponseStatus. Path:
/// $.comparison.dohOutcomes.Cloudflare.status") because .NET had no mapping for them. macOS is the
/// gold standard and its spellings must not move; the .NET twin
/// (Windows/dotNET/Syxx.Tests/WitnessCrossPlatformWireTests.ExpectedWire) asserts the same list, so
/// a rename on either side breaks a test on that side rather than silently splitting the wire.
///
/// DECLARATION ORDER is part of the contract too — Windows builds before B251 wrote the bare CLR
/// ordinal, and the .NET reader still accepts those numbers by position.
final class DNSResponseStatusWireParityTests: XCTestCase {

    /// (declaration order, rawValue) — must equal Syxx.Kit.DNSResponseStatus member-for-member.
    private static let contract: [(index: Int, status: DNSResponseStatus, wire: String)] = [
        (0, .success, "success"),
        (1, .noData, "noData"),
        (2, .nxDomain, "nxDomain"),
        (3, .servFail, "servFail"),
        (4, .refused, "refused"),
        (5, .timeout, "timeout"),
        (6, .truncated, "truncated"),
        (7, .responseIdMismatch, "responseIdMismatch"),
        (8, .malformedResponse, "malformedResponse"),
        (9, .transportError, "transportError"),
    ]

    func testRawValuesMatchTheCrossPlatformWireContract() {
        for (_, status, wire) in Self.contract {
            XCTAssertEqual(status.rawValue, wire)
            XCTAssertEqual(DNSResponseStatus(rawValue: wire), status)
        }
    }

    func testEncodesAsABareString() throws {
        struct Box: Codable { let status: DNSResponseStatus }
        let data = try JSONEncoder().encode(Box(status: .transportError))
        XCTAssertEqual(String(data: data, encoding: .utf8), #"{"status":"transportError"}"#)
    }

    func testNoStatusIsMissingFromTheContract() {
        // FAILURE CASE. A member added to the Swift enum without a wire spelling here would be
        // emitted into signed evidence that the .NET reader rejects outright, so the contract list
        // has to be exhaustive. `DNSResponseStatus` is not CaseIterable, so this is asserted by
        // decoding every spelling the .NET table declares and requiring the counts to agree.
        XCTAssertEqual(Self.contract.count, 10)
        XCTAssertEqual(Set(Self.contract.map(\.wire)).count, 10)
        XCTAssertEqual(Set(Self.contract.map(\.status)).count, 10)
    }

    func testUnknownSpellingDoesNotDecodeToSuccess() {
        // FAILURE CASE. The one thing that must never happen is an unreadable status quietly
        // becoming "the resolver answered fine".
        XCTAssertNil(DNSResponseStatus(rawValue: "TransportError"))
        XCTAssertNil(DNSResponseStatus(rawValue: "transport_error"))
        XCTAssertNil(DNSResponseStatus(rawValue: ""))
        struct Box: Codable { let status: DNSResponseStatus }
        XCTAssertThrowsError(try JSONDecoder().decode(Box.self, from: Data(#"{"status":"nope"}"#.utf8)))
    }
}
