import XCTest
@testable import SyxxKit

/// B281 — coverage for the SHARED cross-vantage agreement policy (`CrossVantageConsensus`) that
/// Prophet's consensus card and Mirror's `VantageComparer` both consume. The adversarial review
/// (2026-08-02) found ZERO coverage for the strength primitives; this file is that coverage. Every
/// test names the mutation that turns it red, so a wrong policy edit cannot pass silently.
///
/// Byte-parity twin: Windows `Syxx.Tests/CrossVantageConsensusTests.cs`.
final class CrossVantageConsensusTests: XCTestCase {

    // GitHub's announced 140.82.112.0/20 — .3 and .4 are the owner's reported pool members.
    private let poolA = "140.82.113.3"
    private let poolB = "140.82.113.4"        // adjacent, same /16
    private let poolFar = "140.82.121.9"      // same /16 (140.82), different /24
    private let unrelated = "45.33.32.156"    // different /16 → different responsible network
    private let quad9 = "9.9.9.9"             // different /16, routable
    private let blocked = "127.0.0.1"         // loopback → redirect-to-block signature

    private func rec(_ ip: String, type: UInt16 = 1) -> DNSRecord {
        DNSRecord(name: "example.com", type: type, ttl: 60, rdata: ip)
    }
    private func outcome(_ ips: [String], type: UInt16 = 1, status: DNSResponseStatus = .success) -> ResolverQueryOutcome {
        ResolverQueryOutcome(records: ips.map { rec($0, type: type) }, status: status)
    }

    // MARK: - classifyPeerConsensus (Prophet's decision, no DoH reference today)

    func testPeerConsensus_identicalSingleAddress_isMatch() {
        let r = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: [[poolB], [poolB], [poolB]])
        XCTAssertEqual(r, .match, "all resolvers returning the same RRset is agreement")
        // Mutation: return .mismatch/.inconclusive here → red.
    }

    func testPeerConsensus_identicalMultiAddress_orderIndependent_isMatch() {
        // Same RRset, different record order — set equality must treat these as identical.
        let r = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: [[poolA, poolB], [poolB, poolA]])
        XCTAssertEqual(r, .match)
        // Mutation: compare ordered arrays instead of sets → red.
    }

    func testPeerConsensus_ownerCase_sameNetworkSplit_isInconclusiveNotMismatch() {
        // THE OWNER'S REPORTED FALSE POSITIVE. Some resolvers say .3, others .4 (same /16, GitHub).
        // Prophet has no DoH reference, so this is honestly inconclusive — NOT "possible tampering".
        let r = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: [[poolA], [poolB], [poolA]])
        XCTAssertEqual(r, .inconclusive, "near-identical pool answers must NOT read as tampering")
        XCTAssertNotEqual(r, .mismatch, "the owner's .3/.4 split is a false positive as a mismatch")
        // Mutation: treat any difference as .mismatch → red (this is the bug being fixed).
    }

    func testPeerConsensus_P0c_differentMultiAddressSharingFirstElement_isNotAgreement() {
        // ADVERSARIAL-REVIEW P0-c, the worse-than-reported defect. Under `records.first` both sets
        // collapse to poolA and render as AGREEMENT (a false clean). With full RRsets they DIFFER
        // (one has poolB, the other has quad9), so this must never be `.match`.
        let a: Set<String> = [poolA, poolB]
        let b: Set<String> = [poolA, quad9]
        let r = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: [a, b])
        XCTAssertNotEqual(r, .match, "different RRsets sharing a first element must NOT be a clean match")
        // Mutation: reduce each set to its first element (records.first) → both become {poolA} →
        // .match → red. That is precisely the false clean the honesty spine forbids.
    }

    func testPeerConsensus_blockingBesideRoutable_isMismatch() {
        // A loopback answer beside a routable one is the redirect-to-block signature — a mismatch
        // even without a second transport.
        let r = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: [[blocked], [poolB]])
        XCTAssertEqual(r, .mismatch, "blocking answer beside routable is redirect-to-block")
        // Mutation: drop the blocking check → .inconclusive → red.
    }

    func testPeerConsensus_zeroUsable_isNoData() {
        let r = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: [[], []])
        XCTAssertEqual(r, .noData, "no usable answer is noData, never a clean match")
    }

    func testPeerConsensus_oneUsable_isInsufficient() {
        let r = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: [[poolB], []])
        XCTAssertEqual(r, .insufficient, "one reachable resolver cannot establish consensus")
        // Mutation: return .match for a single resolver → red (a lone resolver is not agreement).
    }

    func testPeerConsensus_differentNetwork_noReference_isInconclusiveNotMismatch() {
        // Two routable answers in different /16s, but Prophet has NO attacker-independent reference.
        // A big multi-/16 CDN looks identical to a redirect from addresses alone, so this is honestly
        // inconclusive — NOT a bare accusation.
        let r = CrossVantageConsensus.classifyPeerConsensus(vantageAnswers: [[poolA], [unrelated]])
        XCTAssertEqual(r, .inconclusive)
        // Mutation: call different-/16 a mismatch without a trusted reference → red (CDN false positive).
    }

    // MARK: - classifyDivergence (the one shared strength decision, given a trusted reference)

    func testDivergence_noTrustedReference_isInconclusive() {
        // Finding 2: with no attacker-independent reference, strength is not computable.
        let r = CrossVantageConsensus.classifyDivergence(candidate: [unrelated], trustedReference: [])
        XCTAssertEqual(r, .inconclusive, "no DoH reference ⇒ never a confident mismatch, never weak-clean")
    }

    func testDivergence_sameNetworkAsTrustedReference_isInconclusive() {
        // local .3 vs a DoH consensus of .4 (same /16). Cannot confirm a redirect → inconclusive.
        let r = CrossVantageConsensus.classifyDivergence(candidate: [poolA], trustedReference: [poolB])
        XCTAssertEqual(r, .inconclusive)
        // Mutation: return .match here (a clean) → red. Adjacency must NEVER grant a match.
    }

    func testDivergence_differentNetworkFromTrustedReference_isMismatch() {
        // local in 45.33/16 vs a DoH consensus in 140.82/16 → different responsible network → redirect.
        let r = CrossVantageConsensus.classifyDivergence(candidate: [unrelated], trustedReference: [poolB])
        XCTAssertEqual(r, .mismatch)
        // Mutation: treat different-/16 as inconclusive against a trusted reference → red.
    }

    // MARK: - isStrongDisagreement (family-aware)

    func testStrong_differentSlash16_isStrong() {
        XCTAssertTrue(CrossVantageConsensus.isStrongDisagreement(candidate: [unrelated], reference: [poolB]))
    }

    func testStrong_sameSlash16DifferentSlash24_isWeak() {
        // Same operator, different /24 pod — must be WEAK, or B281 re-creates the false positive.
        XCTAssertFalse(CrossVantageConsensus.isStrongDisagreement(candidate: [poolFar], reference: [poolB]))
        // Mutation: use /24 instead of /16 as the responsible network → this returns true → red.
    }

    func testStrong_mixedFamily_isNotStrong() {
        // ADVERSARIAL-REVIEW fix: a NAT64/DNS64 AAAA compared against A references is NOT comparable
        // and must not be a guaranteed false mismatch.
        let v6 = "2606:50c0:8000::153"
        XCTAssertFalse(CrossVantageConsensus.isStrongDisagreement(candidate: [v6], reference: [poolB]),
                       "cross-family (NAT64) must not be a forced strong disagreement")
        // Mutation: return true whenever no same-family reference shares a network → red.
    }

    func testStrong_ipv6DifferentSlash32_isStrong() {
        let a = "2606:50c0:8000::153"   // 2606:50c0/32
        let b = "2001:4860:4860::8888"  // 2001:4860/32
        XCTAssertTrue(CrossVantageConsensus.isStrongDisagreement(candidate: [a], reference: [b]))
    }

    func testStrong_ipv6SameSlash32_isWeak() {
        let a = "2606:50c0:8000::153"
        let b = "2606:50c0:9000::200"   // same 2606:50c0/32
        XCTAssertFalse(CrossVantageConsensus.isStrongDisagreement(candidate: [a], reference: [b]))
    }

    // MARK: - sharesResponsibleNetwork

    func testSharesNetwork_sameSlash16_true() {
        XCTAssertTrue(CrossVantageConsensus.sharesResponsibleNetwork(poolA, poolFar))
    }
    func testSharesNetwork_differentSlash16_false() {
        XCTAssertFalse(CrossVantageConsensus.sharesResponsibleNetwork(poolA, unrelated))
    }
    func testSharesNetwork_mixedFamily_false() {
        XCTAssertFalse(CrossVantageConsensus.sharesResponsibleNetwork(poolA, "2606:50c0::1"))
    }
    func testSharesNetwork_unparseable_false() {
        XCTAssertFalse(CrossVantageConsensus.sharesResponsibleNetwork("not-an-ip", poolA))
    }

    // MARK: - dohCorroboratedPool (attacker-independent reference; UDP never counted)

    func testDohPool_twoEndpointsAgree_included() {
        let doh = ["Cloudflare": outcome([poolB]), "Google": outcome([poolB]), "Quad9": outcome([poolA])]
        let pool = CrossVantageConsensus.dohCorroboratedPool(doh)
        XCTAssertTrue(pool.contains(poolB), "≥2 DoH endpoints agreeing on an address ⇒ verified pool member")
        XCTAssertFalse(pool.contains(poolA), "a single DoH endpoint is not corroboration")
        // Mutation: lower the threshold to ≥1 → poolA appears → red.
    }

    func testDohPool_nilOrSingle_isEmpty() {
        XCTAssertTrue(CrossVantageConsensus.dohCorroboratedPool(nil).isEmpty)
        XCTAssertTrue(CrossVantageConsensus.dohCorroboratedPool(["Cloudflare": outcome([poolB])]).isEmpty,
                      "one DoH endpoint cannot establish a corroborated pool")
    }

    func testDohPool_failedEndpointsNotCounted() {
        // Two endpoints "return" the address but both FAILED (timeout) — a failure is not a vote.
        let doh = ["Cloudflare": outcome([poolB], status: .timeout),
                   "Google": outcome([poolB], status: .timeout)]
        XCTAssertTrue(CrossVantageConsensus.dohCorroboratedPool(doh).isEmpty,
                      "timed-out DoH answers are not corroboration (missing data is not evidence)")
        // Mutation: count failed outcomes → poolB appears → red.
    }

    // MARK: - addressSet (C117 type filter; full RRsets only)

    func testAddressSet_filtersNonAddressRecords() {
        let records = [rec(poolA, type: 1), rec("e1.akamai.net", type: 5), rec("2606:50c0::1", type: 28)]
        let set = CrossVantageConsensus.addressSet(records)
        XCTAssertEqual(set, [poolA, "2606:50c0::1"], "CNAME(5) rdata must never enter the comparison")
    }
    func testAddressSet_noAddressRecords_isNil() {
        XCTAssertNil(CrossVantageConsensus.addressSet([rec("e1.akamai.net", type: 5)]),
                     "a CNAME-only answer is not a usable address vantage")
    }
}
