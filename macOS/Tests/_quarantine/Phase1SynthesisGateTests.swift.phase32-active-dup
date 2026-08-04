import XCTest
@testable import SyxxKit

final class Phase1SynthesisGateTests: XCTestCase {
    private func st(_ section: Section, _ status: SectionStatus) -> SectionStatusEntry {
        .init(section: section, status: status)
    }

    private var allNegative: [SectionStatusEntry] {
        Section.allCases.map { st($0, .negative) }
    }

    func testCombine_invalidatedDominatesNegativeAndDegraded_butNotPositive() {
        XCTAssertEqual(EvidenceAdapters.combine([.negative, .invalidated]), .invalidated)
        XCTAssertEqual(EvidenceAdapters.combine([.degraded, .invalidated]), .invalidated)
        XCTAssertEqual(EvidenceAdapters.combine([.positive, .invalidated]), .positive)
    }

    func testPositiveSectionBlocksClean() {
        var statuses = allNegative
        statuses[0] = st(.prophet, .positive)
        let evidence = [EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,
                                     observation: "stripped", rationale: "confirmed")]
        let c = SynthesisEngine.synthesize(evidence: evidence, sectionStatuses: statuses)
        let isClean = c.diagnosis == .clean && c.conclusivity == .conclusive
        XCTAssertFalse(isClean)
        XCTAssertTrue(c.referralTriggered)
    }

    func testCommittedThreatMassFloorBlocksCleanWhenConfirmedThreatIsWashed() {
        // P1.6: a confirmed (.positive) strong strip that the clean sections wash below the fused floor
        // must STILL not read clean — the committed-mass backstop reads the per-support focal mass (~0.95),
        // not the washed Bel, and NEVER edits the belief number (R3). Redundant with P1.5 in Phase 1 by
        // design (the Phase-5 cross-vantage discrepancy act reuses the same gate).
        var statuses = allNegative
        statuses[0] = st(.prophet, .positive)
        let c = SynthesisEngine.synthesize(
            evidence: [EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,
                                    observation: "stripped", rationale: "confirmed", source: "canaries")],
            sectionStatuses: statuses)
        let isClean = c.diagnosis == .clean && c.conclusivity == .conclusive
        XCTAssertFalse(isClean)
        XCTAssertTrue(c.referralTriggered)
    }

    func testCommittedMassGateScopedToConfirmedSections_noFalseTampered() {
        // Honesty-symmetric guard: a WEAK positive-weighted overlay (dnsUnencrypted, 0.6 → committed ~0.45)
        // on an otherwise all-clean run must NOT block clean. The committed-mass gate is scoped to
        // .positive (confirmed) sections precisely so a benign Do53 tilt can never manufacture a
        // false-tampered result — the gate cuts only toward honest-inconclusive, never over-claim.
        let c = SynthesisEngine.synthesize(
            evidence: [EvidenceItem(section: .portal, evidenceId: .dnsUnencrypted,
                                    observation: "cleartext", rationale: "benign")],
            sectionStatuses: allNegative)
        XCTAssertEqual(c.diagnosis, .clean)
    }

    func testCriticalInconclusiveBlocksClean() {
        var statuses = allNegative
        statuses[0] = st(.prophet, .inconclusive)
        let c = SynthesisEngine.synthesize(evidence: [], sectionStatuses: statuses)
        XCTAssertEqual(c.conclusivity, .inconclusive)
        XCTAssertTrue(c.referralTriggered)
    }

    func testCriticalDegradedSectionForbidsClean() {
        // P1.2: a degraded CRITICAL section (Glutton) forbids a clean result even with no threat evidence
        // and everything else clean — the run is incomplete, so it reads inconclusive+referral, not clean.
        let clean = SynthesisEngine.synthesize(evidence: [], sectionStatuses: allNegative)
        var degradedStatuses = allNegative
        degradedStatuses[3] = st(.glutton, .degraded)
        let degraded = SynthesisEngine.synthesize(evidence: [], sectionStatuses: degradedStatuses)
        let isClean = degraded.diagnosis == .clean && degraded.conclusivity == .conclusive
        XCTAssertFalse(isClean)
        XCTAssertLessThan(degraded.confidence, clean.confidence)
    }

    func testDegradedSourceDiscountLowersNamedThreatConfidence_neverZeroes() {
        // P1.3 (Shafer α): the SAME confirmed threat evidence from a .degraded source earns strictly LOWER
        // confidence than from a .positive source (m'(A)=α·m(A) toward ignorance) — but is NEVER zeroed
        // (I4 anti-dampening). Cage is non-critical and uniquely covers localCompromise, so the threat is
        // named in both cases and the α effect is isolated from the clean-wash.
        let evidence = [
            EvidenceItem(section: .cage, evidenceId: .exfilLikely,
                         observation: "exfil", rationale: "confirmed"),
            EvidenceItem(section: .portal, evidenceId: .gatewayDhcpOffLinkDNS,
                         observation: "off-subnet", rationale: "confirmed")
        ]
        var pos = allNegative
        pos[4] = st(.cage, .positive)
        pos[2] = st(.portal, .positive)
        var deg = allNegative
        deg[4] = st(.cage, .degraded)
        deg[2] = st(.portal, .positive)
        let full = SynthesisEngine.synthesize(evidence: evidence, sectionStatuses: pos)
        let discounted = SynthesisEngine.synthesize(evidence: evidence, sectionStatuses: deg)
        XCTAssertEqual(full.diagnosis, .localCompromise)
        XCTAssertEqual(discounted.diagnosis, .localCompromise)
        XCTAssertLessThan(discounted.confidence, full.confidence)
        XCTAssertGreaterThan(discounted.confidence, 0)
    }

    func testSingleSourceThreatForcesReferral_whileTwoIndependentSourcesCanStandAlone() {
        var statuses = allNegative
        statuses[3] = st(.glutton, .positive)

        let oneSource = SynthesisEngine.synthesize(
            evidence: [EvidenceItem(section: .glutton, evidenceId: .dnsLeakConfirmed,
                                    observation: "collector confirmed", rationale: "confirmed")],
            sectionStatuses: statuses)
        XCTAssertEqual(oneSource.diagnosis, .privacyLeak)
        XCTAssertTrue(oneSource.referralTriggered)

        let twoSources = SynthesisEngine.synthesize(
            evidence: [
                EvidenceItem(section: .glutton, evidenceId: .dnsLeakConfirmed,
                             observation: "collector confirmed", rationale: "confirmed"),
                EvidenceItem(section: .glutton, evidenceId: .ecsSubnetLeak,
                             observation: "ecs leaked", rationale: "independent corroboration")
            ],
            sectionStatuses: statuses)
        XCTAssertEqual(twoSources.diagnosis, .privacyLeak)
        XCTAssertFalse(twoSources.referralTriggered)
    }

    func testLatencyAndTTLQualityCaveatsStayDegradedWithoutThreatEvidence() {
        let latency = EvidenceAdapters.prophet(
            resolution: nil,
            dnssec: nil,
            nxCanary: nil,
            dnssecCanary: nil,
            latency: ResolverLatency(status: .degraded, sampleCount: 4, successCount: 2, medianMs: 200))
        XCTAssertEqual(latency.status, .degraded)
        XCTAssertTrue(latency.evidence.isEmpty)

        let stability = EvidenceAdapters.prophet(
            resolution: nil,
            dnssec: nil,
            nxCanary: nil,
            dnssecCanary: nil,
            stability: AnswerStability(status: .ttlAnomaly, sampleCount: 3, usableCount: 3, ttlHonored: false))
        XCTAssertEqual(stability.status, .degraded)
        XCTAssertTrue(stability.evidence.isEmpty)
    }
}
