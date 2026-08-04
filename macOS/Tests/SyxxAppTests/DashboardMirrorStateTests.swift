import XCTest
import SyxxKit
@testable import SyxxApp

final class DashboardMirrorStateTests: XCTestCase {

    private func comparison(
        redirected: Bool = false,
        inconclusive: Bool = false,
        degraded: Bool = false
    ) -> SyxxKit.ComparisonResult {
        let records = ["93.184.216.34"].map {
            DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: $0)
        }
        return SyxxKit.ComparisonResult(
            localServer: "192.168.1.1",
            localRecords: records,
            publicResolverResults: ["Cloudflare": records],
            isRedirectionDetected: redirected,
            isInconclusive: inconclusive,
            isDegraded: degraded,
            localOutcome: ResolverQueryOutcome(records: records),
            publicResolverOutcomes: ["Cloudflare": ResolverQueryOutcome(records: records)]
        )
    }

    func testMirrorNotRun() {
        XCTAssertEqual(DashboardWardenState.mirror(isPro: true, result: nil), "NOT RUN")
    }

    func testMirrorLocked() {
        XCTAssertEqual(
            DashboardWardenState.mirror(isPro: false, result: comparison()),
            "LOCKED · PRO"
        )
    }

    func testMirrorInconclusive() {
        XCTAssertEqual(
            DashboardWardenState.mirror(isPro: true, result: comparison(inconclusive: true)),
            "INCONCLUSIVE"
        )
    }

    func testMirrorDegraded() {
        XCTAssertEqual(
            DashboardWardenState.mirror(isPro: true, result: comparison(degraded: true)),
            "DEGRADED"
        )
    }

    func testMirrorMatchingIsPassed() {
        XCTAssertEqual(
            DashboardWardenState.mirror(isPro: true, result: comparison()),
            "PASSED"
        )
    }

    func testMirrorRedirectionIsNotPassed() {
        let label = DashboardWardenState.mirror(
            isPro: true,
            result: comparison(redirected: true)
        )
        XCTAssertEqual(label, "TAMPERING DETECTED")
        XCTAssertNotEqual(label, "PASSED")
    }

    /// Mutation path: if the redirection check is removed from production,
    /// a redirected result would fall through to PASSED and this test must go red.
    func testRedirectionCheckIsRequiredForHonestPassed() {
        let redirected = comparison(redirected: true)
        // Production path must inspect isRedirectionDetected before PASSED.
        XCTAssertTrue(redirected.isRedirectionDetected)
        XCTAssertEqual(
            DashboardWardenState.mirror(isPro: true, result: redirected),
            "TAMPERING DETECTED"
        )
        // Simulated broken predicate (old false-pass): only inconclusive/degraded checked.
        let brokenPredicate: (SyxxKit.ComparisonResult) -> String = { r in
            if r.isInconclusive { return "INCONCLUSIVE" }
            if r.isDegraded { return "DEGRADED" }
            return "PASSED"
        }
        XCTAssertEqual(
            brokenPredicate(redirected),
            "PASSED",
            "fixture proves the old predicate would false-pass; production must differ"
        )
        XCTAssertNotEqual(
            DashboardWardenState.mirror(isPro: true, result: redirected),
            brokenPredicate(redirected)
        )
    }
}
