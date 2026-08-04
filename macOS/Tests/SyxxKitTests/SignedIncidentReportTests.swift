import XCTest
@testable import SyxxKit

/// B1 — SignedIncidentReport renderer tests. macOS twin of
/// Windows/Syxx.Tests/SignedIncidentReportTests.cs.
///
/// Every test builds a real signed WitnessSnapshot through the production
/// WitnessSigner path, so output is tested against real signed data.
///
/// Failure cases (Protocol §3): every assertion fails when the renderer
/// produces wrong result text, omits required sections, or fabricates
/// data — never passes vacuously.
final class SignedIncidentReportTests: XCTestCase {

    // MARK: - Helpers

    private var testKeyPair: (privateKey: String, publicKey: String) {
        WitnessSigner.generateKeyPair()
    }

    /// Builds a ComparisonResult matching the Windows test's `MakeComparison` shape.
    /// On macOS the struct has `let` fields and uses the memberwise init with defaults.
    private func makeComparison(
        domain: String = "example.com",
        redirected: Bool = false,
        inconclusive: Bool = false,
        degraded: Bool = false
    ) -> SyxxKit.ComparisonResult {
        let rec = DNSRecord(name: domain, type: 1, ttl: 60, rdata: "93.184.216.34")
        let okOutcome = ResolverQueryOutcome(records: [rec], status: .success)
        return ComparisonResult(
            localServer: "1.1.1.1",
            localRecords: [rec],
            publicResolverResults: ["Cloudflare": [rec]],
            isRedirectionDetected: redirected,
            isInconclusive: inconclusive,
            isDegraded: degraded,
            localOutcome: okOutcome,
            publicResolverOutcomes: ["Cloudflare": okOutcome]
        )
    }

    /// Builds a real signed snapshot and renders it. Optionally includes a
    /// PlainExplanation derived from a bare Synthesizer run.
    private func captureReport(
        comparison: SyxxKit.ComparisonResult,
        domain: String = "example.com",
        includeExplanation: Bool = false
    ) -> String {
        let keypair = testKeyPair
        let snapshot = try! WitnessSigner.createSignedSnapshot(
            domain: domain,
            comparison: comparison,
            privateKeyBase64: keypair.privateKey
        )
        var explanation: PlainExplanation? = nil
        if includeExplanation {
            let conclusion = Synthesizer.conclude(from: SectionInputs())
            explanation = PlainExplanations.For(conclusion)
        }
        return SignedIncidentReport.render(snapshot: snapshot, explanation: explanation)
    }

    /// Renders a snapshot directly (for tests that build the snapshot themselves).
    private func captureReportFromSnapshot(_ snapshot: WitnessSnapshot) -> String {
        SignedIncidentReport.render(snapshot: snapshot)
    }

    // MARK: - Result rendering

    func testCleanComparison_RendersMatchesResult() {
        let report = captureReport(
            comparison: makeComparison(redirected: false, inconclusive: false, degraded: false)
        )
        XCTAssertTrue(report.contains("MATCHES"), "Clean comparison must render MATCHES")
        XCTAssertFalse(report.contains("DIVERGES"), "Clean comparison must not render DIVERGES")
        XCTAssertFalse(report.contains("INCONCLUSIVE"), "Clean comparison must not render INCONCLUSIVE")
        XCTAssertFalse(report.contains("DEGRADED"), "Clean comparison must not render DEGRADED")
    }

    func testRedirectedComparison_RendersDivergesResult() {
        let report = captureReport(
            comparison: makeComparison(redirected: true)
        )
        XCTAssertTrue(report.contains("DIVERGES"), "Redirected comparison must render DIVERGES")
        XCTAssertFalse(report.contains("MATCHES"), "Redirected comparison must not render MATCHES")
    }

    func testInconclusiveComparison_RendersInconclusiveResult() {
        let report = captureReport(
            comparison: makeComparison(inconclusive: true)
        )
        XCTAssertTrue(report.contains("INCONCLUSIVE"), "Inconclusive comparison must render INCONCLUSIVE")
        XCTAssertFalse(report.contains("MATCHES"), "Inconclusive comparison must not render MATCHES")
        XCTAssertFalse(report.contains("DIVERGES"), "Inconclusive comparison must not render DIVERGES")
    }

    func testDegradedComparison_RendersDegradedResult() {
        let report = captureReport(
            comparison: makeComparison(degraded: true)
        )
        XCTAssertTrue(report.contains("DEGRADED"), "Degraded comparison must render DEGRADED")
        XCTAssertFalse(report.contains("MATCHES"), "Degraded comparison must not render MATCHES")
    }

    func testDegradedAndInconclusive_RendersDegraded() {
        let report = captureReport(
            comparison: makeComparison(inconclusive: true, degraded: true)
        )
        XCTAssertTrue(report.contains("DEGRADED"),
                      "Degraded + inconclusive must render DEGRADED (redirected > degraded > inconclusive in priority)")
    }

    // MARK: - Decision section presence

    func testNoConclusionProvided_OmitsDecisionSection() {
        let report = captureReport(
            comparison: makeComparison(),
            includeExplanation: false
        )
        XCTAssertFalse(report.contains("DECISION"),
                       "No explanation provided must omit the DECISION section entirely")
    }

    func testConclusionProvided_IncludesDecisionSection() {
        let report = captureReport(
            comparison: makeComparison(),
            includeExplanation: true
        )
        XCTAssertTrue(report.contains("DECISION"),
                      "Explanation provided must include the DECISION section")
    }

    // MARK: - Metadata accuracy

    func testReportContainsAccurateMetadata() {
        let keypair = testKeyPair
        let snapshot = try! WitnessSigner.createSignedSnapshot(
            domain: "example.com",
            comparison: makeComparison(),
            privateKeyBase64: keypair.privateKey
        )
        let report = captureReportFromSnapshot(snapshot)

        XCTAssertTrue(report.contains("example.com"),
                      "Report must contain the target domain")
        XCTAssertTrue(report.contains("SYXX SIGNED INCIDENT REPORT"),
                      "Report must contain the header title")
        XCTAssertTrue(report.contains("Key ID:     \(snapshot.keyId)"),
                      "Report must contain the exact key ID")
    }

    // MARK: - Cryptographic seal

    func testReportContainsCryptographicSeal() {
        let keypair = testKeyPair
        let snapshot = try! WitnessSigner.createSignedSnapshot(
            domain: "example.com",
            comparison: makeComparison(),
            privateKeyBase64: keypair.privateKey
        )
        let report = captureReportFromSnapshot(snapshot)

        XCTAssertTrue(report.contains("CRYPTOGRAPHIC SEAL"),
                      "Report must contain the CRYPTOGRAPHIC SEAL section")
        XCTAssertTrue(report.contains(snapshot.keyId),
                      "Report must contain the snapshot's key ID in the seal")
        let pubPrefix = String(snapshot.publicKey.prefix(48))
        XCTAssertTrue(report.contains(pubPrefix),
                      "Report must contain the first 48 characters of the public key")
        XCTAssertTrue(report.contains("syxx witness verify"),
                      "Report must contain the verification command")
    }

    // MARK: - Evidence summary

    func testReportContainsEvidenceSummary() {
        let report = captureReport(comparison: makeComparison())

        XCTAssertTrue(report.contains("EVIDENCE SUMMARY"),
                      "Report must contain the EVIDENCE SUMMARY section")
        XCTAssertTrue(report.contains("1.1.1.1"),
                      "Report must contain the local resolver IP")
        XCTAssertTrue(report.contains("93.184.216.34"),
                      "Report must contain the answer IP")
        XCTAssertTrue(report.contains("Cloudflare"),
                      "Report must contain the public resolver name")
    }

    // MARK: - Empty evidence honesty

    func testEmptyEvidence_DoesNotFabricateIps() {
        // Build a ComparisonResult with completely empty evidence.
        // ComparisonResult is a struct with `let` fields, so we construct fresh.
        let emptyComparison = SyxxKit.ComparisonResult(
            localServer: "",
            localRecords: [],
            publicResolverResults: [:],
            isRedirectionDetected: false,
            isInconclusive: false,
            isDegraded: true,
            localOutcome: ResolverQueryOutcome(records: [], status: .noData),
            publicResolverOutcomes: [:]
        )
        let keypair = testKeyPair
        let snapshot = try! WitnessSigner.createSignedSnapshot(
            domain: "empty.test",
            comparison: emptyComparison,
            privateKeyBase64: keypair.privateKey
        )
        let report = captureReportFromSnapshot(snapshot)

        XCTAssertFalse(report.contains("93.184.216.34"),
                       "Empty evidence must NOT fabricate IPs from other test cases")
        XCTAssertTrue(report.contains("no reliable reference"),
                      "Empty degraded evidence must honestly state no reliable reference is available")
    }

    // MARK: - B157: empty comparison must not render MATCHES

    func testB157_EmptyComparison_RendersInconclusiveNotMatches() {
        // B157: a ComparisonResult with no local records AND no public results compared nothing.
        // The flags are all false because nothing was observed — the terminal `else` rendered
        // "MATCHES — Answers match reference", making a zero-data comparison Ed25519-signed
        // and presented as "third-party-verifiable evidence of clean DNS."
        // Mutation proof: remove the empty-set guard in SignedIncidentReport.render and this
        // test goes RED.
        let emptyComparison = SyxxKit.ComparisonResult(
            localServer: "",
            localRecords: [],
            publicResolverResults: [:],
            isRedirectionDetected: false,
            isInconclusive: false,
            isDegraded: false,
            localOutcome: ResolverQueryOutcome(records: [], status: .noData),
            publicResolverOutcomes: [:]
        )
        let keypair = testKeyPair
        let snapshot = try! WitnessSigner.createSignedSnapshot(
            domain: "empty.test",
            comparison: emptyComparison,
            privateKeyBase64: keypair.privateKey
        )
        let report = captureReportFromSnapshot(snapshot)

        // Primary assertion: must NOT print MATCHES on a comparison of nothing.
        XCTAssertFalse(report.contains("MATCHES"),
                       "B157: zero-data comparison must never render MATCHES")
        // Must instead render the honest INCONCLUSIVE result.
        XCTAssertTrue(report.contains("INCONCLUSIVE"),
                      "B157: zero-data comparison must render INCONCLUSIVE — Nothing was compared")
        XCTAssertTrue(report.contains("Nothing was compared"),
                      "B157: the specific honest message 'Nothing was compared' must appear")
    }
}
