import XCTest
@testable import SyxxKit

/// B029 — the source-group correlation map and its effect on synthesized confidence.
///
/// The defect this guards: correlated signals (two symptoms of one underlying tampering act) were
/// only grouped for the "canaries" probe; every other correlated pair was counted as independent
/// evidence, so Dempster's rule over-counted their combined weight and could inflate confidence past
/// the 75%/30-point referral gate — suppressing the honest "go ask a human" referral.
///
/// The fix wires a deliberate correlation map (`EvidenceItem.correlationGroup`) and makes the engine
/// contribute each correlated group as ONE merged support capped at `maxWeightPerSource`, while
/// leaving genuinely independent (different-source / unsourced) signals undiscounted. Offline.
final class SourceCorrelationTests: XCTestCase {

    // MARK: - The deliberate correlation map (rebuilt from the EvidenceId enum, B029)

    func testCorrelationMap_groupsOnlyTheDeliberateFamilies() {
        // NXDOMAIN / denial-fabrication family: a resolver hijacking names it should deny co-fires
        // the subdomain-synthesis probe and the denial-of-existence probe.
        XCTAssertEqual(EvidenceItem.correlationGroup(for: .nxSubdomainHijack), "nxdomain_hijack")
        XCTAssertEqual(EvidenceItem.correlationGroup(for: .denialInjected), "nxdomain_hijack")
        XCTAssertEqual(EvidenceItem.correlationGroup(for: .denialBare), "nxdomain_hijack")
        // Transport-downgrade family: cleartext Do53, a blocked DoH path, and SVCB/ECH stripping are
        // one "no protected transport" condition observed three ways.
        XCTAssertEqual(EvidenceItem.correlationGroup(for: .dnsUnencrypted), "transport_downgrade")
        XCTAssertEqual(EvidenceItem.correlationGroup(for: .encryptedDnsBlocked), "transport_downgrade")
        XCTAssertEqual(EvidenceItem.correlationGroup(for: .svcbStripped), "transport_downgrade")
        // Authoritative-vantage divergence family: a recursive answer that does not match the
        // domain's authoritative NS and the same finding confirmed by Dimension ground truth are
        // the same underlying phenomenon observed at different trust levels.
        XCTAssertEqual(EvidenceItem.correlationGroup(for: .authoritativeDiverges), "authoritative_divergence")
        XCTAssertEqual(EvidenceItem.correlationGroup(for: .authoritativeDivergesConfirmed), "authoritative_divergence")
        // Genuinely independent probes must NOT be grouped (or they would be wrongly merged).
        for id in [EvidenceId.redirection, .dnsLeakConfirmed, .ecsSubnetLeak, .ttlLockstep,
                   .ttlExtreme, .cnameChainDivergent, .scriptMismatch, .resolverStatus,
                   .dnssecExpired, .answerUnstable, .ipv4Suppressed, .ednsBlocked, .exfilLikely] {
            XCTAssertNil(EvidenceItem.correlationGroup(for: id), "\(id) must be independent")
        }
    }

    func testEvidenceItem_derivesSourceFromMap_explicitSourceWins() {
        let hijack = EvidenceItem(section: .prophet, evidenceId: .nxSubdomainHijack, observation: "o", rationale: "r")
        XCTAssertEqual(hijack.source, "nxdomain_hijack")
        let independent = EvidenceItem(section: .mirror, evidenceId: .redirection, observation: "o", rationale: "r")
        XCTAssertNil(independent.source)
        // An explicit source (as DNSCanaries passes) overrides the derived map value.
        let canary = EvidenceItem(section: .prophet, evidenceId: .nxCanaryHijack, observation: "o", rationale: "r", source: "canaries")
        XCTAssertEqual(canary.source, "canaries")
    }

    // MARK: - The engine effect: correlated groups are discounted, independent signals are not

    private func statusesProphetPositive() -> [SectionStatusEntry] {
        [ SectionStatusEntry(section: .prophet, status: .positive),
          SectionStatusEntry(section: .mirror,  status: .untested),
          SectionStatusEntry(section: .portal,  status: .untested),
          SectionStatusEntry(section: .glutton, status: .untested),
          SectionStatusEntry(section: .cage,    status: .untested),
          SectionStatusEntry(section: .witness, status: .untested) ]
    }

    /// Two same-source correlated signals must yield LESS confidence than the identical two signals
    /// treated as independent (distinct sources) — the correlation discount is real, not dead.
    func testCorrelatedGroup_isDiscountedBelowIndependentStacking() {
        let statuses = statusesProphetPositive()
        // Correlated: both derive source "nxdomain_hijack" → merged into one capped support.
        let correlated = [
            EvidenceItem(section: .prophet, evidenceId: .nxSubdomainHijack, observation: "o", rationale: "r"),
            EvidenceItem(section: .prophet, evidenceId: .denialInjected,     observation: "o", rationale: "r"),
        ]
        // Same two evidence ids, forced to DISTINCT sources → engine treats them as independent.
        let independent = [
            EvidenceItem(section: .prophet, evidenceId: .nxSubdomainHijack, observation: "o", rationale: "r", source: "indepA"),
            EvidenceItem(section: .prophet, evidenceId: .denialInjected,     observation: "o", rationale: "r", source: "indepB"),
        ]
        let cCorr  = SynthesisEngine.synthesize(evidence: correlated,  sectionStatuses: statuses)
        let cIndep = SynthesisEngine.synthesize(evidence: independent, sectionStatuses: statuses)
        XCTAssertLessThan(cCorr.confidence, cIndep.confidence,
            "correlated same-source signals must not stack like independent evidence")
    }

    /// Two genuinely independent (different-source) signals must NOT be discounted relative to each
    /// other — corroboration from distinct probes is exactly what should raise confidence.
    func testDifferentSourceSignals_areNotDiscounted() {
        let statuses = statusesProphetPositive()
        // Two independent signals (nil source in the real map) each contribute their own support.
        let a = EvidenceItem(section: .prophet, evidenceId: .nxSubdomainHijack, observation: "o", rationale: "r", source: "srcA")
        let b = EvidenceItem(section: .prophet, evidenceId: .denialInjected,     observation: "o", rationale: "r", source: "srcB")
        let two   = SynthesisEngine.synthesize(evidence: [a, b], sectionStatuses: statuses)
        let onlyA = SynthesisEngine.synthesize(evidence: [a],    sectionStatuses: statuses)
        // Adding a second independent corroborating signal raises (never lowers) confidence.
        XCTAssertGreaterThan(two.confidence, onlyA.confidence,
            "a second independent corroborating signal must raise confidence")
    }

    /// A strong correlated group (max item > maxWeightPerSource) is capped: its combined confidence
    /// must not exceed a single independent signal already at the cap by more than the merge allows.
    func testStrongCorrelatedGroup_isCappedAtMaxWeightPerSource() {
        let statuses = statusesProphetPositive()
        // Two strong same-source items (canary-strength, 3.0) merged and capped at 2.0.
        let group = [
            EvidenceItem(section: .prophet, evidenceId: .nxCanaryHijack,        observation: "o", rationale: "r", source: "grp"),
            EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,  observation: "o", rationale: "r", source: "grp"),
        ]
        // The same two as independent → higher confidence (uncapped stacking).
        let indep = [
            EvidenceItem(section: .prophet, evidenceId: .nxCanaryHijack,        observation: "o", rationale: "r", source: "g1"),
            EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped,  observation: "o", rationale: "r", source: "g2"),
        ]
        let cGroup = SynthesisEngine.synthesize(evidence: group, sectionStatuses: statuses)
        let cIndep = SynthesisEngine.synthesize(evidence: indep, sectionStatuses: statuses)
        XCTAssertLessThan(cGroup.confidence, cIndep.confidence,
            "a strong correlated group must be capped below its uncapped independent stacking")
    }
}
