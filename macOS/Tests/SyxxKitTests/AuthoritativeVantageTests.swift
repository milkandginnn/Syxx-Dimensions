import XCTest
@testable import SyxxKit

/// Offline tests for the authoritative-vs-resolver ground-truth datapoint (Workstream H · A2).
/// Every DNS call is served by an injected mock, so these run with no network.
final class AuthoritativeVantageTests: XCTestCase {

    /// A self-contained `@Sendable` mock: constant delegation (example.com → ns1/ns2 at
    /// 203.0.113.1/.2) plus a configurable authoritative answer for the direct query. Captures
    /// only Sendable value types, so it satisfies the `QueryFn` Sendable requirement.
    /// Responds to multiple bootstrap resolvers so that majorityConsensus can form a set (C007).
    private static func mock(authIPs: [String], aa: Bool = true,
                             authStatus: DNSResponseStatus = .success) -> AuthoritativeVantage.QueryFn {
        { q in
            switch (q.type, q.server) {
            case (2, let bs) where ["1.1.1.1", "8.8.8.8", "9.9.9.9"].contains(bs):   // NS for the domain, via any bootstrap
                return DNSResponse(status: .success, records: [
                    DNSRecord(name: q.name, type: 2, ttl: 3600, rdata: "ns1.example.com."),
                    DNSRecord(name: q.name, type: 2, ttl: 3600, rdata: "ns2.example.com."),
                ])
            case (1, let bs) where ["1.1.1.1", "8.8.8.8", "9.9.9.9"].contains(bs):   // resolve an NS hostname to its IP
                let ip = q.name == "ns1.example.com" ? "203.0.113.1" : "203.0.113.2"
                return DNSResponse(status: .success, records: [DNSRecord(name: q.name, type: 1, ttl: 3600, rdata: ip)])
            default:               // direct query to an authoritative NS IP
                return DNSResponse(status: authStatus,
                                   records: authIPs.map { DNSRecord(name: q.name, type: 1, ttl: 300, rdata: $0) },
                                   isAuthoritative: aa)
            }
        }
    }

    private func compare(_ resolverAnswers: [String], _ query: AuthoritativeVantage.QueryFn,
                         resolverDidFail: Bool = false) async -> AuthoritativeComparison {
        await AuthoritativeVantage.compare(domain: "example.com", resolverAnswers: resolverAnswers,
                                           query: query, resolverDidFail: resolverDidFail)
    }

    func testMatchesWhenResolverAnswerSubsetOfAuthoritative() async {
        let r = await compare(["1.2.3.4"], Self.mock(authIPs: ["1.2.3.4", "5.6.7.8"]))
        XCTAssertEqual(r.status, .matches)
        XCTAssertEqual(r.authoritativeAnswers, ["1.2.3.4", "5.6.7.8"])
    }

    func testDivergesWhenAuthoritativeNegativeButResolverReturnsAnswer() async {
        // Authoritative says NODATA (AA, no records); the recursive path handed back an address anyway.
        let r = await compare(["6.6.6.6"], Self.mock(authIPs: [], authStatus: .noData))
        XCTAssertEqual(r.status, .diverges)
        XCTAssertTrue(r.detail.contains("injected"))
    }

    func testDivergesWhenDisjoint() async {
        let r = await compare(["1.2.3.4"], Self.mock(authIPs: ["9.9.9.9"]))
        XCTAssertEqual(r.status, .diverges)
    }

    func testInconclusiveOnPartialOverlap() async {
        // CDN signature: one address matches authoritative, one doesn't.
        let r = await compare(["1.2.3.4", "6.6.6.6"], Self.mock(authIPs: ["1.2.3.4", "5.6.7.8"]))
        XCTAssertEqual(r.status, .inconclusive)
    }

    func testMatchesWhenBothEmpty() async {
        let r = await compare([], Self.mock(authIPs: [], authStatus: .noData))
        XCTAssertEqual(r.status, .matches)
    }

    func testInconclusiveWhenBothEmptyButResolverFailed() async {
        // B036: empty+empty with resolver query failure must not produce .matches —
        // a timeout/error should be .inconclusive.
        let r = await compare([], Self.mock(authIPs: [], authStatus: .noData), resolverDidFail: true)
        XCTAssertEqual(r.status, .inconclusive)
    }

    func testInconclusiveWhenNoNameservers() async {
        // NS lookup returns nothing at any level → delegation undiscoverable.
        let query: AuthoritativeVantage.QueryFn = { _ in DNSResponse(status: .noData) }
        let r = await compare(["1.2.3.4"], query)
        XCTAssertEqual(r.status, .inconclusive)
        XCTAssertTrue(r.detail.contains("nameservers"))
    }

    func testInconclusiveWhenAuthoritativeLacksAABit() async {
        // The nameserver IP answers, but without the AA bit — not authoritative, so unverifiable.
        let r = await compare(["1.2.3.4"], Self.mock(authIPs: ["1.2.3.4"], aa: false))
        XCTAssertEqual(r.status, .inconclusive)
        XCTAssertTrue(r.detail.contains("AA"))
    }

    func testInconclusiveWhenAuthoritativeUnreachable() async {
        // Every direct query to the NS IPs times out.
        let r = await compare(["1.2.3.4"], Self.mock(authIPs: [], aa: false, authStatus: .timeout))
        XCTAssertEqual(r.status, .inconclusive)
    }

    // C007 / B031 — a single bootstrap resolver cannot form majority consensus, so the delegation
    // is undiscoverable and the ground truth must be INCONCLUSIVE, never single-sourced from the one
    // responder. Guards against the regression where a 1-element bootstrap set silently steers the
    // "authoritative" answer through the sole responder an attacker could control.
    func testSingleBootstrapResolver_isInconclusive_notSingleSourced() async {
        let r = await AuthoritativeVantage.compare(
            domain: "example.com", resolverAnswers: ["1.2.3.4"],
            bootstrapResolvers: ["1.1.1.1"], query: Self.mock(authIPs: ["9.9.9.9"]))
        XCTAssertEqual(r.status, .inconclusive)
        XCTAssertTrue(r.detail.contains("nameservers"))
    }

    // C007 at the consensus primitive: one responder set → nil (cannot be ground truth); no
    // responders → nil; two agreeing responders → the members they share.
    func testMajorityConsensus_singleResponderReturnsNil() {
        XCTAssertNil(AuthoritativeVantage.majorityConsensus([Set(["ns1.example.com"])]))
        XCTAssertNil(AuthoritativeVantage.majorityConsensus([]))
        XCTAssertEqual(AuthoritativeVantage.majorityConsensus([Set(["a", "b"]), Set(["a", "c"])]), ["a"])
    }

    // Pure classify() unit — no delegation/network path at all.
    func testClassifyDirect() {
        XCTAssertEqual(AuthoritativeVantage.classify(resolverSet: ["1.1.1.1"], authSet: ["1.1.1.1", "2.2.2.2"],
                                                     nameservers: [], domain: "x").status, .matches)
        XCTAssertEqual(AuthoritativeVantage.classify(resolverSet: ["1.1.1.1"], authSet: ["2.2.2.2"],
                                                     nameservers: [], domain: "x").status, .diverges)
        XCTAssertEqual(AuthoritativeVantage.classify(resolverSet: ["1.1.1.1", "2.2.2.2"], authSet: ["1.1.1.1", "3.3.3.3"],
                                                     nameservers: [], domain: "x").status, .inconclusive)
        XCTAssertEqual(AuthoritativeVantage.classify(resolverSet: [], authSet: [],
                                                      nameservers: [], domain: "x").status, .matches)
        // B036: empty+empty with resolver failure → .inconclusive.
        XCTAssertEqual(AuthoritativeVantage.classify(resolverSet: [], authSet: [],
                                                      nameservers: [], domain: "x",
                                                      resolverDidFail: true).status, .inconclusive)
    }
}
