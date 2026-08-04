import XCTest
@testable import SyxxKit

/// Offline tests for the CDN/cloud cohort classifier (Workstream H · A7 support) and its use
/// as an A2 CDN-false-positive dampener.
final class NetworkCohortTests: XCTestCase {

    func testProviderLookup() {
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "104.16.1.1"), "Cloudflare")   // 104.16.0.0/13
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "104.23.9.9"), "Cloudflare")   // still /13
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "151.101.65.5"), "Fastly")
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "142.250.1.1"), "Google")
        XCTAssertNil(NetworkCohort.provider(forIPv4: "8.8.8.8"))                        // not a listed edge block
        XCTAssertNil(NetworkCohort.provider(forIPv4: "1.2.3.4"))
        XCTAssertNil(NetworkCohort.provider(forIPv4: "not-an-ip"))
        XCTAssertNil(NetworkCohort.provider(forIPv4: "2606:4700::1"))                   // IPv6 → nil in v1
    }

    /// Representative addresses from the expanded 2026-07 edge table — one per newly-added block
    /// family, so a bad CIDR or a cross-provider overlap surfaces here.
    func testExpandedProviderCoverage() {
        // Cloudflare additions
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "104.25.1.1"), "Cloudflare")    // 104.24.0.0/14
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "141.101.90.5"), "Cloudflare")  // 141.101.64.0/18
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "108.162.200.1"), "Cloudflare") // 108.162.192.0/18
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "131.0.72.9"), "Cloudflare")    // 131.0.72.0/22
        // Fastly additions
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "146.75.1.1"), "Fastly")        // 146.75.0.0/16
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "23.235.40.1"), "Fastly")       // 23.235.32.0/20
        // Akamai additions (distinct 23.x blocks must not collide with Fastly's 23.235)
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "23.5.1.1"), "Akamai")          // 23.0.0.0/12
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "23.40.1.1"), "Akamai")         // 23.32.0.0/11
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "2.19.1.1"), "Akamai")          // 2.16.0.0/13
        // Google serving additions
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "74.125.1.1"), "Google")        // 74.125.0.0/16
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "172.253.1.1"), "Google")       // 172.253.0.0/16
        // CloudFront additions (18.x / 65.x designated-edge, distinct from broad AWS compute)
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "18.65.1.1"), "CloudFront")     // 18.64.0.0/14
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "108.157.1.1"), "CloudFront")   // 108.156.0.0/14
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "143.204.1.1"), "CloudFront")   // 143.204.0.0/16
        // New providers
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "45.60.1.1"), "Imperva")        // 45.60.0.0/16
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "151.139.1.1"), "StackPath")    // 151.139.0.0/16
        // Guard: 108.177 (Google) and 108.156/108.138 (CloudFront) and 108.162 (Cloudflare) stay distinct
        XCTAssertEqual(NetworkCohort.provider(forIPv4: "108.177.1.1"), "Google")       // 108.177.0.0/17
    }

    func testSharedProvider() {
        XCTAssertEqual(NetworkCohort.sharedProvider(["104.16.1.1"], ["104.20.2.2"]), "Cloudflare")
        XCTAssertNil(NetworkCohort.sharedProvider(["104.16.1.1"], ["151.101.1.1"]))     // Cloudflare vs Fastly
        XCTAssertNil(NetworkCohort.sharedProvider(["1.2.3.4"], ["104.16.1.1"]))         // one unknown → no claim
        XCTAssertNil(NetworkCohort.sharedProvider([], []))
    }

    // A2 uses the cohort to soften a disjoint result to inconclusive when both sides are one CDN.
    func testAuthoritativeDisjointSameCDNIsInconclusive() {
        let r = AuthoritativeVantage.classify(resolverSet: ["104.16.1.1"], authSet: ["104.20.2.2"],
                                              nameservers: [], domain: "cdn.example")
        XCTAssertEqual(r.status, .inconclusive)
        XCTAssertTrue(r.detail.contains("Cloudflare"))
    }

    func testAuthoritativeDisjointDifferentProvidersStillDiverges() {
        // Cloudflare vs Fastly — genuinely different networks, not benign geo-routing.
        let r = AuthoritativeVantage.classify(resolverSet: ["104.16.1.1"], authSet: ["151.101.1.1"],
                                              nameservers: [], domain: "x")
        XCTAssertEqual(r.status, .diverges)
    }

    func testAuthoritativeDisjointUnknownIPsStillDiverges() {
        let r = AuthoritativeVantage.classify(resolverSet: ["1.2.3.4"], authSet: ["9.9.9.9"],
                                              nameservers: [], domain: "x")
        XCTAssertEqual(r.status, .diverges)
    }
}
