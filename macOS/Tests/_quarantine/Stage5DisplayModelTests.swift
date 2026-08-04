import XCTest
@testable import SyxxApp
import SyxxKit

/// Fixture-driven honesty proof for the Stage-5 SURFACE (CX-1/CX-2/CX-3). Drives the shared v1
/// fixture through the presentation display model and asserts the honesty spine holds: the tampered
/// result never reads green, the three honesty signals stay DISTINCT, the M4 decisive-gate pair uses
/// the approved "Corroboration" framing (never "Confidence"), nil confidence never renders "0%", and
/// an ABSENT / schema-invalid object fails closed to unavailable — never a fabricated clean preview.
final class Stage5DisplayModelTests: XCTestCase {

    private var fixture: Stage5Presentation { Stage5PreviewFixture.v1 }

    // MARK: Absence / fail-closed (STAGE5 §12/§13)

    func testAbsentStage5RendersUnavailableNotClean() {
        XCTAssertNil(Stage5DisplayModel.make(from: nil),
                     "an absent stage5 must not build a display model — the view shows the unavailable note")
    }

    func testInvalidStage5FailsClosed() {
        // Break invariant §13.13: a wrong priority score must make the object invalid → unavailable.
        let bad = Stage5Presentation(
            generatedAt: fixture.generatedAt, dataRegime: fixture.dataRegime,
            completeness: fixture.completeness, provenance: fixture.provenance,
            answerMatrix: fixture.answerMatrix, honesty: fixture.honesty,
            determination: fixture.determination,
            findings: [PrioritizedFinding(findingId: "find.rewrite.1", mechanismClass: .rewrite, inferenceId: nil,
                sourceObservationIds: ["obs.local.a"],
                priority: FindingPriority(modelId: "syxx.stage5.sd", modelVersion: 1,
                    factors: PriorityFactors(severity: 5, detectability: 4, occurrence: nil), score: 999, rank: 1))])
        XCTAssertFalse(bad.isValid)
        XCTAssertNil(Stage5DisplayModel.make(from: bad), "a schema-invalid object must fail closed to unavailable")
    }

    func testGenericFallbackIsNeverGreen() {
        // The fail-closed generic string is an inconclusive line — never "clean"/"secure".
        let s = Stage5Copy.genericInconclusive.lowercased()
        XCTAssertFalse(s.contains("clean"))
        XCTAssertFalse(s.contains("secure"))
        XCTAssertTrue(s.contains("inconclusive"))
    }

    // MARK: CX-3 headline + severity (never green for tampered)

    func testTamperedHeadlineIsNotGreen() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        XCTAssertEqual(dm.severity, .tampered)
        XCTAssertNotEqual(dm.severity, .clean)
        XCTAssertEqual(dm.headline, "The address you were given was changed")
    }

    // MARK: CX-3 M4 decisive-gate pair uses "Corroboration", never "Confidence"

    func testDecisiveGateUsesCorroborationFramingNotConfidence() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        XCTAssertTrue(dm.isDecisiveGate)
        // Framing 3: the low belief number is reframed as corroboration, not "confidence".
        let pct = Stage5Copy.percent(fixture.determination.confidence!.value)
        XCTAssertEqual(Stage5Copy.corroboration(percent: pct), "Corroborating belief: 20%")
        XCTAssertFalse(Stage5Copy.corroboration(percent: pct).contains("Confidence"))
        XCTAssertTrue(Stage5Copy.decisiveExplainer(percent: pct).contains("not yet corroborated"))
    }

    func testNilConfidenceRendersNoEstimateNotZeroPercent() {
        let det = MechanismDetermination(state: .inconclusive, mechanismClass: nil, confidence: nil,
            withinClass: [], basis: .ordinaryCredalGate, referralRequired: false, referralToken: nil)
        let s5 = Stage5Presentation(generatedAt: fixture.generatedAt, dataRegime: fixture.dataRegime,
            completeness: fixture.completeness, provenance: fixture.provenance,
            answerMatrix: fixture.answerMatrix, honesty: HonestySignals(hardGate: nil, conflict: nil, uncertainty: nil),
            determination: det, findings: [])
        let dm = Stage5DisplayModel.make(from: s5)
        XCTAssertEqual(dm?.confidenceText, "No estimate — not enough evidence")
        XCTAssertFalse(dm?.confidenceText.contains("0%") ?? true)
    }

    // MARK: CX-2 three DISTINCT honesty lines + ISO time anchor

    func testThreeHonestyLinesAreDistinctAndPresent() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        let kinds = dm.honestyLines.map(\.0)
        XCTAssertEqual(kinds, [.hardGate, .conflict, .uncertainty])
        let texts = Set(dm.honestyLines.map(\.1))
        XCTAssertEqual(texts.count, 3, "the three signals must be three distinct lines, not collapsed")
        XCTAssertEqual(dm.honestyLines[1].1,
            "The evidence genuinely disagrees (50%) — different checks point to different conclusions.")
        XCTAssertEqual(dm.honestyLines[2].1,
            "Evidence is thin (25%) — there isn't enough to be sure either way.")
    }

    func testTimeAnchorIsExactRFC3339() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        XCTAssertEqual(dm.timeAnchor, "2026-07-11T21:14:37.123Z", "the audit anchor is rendered verbatim, no locale conversion")
    }

    // MARK: CX-3 within-class split (both members are tampering)

    func testWithinClassSplitUsesApprovedMembers() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        XCTAssertEqual(dm.withinClassLines, [
            "Your resolver is returning a forged answer",
            "Something on the path is rewriting answers",
        ])
    }

    // MARK: completeness as a fraction, never a bare percent that reads as confidence

    func testCompletenessIsAFractionNotAPercent() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        XCTAssertEqual(dm.completenessText, "4 of 6 checks reached a conclusion")
        XCTAssertFalse(dm.completenessText.contains("%"))
    }
}
