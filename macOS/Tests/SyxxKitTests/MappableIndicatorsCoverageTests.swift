import XCTest
@testable import SyxxKit

/// F-09 / B156 — the coverage denominator must be normalised over *mappable* indicator weight only.
/// Rules referencing indicators with no reachable EvidenceId mapping are permanent dead weight:
/// they can never be observed, so counting them hard-caps coverage below the emission thresholds
/// and makes entire diagnoses unreachable.
///
/// B156 landed here without a test on either platform, and the shared golden vectors do NOT
/// discriminate on it (gv-3's stored coverage 0.560976 equals the *unfiltered* ratio 2.3/4.1, so
/// that number predates B156). This is the discriminating test: it fails if the filter is removed.
///
/// Twin of Windows/dotNET/Syxx.Tests/MappableIndicatorsCoverageTests.cs.
final class MappableIndicatorsCoverageTests: XCTestCase {

    // Half the support weight is an unmappable indicator. Observed: only the mappable one, fully.
    //   filtered denominator   = 1.0  -> coverage 1.0  >= 0.55 -> emitted
    //   unfiltered denominator = 2.0  -> coverage 0.5  <  0.55 -> not emitted
    private func spec(mappable: Set<IndicatorID>?) -> MatrixSpec {
        MatrixSpec(
            diagnoses: [
                DiagnosisSpec(
                    id: DiagnosisID("coverageProbe"),
                    displayName: "Coverage probe",
                    rules: [
                        DiagnosisIndicatorRule(indicatorID: IndicatorID("mappedSignal"),
                                               polarity: .supports,
                                               expectedStrength: 0.9, weight: 1.0,
                                               evidenceClass: .decisive, independenceGroup: "g1"),
                        DiagnosisIndicatorRule(indicatorID: IndicatorID("ghostSignal"),
                                               polarity: .supports,
                                               expectedStrength: 0.9, weight: 1.0,
                                               evidenceClass: .major, independenceGroup: "g2"),
                    ],
                    requiredSections: [SectionID("prophet")]),
            ],
            thresholds: ThresholdProfile(
                minimumCoverageForProbable: 0.55,
                minimumCoverageForConclusive: 0.80,
                minimumFitForProbable: 0.62,
                minimumFitForConclusive: 0.84,
                minimumMarginForProbable: 0.08,
                minimumMarginForConclusive: 0.18,
                maxContradictionForConclusive: 0.10,
                maxContradictionForProbable: 0.22,
                minimumIndependentSupportGroupsForProbable: 1,
                minimumIndependentSupportGroupsForConclusive: 2,
                requireDecisiveOrTwoMajorsForConclusive: true,
                hardGateOnMissingRequiredSections: true,
                failClosedOnInvalidatedRequiredSections: true),
            mappableIndicators: mappable)
    }

    private func fullyObservedMappedSignal() -> EvidenceBundle {
        EvidenceBundle(
            sections: [SectionResult(id: SectionID("prophet"), status: .passed)],
            indicators: [ObservedIndicator(id: IndicatorID("mappedSignal"),
                                           section: SectionID("prophet"),
                                           observedStrength: 0.9)])
    }

    func testMappableFilterExcludesDeadWeightFromCoverage_diagnosisEmitted() throws {
        let engine = try MatrixDeterminativeEngine(spec: spec(mappable: [IndicatorID("mappedSignal")]))
        let result = try engine.evaluate(fullyObservedMappedSignal())

        XCTAssertEqual(result.state, .threatDetected)
        XCTAssertEqual(result.diagnoses.count, 1)
        XCTAssertEqual(result.diagnoses.first?.id.rawValue, "coverageProbe")
    }

    /// Legacy semantics pinned: with `mappableIndicators` nil, ALL rules count and the same
    /// evidence must NOT reach the probable tier. This also proves the two specs genuinely
    /// produce different outcomes, so the test above cannot pass by accident.
    func testNilMappableLegacyDenominator_diagnosisNotEmitted() throws {
        let engine = try MatrixDeterminativeEngine(spec: spec(mappable: nil))
        let result = try engine.evaluate(fullyObservedMappedSignal())

        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty)
    }
}
