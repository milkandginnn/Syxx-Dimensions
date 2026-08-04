import XCTest
@testable import SyxxKit

/// P2b (MATRIX-MIGRATION-PLAN.md §3, Step 2b) — the matrix engine's own unit/vector suite.
/// Items 11 (C063 quality attenuation, GV-7 shape) and 12 (C062 belowTier overhang, GV-8/GV-9
/// shape) land here with their Phase-1 fixes; the remaining 2b items (spec/bundle validation,
/// abstention property grid, exclusion/tie-break/rounding vectors) follow with the rest of
/// Phase 2b. Every test cites the design intent (plan step / delta ID / engine doc-comment),
/// not the code line it was copied from.
final class MatrixDeterminativeEngineTests: XCTestCase {

    // MARK: - Item 11 / C063 / GV-7: quality attenuates strength AND tier

    /// Plan Step 1.3 (C063): an indicator observed on a 20%-complete section must weigh measurably
    /// less than the identical observation on a full section — different `strength` AND a
    /// different tier (probable vs not emitted). Failure case: drop `clamp01(obs.quality)` from
    /// the engine's `effective` term (rawScore) → both evaluations emit identically → red.
    func testC063_identicalBundleAtQuality1vsQuality02_differsInStrengthAndTier() throws {
        func bundle(quality: Double) -> EvidenceBundle {
            EvidenceBundle(
                sections: [
                    SectionResult(id: SectionID("prophet"), status: .passed),
                    SectionResult(id: SectionID("mirror"), status: .passed),
                    SectionResult(id: SectionID("witness"), status: .passed),
                ],
                indicators: [
                    ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                      section: SectionID("prophet"),
                                      observedStrength: 0.95, quality: quality),
                    ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                      section: SectionID("prophet"),
                                      observedStrength: 0.90, quality: quality),
                ])
        }
        let engine = try SyxxMatrixIntegration.makeEngine()

        let full = try engine.evaluate(bundle(quality: 1.0))
        XCTAssertEqual(full.state, .threatDetected, "full-quality decisive+major support must emit")
        let top = try XCTUnwrap(full.diagnoses.first)
        XCTAssertEqual(top.id.rawValue, "dnsSpoofing")
        XCTAssertEqual(top.confidence, .probable)
        XCTAssertGreaterThan(top.strength, 0.5)

        let thin = try engine.evaluate(bundle(quality: 0.2))
        XCTAssertEqual(thin.state, .inconclusive,
                       "the SAME observations at quality 0.2 must not support a named threat")
        XCTAssertTrue(thin.diagnoses.isEmpty, "abstention never carries ranked diagnoses")
        // C062 interplay: the attenuated-but-SEEN signals must not vanish silently — they join
        // the overhang as belowTier (quality attenuates scoring, never hides a fired signal).
        XCTAssertTrue(thin.suppressed.contains { $0.id.rawValue == "dnsSpoofing" && $0.reason == .belowTier },
                      "attenuated threat support must surface as belowTier overhang, got \(thin.suppressed)")
    }

    // MARK: - Item 12 / C062 / GV-8: a sub-probable, non-gated threat joins the overhang

    private func allSectionsPassed(_ ids: [String] = ["prophet", "mirror", "portal", "glutton", "cage", "witness"]) -> [SectionResult] {
        ids.map { SectionResult(id: SectionID($0), status: .passed) }
    }

    /// Plan Step 1.4 guard (i): real observed support (≥ overhangFloor) below the probable tier,
    /// no gates → `.inconclusive`, EMPTY diagnoses, and the threat NAMED as `.belowTier`.
    /// Failure case: remove the belowTier append in the pass-3 tier miss → suppressed empty → red.
    func testC062_weakThreatOnly_isInconclusiveWithBelowTierOverhang() throws {
        let bundle = EvidenceBundle(
            sections: allSectionsPassed(),
            indicators: [
                // signedAnswerMismatch 0.80: observedSupport ≈ 0.98 (≥ 0.30) but coverage ≈ 0.27,
                // far below the probable tier — the exact silent-vanish shape C062 forbids.
                ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.80),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty, "abstention never carries ranked diagnoses")
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "dnsSpoofing" && $0.reason == .belowTier },
                      "a seen-but-sub-tier threat must be NAMED, not dropped; got \(result.suppressed)")
        XCTAssertTrue(result.explanation.lowercased().contains("not treated as clean"),
                      "the abstention explanation must state the never-clean posture")
    }

    /// Plan Step 1.4 guard (ii), the C062 killer vector (GV-9 shape): weak threat + STRONG clean
    /// support (hand-built — production never wires clean indicators pre-Step-1.5) must NOT return
    /// `.clean`. Failure case: remove the belowTier append, or re-filter the overhang on
    /// `rawStrength` (the C065 defect) → `.clean` over a seen threat signal → red.
    func testC062_weakThreatPlusStrongCleanSupport_neverReturnsClean() throws {
        let bundle = EvidenceBundle(
            sections: allSectionsPassed(),
            indicators: [
                // Affirmative clean pattern at full strength (deliberately WITHOUT
                // trustedWitnessOK, which is exclusionary for dnsSpoofing and would convert the
                // threat into a hard exclusion — a different, legitimately clean-permitting case).
                ObservedIndicator(id: IndicatorID("resolverAgreement"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
                ObservedIndicator(id: IndicatorID("routeClean"),
                                  section: SectionID("portal"), observedStrength: 0.90),
                ObservedIndicator(id: IndicatorID("localEvidenceSealValid"),
                                  section: SectionID("witness"), observedStrength: 0.95),
                // The seen-but-weak threat signal.
                ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.80),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertNotEqual(result.state, .clean,
            "C062: a clean headline over a real, unconfirmed threat observation is the exact retired-engine disease")
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "dnsSpoofing" && $0.reason == .belowTier })
    }

    /// Plan Step 1.4 guard (iii), the trivial-noise floor: observed support BELOW the overhang
    /// floor must yield a plain `.inconclusive` with an EMPTY overhang — crying wolf on nothing
    /// would train users to ignore the overhang. Failure case: drop the overhangFloor comparison
    /// from the belowTier append → the noise entry appears → red.
    func testC062_trivialNoiseStaysOutOfTheOverhang() throws {
        let bundle = EvidenceBundle(
            sections: allSectionsPassed(),
            indicators: [
                // resolverDisagreement at 0.20: observedSupport = 0.8·(0.20/0.80) = 0.20 < 0.30.
                ObservedIndicator(id: IndicatorID("resolverDisagreement"),
                                  section: SectionID("prophet"), observedStrength: 0.20),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty)
        XCTAssertTrue(result.suppressed.isEmpty,
                      "sub-floor noise must not inflate the overhang; got \(result.suppressed)")
    }

    /// Plan Step 1.4 sanity (step 5): suppression happens pre-exclusion-resolution, so a belowTier
    /// rival never knocks out an emitted diagnosis — the run stays `.threatDetected` with the
    /// rival LISTED in suppressed, not dropped and not emitted.
    func testC062_belowTierRivalNeverDisplacesAnEmittedThreat() throws {
        let bundle = EvidenceBundle(
            sections: allSectionsPassed(),
            indicators: [
                // dnsSpoofing at probable (decisive + major, full quality).
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
                ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.90),
                // dnsPrivacyLeak support below tier (coverage ≈ 0.55 boundary miss).
                ObservedIndicator(id: IndicatorID("dnsLeakDetected"),
                                  section: SectionID("glutton"), observedStrength: 0.60),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertEqual(result.state, .threatDetected)
        XCTAssertEqual(result.diagnoses.first?.id.rawValue, "dnsSpoofing")
        XCTAssertFalse(result.diagnoses.contains { $0.id.rawValue == "dnsPrivacyLeak" })
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "dnsPrivacyLeak" && $0.reason == .belowTier },
                      "the sub-tier rival must be listed, not silently dropped; got \(result.suppressed)")
    }

    /// GV-1 pin at the engine level: zero evidence ⇒ `.inconclusive`, EMPTY diagnoses, EMPTY
    /// overhang. The dead uniform-12% rendering can never return. Failure case: emit anything
    /// (a diagnosis, a suppressed entry, or a non-inconclusive state) from nothing → red.
    func testZeroEvidence_isUntestedEmptyEverything() throws {
        let bundle = EvidenceBundle(sections: allSectionsPassed(), indicators: [])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty)
        XCTAssertTrue(result.suppressed.isEmpty)
    }

    // MARK: - Item 1 / Spec validation: every MatrixEngineError construction path throws

    /// Plan 2b item 1: a well-formed spec must pass validation; every error path must throw the
    /// correct `MatrixEngineError` with the correct associated values. Failure case: remove the
    /// validation check for any error → that construction path no longer throws → red.
    func testSpecValidation_emptySpecThrows() {
        let spec = MatrixSpec(diagnoses: [], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError, .emptySpec)
        }
    }

    func testSpecValidation_duplicateDiagnosisThrows() {
        let d1 = minimalDiagnosisSpec(id: "dup")
        let d2 = minimalDiagnosisSpec(id: "dup")
        let spec = MatrixSpec(diagnoses: [d1, d2], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError, .duplicateDiagnosis(DiagnosisID("dup")))
        }
    }

    func testSpecValidation_diagnosisHasNoRulesThrows() {
        let d = DiagnosisSpec(id: DiagnosisID("noRules"), displayName: "N", rules: [])
        let spec = MatrixSpec(diagnoses: [d], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError, .diagnosisHasNoRules(DiagnosisID("noRules")))
        }
    }

    func testSpecValidation_unknownRequiredSectionThrows() {
        let d = DiagnosisSpec(id: DiagnosisID("badSec"), displayName: "B", rules: [minimalRule()],
                              requiredSections: [SectionID("nonexistent")])
        let spec = MatrixSpec(diagnoses: [d], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError,
                           .unknownRequiredSection(diagnosis: DiagnosisID("badSec"), section: SectionID("nonexistent")))
        }
    }

    func testSpecValidation_contradictoryIndicatorRoleThrows() {
        let indicator = IndicatorID("dual")
        let d = DiagnosisSpec(id: DiagnosisID("contra"), displayName: "C",
                              rules: [
                                  DiagnosisIndicatorRule(indicatorID: indicator, polarity: .supports,
                                                         expectedStrength: 1.0, weight: 1.0,
                                                         evidenceClass: .major, independenceGroup: "g"),
                              ],
                              exclusionaryIndicators: [indicator])
        let spec = MatrixSpec(diagnoses: [d], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError,
                           .contradictoryIndicatorRole(diagnosis: DiagnosisID("contra"), indicator: indicator))
        }
    }

    func testSpecValidation_duplicateRuleThrows() {
        let indicator = IndicatorID("dupRule")
        let d = DiagnosisSpec(id: DiagnosisID("dupR"), displayName: "D",
                              rules: [
                                  DiagnosisIndicatorRule(indicatorID: indicator, polarity: .supports,
                                                         expectedStrength: 1.0, weight: 1.0,
                                                         evidenceClass: .major, independenceGroup: "g"),
                                  DiagnosisIndicatorRule(indicatorID: indicator, polarity: .supports,
                                                         expectedStrength: 0.5, weight: 0.5,
                                                         evidenceClass: .minor, independenceGroup: "g2"),
                              ])
        let spec = MatrixSpec(diagnoses: [d], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError,
                           .duplicateRule(diagnosis: DiagnosisID("dupR"), indicator: indicator))
        }
    }

    func testSpecValidation_strengthOutOfRangeThrows() {
        let d = DiagnosisSpec(id: DiagnosisID("badStr"), displayName: "B",
                              rules: [DiagnosisIndicatorRule(indicatorID: IndicatorID("i"), polarity: .supports,
                                                             expectedStrength: 1.5, weight: 1.0,
                                                             evidenceClass: .major, independenceGroup: "g")])
        let spec = MatrixSpec(diagnoses: [d], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError,
                           .strengthOutOfRange(diagnosis: DiagnosisID("badStr"), indicator: IndicatorID("i")))
        }
    }

    func testSpecValidation_invalidRuleParameterThrows() {
        let d = DiagnosisSpec(id: DiagnosisID("badParam"), displayName: "B",
                              rules: [DiagnosisIndicatorRule(indicatorID: IndicatorID("i"), polarity: .supports,
                                                             expectedStrength: 1.0, weight: -1.0,
                                                             evidenceClass: .major, independenceGroup: "g")])
        let spec = MatrixSpec(diagnoses: [d], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError,
                           .invalidRuleParameter(diagnosis: DiagnosisID("badParam"), indicator: IndicatorID("i")))
        }
    }

    func testSpecValidation_danglingReferenceThrows() {
        let d = DiagnosisSpec(id: DiagnosisID("dangler"), displayName: "D", rules: [minimalRule()],
                              mutuallyExclusiveWith: [DiagnosisID("phantom")])
        let spec = MatrixSpec(diagnoses: [d], thresholds: minimalThresholds())
        XCTAssertThrowsError(try spec.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError,
                           .danglingReference(diagnosis: DiagnosisID("dangler"), referenced: DiagnosisID("phantom")))
        }
    }

    // MARK: - Item 2 / Bundle validation: duplicate section/indicator, out-of-range strength/quality

    /// Plan 2b item 2: the bundle's own validate() must catch structural errors before the engine
    /// processes them. Failure case: remove the duplicate check → engine silently processes the
    /// second occurrence (order-dependent double-counting) → red.
    func testBundleValidation_duplicateSectionThrows() throws {
        let bundle = EvidenceBundle(sections: [
            SectionResult(id: SectionID("prophet"), status: .passed),
            SectionResult(id: SectionID("prophet"), status: .passed),
        ], indicators: [])
        XCTAssertThrowsError(try bundle.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError, .duplicateSectionInBundle(SectionID("prophet")))
        }
    }

    func testBundleValidation_duplicateIndicatorThrows() throws {
        let bundle = EvidenceBundle(sections: allSectionsPassed(), indicators: [
            ObservedIndicator(id: IndicatorID("dup"), section: SectionID("prophet"), observedStrength: 0.5),
            ObservedIndicator(id: IndicatorID("dup"), section: SectionID("prophet"), observedStrength: 0.6),
        ])
        XCTAssertThrowsError(try bundle.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError, .duplicateIndicatorInBundle(IndicatorID("dup")))
        }
    }

    func testBundleValidation_indicatorStrengthOutOfRangeThrows() throws {
        let bundle = EvidenceBundle(sections: allSectionsPassed(), indicators: [
            ObservedIndicator(id: IndicatorID("bad"), section: SectionID("prophet"), observedStrength: 1.5),
        ])
        XCTAssertThrowsError(try bundle.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError, .indicatorStrengthOutOfRange(IndicatorID("bad")))
        }
    }

    func testBundleValidation_indicatorQualityOutOfRangeThrows() throws {
        let bundle = EvidenceBundle(sections: allSectionsPassed(), indicators: [
            ObservedIndicator(id: IndicatorID("badQ"), section: SectionID("prophet"),
                              observedStrength: 0.5, quality: -0.1),
        ])
        XCTAssertThrowsError(try bundle.validate()) { error in
            XCTAssertEqual(error as? MatrixEngineError, .indicatorQualityOutOfRange(IndicatorID("badQ")))
        }
    }

    // MARK: - Item 3 / Abstention invariant: .inconclusive ⇒ diagnoses EMPTY

    /// Plan 2b item 3: the hard invariant from the engine doc-comment — `.inconclusive` carries an
    /// EMPTY ranked-diagnoses list, always. Property-style over a grid of degenerate bundles.
    /// Failure case: emit a diagnosis with `.inconclusive` state → red.
    func testAbstentionInvariant_untestedAlwaysHasEmptyDiagnoses() throws {
        let engine = try SyxxMatrixIntegration.makeEngine()

        // Grid of degenerate bundles that should all yield .inconclusive.
        let bundles: [(String, EvidenceBundle)] = [
            ("zero evidence",
             EvidenceBundle(sections: allSectionsPassed(), indicators: [])),
            ("all untested",
             EvidenceBundle(sections: [
                SectionResult(id: SectionID("prophet"), status: .inconclusive),
                SectionResult(id: SectionID("mirror"), status: .inconclusive),
                SectionResult(id: SectionID("portal"), status: .inconclusive),
                SectionResult(id: SectionID("glutton"), status: .inconclusive),
                SectionResult(id: SectionID("cage"), status: .inconclusive),
                SectionResult(id: SectionID("witness"), status: .inconclusive),
             ], indicators: [])),
            ("weak noise below overhang floor",
             EvidenceBundle(sections: allSectionsPassed(), indicators: [
                ObservedIndicator(id: IndicatorID("resolverDisagreement"),
                                  section: SectionID("prophet"), observedStrength: 0.10),
             ])),
            ("missing required section with support",
             EvidenceBundle(sections: [SectionResult(id: SectionID("prophet"), status: .passed)],
                            indicators: [
                                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                                  section: SectionID("prophet"), observedStrength: 0.95),
                            ])),
        ]

        for (label, bundle) in bundles {
            let result = try engine.evaluate(bundle)
            XCTAssertEqual(result.state, .inconclusive,
                           "\(label): must be inconclusive, got \(result.state)")
            XCTAssertTrue(result.diagnoses.isEmpty,
                          "\(label): abstention must have empty diagnoses, got \(result.diagnoses.map { $0.id.rawValue })")
        }
    }

    // MARK: - Item 5 / GV-2: all-inconclusive sections ⇒ inconclusive

    /// F037: renamed from `testAllUntestedSections_isUntestedWithGatedDiagnoses`. The fixture uses
    /// `.inconclusive` (ran, valid data, did not resolve), not `.untested` (never ran). The real
    /// `.untested` coverage lives in `MatrixUntestedStateTests.swift`. Pin the suppression reason
    /// to `.belowTier` (the outcome that actually occurs for inconclusive sections with a strong
    /// indicator below the `.probable` tier) so a regression in the gating path fails this test.
    func testAllInconclusiveSections_isInconclusiveWithGatedDiagnoses() throws {
        let bundle = EvidenceBundle(
            sections: [
                SectionResult(id: SectionID("prophet"), status: .inconclusive),
                SectionResult(id: SectionID("mirror"), status: .inconclusive),
                SectionResult(id: SectionID("portal"), status: .inconclusive),
                SectionResult(id: SectionID("glutton"), status: .inconclusive),
                SectionResult(id: SectionID("cage"), status: .inconclusive),
                SectionResult(id: SectionID("witness"), status: .inconclusive),
            ],
            indicators: [
                // A strong signal that falls below the `.probable` tier is suppressed, not emitted.
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty)
        // dnsSpoofing requires prophet+mirror+witness — all inconclusive → suppressed as belowTier.
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "dnsSpoofing" && $0.reason == .belowTier },
                      "dnsSpoofing with inconclusive required sections must be suppressed as belowTier; got \(result.suppressed)")
    }

    // MARK: - Item 6 / GV-3: single decisive threat ⇒ threatDetected

    /// Plan 2b item 6 / GV-3: a single decisive threat at full strength, with required sections
    /// passed, must emit `.threatDetected` with the correct diagnosis and supporting indicators.
    /// Failure case: drop the decisive observation → threat not emitted → red.
    func testSingleDecisiveThreat_isThreatDetectedWithCorrectDiagnosis() throws {
        let bundle = EvidenceBundle(
            sections: [
                SectionResult(id: SectionID("prophet"), status: .passed),
                SectionResult(id: SectionID("mirror"), status: .passed),
                SectionResult(id: SectionID("witness"), status: .passed),
            ],
            indicators: [
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
                ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.90),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertEqual(result.state, .threatDetected)
        let top = try XCTUnwrap(result.diagnoses.first)
        XCTAssertEqual(top.id.rawValue, "dnsSpoofing")
        XCTAssertEqual(top.confidence, .probable)
        XCTAssertTrue(top.supportingIndicators.contains(IndicatorID("authoritativeMismatch")),
                      "supportingIndicators must include the decisive observation")
        XCTAssertTrue(top.supportingIndicators.contains(IndicatorID("signedAnswerMismatch")))
        XCTAssertGreaterThan(top.strength, 0.5)
        XCTAssertTrue(top.explanation.contains("fit"), "explanation must contain actual metrics")
        XCTAssertTrue(top.explanation.contains("coverage"))
        XCTAssertTrue(top.explanation.contains("margin"))
    }

    // MARK: - Item 7 / GV-4: hard-gated overhang ⇒ inconclusive, never clean

    /// Plan 2b item 7 / GV-4: threat support ≥ overhangFloor with a required section missing or
    /// invalidated → `.inconclusive` + suppressed with the correct gate reason. NEVER clean.
    /// Failure case: skip the gate check → threat may emit or clean may appear → red.
    func testHardGatedOverhang_isUntestedNeverClean() throws {
        // dnsSpoofing requires prophet+mirror+witness. Only provide prophet.
        let bundle = EvidenceBundle(
            sections: [
                SectionResult(id: SectionID("prophet"), status: .passed),
            ],
            indicators: [
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertNotEqual(result.state, .clean,
                          "GV-4: missing required section must never permit clean")
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty)
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "dnsSpoofing" && $0.reason == .missingRequiredSection },
                      "dnsSpoofing must be gated with missingRequiredSection; got \(result.suppressed)")
    }

    /// Also test invalidated-required-section gate (failClosedOnInvalidatedRequiredSections = true).
    func testInvalidatedRequiredSection_gatesDiagnosis() throws {
        let bundle = EvidenceBundle(
            sections: [
                SectionResult(id: SectionID("prophet"), status: .passed),
                SectionResult(id: SectionID("mirror"), status: .passed),
                SectionResult(id: SectionID("witness"), status: .invalidated),
            ],
            indicators: [
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "dnsSpoofing" && $0.reason == .invalidatedRequiredSection },
                      "invalidated required section must gate the diagnosis; got \(result.suppressed)")
    }

    // MARK: - Item 8 / GV-5: hard exclusion dominates

    /// Plan 2b item 8 / GV-5: a firing exclusionary indicator suppresses the diagnosis with
    /// `.hardExcluded` regardless of how much support it has (exclusionFiringThreshold = 0.5).
    /// Failure case: drop the exclusion check → supported threat emits despite exclusion → red.
    func testHardExclusionDominates_overSupportedThreat() throws {
        // dnsSpoofing has trustedWitnessOK as exclusionary. Provide strong support AND the
        // exclusionary indicator firing at 0.95.
        let bundle = EvidenceBundle(
            sections: [
                SectionResult(id: SectionID("prophet"), status: .passed),
                SectionResult(id: SectionID("mirror"), status: .passed),
                SectionResult(id: SectionID("witness"), status: .passed),
            ],
            indicators: [
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
                ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.90),
                ObservedIndicator(id: IndicatorID("trustedWitnessOK"),
                                  section: SectionID("witness"), observedStrength: 0.95),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertFalse(result.diagnoses.contains { $0.id.rawValue == "dnsSpoofing" },
                       "dnsSpoofing must NOT emit when its exclusionary indicator fires")
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "dnsSpoofing" && $0.reason == .hardExcluded },
                      "dnsSpoofing must be suppressed with hardExcluded; got \(result.suppressed)")
    }

    /// Exclusionary indicator below the firing threshold does NOT suppress.
    func testHardExclusion_belowThresholdDoesNotSuppress() throws {
        let bundle = EvidenceBundle(
            sections: [
                SectionResult(id: SectionID("prophet"), status: .passed),
                SectionResult(id: SectionID("mirror"), status: .passed),
                SectionResult(id: SectionID("witness"), status: .passed),
            ],
            indicators: [
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
                ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.90),
                // Exclusionary indicator below 0.5 threshold — does NOT suppress.
                ObservedIndicator(id: IndicatorID("trustedWitnessOK"),
                                  section: SectionID("witness"), observedStrength: 0.40),
            ])
        let result = try SyxxMatrixIntegration.makeEngine().evaluate(bundle)
        XCTAssertTrue(result.diagnoses.contains { $0.id.rawValue == "dnsSpoofing" },
                      "dnsSpoofing must emit when exclusionary indicator is below firing threshold")
    }

    // MARK: - Item 9 / GV-6: mutual-exclusion tie-break determinism

    /// Plan 2b item 9 / GV-6: when two mutually-exclusive diagnoses have equal strength AND equal
    /// fit, the tie-break is deterministic by id rawValue order (lower wins). Run 100× across
    /// dictionary-order permutations to prove outcome-invariance. Failure case: flip the tie-break
    /// comparator → a different diagnosis wins → red.
    func testMutualExclusionTieBreak_isDeterministicByIdOrder() throws {
        // Build a custom minimal spec with two mutually-exclusive diagnoses that share the same
        // rule, producing identical strength and fit. Margin thresholds are zeroed so the
        // diagnoses pass tiering (they are competitors, so margin = strength - strength = 0).
        let indicator = IndicatorID("shared")
        let diagA = DiagnosisSpec(id: DiagnosisID("aaa"), displayName: "A",
                                  rules: [DiagnosisIndicatorRule(indicatorID: indicator, polarity: .supports,
                                                                  expectedStrength: 1.0, weight: 1.0,
                                                                  evidenceClass: .major, independenceGroup: "g")],
                                  requiredSections: [],
                                  mutuallyExclusiveWith: [DiagnosisID("bbb")])
        let diagB = DiagnosisSpec(id: DiagnosisID("bbb"), displayName: "B",
                                  rules: [DiagnosisIndicatorRule(indicatorID: indicator, polarity: .supports,
                                                                  expectedStrength: 1.0, weight: 1.0,
                                                                  evidenceClass: .major, independenceGroup: "g")],
                                  requiredSections: [],
                                  mutuallyExclusiveWith: [DiagnosisID("aaa")])
        let thresholds = ThresholdProfile(
            minimumCoverageForProbable: 0.55, minimumCoverageForConclusive: 0.80,
            minimumFitForProbable: 0.62, minimumFitForConclusive: 0.84,
            minimumMarginForProbable: 0.0, minimumMarginForConclusive: 0.0,
            maxContradictionForConclusive: 0.10, maxContradictionForProbable: 0.22,
            minimumIndependentSupportGroupsForProbable: 1,
            minimumIndependentSupportGroupsForConclusive: 2,
            requireDecisiveOrTwoMajorsForConclusive: false,
            hardGateOnMissingRequiredSections: false,
            failClosedOnInvalidatedRequiredSections: false
        )
        let spec = MatrixSpec(diagnoses: [diagA, diagB], thresholds: thresholds)
        let engine = try MatrixDeterminativeEngine(spec: spec)
        let bundle = EvidenceBundle(
            sections: [SectionResult(id: SectionID("prophet"), status: .passed)],
            indicators: [ObservedIndicator(id: indicator, section: SectionID("prophet"), observedStrength: 1.0)]
        )

        // Run 100× — the outcome must be identical every time.
        var winnerIDs: [String] = []
        for _ in 0..<100 {
            let result = try engine.evaluate(bundle)
            XCTAssertEqual(result.state, .threatDetected)
            XCTAssertEqual(result.diagnoses.count, 1, "exactly one of the mutually-exclusive pair must survive")
            winnerIDs.append(result.diagnoses[0].id.rawValue)
        }
        let uniqueWinners = Set(winnerIDs)
        XCTAssertEqual(uniqueWinners.count, 1, "tie-break must be deterministic; got \(uniqueWinners)")
        XCTAssertEqual(uniqueWinners.first, "aaa", "lower rawValue id must win the tie-break")
    }

    // MARK: - Item 10 / GV-10: round6 boundary

    /// Plan 2b item 10 / GV-10: Banker's rounding at the 6th decimal place. The engine uses
    /// `.toNearestOrEven` (banker's rounding), not schoolbook away-from-zero. These tie values
    /// MUST produce the exact bit pattern specified. Failure case: change the rounding mode →
    /// tie values differ → red.
    func testRound6_boundaryTies() {
        // Banker's rounding: ties round to even.
        // 0.0000005 → exactly 5 at the 7th decimal → rounds to 0.000000 (even last digit)
        let r1 = round6(0.0000005)
        XCTAssertEqual(r1, 0.0, "0.0000005 banker's-rounds to 0.000000 (even)")

        // 0.0000015 → exactly 15 at the 7th decimal → rounds to 0.000002 (even)
        let r2 = round6(0.0000015)
        XCTAssertEqual(r2, 0.000002, "0.0000015 banker's-rounds to 0.000002 (even)")

        // 0.0000025 → exactly 25 at the 7th decimal → 0.000002 (even)
        let r3 = round6(0.0000025)
        XCTAssertEqual(r3, 0.000002, "0.0000025 banker's-rounds to 0.000002 (even)")

        // 0.0000035 → 0.000004 (even)
        let r4 = round6(0.0000035)
        XCTAssertEqual(r4, 0.000004, "0.0000035 banker's-rounds to 0.000004 (even)")

        // Negative ties: -0.0000005 → 0.0 (even)
        let r5 = round6(-0.0000005)
        XCTAssertEqual(r5, 0.0, "-0.0000005 banker's-rounds to 0.000000 (even)")

        // -0.0000015 → -0.000002
        let r6 = round6(-0.0000015)
        XCTAssertEqual(r6, -0.000002, "-0.0000015 banker's-rounds to -0.000002 (even)")

        // Non-tie: 0.0000007 → 0.000001 (normal rounding)
        let r7 = round6(0.0000007)
        XCTAssertEqual(r7, 0.000001, "0.0000007 rounds to 0.000001")

        // Non-finite: NaN/Inf → 0
        XCTAssertEqual(round6(Double.nan), 0.0, "NaN must round to 0")
        XCTAssertEqual(round6(Double.infinity), 0.0, "inf must round to 0")
        XCTAssertEqual(round6(-Double.infinity), 0.0, "-inf must round to 0")

        // Large value: 1.0 stays 1.0
        XCTAssertEqual(round6(1.0), 1.0)
        XCTAssertEqual(round6(0.9999999), 1.0)
    }

    // MARK: - Item 13: contradiction bounded

    /// Plan 2b item 13: a contradictory observation attenuates a threat but can never
    /// arithmetically zero it. The engine doc-comment at :24-27 states the exclusionary /
    /// contradiction mass is bounded — it can attenuate, never silently zero.
    /// Failure case: remove the `!obs.contradictory` guard from the effective > 0 check → red.
    func testContradictionBounded_attenuatesNeverZeroes() throws {
        let engine = try SyxxMatrixIntegration.makeEngine()

        // Full support without contradiction.
        let cleanBundle = EvidenceBundle(
            sections: [SectionResult(id: SectionID("prophet"), status: .passed),
                       SectionResult(id: SectionID("mirror"), status: .passed),
                       SectionResult(id: SectionID("witness"), status: .passed)],
            indicators: [
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95),
                ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.90),
            ])
        let cleanResult = try engine.evaluate(cleanBundle)
        XCTAssertEqual(cleanResult.state, .threatDetected)
        let cleanStrength = try XCTUnwrap(cleanResult.diagnoses.first?.strength)

        // Same support but with the decisive indicator flagged contradictory.
        let contradictedBundle = EvidenceBundle(
            sections: [SectionResult(id: SectionID("prophet"), status: .passed),
                       SectionResult(id: SectionID("mirror"), status: .passed),
                       SectionResult(id: SectionID("witness"), status: .passed)],
            indicators: [
                ObservedIndicator(id: IndicatorID("authoritativeMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.95,
                                  contradictory: true),
                ObservedIndicator(id: IndicatorID("signedAnswerMismatch"),
                                  section: SectionID("prophet"), observedStrength: 0.90),
            ])
        let contradictedResult = try engine.evaluate(contradictedBundle)
        // Contradiction attenuates fit → strength drops, but the threat is still observed.
        // The contradiction mass is bounded: it cannot zero the threat.
        // F045: replaced if-let/else with unconditional XCTUnwrap. The diagnosis must be present
        // and attenuated — accepting its absence (the old else branch that checked suppressed)
        // accepts the thesis's negation (threat zeroed out of emission).
        let top = try XCTUnwrap(contradictedResult.diagnoses.first,
                                "contradicted threat must not silently vanish")
        XCTAssertLessThan(top.strength, cleanStrength,
                          "contradiction must attenuate strength")
        XCTAssertGreaterThan(top.strength, 0.0,
                             "contradiction must not zero the threat — bounded attenuation")
    }

    // MARK: - Item 14: requireDecisiveOrTwoMajorsForConclusive

    /// Plan 2b item 14: minors-only support at high coverage can reach `.probable` but never
    /// `.conclusive` — the `requireDecisiveOrTwoMajorsForConclusive` gate holds. Failure case:
    /// remove the decisiveOK check from `tier(for:margin:)` → minors-only reaches conclusive → red.
    func testRequireDecisiveOrTwoMajors_minorsOnlyReachesProbableNotConclusive() throws {
        // Build a custom spec with a diagnosis that has ONLY minor rules, high coverage threshold.
        let minorIndicators = (0..<3).map { i in
            DiagnosisIndicatorRule(indicatorID: IndicatorID("minor\(i)"), polarity: .supports,
                                   expectedStrength: 1.0, weight: 1.0,
                                   evidenceClass: .minor, independenceGroup: "g\(i)")
        }
        let diag = DiagnosisSpec(id: DiagnosisID("minorOnly"), displayName: "MinorOnly",
                                 rules: minorIndicators, requiredSections: [])
        let thresholds = ThresholdProfile(
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
            hardGateOnMissingRequiredSections: false,
            failClosedOnInvalidatedRequiredSections: false
        )
        let spec = MatrixSpec(diagnoses: [diag], thresholds: thresholds)
        let engine = try MatrixDeterminativeEngine(spec: spec)

        let bundle = EvidenceBundle(
            sections: [SectionResult(id: SectionID("prophet"), status: .passed)],
            indicators: minorIndicators.map { rule in
                ObservedIndicator(id: rule.indicatorID, section: SectionID("prophet"),
                                  observedStrength: 1.0)
            }
        )
        let result = try engine.evaluate(bundle)

        // With all 3 minors at full strength, coverage=1.0, fit=1.0, 3 independent groups, margin=1.0
        // — this exceeds all conclusive thresholds EXCEPT decisiveOK.
        // F045: replaced if-let/else with unconditional XCTUnwrap. The diagnosis must be present
        // and at probable confidence — accepting its absence (the old else branch) lets the test
        // survive deletion of the emission path it names.
        let top = try XCTUnwrap(result.diagnoses.first,
                                "minorOnly diagnosis must emit, not vanish")
        XCTAssertEqual(top.confidence, .probable,
                       "minors-only must cap at probable, not conclusive")
        XCTAssertGreaterThanOrEqual(top.coverage, 0.80)
        XCTAssertGreaterThanOrEqual(top.fit, 0.84)
        // The conclusive tier must never be reached.
        XCTAssertFalse(result.diagnoses.contains { $0.confidence == .conclusive },
                       "minors-only must never reach conclusive tier")
    }

    // MARK: - Helpers

    private func minimalThresholds() -> ThresholdProfile {
        ThresholdProfile(
            minimumCoverageForProbable: 0.55, minimumCoverageForConclusive: 0.80,
            minimumFitForProbable: 0.62, minimumFitForConclusive: 0.84,
            minimumMarginForProbable: 0.08, minimumMarginForConclusive: 0.18,
            maxContradictionForConclusive: 0.10, maxContradictionForProbable: 0.22,
            minimumIndependentSupportGroupsForProbable: 1,
            minimumIndependentSupportGroupsForConclusive: 2,
            requireDecisiveOrTwoMajorsForConclusive: false,
            hardGateOnMissingRequiredSections: false,
            failClosedOnInvalidatedRequiredSections: false
        )
    }

    private func minimalDiagnosisSpec(id: String) -> DiagnosisSpec {
        DiagnosisSpec(id: DiagnosisID(id), displayName: id, rules: [minimalRule()])
    }

    private func minimalRule() -> DiagnosisIndicatorRule {
        DiagnosisIndicatorRule(indicatorID: IndicatorID("minimal"), polarity: .supports,
                               expectedStrength: 1.0, weight: 1.0,
                               evidenceClass: .major, independenceGroup: "g")
    }

    // MARK: - Threshold profile: hard-gating behavior

    /// When `hardGateOnMissingRequiredSections` is true, a diagnosis that requires a section
    /// not present in the bundle must be gated (suppressed, never emitted). Failure case:
    /// remove the gate check → missing section silently passes → diagnosis emits → red.
    func testHardGate_missingRequiredSectionGatesDiagnosis() throws {
        let thresholds = ThresholdProfile(
            minimumCoverageForProbable: 0.55, minimumCoverageForConclusive: 0.80,
            minimumFitForProbable: 0.62, minimumFitForConclusive: 0.84,
            minimumMarginForProbable: 0.08, minimumMarginForConclusive: 0.18,
            maxContradictionForConclusive: 0.10, maxContradictionForProbable: 0.22,
            minimumIndependentSupportGroupsForProbable: 1,
            minimumIndependentSupportGroupsForConclusive: 2,
            requireDecisiveOrTwoMajorsForConclusive: false,
            hardGateOnMissingRequiredSections: true,
            failClosedOnInvalidatedRequiredSections: false
        )
        let diag = DiagnosisSpec(id: DiagnosisID("gated"), displayName: "Gated",
                                 rules: [minimalRule()],
                                 requiredSections: [SectionID("prophet")])
        let spec = MatrixSpec(diagnoses: [diag], thresholds: thresholds)
        let engine = try MatrixDeterminativeEngine(spec: spec)

        // Bundle with NO prophet section — the required section is missing.
        let bundle = EvidenceBundle(
            sections: [SectionResult(id: SectionID("mirror"), status: .passed)],
            indicators: [
                ObservedIndicator(id: IndicatorID("minimal"), section: SectionID("mirror"),
                                  observedStrength: 1.0)
            ])
        let result = try engine.evaluate(bundle)
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty, "gated diagnosis must not emit")
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "gated" && $0.reason == .missingRequiredSection },
                      "gated diagnosis must appear in suppressed with missingRequiredSection reason")
    }

    /// When `failClosedOnInvalidatedRequiredSections` is true, a diagnosis that requires a
    /// section that ran but produced invalidated/corrupt data must be gated. Failure case:
    /// remove the invalidated gate → corrupt section feeds diagnosis → red.
    func testHardGate_invalidatedRequiredSectionGatesDiagnosis() throws {
        let thresholds = ThresholdProfile(
            minimumCoverageForProbable: 0.55, minimumCoverageForConclusive: 0.80,
            minimumFitForProbable: 0.62, minimumFitForConclusive: 0.84,
            minimumMarginForProbable: 0.08, minimumMarginForConclusive: 0.18,
            maxContradictionForConclusive: 0.10, maxContradictionForProbable: 0.22,
            minimumIndependentSupportGroupsForProbable: 1,
            minimumIndependentSupportGroupsForConclusive: 2,
            requireDecisiveOrTwoMajorsForConclusive: false,
            hardGateOnMissingRequiredSections: false,
            failClosedOnInvalidatedRequiredSections: true
        )
        let diag = DiagnosisSpec(id: DiagnosisID("gated"), displayName: "Gated",
                                 rules: [minimalRule()],
                                 requiredSections: [SectionID("prophet")])
        let spec = MatrixSpec(diagnoses: [diag], thresholds: thresholds)
        let engine = try MatrixDeterminativeEngine(spec: spec)

        // Prophet section is present but INVALIDATED — corrupted data.
        let bundle = EvidenceBundle(
            sections: [SectionResult(id: SectionID("prophet"), status: .invalidated),
                       SectionResult(id: SectionID("mirror"), status: .passed)],
            indicators: [
                ObservedIndicator(id: IndicatorID("minimal"), section: SectionID("mirror"),
                                  observedStrength: 1.0)
            ])
        let result = try engine.evaluate(bundle)
        XCTAssertEqual(result.state, .inconclusive)
        XCTAssertTrue(result.diagnoses.isEmpty)
        XCTAssertTrue(result.suppressed.contains { $0.id.rawValue == "gated" && $0.reason == .invalidatedRequiredSection },
                      "gated diagnosis with invalidated section must appear in suppressed")
    }

    /// An `.inconclusive` section is USABLE evidence — it does NOT hard-gate diagnoses that
    /// depend on it, unlike `.untested` or `.invalidated`. Failure case: treat inconclusive
    /// as isUsable=false → diagnosis is gated → red.
    func testHardGate_inconclusiveSectionDoesNotGate() throws {
        let thresholds = ThresholdProfile(
            minimumCoverageForProbable: 0.55, minimumCoverageForConclusive: 0.80,
            minimumFitForProbable: 0.62, minimumFitForConclusive: 0.84,
            minimumMarginForProbable: 0.08, minimumMarginForConclusive: 0.18,
            maxContradictionForConclusive: 0.10, maxContradictionForProbable: 0.22,
            minimumIndependentSupportGroupsForProbable: 1,
            minimumIndependentSupportGroupsForConclusive: 2,
            requireDecisiveOrTwoMajorsForConclusive: false,
            hardGateOnMissingRequiredSections: true,
            failClosedOnInvalidatedRequiredSections: true
        )
        let diag = DiagnosisSpec(id: DiagnosisID("notGated"), displayName: "NotGated",
                                 rules: [minimalRule()],
                                 requiredSections: [SectionID("prophet")])
        let spec = MatrixSpec(diagnoses: [diag], thresholds: thresholds)
        let engine = try MatrixDeterminativeEngine(spec: spec)

        // Prophet section is INCONCLUSIVE — valid data, no resolution — must NOT hard-gate.
        let bundle = EvidenceBundle(
            sections: [SectionResult(id: SectionID("prophet"), status: .inconclusive),
                       SectionResult(id: SectionID("mirror"), status: .passed)],
            indicators: [
                ObservedIndicator(id: IndicatorID("minimal"), section: SectionID("mirror"),
                                  observedStrength: 1.0)
            ])
        let result = try engine.evaluate(bundle)
        // The diagnosis is NOT gated — inconclusive section is usable.
        let isGated = result.suppressed.contains { $0.id.rawValue == "notGated" && ($0.reason == .missingRequiredSection || $0.reason == .invalidatedRequiredSection) }
        XCTAssertFalse(isGated, "inconclusive section must NOT hard-gate; it is usable evidence")
        // F045: assert the positive — the diagnosis must actually emit, not just "not be gated".
        // The old test passed if notGated was suppressed for any OTHER reason, or if the engine
        // stopped evaluating the diagnosis at all — the pure != X shape.
        XCTAssertTrue(result.diagnoses.contains { $0.id.rawValue == "notGated" },
                      "notGated diagnosis must emit when its required section is inconclusive, not missing")
    }

    // MARK: - EvidenceUsability mapping correctness

    /// Verify that `EvidenceUsability.init(sectionStatus:)` maps the six `SectionStatus` values
    /// to the correct usability states. Failure case: a mapping error in the switch statement
    /// changes the hard-gating behavior → red.
    func testEvidenceUsabilityMapping() {
        XCTAssertEqual(EvidenceUsability(sectionStatus: .untested).isUsable, false)
        XCTAssertEqual(EvidenceUsability(sectionStatus: .invalidated).isUsable, false)
        XCTAssertEqual(EvidenceUsability(sectionStatus: .inconclusive).isUsable, true,
                       "inconclusive IS usable — it means the check ran with valid data")
        XCTAssertEqual(EvidenceUsability(sectionStatus: .degraded).isUsable, true)
        XCTAssertEqual(EvidenceUsability(sectionStatus: .positive).isUsable, true)
        XCTAssertEqual(EvidenceUsability(sectionStatus: .negative).isUsable, true)
    }

    /// `SectionStatus.blocksClean` must only block for `.untested` and `.invalidated`.
    /// `.inconclusive` must NOT block clean — it means the check ran with valid data.
    func testSectionStatusBlocksClean() {
        XCTAssertTrue(SectionStatus.untested.blocksClean)
        XCTAssertTrue(SectionStatus.invalidated.blocksClean)
        XCTAssertFalse(SectionStatus.inconclusive.blocksClean,
                       "inconclusive must NOT block clean — it is valid, just ambiguous")
        XCTAssertFalse(SectionStatus.degraded.blocksClean)
        XCTAssertFalse(SectionStatus.positive.blocksClean)
        XCTAssertFalse(SectionStatus.negative.blocksClean)
    }
}
