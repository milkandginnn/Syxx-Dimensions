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

    // MARK: CX-1 provenance partition + matrix + RPN order

    func testProvenancePartitionIsDirectInferredUnavailable() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        XCTAssertEqual(dm.directRows.map(\.observationId), ["obs.local.a", "obs.public.a"])
        XCTAssertEqual(dm.inferredRows.map(\.findingId), ["find.rewrite.1"])
        XCTAssertEqual(dm.unavailableRows.map(\.datapointId), ["leak"])
        XCTAssertEqual(dm.unavailableRows.first?.reason, .timedOut)
        // Failure path: inventing a third direct row must break identity with the engine partition.
        XCTAssertNotEqual(dm.directRows.count, 3)
    }

    func testCrossVantageMatrixPreservesDiscrepancyAndOutlier() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        let row = try XCTUnwrap(dm.matrixRows.first)
        XCTAssertEqual(row.classification, .discrepancy)
        XCTAssertEqual(row.outlierVantageIds, ["local"])
        XCTAssertEqual(row.consensusAnswers, ["198.51.100.10"])
        XCTAssertEqual(row.entries.map(\.vantageId), ["local", "public1", "public2"])
        // Failure path: treating local as consensus is a lie.
        XCTAssertFalse(row.consensusAnswers.contains("203.0.113.5"))
    }

    func testRPNFindingsPreserveEngineRankOrder() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        XCTAssertEqual(dm.findingRows.map(\.rank), [1, 2])
        XCTAssertEqual(dm.findingRows.map(\.score), [20, 9])
        XCTAssertEqual(dm.findingRows.map(\.mechanismClass), [.rewrite, .leak])
        // Failure path: UI re-sorting by score descending would put leak first.
        XCTAssertNotEqual(dm.findingRows.map(\.findingId), ["find.leak.2", "find.rewrite.1"])
    }

    // MARK: Empty / partial / hard-gate / conflict / low-separation / complete-evidence

    func testEmptyNoDataIsInconclusiveWithNilConfidence() throws {
        let empty = Stage5Presentation(
            generatedAt: "2026-07-11T21:14:37.123Z",
            dataRegime: .noData,
            completeness: EvidenceCompleteness(observed: 0, expected: 6, criticalAvailable: 0, criticalExpected: 3, expectedProfileId: "full"),
            provenance: ProvenancePartition(direct: [], inferred: [], unavailable: [
                UnavailableDatapoint(datapointId: "a", section: .prophet, reason: .notRequested, reasonToken: .unavailableTimedOut)
            ]),
            answerMatrix: [],
            honesty: HonestySignals(hardGate: nil, conflict: nil, uncertainty: nil),
            determination: MechanismDetermination(state: .inconclusive, mechanismClass: nil, confidence: nil,
                withinClass: [], basis: .insufficientEvidence, referralRequired: false, referralToken: nil),
            findings: [])
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: empty))
        XCTAssertEqual(dm.severity, .inconclusive)
        XCTAssertEqual(dm.confidenceText, Stage5Copy.confidenceNone)
        XCTAssertTrue(dm.directRows.isEmpty)
        XCTAssertTrue(dm.inferredRows.isEmpty)
        XCTAssertFalse(dm.unavailableRows.isEmpty)
        // Failure path: empty must never read clean/green.
        XCTAssertNotEqual(dm.severity, .clean)
        XCTAssertFalse(dm.confidenceText.contains("0%"))
    }

    func testPartialDataKeepsHardGateAndDoesNotInventEvidence() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        XCTAssertEqual(dm.dataRegime, .partialData)
        XCTAssertEqual(dm.honestyLines.first?.0, .hardGate)
        // Failure path: adapter must not fill unavailable slots with fabricated direct observations.
        let directIds = Set(dm.directRows.map(\.datapointId))
        let unavailableIds = Set(dm.unavailableRows.map(\.datapointId))
        XCTAssertTrue(directIds.isDisjoint(with: unavailableIds))
    }

    func testHardGateExplanationIsDistinctFromConflict() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        let hard = try XCTUnwrap(dm.honestyLines.first(where: { $0.0 == .hardGate })?.1)
        let conflict = try XCTUnwrap(dm.honestyLines.first(where: { $0.0 == .conflict })?.1)
        XCTAssertTrue(hard.contains("Glutton") || hard.lowercased().contains("required"))
        XCTAssertNotEqual(hard, conflict)
        // Failure path: collapsing both into one line.
        XCTAssertEqual(dm.honestyLines.filter { $0.0 == .hardGate }.count, 1)
        XCTAssertEqual(dm.honestyLines.filter { $0.0 == .conflict }.count, 1)
    }

    func testConflictOnlyRunDoesNotFabricateHardGate() throws {
        let honesty = HonestySignals(
            hardGate: nil,
            conflict: ScalarHonestySignal(token: .conflictEvidenceDisagrees, value: 0.6),
            uncertainty: nil)
        let det = MechanismDetermination(state: .inconclusive, mechanismClass: nil, confidence: nil,
            withinClass: [], basis: .ordinaryCredalGate, referralRequired: false, referralToken: nil)
        // Build a §13-valid partial object without findings/hard gate.
        let s5 = Stage5Presentation(
            generatedAt: fixture.generatedAt,
            dataRegime: .partialData,
            completeness: EvidenceCompleteness(observed: 2, expected: 6, criticalAvailable: 2, criticalExpected: 2, expectedProfileId: "full"),
            provenance: ProvenancePartition(direct: fixture.provenance.direct, inferred: [], unavailable: fixture.provenance.unavailable),
            answerMatrix: fixture.answerMatrix,
            honesty: honesty,
            determination: det,
            findings: [])
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: s5))
        XCTAssertEqual(dm.honestyLines.map(\.0), [.conflict])
        // Failure path: conflict alone must not invent a hard-gate line.
        XCTAssertFalse(dm.honestyLines.map(\.0).contains(.hardGate))
    }

    func testLowSeparationWithinClassPreservesDisplayOrder() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        // Shares are 0.6 / 0.4 — low separation; order is displayOrder, not share magnitude.
        XCTAssertEqual(dm.withinClassLines.count, 2)
        XCTAssertEqual(dm.withinClassLines.first, "Your resolver is returning a forged answer")
        // Failure path: sorting by share alone would still put spoofing first here; reverse
        // displayOrder in a clone would flip the lines and must be detectable.
        let flipped = MechanismDetermination(
            state: .tampered, mechanismClass: .rewrite,
            confidence: fixture.determination.confidence,
            withinClass: [
                WithinClassEntry(hypothesis: .interception_onPath, displayOrder: 0, share: 0.4),
                WithinClassEntry(hypothesis: .spoofing, displayOrder: 1, share: 0.6),
            ],
            basis: .decisiveSafetyGate, referralRequired: true, referralToken: .referralMechanismUnresolved)
        let s5 = Stage5Presentation(
            generatedAt: fixture.generatedAt, dataRegime: fixture.dataRegime,
            completeness: fixture.completeness, provenance: fixture.provenance,
            answerMatrix: fixture.answerMatrix, honesty: fixture.honesty,
            determination: flipped, findings: fixture.findings)
        let dm2 = try XCTUnwrap(Stage5DisplayModel.make(from: s5))
        XCTAssertEqual(dm2.withinClassLines.first, "Something on the path is rewriting answers")
        XCTAssertNotEqual(dm.withinClassLines, dm2.withinClassLines)
    }

    func testCompleteEvidenceHasNoUnavailableAndCanBeClean() throws {
        let local = VantageIdentity(vantageId: "local", kind: .localSystem, endpoint: nil,
                                    transport: "do53", authenticated: false, freshness: .currentRun)
        let obs = DirectObservation(observationId: "obs.1", datapointId: "dp.1", section: .prophet,
            observedAt: "2026-07-11T21:14:30.000Z", vantage: local, rawValues: ["198.51.100.10"],
            valueKind: .dnsAnswerSet, sourceEvidenceIds: [])
        let row = AnswerMatrixRow(rowId: "r1", queryName: "example.com.", queryType: 1, entries: [
            VantageAnswer(vantage: local, status: .answered, canonicalAnswers: ["198.51.100.10"], rcode: 0, observationId: "obs.1")
        ], comparison: AnswerComparison(classification: .agreement, localVantageIds: ["local"],
            referenceVantageIds: [], consensusAnswers: [], outlierVantageIds: []))
        let complete = Stage5Presentation(
            generatedAt: "2026-07-11T21:14:37.123Z",
            dataRegime: .fullData,
            completeness: EvidenceCompleteness(observed: 1, expected: 1, criticalAvailable: 1, criticalExpected: 1, expectedProfileId: "prophet"),
            provenance: ProvenancePartition(direct: [obs], inferred: [], unavailable: []),
            answerMatrix: [row],
            honesty: HonestySignals(hardGate: nil, conflict: nil, uncertainty: nil),
            determination: MechanismDetermination(state: .clean, mechanismClass: nil, confidence: CredalMeasure(kind: .belief, value: 0.9, target: "mechanism:clean"),
                withinClass: [], basis: .ordinaryCredalGate, referralRequired: false, referralToken: nil),
            findings: [])
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: complete))
        XCTAssertEqual(dm.severity, .clean)
        XCTAssertTrue(dm.unavailableRows.isEmpty)
        XCTAssertEqual(dm.mechanismClass, nil)
        XCTAssertTrue(dm.findingRows.isEmpty)
        // Failure path: full data is not a license for the adapter to invent findings.
        XCTAssertEqual(dm.inferredRows.count, 0)
    }

    func testAdapterDoesNotStrengthenEngineResult() throws {
        let dm = try XCTUnwrap(Stage5DisplayModel.make(from: fixture))
        // Confidence value stays 0.2 → 20%; never floored upward for a decisive gate.
        XCTAssertEqual(Stage5Copy.percent(fixture.determination.confidence!.value), 20)
        XCTAssertEqual(dm.mechanismClass, .rewrite)
        XCTAssertEqual(dm.p.determination.state, .tampered)
        // Failure path: promoting inconclusive confidence text when confidence is present.
        XCTAssertNotEqual(dm.confidenceText, Stage5Copy.confidenceNone)
    }
}
