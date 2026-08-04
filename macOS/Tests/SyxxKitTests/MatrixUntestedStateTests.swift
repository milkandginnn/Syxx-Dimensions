import XCTest
@testable import SyxxKit

/// macOS half of the Untested-state parity guard for the matrix determinative engine.
/// Twin of `Windows/dotNET/Syxx.Tests/MatrixUntestedStateTests.cs` — same scenarios, same
/// assertions, so a divergence in either engine fails on the platform that drifted.
///
/// WHY THIS EXISTS (engine doc-comment, CRITICAL DISTINCTION,
/// MatrixDeterminativeEngine.swift:16-22): `.inconclusive` means the check RAN and produced VALID
/// data that did not resolve — it is USABLE evidence and does NOT hard-gate. `.untested` (never
/// ran) and `.invalidated` (ran, data corrupt) are NOT usable and DO hard-gate. Collapsing the
/// three misclassifies working evidence, which is why `.inconclusive` is a decision state in its
/// own right and never a synonym for "no result".
///
/// The Windows twin shipped without an `Untested` case at all, so an unmeasured run reported
/// `Inconclusive` there and `untested` here. Writing the twin surfaced a defect present on BOTH
/// platforms: `.untested` fell past the ungraded guards in `ReportCards.For` into the
/// threat-grading path, where `severity(forDiagnosisID:)`'s `default: return .warning` graded a
/// never-run scan **C**. `testReportCard_untestedIsUngraded` is the regression guard for that.
final class MatrixUntestedStateTests: XCTestCase {

    private let allSectionIDs = ["prophet", "mirror", "portal", "glutton", "cage", "witness"]

    private func allUntestedBundle() -> EvidenceBundle {
        EvidenceBundle(sections: allSectionIDs.map {
            SectionResult(id: SectionID($0), status: .untested)
        }, indicators: [])
    }

    private func conclusion(from bundle: EvidenceBundle) throws -> Conclusion {
        let det = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        return Conclusion(generatedAt: Date(timeIntervalSince1970: 0), determination: det)
    }

    // MARK: - The engine emits it

    /// Every section untested AND no indicators observed ⇒ `.untested`, never `.inconclusive`.
    /// Failure case: delete the all-untested guard in `evaluate` → falls through to the abstention
    /// path → returns `.inconclusive` → red.
    func testAllUntestedSectionsWithNoIndicators_isUntestedNotInconclusive() throws {
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(allUntestedBundle())

        XCTAssertEqual(result.state, .untested)
        XCTAssertNotEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty)
        XCTAssertEqual(result.explanation, "No sections have been evaluated yet.")
    }

    /// The converse, and the reason the guard is two-part: sections that RAN and produced valid
    /// but ambiguous data are `.inconclusive`, NOT `.untested`.
    /// Failure case: widen the guard to fire on any non-passed status → red.
    func testAllInconclusiveSections_isInconclusiveNotUntested() throws {
        let bundle = EvidenceBundle(sections: allSectionIDs.map {
            SectionResult(id: SectionID($0), status: .inconclusive)
        }, indicators: [])

        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)

        // Assert the POSITIVE state. `!= .untested` alone would also pass on `.clean`,
        // so the test named "isInconclusiveNotUntested" could survive a false-clean regression.
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty)
    }

    /// Indicators are real measured data even when the sections did not complete, so their
    /// presence must defeat the untested shortcut and force real evaluation.
    /// Failure case: drop the `noIndicators` half of the guard → observed evidence is discarded
    /// and the run reports `.untested` → red.
    func testUntestedSectionsWithObservedIndicators_isNotUntested() throws {
        let bundle = EvidenceBundle(
            sections: allSectionIDs.map { SectionResult(id: SectionID($0), status: .untested) },
            indicators: [
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
            ])

        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)

        // THE FALSE-CLEAN HOLE this test exists to guard: a strong (0.95) threat indicator
        // sits behind untested required sections. Asserting only `!= .untested` would pass if
        // the gate regressed and the engine returned `.clean` — the exact scenario. Assert the
        // positive state AND that the diagnosis was gated for the right reason.
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertNotEqual(result.state, .clean, "a gated threat signal must never read clean")
        XCTAssertTrue(result.diagnoses.isEmpty)
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "dnsSpoofing" },
                      "dnsSpoofing depends on untested required sections and must be suppressed; got \(result.suppressed.map { $0.id.rawValue })")
    }

    /// Untested must never read as clean or as a threat — honesty spine rule 2 (missing data is
    /// not clean data). Failure case: map `.untested` to `.clean` anywhere → red.
    func testUntestedIsNeverCleanAndNeverAThreat() throws {
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(allUntestedBundle())

        XCTAssertNotEqual(result.state, .clean)
        XCTAssertNotEqual(result.state, .threatDetected)
        XCTAssertTrue(result.diagnoses.isEmpty)
    }

    /// The wire value must be "untested", byte-identical to the Windows `RawValue()`, or a signed
    /// cross-platform payload diverges. Failure case: rename the raw value → red.
    func testUntestedRawValueMatchesWindowsWireFormat() {
        XCTAssertEqual(DeterminativeState.untested.rawValue, "untested")
    }

    // MARK: - F048: three missing tests from the C# twin

    /// F048: port of C# `UntestedIsADistinctResultCase`. Asserts the enum has exactly 4 distinct
    /// cases and Untested != Inconclusive. Failure case: alias Untested to Inconclusive → red.
    func testUntestedIsADistinctResultCase() {
        let all: [DeterminativeState] = [.clean, .threatDetected, .inconclusive, .untested]
        XCTAssertEqual(Set(all).count, 4, "all four determinative states must be distinct")
        XCTAssertNotEqual(DeterminativeState.inconclusive, .untested,
                          "Untested must not be aliased to Inconclusive")
    }

    /// F048: port of C# `UntestedRoundTripsThroughJson`. Asserts "untested" DECODES back to
    /// .untested. The Swift file only pinned the encode side; a decode regression that mapped
    /// the wire value "untested" onto .inconclusive — an unmeasured run silently becoming an
    /// "evidence was ambiguous" run in a signed cross-platform payload — was caught on Windows only.
    func testUntestedRoundTripsThroughJson() throws {
        let encoder = JSONEncoder()
        let decoder = JSONDecoder()
        let json = try encoder.encode(DeterminativeState.untested)
        XCTAssertEqual(String(data: json, encoding: .utf8), "\"untested\"")
        let decoded = try decoder.decode(DeterminativeState.self, from: json)
        XCTAssertEqual(decoded, .untested, "untested must round-trip through JSON")
    }

    /// F048: port of C# `Report_UntestedRendersItsOwnLabel`. Pins the report label for untested
    /// and asserts it doesn't render as INCONCLUSIVE or UNKNOWN. The C# twin pins
    /// "Conclusion: NOT YET TESTED" — the Swift Report.swift:136-139 renders "Not Yet Tested".
    /// Both platforms must agree on the canonical casing (see F048 delta for the divergence).
    func testReport_UntestedRendersItsOwnLabel() throws {
        let conclusion = try conclusion(from: allUntestedBundle())
        let report = SynthesisReport(conclusion: conclusion, datapoints: [])
        let text = report.renderText()

        XCTAssertTrue(text.contains("Conclusion: Not Yet Tested") || text.contains("Conclusion: NOT YET TESTED"),
                      "untested must render its own label, got: \(text)")
        XCTAssertFalse(text.contains("Conclusion: INCONCLUSIVE") || text.contains("Conclusion: Inconclusive"),
                       "untested must not render as inconclusive")
        XCTAssertFalse(text.contains("Conclusion: UNKNOWN") || text.contains("Conclusion: Unknown"),
                       "untested must not render as unknown")
    }

    // MARK: - Renderers keep it distinct

    /// Failure case: delete the `.untested` case in `PlainExplanation.For` → falls through to the
    /// inconclusive/threat copy → red.
    func testPlainExplanation_untestedSaysNothingRan() throws {
        let explanation = PlainExplanations.For(try conclusion(from: allUntestedBundle()))

        XCTAssertEqual(explanation.summary,
                       "No sections have been evaluated yet — run a scan to see results")
        XCTAssertTrue(explanation.because.isEmpty)
        // The inconclusive copy must not be reused: it asserts a completed-but-unresolved run.
        XCTAssertFalse(explanation.summary.contains("could not confirm"))
    }

    /// THE WORST CONFLATION: `ResultActions.For` uses `if` guards, not an exhaustive `switch`,
    /// so before the fix an `.untested` result fell to the threat arm and rendered
    /// "Your DNS shows signs of tampering." at `.high` — a fabricated threat result for a scan
    /// that never ran. Failure case: delete the `.untested` arm in ResultAction.swift → red.
    func testResultAction_untestedIsNeverReportedAsTampering() throws {
        let action = ResultActions.For(try conclusion(from: allUntestedBundle()))

        XCTAssertFalse(action.meaning.lowercased().contains("tampering"),
                       "an unmeasured run must never be described as tampering")
        XCTAssertNotEqual(action.severity, .high)
        XCTAssertNotEqual(action.severity, .critical)
        XCTAssertEqual(action.severity, .unknown)
        XCTAssertTrue(action.qualified, "an unmeasured run must be marked qualified")
        // ...and equally must not read as clean.
        XCTAssertFalse(action.meaning.lowercased().contains("no tampering"))
    }

    /// A grade must not be invented for a run that never happened. This is the shared-defect guard:
    /// before the fix this returned `.c`.
    /// Failure case: drop `.untested` from the ungraded guard in `ReportCards.For` → grades C → red.
    func testReportCard_untestedIsUngraded() throws {
        let card = ReportCards.For(try conclusion(from: allUntestedBundle()))

        XCTAssertEqual(card.grade, .ungraded,
                       "a never-run scan must not receive a letter grade")
        XCTAssertNotEqual(card.severity, .clean)
    }
}
