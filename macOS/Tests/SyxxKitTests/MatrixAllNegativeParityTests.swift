import XCTest
@testable import SyxxKit

/// macOS half of the ALL-NEGATIVE cross-platform convergence guard
/// (UNIFIED_IMPLEMENTATION_PLAN.md Phase 7.1 QA-1). Twin of
/// `Windows/dotNET/Syxx.Tests/MatrixAllNegativeParityTests.cs` — same two fixtures, same asserted
/// states, so whichever platform drifts is the one that goes red.
///
/// THE DEFECT THIS EXISTS TO PREVENT
/// ---------------------------------
/// The retired Dempster-Shafer suite asserted that all-negative section statuses with NO observed
/// evidence committed to a reassuring clean result:
///     archive/dst-tests-windows/SynthesisEngineTests.cs:22-25
///         `Assert.True(c.ReadsClean(), "an all-clean complete run must commit to clean")`
/// The current matrix engine returns `.inconclusive` for that same input. For a period both suites
/// were green while asserting OPPOSITE answers to identical evidence, because each platform was
/// exercising a different engine. Retiring the DST engine removed the wrong answer but ALSO removed
/// the only test that pinned this scenario at all: after the archive/quarantine move, NO test on
/// EITHER platform asserted what all-negative evidence produces. This file restores that assertion
/// against the engine that actually ships, rather than leaving the question unpinned.
///
/// WHY `.inconclusive` IS THE CORRECT ANSWER, NOT `.clean`
/// -------------------------------------------------------
/// Engine doc-comment, CRITICAL DISTINCTION (MatrixDeterminativeEngine.swift:16-22):
/// `.inconclusive` means the check RAN and produced VALID data that did not resolve; it is USABLE
/// evidence and does NOT hard-gate. It is a decision state, not a synonym for "no result".
/// The clean branch (MatrixDeterminativeEngine.swift:894-901, Windows twin :1108-1116) emits
/// `.clean` ONLY when the `clean` diagnosis was itself AFFIRMATIVELY emitted — i.e. its supporting
/// indicators were actually observed. Sections that merely "ran and found nothing" observe no
/// indicators at all, so nothing supports clean and the engine abstains. Under the honesty spine
/// (rule 2, missing data is not clean data) that is the required behaviour: absence of observed
/// threat evidence is not evidence of absence, and the reassuring answer must earn its place.
final class MatrixAllNegativeParityTests: XCTestCase {

    private let allSectionIDs = ["prophet", "mirror", "portal", "glutton", "cage", "witness"]

    private func allSectionsRanAndPassed() -> [SectionResult] {
        allSectionIDs.map { SectionResult(id: SectionID($0), status: .passed) }
    }

    /// FIXTURE A — the exact input the retired DST test called "an all-clean complete run":
    /// every section RAN and produced valid data, and not one indicator was observed.
    ///
    /// Failure case: if the clean branch ever stops requiring an affirmatively emitted `clean`
    /// diagnosis (e.g. someone makes clean the fall-through default), this returns `.clean` → red.
    func testAllSectionsPassedWithNoObservedIndicators_isInconclusiveNeverClean() throws {
        let bundle = EvidenceBundle(sections: allSectionsRanAndPassed(), indicators: [])

        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)

        // Assert the POSITIVE state. `!= .clean` alone would also pass on .untested/.threatDetected.
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertNotEqual(result.state, .clean)
        // Sections RAN, so this is NOT the never-looked case either.
        XCTAssertNotEqual(result.state, .untested)
        // HARD INVARIANT: non-scored states are never emitted as a ranked diagnosis.
        XCTAssertTrue(result.diagnoses.isEmpty)
    }

    /// FIXTURE B — what an EARNED clean actually requires: the same all-passed sections, plus the
    /// four indicators the `clean` DiagnosisSpec lists as `.supports`, actually observed at their
    /// expected strengths.
    ///
    /// This is the positive half of the guard and it is deliberate: before this file, every
    /// clean-related assertion in BOTH suites was a negative (`XCTAssertNotEqual(.clean)`), so the
    /// entire clean-emission path could have been dead code and both suites would still have been
    /// green. A suite that can only prove clean is NOT produced cannot detect that clean can never
    /// be produced — which would make the product unable to ever tell a user their DNS is fine.
    ///
    /// Failure case: break the clean branch or the clean spec's rules → `.inconclusive` → red.
    func testAllSectionsPassedWithCleanSupportingIndicators_isClean() throws {
        let bundle = EvidenceBundle(
            sections: allSectionsRanAndPassed(),
            indicators: [
                ObservedIndicator(id: IndicatorID("resolverAgreement"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
                ObservedIndicator(id: IndicatorID("routeClean"),
                                  section: SectionID("portal"), observedStrength: 0.90),
                ObservedIndicator(id: IndicatorID("trustedWitnessOK"),
                                  section: SectionID("witness"), observedStrength: 0.95),
                ObservedIndicator(id: IndicatorID("localEvidenceSealValid"),
                                  section: SectionID("cage"), observedStrength: 0.95),
            ])

        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)

        XCTAssertEqual(result.state, .clean)
        XCTAssertEqual(result.diagnoses.count, 1)
        XCTAssertEqual(result.diagnoses.first?.id.rawValue, "clean")
    }

    /// The wire value is the cross-platform contract that the CI parity gate
    /// (scripts/cross-platform-parity-gate.py) also asserts from source. Pinning it from a LIVE
    /// engine result too means a rename that changes what the engine actually emits — as opposed to
    /// what the enum declares — cannot pass unnoticed.
    func testInconclusiveFromAllNegativeSerialisesToTheSharedWireValue() throws {
        let bundle = EvidenceBundle(sections: allSectionsRanAndPassed(), indicators: [])

        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)

        XCTAssertEqual(result.state.rawValue, "inconclusive")
    }
}
