import XCTest
@testable import SyxxKit

/// Structural safeguard for the Witness testify decision core (DELTAS.md D093 + the 2026-07-22
/// extraction refactor). D093 was a drift-population step that got silently dropped on one platform
/// during hand-duplication of the GUI view-model, undetected because the logic lived only inside a
/// GUI project no CI job could unit-test. These tests pin the now-single-sourced pure core
/// (`WitnessTestifyPlanner`) so a future drop of the drift step fails here instead of shipping —
/// this target runs under plain `swift test`, no GUI/app toolchain required, the whole point of the
/// extraction. Parity twin: Windows/Syxx.Tests/WitnessTestifyPlannerTests.cs.
final class WitnessTestifyPlannerTests: XCTestCase {

    // `SyxxKit.` qualifier is load-bearing: Foundation bridges NSComparisonResult into Swift as the
    // unqualified `ComparisonResult`, which would otherwise collide with SyxxKit's type here.
    private func makeComparison(redirected: Bool,
                               localRdata: [String],
                               publicRdata: [String]) -> SyxxKit.ComparisonResult {
        let localRecords = localRdata.map { DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: $0) }
        let publicRecords = publicRdata.map { DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: $0) }
        return SyxxKit.ComparisonResult(
            localServer: "192.168.1.1",
            localRecords: localRecords,
            publicResolverResults: ["Cloudflare": publicRecords],
            isRedirectionDetected: redirected,
            localOutcome: ResolverQueryOutcome(records: localRecords),
            publicResolverOutcomes: ["Cloudflare": ResolverQueryOutcome(records: publicRecords)]
        )
    }

    // D093: redirection detected MUST yield a drift, with the fields derived exactly as the two
    // former inlined blocks did (previousAnswer = public rdata, newAnswer = local rdata).
    func testDriftIfRedirectedProducesDriftOnRedirection() {
        let comparison = makeComparison(redirected: true,
                                        localRdata: ["10.0.0.5"],
                                        publicRdata: ["93.184.216.34"])
        guard let d = WitnessTestifyPlanner.driftIfRedirected(
            domain: "example.com", local: "192.168.1.1", comparison: comparison) else {
            return XCTFail("D093: a redirected comparison must produce a drift, not nil")
        }
        XCTAssertEqual(d.domain, "example.com")
        XCTAssertEqual(d.queryType, "A")
        XCTAssertEqual(d.previousAnswer, "93.184.216.34", "previousAnswer must be the public resolver's rdata")
        XCTAssertEqual(d.newAnswer, "10.0.0.5", "newAnswer must be the local resolver's rdata")
        XCTAssertEqual(d.resolver, "192.168.1.1")
    }

    // No redirection ⇒ nil (caller maps to an empty drift list). The honest, no-false-positive side.
    func testDriftIfRedirectedReturnsNilWhenNoRedirection() {
        let comparison = makeComparison(redirected: false,
                                        localRdata: ["93.184.216.34"],
                                        publicRdata: ["93.184.216.34"])
        XCTAssertNil(WitnessTestifyPlanner.driftIfRedirected(
            domain: "example.com", local: "192.168.1.1", comparison: comparison))
    }

    // seal() must sign AND carry the drift through in one shot — the exact contract witnessTestify()
    // now relies on. A real Ed25519 keypair, no network.
    func testSealSignsAndCarriesDrift() throws {
        let (priv, _) = WitnessSigner.generateKeyPair()
        let comparison = makeComparison(redirected: true,
                                        localRdata: ["10.0.0.5"],
                                        publicRdata: ["93.184.216.34"])
        let outcome = try WitnessTestifyPlanner.seal(
            domain: "example.com", local: "192.168.1.1", comparison: comparison, privateKeyBase64: priv)
        XCTAssertFalse(outcome.snapshot.signature.isEmpty, "seal must return a signed snapshot")
        XCTAssertEqual(try WitnessSigner.verifySnapshot(outcome.snapshot), .valid, "the sealed snapshot must self-verify")
        XCTAssertNotNil(outcome.drift, "seal must carry the drift for a redirected comparison")
        XCTAssertEqual(outcome.drift?.newAnswer, "10.0.0.5")
    }

    // An unusable key must propagate the signer's throw so the caller's existing catch renders the
    // existing "Could not create witness snapshot: …" message — the seal must not swallow it.
    func testSealPropagatesSigningError() {
        let comparison = makeComparison(redirected: false, localRdata: ["1.1.1.1"], publicRdata: ["1.1.1.1"])
        XCTAssertThrowsError(try WitnessTestifyPlanner.seal(
            domain: "example.com", local: "192.168.1.1", comparison: comparison, privateKeyBase64: ""))
    }
}
