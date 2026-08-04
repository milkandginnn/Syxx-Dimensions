import XCTest
@testable import SyxxKit

/// A DNS query stream IS a browsing history. Syxx's users are people whose DNS is being
/// surveilled; exporting the names they look up — to any endpoint, ours included — would make
/// this product a second surveillance channel and contradict the privacy policy that describes it.
///
/// Before this guard, `dns.hostname` carried the user's queried name verbatim on three macOS
/// spans and two Windows spans. These tests pin that the name never reaches a span attribute,
/// while the shape needed to diagnose malformed/long-name failures still does.
final class TelemetryRedactionTests: XCTestCase {

    private let secret = "very-private-internal-host.example.invalid"

    /// The headline gate. Failure case: re-add `"dns.hostname": .string(query.name)` to any span
    /// → the queried name appears in the attribute values → red.
    func testQueriedNameNeverAppearsInSpanAttributes() {
        let attrs = DNSResolver.redactedAttrs(domain: secret, type: 1, transport: "udp",
                                              resolver: "192.168.1.1", port: 53)

        let rendered = attrs.map { "\($0.key)=\($0.value)" }.joined(separator: "|")
        XCTAssertFalse(rendered.contains(secret),
                       "the user's queried name must never reach a span attribute")
        XCTAssertFalse(rendered.contains("example.invalid"),
                       "not even the registrable portion of the name may be exported")
        XCTAssertNil(attrs["dns.hostname"], "the dns.hostname attribute must no longer exist")
    }

    /// Redaction must not blind us: shape is still exported, because malformed- and long-name
    /// failures are real diagnostics. Failure case: drop the shape attributes → red.
    func testShapeIsStillExportedSoDiagnosticsSurvive() {
        let attrs = DNSResolver.redactedAttrs(domain: secret, type: 1, transport: "udp")

        XCTAssertEqual(attrs["dns.target_class"]?.description, "userSupplied")
        XCTAssertNotNil(attrs["dns.name.labels"])
        XCTAssertNotNil(attrs["dns.name.length"])
        XCTAssertNotNil(attrs["dns.name.is_idn"])
    }

    /// Syxx's OWN constants say nothing about the user and stay verbatim — otherwise we could not
    /// tell our default target apart from a user's lookup when reading a trace.
    func testSyxxOwnedDomainStaysVerbatim() {
        let attrs = DNSResolver.redactedAttrs(domain: "github.com", type: 1, transport: "doh")

        XCTAssertEqual(attrs["dns.target_class"]?.description, "syxxOwned")
        XCTAssertEqual(attrs["dns.target"]?.description, "github.com")
        // A Syxx constant carries no shape fields — there is nothing to hide.
        XCTAssertNil(attrs["dns.name.length"])
    }

    /// A public resolver is a meaningful, non-identifying dimension and stays verbatim.
    func testKnownPublicResolverIsKept() {
        let attrs = DNSResolver.redactedAttrs(domain: "github.com", type: 1,
                                              transport: "udp", resolver: "1.1.1.1")

        XCTAssertEqual(attrs["dns.server"]?.description, "1.1.1.1")
        XCTAssertEqual(attrs["dns.server_class"]?.description, "public")
    }

    /// A home/ISP/corporate resolver IS identifying and must be reduced to a class.
    /// Failure case: emit `dns.server` for a private address → red.
    func testPrivateResolverAddressIsReducedToAClass() {
        for address in ["192.168.1.1", "10.0.0.1", "127.0.0.1"] {
            let attrs = DNSResolver.redactedAttrs(domain: "github.com", type: 1,
                                                  transport: "udp", resolver: address)
            let rendered = attrs.map { "\($0.key)=\($0.value)" }.joined(separator: "|")
            XCTAssertFalse(rendered.contains(address),
                           "\(address) is identifying and must not be exported verbatim")
            XCTAssertNotNil(attrs["dns.server_class"])
        }
    }

    /// IDN detection must work on the shape path, since punycode names are a real failure mode.
    func testIDNShapeIsDetectedWithoutExportingTheName() {
        let attrs = DNSResolver.redactedAttrs(domain: "xn--bcher-kva.example", type: 1,
                                              transport: "udp")
        XCTAssertEqual(attrs["dns.name.is_idn"]?.description, "true")
        let rendered = attrs.map { "\($0.key)=\($0.value)" }.joined(separator: "|")
        XCTAssertFalse(rendered.contains("bcher"))
    }
}
