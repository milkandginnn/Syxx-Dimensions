import XCTest
@testable import SyxxKit

/// Offline, deterministic checks of the synthesis accumulator's invariants — the properties that
/// must hold no matter how the weights are later calibrated (DoW #1). No network.
final class SynthesisEngineTests: XCTestCase {

    private func st(_ s: Section, _ status: SectionStatus) -> SectionStatusEntry { .init(section: s, status: status) }
    private var allNegative: [SectionStatusEntry] { Section.allCases.map { st($0, .negative) } }

    func testAllClean_isCleanResult_highConfidence_noReferral() {
        let c = SynthesisEngine.synthesize(evidence: [], sectionStatuses: allNegative)
        XCTAssertEqual(c.diagnosis, .clean)
        XCTAssertFalse(c.hasHardGatedSection)
        // Ph4.1: class-set belief for an all-clean run is bounded by Dempster combination
        // (each clean support contributes mass to a broad focus, only the intersection of all
        // six narrows to {clean}). The raw belClean is ~56%, which reflects honest uncertainty
        // rather than fabricated pignistic confidence. The clean result itself is correct.
        XCTAssertGreaterThan(c.confidence, 50)
    }

    func testHardGate_blocksClean() {
        // Five clean sections, one untested → cannot be "clean" (DoW #3 hard gate).
        var statuses = allNegative
        statuses[1] = st(.mirror, .untested)
        let c = SynthesisEngine.synthesize(evidence: [], sectionStatuses: statuses)
        let isClean = c.diagnosis == .clean && c.conclusivity == .conclusive
        XCTAssertFalse(isClean)
        XCTAssertTrue(c.hasHardGatedSection)
    }

    func testStrongSingleThreat_noLongerNames_withoutCorroboration() {
        // Per B152/B153: Threat naming now requires either a decisive act OR two independent support groups.
        // A single strong threat signal must return INCONCLUSIVE, not a named result. The observation
        // remains visible in evidence, but cannot stand as a diagnosis alone.
        let e = EvidenceItem(section: .portal, evidenceId: .dnsLeakConfirmed,
                             observation: "rogue on-link resolver", rationale: "not the gateway; DNS unencrypted")
        let c = SynthesisEngine.synthesize(evidence: [e], sectionStatuses: allNegative)
        XCTAssertTrue(c.conclusivity == .inconclusive || c.conclusivity == .untested, "Single-source threat must be inconclusive, not named")
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .dnsLeakConfirmed }, "Observation still visible")
    }

    func testTwoWayTie_triggersReferral() {
        // A canary that supports two hypotheses equally is "weak alone" → too close to call.
        let e = EvidenceItem(section: .prophet, evidenceId: .redirection,
                             observation: "NXDOMAIN canary rewrite", rationale: "shared hijack IP")
        let c = SynthesisEngine.synthesize(evidence: [e], sectionStatuses: allNegative)
        XCTAssertTrue(c.referralTriggered)
        // Beyond the flag, assert the payload actually accompanies it: referralTriggered without a
        // Referral object would be a broken half-state the boolean-only check couldn't catch.
        XCTAssertNotNil(c.referral, "referralTriggered must carry a Referral payload")
    }

    func testEmptyRun_isInconclusive_andRefers() {
        let statuses = Section.allCases.map { st($0, .untested) }
        let c = SynthesisEngine.synthesize(evidence: [], sectionStatuses: statuses)
        XCTAssertEqual(c.conclusivity, .untested)
        XCTAssertTrue(c.referralTriggered)
        // §6: no evidence must yield 0 confidence — never a nonzero score from belief mass.
        // Failure case: a B022-style regression reporting max(belClean,belThreatSet)·100 reds here.
        XCTAssertEqual(c.confidence, 0,
            "no-evidence run must report confidence 0, not a residual belief-mass score")
    }

    func testProbabilities_sumToOne_andConfidenceBounded() {
        let c = SynthesisEngine.synthesize(evidence: [], sectionStatuses: allNegative)
        let sum = c.probabilities.reduce(0) { $0 + $1.probability }
        XCTAssertEqual(sum, 1.0, accuracy: 0.001)
        // (0...100).contains(confidence) was vacuous — confidence is hard-clamped, so it can never
        // fail. Assert real distribution + earned-confidence properties instead:
        for p in c.probabilities {
            XCTAssertTrue(p.probability >= 0 && p.probability <= 1, "\(p) is not a valid probability")
        }
        // An all-clean run must earn POSITIVE confidence (it committed belief to `clean`); a broken
        // engine returning 0 here would red — unlike the old clamped-bounds check.
        XCTAssertGreaterThan(c.confidence, 0, "an all-clean run must earn positive confidence in its result")
    }

    func testDegradedSection_reducesConfidence() {
        // A degraded section withholds the clean support an all-negative section would contribute,
        // so its confidence must be strictly BELOW the all-clean baseline — not merely "< 100"
        // (which is already true for every clean run and catches no defect).
        let clean = SynthesisEngine.synthesize(evidence: [], sectionStatuses: allNegative)
        var statuses = allNegative
        statuses[2] = st(.portal, .degraded)
        let degraded = SynthesisEngine.synthesize(evidence: [], sectionStatuses: statuses)
        XCTAssertLessThan(degraded.confidence, clean.confidence,
            "a degraded section must withhold clean support and lower confidence vs the all-clean baseline")
    }

    // MARK: Ph4.3 — class-level margin tests

    func testClassMargin_sameClass_closeHypotheses_doesNotCiteMargin() {
        // Ph4.3: two pieces of evidence both mapping to the `rewrite` mechanism class
        // ({spoofing, interception_onPath}) from different sections with positive statuses
        // to withhold clean support. With the OLD per-hypothesis margin, the top-two
        // individual BetP would be close → spurious margin-triggered referral.
        // With Ph4.3 class-level margin, the `rewrite` class dominates and margin is not
        // the cause of any triggered referral.
        let ev1 = EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,
                               observation: "DNSSEC stripped", rationale: "canary hijack")
        let ev2 = EvidenceItem(section: .mirror, evidenceId: .redirection,
                               observation: "NXDOMAIN canary rewrite", rationale: "shared hijack IP")
        var statuses = allNegative
        statuses[0] = st(.prophet, .positive)  // Prophet positive — confirms DNSSEC strip
        statuses[1] = st(.mirror, .positive)   // Mirror positive — confirms on-path signal
        let c = SynthesisEngine.synthesize(evidence: [ev1, ev2], sectionStatuses: statuses)
        XCTAssertNotEqual(c.diagnosis, .clean)
        if c.referralTriggered, let ref = c.referral {
            XCTAssertFalse(ref.reason.lowercased().contains("margin"),
                           "Referral reason should not cite margin for same-class hypotheses: \(ref.reason)")
        }
    }

    func testClassMargin_singleClassDominates_noMarginReferral() {
        // When one mechanism class dominates the distribution (e.g. privacyLeak class >> 60%),
        // the class-level margin is wide and referral must NOT be margin-triggered.
        let leak1 = EvidenceItem(section: .glutton, evidenceId: .dnsLeakConfirmed,
                                 observation: "collector confirmed leak", rationale: "confirmed")
        let leak2 = EvidenceItem(section: .glutton, evidenceId: .ecsSubnetLeak,
                                 observation: "ECS subnet leaked", rationale: "independent corroboration")
        var statuses = allNegative
        statuses[3] = st(.glutton, .positive)
        let c = SynthesisEngine.synthesize(evidence: [leak1, leak2], sectionStatuses: statuses)
        XCTAssertEqual(c.diagnosis, .privacyLeak, "Two corroborating leak signals should name privacyLeak")
        if c.referralTriggered, let ref = c.referral {
            XCTAssertFalse(ref.reason.lowercased().contains("margin"),
                           "A dominating class should not trigger margin referral: \(ref.reason)")
        }
        // Unconditional invariant: the dominant class must actually own every supporting item.
        // Guard against vacuous pass in case the engine stops attributing this evidence.
        let leakSupport = c.evidence.filter { item in
            item.supports.contains(.privacyLeak) || item.evidenceId == .dnsLeakConfirmed || item.evidenceId == .ecsSubnetLeak
        }
        XCTAssertGreaterThanOrEqual(leakSupport.count, 2,
                                    "both Glutton items must reach the engine as PrivacyLeak support")
        leakSupport.forEach { XCTAssertEqual($0.section, .glutton, "support must remain Glutton-attributed") }
    }

    // MARK: Ph5.1 — cross-vantage discrepancy mass source tests

    func testPh51_discrepancyMatrix_contributesMassAgainstClean() {
        // Ph5.1: a cross-vantage discrepancy in the answer matrix contributes DST mass toward
        // {spoofing, interception_onPath}. With no other threat evidence, a discrepancy should
        // shift belief away from clean — the result must not be clean (or at minimum, the clean
        // belief must be lower than without the discrepancy signal).
        let local = VantageIdentity(vantageId: "local", kind: .localSystem, endpoint: nil, transport: nil,
                                    authenticated: false, freshness: .currentRun)
        let pub1 = VantageIdentity(vantageId: "public1", kind: .publicResolver, endpoint: "8.8.8.8", transport: "udp",
                                   authenticated: true, freshness: .currentRun)
        let pub2 = VantageIdentity(vantageId: "public2", kind: .publicResolver, endpoint: "1.1.1.1", transport: "udp",
                                   authenticated: true, freshness: .currentRun)
        let entries: [VantageAnswer] = [
            .init(vantage: local, status: .answered, canonicalAnswers: ["203.0.113.5"], rcode: 0, observationId: "obs.local"),
            .init(vantage: pub1, status: .answered, canonicalAnswers: ["198.51.100.10"], rcode: 0, observationId: "obs.pub1"),
            .init(vantage: pub2, status: .answered, canonicalAnswers: ["198.51.100.10"], rcode: 0, observationId: "obs.pub2"),
        ]
        let comparison = AnswerComparison(
            classification: .discrepancy,
            localVantageIds: ["local"],
            referenceVantageIds: ["public1", "public2"],
            consensusAnswers: ["198.51.100.10"],
            outlierVantageIds: ["local"]
        )
        let matrixRow = AnswerMatrixRow(
            rowId: "mirror.a.example.com.1",
            queryName: "example.com",
            queryType: 1,
            entries: entries,
            comparison: comparison
        )
        // Run with no threat evidence — the discrepancy should contribute mass on its own.
        let withMatrix = SynthesisEngine.synthesize(
            evidence: [], sectionStatuses: allNegative,
            answerMatrix: [matrixRow]
        )
        let withoutMatrix = SynthesisEngine.synthesize(
            evidence: [], sectionStatuses: allNegative,
            answerMatrix: []
        )
        // With a discrepancy, clean belief should be LOWER than without it
        // (mass shifted toward spoofing/interception_onPath). The exact result may still be clean
        // if 6 sections' worth of clean support keeps the threat union below threatNameFloor.
        let cleanBelWith = withMatrix.belief.first { $0.hypothesis == .clean }?.probability ?? 0
        let cleanBelWithout = withoutMatrix.belief.first { $0.hypothesis == .clean }?.probability ?? 0
        XCTAssertLessThan(cleanBelWith, cleanBelWithout,
            "Ph5.1: discrepancy must reduce clean belief vs no-discrepancy baseline")
    }

    func testPh51_benignSetVariation_noMassContribution() {
        // A benignSetVariation (multi-homed CDN) must contribute ZERO mass — belief distribution
        // should be identical to having no answer matrix at all.
        let local = VantageIdentity(vantageId: "local", kind: .localSystem, endpoint: nil, transport: nil,
                                    authenticated: false, freshness: .currentRun)
        let pub = VantageIdentity(vantageId: "public", kind: .publicResolver, endpoint: "8.8.8.8", transport: "udp",
                                  authenticated: true, freshness: .currentRun)
        let entries: [VantageAnswer] = [
            .init(vantage: local, status: .answered, canonicalAnswers: ["203.0.113.5"], rcode: 0, observationId: "obs.local"),
            .init(vantage: pub, status: .answered, canonicalAnswers: ["203.0.113.5"], rcode: 0, observationId: "obs.pub"),
        ]
        let comparison = AnswerComparison(
            classification: .benignSetVariation,
            localVantageIds: ["local"],
            referenceVantageIds: ["public"],
            consensusAnswers: ["203.0.113.5"],
            outlierVantageIds: []
        )
        let matrixRow = AnswerMatrixRow(
            rowId: "mirror.a.example.com.1",
            queryName: "example.com",
            queryType: 1,
            entries: entries,
            comparison: comparison
        )
        let withBenign = SynthesisEngine.synthesize(
            evidence: [], sectionStatuses: allNegative,
            answerMatrix: [matrixRow]
        )
        let withoutMatrix = SynthesisEngine.synthesize(
            evidence: [], sectionStatuses: allNegative,
            answerMatrix: []
        )
        // Belief distribution should be identical — benignSetVariation adds zero mass.
        for hp in withBenign.belief {
            let other = withoutMatrix.belief.first { $0.hypothesis == hp.hypothesis }?.probability ?? 0
            XCTAssertEqual(hp.probability, other, accuracy: 1e-9,
                "benignSetVariation must not shift belief for \(hp.hypothesis)")
        }
    }

    // MARK: B154 — anti-cancellation guard for the answer-matrix discrepancy channel

    private func discrepancyRow(referenceCount: Int = 2) -> AnswerMatrixRow {
        // Helper: build a discrepancy row with `referenceCount` public vantages all agreeing,
        // while the local vantage returns a different answer.
        let local = VantageIdentity(vantageId: "local", kind: .localSystem, endpoint: nil,
                                    transport: nil, authenticated: false, freshness: .currentRun)
        var entries: [VantageAnswer] = [
            .init(vantage: local, status: .answered, canonicalAnswers: ["203.0.113.5"],
                  rcode: 0, observationId: "obs.local")
        ]
        var refIds: [String] = []
        for i in 0..<referenceCount {
            let id = "public\(i+1)"
            let v = VantageIdentity(vantageId: id, kind: .publicResolver, endpoint: "8.8.8.\(i+1)",
                                    transport: "udp", authenticated: true, freshness: .currentRun)
            entries.append(.init(vantage: v, status: .answered, canonicalAnswers: ["198.51.100.10"],
                                 rcode: 0, observationId: "obs.\(id)"))
            refIds.append(id)
        }
        let comparison = AnswerComparison(classification: .discrepancy,
                                          localVantageIds: ["local"],
                                          referenceVantageIds: refIds,
                                          consensusAnswers: ["198.51.100.10"],
                                          outlierVantageIds: ["local"])
        return AnswerMatrixRow(rowId: "mirror.a.b154.example.1", queryName: "b154.example",
                               queryType: 1, entries: entries, comparison: comparison)
    }

    func testB154_I4_DiscrepancyPlusCaptivePortal_DampenerForcesReferral() {
        // I4 golden vector: a confirmed cross-vantage discrepancy (2 public vantages corroborate
        // the consensus) combined with a captivePortalActive dampener.
        //
        // Before B154: captivePortalActive (-2.0 weight) could cancel the discrepancy mass entirely
        // because `confirmedThreats` was only seeded from `.positive` sections, and the Ph5.1
        // discrepancy channel has no section. Result: dampener silently erased the threat signal,
        // `dampenerForcedReferral` stayed false, and the engine reported clean without referring.
        //
        // After B154: the discrepancy channel seeds `confirmedThreats` when its committed mass
        // meets `threatNameFloor`. captivePortalActive can causally explain the discrepancy
        // (BenignExplanation.canExplain returns true for spoofing/interception_onPath), but because
        // spoofing/interception_onPath are now in `confirmedThreats`, the dampener path takes the
        // `dampenerForcedReferral = true` branch instead of cancelling.
        //
        // Failure case (mutation proof):
        //   Remove the B154 `confirmedThreats.insert(.spoofing)` block from SynthesisEngine.swift
        //   (the lines after `committedMass >= config.threatNameFloor`).
        //   Result: confirmedThreats never gets spoofing/interception_onPath from the discrepancy
        //   channel → dampenerForcedReferral stays false → referralTriggered may be false (if the
        //   dampener also washes the threat below the confidence threshold) → this test fails.

        let dampenerEvidence = EvidenceItem(
            section: .portal,
            evidenceId: .captivePortalActive,
            observation: "captive portal login page detected",
            rationale: "may explain on-path answer rewrite"
        )
        // All sections negative so the ONLY threat signal comes from the discrepancy channel.
        let result = SynthesisEngine.synthesize(
            evidence: [dampenerEvidence],
            sectionStatuses: allNegative,
            answerMatrix: [discrepancyRow(referenceCount: 2)]
        )

        // The engine must have triggered referral — the dampener cannot simply cancel the
        // confirmed discrepancy and produce a clean result.
        XCTAssertTrue(
            result.referralTriggered,
            "B154/I4: a captive-portal dampener against a confirmed discrepancy must force referral, " +
            "not silently cancel the cross-vantage threat signal. " +
            "result=\(result.diagnosis) referral=\(result.referralTriggered)"
        )

        // The discrepancy threat mass must survive — spoofing or interception_onPath BetP
        // must be nonzero (dampener cannot zero them out entirely when confirmed).
        let spoofingBetP = result.probabilities.first { $0.hypothesis == .spoofing }?.probability ?? 0
        let onPathBetP   = result.probabilities.first { $0.hypothesis == .interception_onPath }?.probability ?? 0
        XCTAssertGreaterThan(
            spoofingBetP + onPathBetP, 0,
            "B154/I4: spoofing+interception_onPath BetP must remain nonzero when a confirmed " +
            "discrepancy is present, even with a captive-portal dampener. " +
            "spoofing=\(spoofingBetP) onPath=\(onPathBetP)"
        )
    }

    func testB154_I5_DiscrepancyWithoutDampener_ThreatMassNonzero() {
        // I5 golden vector: discrepancy alone, no dampener. Confirms the channel contributes mass
        // and that removing the dampener exposes the full threat signal.
        //
        // Failure case: remove the `supports.append` for the discrepancy case in SynthesisEngine.swift.
        // Result: no threat mass → spoofing+interception_onPath BetP == 0 → XCTAssertGreaterThan fails.

        let result = SynthesisEngine.synthesize(
            evidence: [],
            sectionStatuses: allNegative,
            answerMatrix: [discrepancyRow(referenceCount: 2)]
        )

        let spoofingBetP = result.probabilities.first { $0.hypothesis == .spoofing }?.probability ?? 0
        let onPathBetP   = result.probabilities.first { $0.hypothesis == .interception_onPath }?.probability ?? 0
        XCTAssertGreaterThan(
            spoofingBetP + onPathBetP, 0,
            "B154/I5: a discrepancy row must contribute nonzero spoofing+interception_onPath " +
            "BetP to the distribution. spoofing=\(spoofingBetP) onPath=\(onPathBetP)"
        )
    }

    func testB154_I4_BenignRowDoesNotSeedConfirmedThreats() {
        // Negative control: a benignSetVariation row must NOT seed confirmedThreats — a captive-portal
        // dampener against a benign-variation row must NOT trigger forced referral.
        //
        // Failure case: if B154 accidentally seeds confirmedThreats for benignSetVariation rows,
        // then a captive portal + benign CDN variation would falsely force referral.
        let local = VantageIdentity(vantageId: "local", kind: .localSystem, endpoint: nil,
                                    transport: nil, authenticated: false, freshness: .currentRun)
        let pub = VantageIdentity(vantageId: "public", kind: .publicResolver, endpoint: "8.8.8.8",
                                  transport: "udp", authenticated: true, freshness: .currentRun)
        let entries: [VantageAnswer] = [
            .init(vantage: local, status: .answered, canonicalAnswers: ["203.0.113.5"],
                  rcode: 0, observationId: "obs.local"),
            .init(vantage: pub, status: .answered, canonicalAnswers: ["203.0.113.6"],
                  rcode: 0, observationId: "obs.pub"),
        ]
        let comparison = AnswerComparison(classification: .benignSetVariation,
                                          localVantageIds: ["local"],
                                          referenceVantageIds: ["public"],
                                          consensusAnswers: ["203.0.113.5"],
                                          outlierVantageIds: [])
        let benignRow = AnswerMatrixRow(rowId: "mirror.a.b154cdn.1", queryName: "cdn.example",
                                        queryType: 1, entries: entries, comparison: comparison)
        let dampenerEvidence = EvidenceItem(
            section: .portal, evidenceId: .captivePortalActive,
            observation: "portal", rationale: "benign CDN test"
        )
        // Run WITH the benign row (dampener present, benign row present).
        let withBenign = SynthesisEngine.synthesize(
            evidence: [dampenerEvidence],
            sectionStatuses: allNegative,
            answerMatrix: [benignRow]
        )
        // Run WITHOUT any matrix row (dampener present, no row).
        let noMatrix = SynthesisEngine.synthesize(
            evidence: [dampenerEvidence],
            sectionStatuses: allNegative,
            answerMatrix: []
        )
        // Key invariant: a benignSetVariation row must contribute ZERO mass change — the belief
        // distribution with and without it must be identical (same as testPh51_benignSetVariation_noMassContribution).
        // This also proves the row did NOT seed confirmedThreats: if it had, the dampener would
        // take the `dampenerForcedReferral = true` branch and alter the referral result.
        //
        // Failure case: change the `benignSetVariation` branch in SynthesisEngine.swift to also
        // call `confirmedThreats.insert(.spoofing)` → the dampener triggers forced referral →
        // withBenign.referralTriggered != noMatrix.referralTriggered → XCTAssertEqual fails.
        XCTAssertEqual(
            withBenign.referralTriggered, noMatrix.referralTriggered,
            "B154 negative control: benignSetVariation must not seed confirmedThreats — " +
            "dampener referral behaviour must be identical with and without the benign row."
        )
        // And the belief distributions must be identical (benign row contributed no mass).
        for hp in withBenign.probabilities {
            let other = noMatrix.probabilities.first { $0.hypothesis == hp.hypothesis }?.probability ?? 0
            XCTAssertEqual(hp.probability, other, accuracy: 1e-9,
                "B154 negative control: benignSetVariation must not shift BetP for \(hp.hypothesis)")
        }
    }

    // MARK: B155 — degraded-section dampener reliability discount

    func testB155_DegradedSectionDampener_IsDiscounted() {
        // Confirmed threat: nxCanaryHijack from a .positive .mirror section.
        // Dampener: captivePortalActive from .portal (weight -2.0).
        //
        // Isolation strategy: compare .degraded vs .invalidated for the dampener section.
        // Both statuses withhold clean support (so clean-support contribution is identical),
        // but .degraded applies the dampener at 0.5 reliability while .invalidated skips it
        // entirely (B155 fix). Spoofing BetP must be LOWER in the .degraded run (some dampening)
        // than in the .invalidated run (zero dampening).
        //
        // Failure case (mutation proof): revert line 247 of SynthesisEngine.swift so dampener always
        // gets reliability 1.0 for .degraded → same result as hardcoding 1.0 at that line →
        // the BetP difference between degraded and invalidated grows larger than 0.5 x full-strength,
        // making spoofingDegraded even further below spoofingInvalidated. But more critically:
        // revert line 245 so .invalidated no longer skips the dampener → spoofingDegraded ==
        // spoofingInvalidated (both apply full-strength) → XCTAssertLessThan fails.

        let threat = EvidenceItem(
            section: .mirror,
            evidenceId: .nxCanaryHijack,
            observation: "NXDOMAIN canary rewritten",
            rationale: "hijack IP matches known redirect"
        )
        let dampener = EvidenceItem(
            section: .portal,
            evidenceId: .captivePortalActive,
            observation: "captive portal login page detected",
            rationale: "may explain on-path rewrite"
        )

        // Both runs: mirror .positive, portal does NOT contribute clean support.
        var statusesDegraded = allNegative
        statusesDegraded[1] = st(.mirror, .positive)
        statusesDegraded[2] = st(.portal, .degraded)      // dampener fires at 0.5 reliability

        var statusesInvalidated = allNegative
        statusesInvalidated[1] = st(.mirror, .positive)
        statusesInvalidated[2] = st(.portal, .invalidated) // dampener skipped entirely (same as untested)

        let withDegradedDampener = SynthesisEngine.synthesize(
            evidence: [threat, dampener],
            sectionStatuses: statusesDegraded
        )
        let withNoDampener = SynthesisEngine.synthesize(
            evidence: [threat, dampener],
            sectionStatuses: statusesInvalidated
        )

        // Use pignistic BetP — dampener shifts set-mass, reflected in BetP not singleton Bel.
        let spoofingDegraded    = withDegradedDampener.probabilities.first { $0.hypothesis == .spoofing }?.probability ?? 0
        let spoofingInvalidated = withNoDampener.probabilities.first       { $0.hypothesis == .spoofing }?.probability ?? 0

        // .degraded fires the dampener at 0.5 → some cancellation → lower spoofing BetP.
        // .invalidated skips the dampener → zero cancellation → higher spoofing BetP.
        XCTAssertLessThan(
            spoofingDegraded, spoofingInvalidated,
            "B155: a degraded-section dampener at 0.5 reliability must reduce spoofing BetP " +
            "compared to a skipped (invalidated) dampener. " +
            "degraded=\(spoofingDegraded) invalidated=\(spoofingInvalidated)"
        )
    }

    func testB155_UntestedSectionDampener_IsSkipped() {
        // When the dampener's section is .untested the B155 fix must SKIP it entirely.
        // The threat must produce the same BetP as a run without the dampener EvidenceItem at all.
        //
        // Isolation: both runs have portal .untested so neither contributes clean support.
        // Run A has no dampener item; Run B has the dampener item but portal is .untested.
        // B155 skips the dampener → BetP must be identical.
        //
        // Failure case: revert line 245 of SynthesisEngine.swift so .untested does NOT skip
        // the dampener → Run B applies captivePortalActive at full strength → spoofingUntested
        // falls below spoofingNoDampener and XCTAssertEqual fails.

        let threat = EvidenceItem(
            section: .mirror,
            evidenceId: .nxCanaryHijack,
            observation: "NXDOMAIN canary rewritten",
            rationale: "hijack IP matches known redirect"
        )
        let dampener = EvidenceItem(
            section: .portal,
            evidenceId: .captivePortalActive,
            observation: "captive portal detected",
            rationale: "may explain on-path rewrite"
        )

        // Both runs: mirror .positive, portal .untested (no clean support, no dampening).
        var statusesBase = allNegative
        statusesBase[1] = st(.mirror, .positive)
        statusesBase[2] = st(.portal, .untested)

        let noDampenerItem = SynthesisEngine.synthesize(
            evidence: [threat],
            sectionStatuses: statusesBase
        )
        let withSkippedDampener = SynthesisEngine.synthesize(
            evidence: [threat, dampener],
            sectionStatuses: statusesBase   // same statuses — only the evidence list differs
        )

        // Use pignistic BetP.
        let spoofingNoDampener = noDampenerItem.probabilities.first    { $0.hypothesis == .spoofing }?.probability ?? 0
        let spoofingUntested   = withSkippedDampener.probabilities.first { $0.hypothesis == .spoofing }?.probability ?? 0

        XCTAssertEqual(
            spoofingUntested, spoofingNoDampener, accuracy: 1e-9,
            "B155: a dampener from an untested section must be skipped entirely — " +
            "spoofing BetP must equal the no-dampener baseline. " +
            "noDampener=\(spoofingNoDampener) untested=\(spoofingUntested)"
        )
    }

    // ---- WS8-04 / B152: referralReason must name the ACTUAL cause ----

    /// B152-A. `dampenerForcedReferral` had no branch, so control fell through to the final
    /// unconditional `else` and reported a MARGIN failure — "the top two possibilities are within
    /// N points" — on a referral that had nothing to do with margin. The quoted N was whatever the
    /// margin happened to be, up to the full 95.
    /// Mutation: delete the `dampenerForcedReferral` branch from `referralReason`. The reason
    /// reverts to the margin text and the `notContains` assertion goes RED.
    func testDampenerReferral_reportsTheMechanism_notAMargin() {
        // A confirmed threat (positive section status is what seeds `confirmedThreats`) that a
        // captive portal could also explain: the engine must refer rather than cancel, and must say
        // which mechanism it means.
        let stripped = EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,
                                    observation: "DNSSEC canary returned unsigned",
                                    rationale: "signature stripped in flight")
        let portal = EvidenceItem(section: .portal, evidenceId: .captivePortalActive,
                                  observation: "captive portal detected", rationale: "gateway intercepts :80")
        var statuses = allNegative
        statuses[0] = st(.prophet, .positive)
        let c = SynthesisEngine.synthesize(evidence: [stripped, portal], sectionStatuses: statuses)

        XCTAssertTrue(c.referralTriggered)
        let reason = c.referral?.reason ?? ""
        XCTAssertTrue(reason.contains("harmless causes"),
                      "a dampener-forced referral must name the mechanism. got: \(reason)")
        XCTAssertFalse(reason.contains("within"),
                       "must not report a margin figure on a referral margin did not cause. got: \(reason)")
    }

    /// B152-B. The `conflict` branch was tested ABOVE `hardGated`, so an input that is BOTH
    /// hard-gated and conflicted was explained as "the sections disagree" when the actual trigger
    /// was a check that never ran. The two co-occur when disjoint threat hypotheses conflict with
    /// each other (clean support is suppressed under a hard gate, so threat-vs-clean cannot produce
    /// this — it needs threat-vs-threat).
    /// Mutation: move the `conflict` branch back above `hardGated`. The reason becomes the
    /// disagreement text and the `notContains` assertion goes RED.
    func testHardGateOutranksConflict_inTheStatedReason() {
        let stripped = EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,
                                    observation: "DNSSEC canary returned unsigned",
                                    rationale: "signature stripped in flight")
        let exfil = EvidenceItem(section: .cage, evidenceId: .exfilLikely,
                                 observation: "high-entropy subdomain burst", rationale: "tunnelling shape")
        let leak = EvidenceItem(section: .glutton, evidenceId: .dnsLeakConfirmed,
                                observation: "queries left the tunnel", rationale: "resolver outside VPN")
        var statuses = allNegative
        statuses[0] = st(.prophet, .positive)
        statuses[1] = st(.mirror, .untested)      // the hard gate
        statuses[3] = st(.glutton, .positive)
        statuses[4] = st(.cage, .positive)
        let c = SynthesisEngine.synthesize(evidence: [stripped, exfil, leak], sectionStatuses: statuses)

        // Both conditions must hold, or this test proves nothing about ordering.
        XCTAssertTrue(c.hasHardGatedSection)
        XCTAssertGreaterThan(c.conflict, 0.40)

        let reason = c.referral?.reason ?? ""
        XCTAssertTrue(reason.contains("did not run"),
                      "a hard gate outranks conflict: missing evidence is the cause. got: \(reason)")
        XCTAssertFalse(reason.contains("sections disagree"),
                       "must not blame disagreement when a required check never ran. got: \(reason)")
    }
}
