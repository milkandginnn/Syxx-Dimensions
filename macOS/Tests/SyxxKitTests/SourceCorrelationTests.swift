import XCTest
@testable import SyxxKit

/// B029 — the source-group correlation map on `EvidenceItem`.
///
/// The correlation map groups evidence items that are symptoms of one underlying phenomenon
/// (e.g. three transport-downgrade signals are one condition observed three ways). The `source`
/// field on `EvidenceItem` is used for display grouping and deduplication.
///
/// Engine-effect tests (previously tested that correlated groups were confidence-discounted
/// relative to independent signals) removed in matrix engine migration (2026-07-21): the
/// Dempster-Shafer confidence metric those tests compared no longer exists. The matrix engine
/// handles indicator-level deduplication via `EvidenceBundle.validate()` (same indicator id
/// collapses to max strength) rather than source-based correlation discounting.
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

    // MARK: - Engine-effect tests

    // REMOVED: testCorrelatedGroup_isDiscountedBelowIndependentStacking —
    // Dempster-Shafer confidence metric removed in matrix engine migration (2026-07-21).
    // The matrix engine deduplicates at the indicator level (same indicator id → max strength),
    // not at the source-correlation-group level. The correlation map still groups evidence for
    // display/provenance but does not drive a confidence discount.

    // REMOVED: testDifferentSourceSignals_areNotDiscounted —
    // Dempster-Shafer confidence metric removed in matrix engine migration (2026-07-21).
    // Two evidence items mapping to DIFFERENT indicators each contribute independently to
    // their respective diagnosis scores; two mapping to the SAME indicator collapse to max.

    // REMOVED: testStrongCorrelatedGroup_isCappedAtMaxWeightPerSource —
    // maxWeightPerSource and confidence capping removed in matrix engine migration (2026-07-21).
    // The matrix engine caps per-indicator (EvidenceBundle.validate rejects duplicates, keeping
    // max strength) rather than per-source-correlation-group.
}
