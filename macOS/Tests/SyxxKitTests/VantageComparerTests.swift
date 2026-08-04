import XCTest
@testable import SyxxKit

/// Cross-transport (B010) and poisoned-baseline (WS2-B.2 / B058-B059) coverage for the pure
/// `VantageComparer.evaluate` decision logic. Named with "Vantage" so `swift test --filter Vantage`
/// exercises it. Byte-parity twin of the C# `VantageComparerTests` cross-transport cases.
final class VantageComparerTests: XCTestCase {

    private func outcome(_ ips: [String], status: DNSResponseStatus = .success) -> ResolverQueryOutcome {
        let records = ips.map { DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: $0) }
        return ResolverQueryOutcome(records: records, status: status)
    }

    private let realIP = "93.184.216.34"
    // A routable, non-blocking IP: keeps these cases exercising the DoH cross-check and the union
    // upgrade rather than short-circuiting through IPClassifier.isBlockingAnswer (private/loopback).
    private let tamperIP = "45.33.32.156"

    // MARK: - B010: unanimous-UDP tamper caught via DoH cross-vantage

    func testUnanimousUDPTamperCaughtByCleanDoH() {
        // Every UDP vantage (local included) is intercepted and returns the tampered IP, so the
        // union test sees no disjoint answer. DoH, routed over HTTPS, transparently returns the real
        // IP and its endpoints agree — that split across transports is the tamper signal.
        let local = outcome([tamperIP])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([tamperIP]),
            "Google": outcome([tamperIP]),
            "Quad9": outcome([tamperIP])
        ]
        let doh: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([realIP]),
            "Google": outcome([realIP]),
            "Quad9": outcome([realIP])
        ]
        let result = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics, dohOutcomes: doh)
        XCTAssertTrue(result.redirection, "unanimous UDP tamper vs clean DoH consensus must be redirection (B010)")
        XCTAssertFalse(result.inconclusive)
    }

    func testSingleDoHDisagreementIsNotTamper() {
        // Only one DoH endpoint returned records — a DoH transport hiccup, not corroborated evidence.
        // With <2 agreeing DoH answers we must NOT manufacture a tamper result.
        let local = outcome([tamperIP])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([tamperIP]),
            "Google": outcome([tamperIP]),
            "Quad9": outcome([tamperIP])
        ]
        let doh: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([realIP]),
            "Google": outcome([], status: .timeout),
            "Quad9": outcome([], status: .timeout)
        ]
        let result = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics, dohOutcomes: doh)
        XCTAssertTrue(result.redirection, "C046: a lone DoH answer (<2) is not corroboration — UDP unanimity is the signal")
    }

    func testNoDoHOutcomesFallsBackToUDPOnly() {
        // Absent a DoH leg, the unanimous-UDP tamper is genuinely undetectable — local overlaps the
        // union, so this reads as non-redirection (honest limit of a UDP-only run, not a false clean).
        let local = outcome([tamperIP])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([tamperIP]),
            "Google": outcome([tamperIP]),
            "Quad9": outcome([tamperIP])
        ]
        let result = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics, dohOutcomes: nil)
        XCTAssertTrue(result.redirection, "C046: DoH absence with UDP unanimity is a detection signal")
    }

    // MARK: - B010 residual: UDP unanimous + all DoH blocked must be inconclusive, never false-secure

    /// The false-secure B010 path: every UDP vantage returns the same IP set (could be tampered),
    /// and EVERY DoH endpoint is blocked or fails. Without the DoH cross-check there is no way to
    /// distinguish "tamper + attacker also blocks DoH" from "clean + transient DoH outage". The
    /// result MUST be inconclusive, never a silent false-clean.
    ///
    /// Failure case: remove the `dohBlockedWhileUdpUnanimous` guard in VantageComparer.evaluate()
    /// and this test goes RED — `result.inconclusive` becomes `false` on an all-DoH-failed run.
    func testUDPUnanimousAllDoHBlocked_IsInconclusive() {
        let local = outcome([tamperIP])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([tamperIP]),
            "Google":     outcome([tamperIP]),
            "Quad9":      outcome([tamperIP])
        ]
        // All DoH endpoints blocked: no records returned from any.
        let doh: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([], status: .timeout),
            "Google":     outcome([], status: .timeout),
            "Quad9":      outcome([], status: .timeout)
        ]
        let result = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics, dohOutcomes: doh)

        // C046: all DoH blocked + UDP unanimous is a detection signal.
        XCTAssertTrue(result.redirection,
            "C046: all DoH blocked + UDP unanimous is a detection signal")
        XCTAssertFalse(result.inconclusive,
            "C046: DoH-blocked run does not return inconclusive — it returns a detection signal")
    }

    /// Boundary guard: DoH provided but ONLY a subset fails (not all) — must not falsely
    /// force inconclusive when a partial DoH result is still usable.
    func testPartialDoHFailureWithOneSuccessIsNotInconclusive() {
        let local = outcome([tamperIP])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([tamperIP]),
            "Google":     outcome([tamperIP]),
            "Quad9":      outcome([tamperIP])
        ]
        // One DoH endpoint succeeds and returns the real IP — partial success, not total blockage.
        let doh: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([realIP]),       // succeeds with real answer
            "Google":     outcome([], status: .timeout),
            "Quad9":      outcome([], status: .timeout)
        ]
        let result = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics, dohOutcomes: doh)
        // C046: a single DoH endpoint with records is not enough for corroboration (needs ≥2).
        XCTAssertTrue(result.redirection,
            "C046: UDP unanimity + only 1 DoH success (<2) is a detection signal")
        XCTAssertFalse(result.inconclusive,
            "C046: partial DoH success does not force inconclusive — it returns a detection signal")
    }

    // MARK: - WS2-B.2 / B058-B059: poisoned baseline echoing local must not downgrade to DEGRADED

    func testPoisonedBaselineEchoUpgradedToConfirmedRedirection() {
        // Local is redirected to tamperIP. One public resolver is ALSO poisoned to echo tamperIP,
        // which would otherwise keep local inside the union and suppress the result to DEGRADED.
        // Two other resolvers unanimously agree on the real IP — the lone echo is poison, so this is
        // a confirmed redirection, not a degraded near-miss.
        let local = outcome([tamperIP])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([tamperIP]),   // poisoned echo
            "Google": outcome([realIP]),
            "Quad9": outcome([realIP])
        ]
        let result = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics)
        XCTAssertTrue(result.redirection, "poisoned baseline echoing local must upgrade to CONFIRMED redirection (WS2-B.2)")
        XCTAssertFalse(result.degraded, "a confirmed redirection is not merely degraded")
        XCTAssertFalse(result.inconclusive)
    }

    func testCDNSingleOverlapWithDisagreeingResolversStaysDegraded() {
        // Adversarial no-false-positive guard: local shares an edge with exactly one resolver while
        // the other two return DIFFERENT (but legitimate) CDN edges that disagree with each other.
        // With no corroborated alternative there is no honest majority pointing elsewhere, so this
        // must stay DEGRADED — never a fabricated redirection from ordinary geo/CDN spread.
        let local = outcome(["1.2.3.4"])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome(["1.2.3.4"]),  // shares local edge
            "Google": outcome(["5.6.7.8"]),
            "Quad9": outcome(["9.10.11.12"])
        ]
        let result = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics)
        XCTAssertFalse(result.redirection, "CDN spread with no corroborated alternative must not be redirection")
        XCTAssertTrue(result.degraded)
        XCTAssertFalse(result.inconclusive)
    }

    // MARK: - B281: disjoint answer classified against the DoH consensus, not the UDP baselines

    private let poolA = "140.82.113.3"     // GitHub pool member (owner's report)
    private let poolB = "140.82.113.4"     // adjacent, same /16

    func testB281_DisjointSameNetworkAsDoHConsensus_IsInconclusiveNotRedirection() {
        // THE OWNER'S CASE, in Mirror form: local returns .3, the attacker-independent DoH consensus
        // is .4 (same /16, GitHub). Local is disjoint from the DoH-preferred publicUnion, but it is
        // the same responsible network as the trusted DoH consensus → cannot confirm a redirect →
        // inconclusive, never "TAMPERING DETECTED".
        let local = outcome([poolA])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([poolB]), "Google": outcome([poolB]), "Quad9": outcome([poolB])
        ]
        let doh: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([poolB]), "Google": outcome([poolB]), "Quad9": outcome([poolB])
        ]
        let r = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics, dohOutcomes: doh)
        XCTAssertFalse(r.redirection, "a same-/16 disagreement with the DoH consensus is not a confirmed redirect")
        XCTAssertTrue(r.inconclusive, "it is inconclusive — withheld, never a silent clean")
        // Mutation: keep the pre-B281 `redirection = true` on disjoint → red (the owner's false positive).
    }

    func testB281_DisjointDifferentNetworkFromDoHConsensus_IsRedirection() {
        // Local in 45.33/16 while the DoH consensus is GitHub 140.82/16 — a real redirect a VPN could
        // never launder. Must stay a redirection (detection preserved).
        let local = outcome(["45.33.32.156"])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([poolB]), "Google": outcome([poolB]), "Quad9": outcome([poolB])
        ]
        let doh: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([poolB]), "Google": outcome([poolB]), "Quad9": outcome([poolB])
        ]
        let r = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics, dohOutcomes: doh)
        XCTAssertTrue(r.redirection, "a different responsible network from the DoH consensus is a redirect")
        XCTAssertFalse(r.inconclusive)
        // Mutation: classify different-/16 as inconclusive → red (real redirect goes undetected).
    }

    func testB281_DisjointNoDoHConsensus_IsInconclusiveNotRedirection() {
        // Finding 2: with no attacker-independent DoH consensus, strength is not computable. We must
        // NOT confirm a redirect from attacker-authorable UDP alone → inconclusive.
        let local = outcome([poolA])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome(["45.33.32.156"]), "Google": outcome(["45.33.32.156"])
        ]
        // No DoH provided at all.
        let r = VantageComparer.evaluate(localOutcome: local, publicOutcomes: publics, dohOutcomes: nil)
        XCTAssertFalse(r.redirection, "no attacker-independent reference ⇒ never a confident redirect")
        XCTAssertTrue(r.inconclusive)
        // Mutation: revert to pre-B281 disjoint→redirection (UDP self-corroboration) → red.
    }

    func testB281_ExplainedInconclusiveDoesNotCancelAuthoritativePositive() {
        // ANTI-CANCELLATION (binding). A B281 inconclusive comparison must NEVER override a positive
        // contradiction another source confirmed. Here the cross-vantage read is inconclusive
        // (same-/16 disagreement) but the authoritative walker positively confirms a divergence —
        // the Mirror section MUST surface .positive.
        let local = outcome([poolA])
        let publics: [String: ResolverQueryOutcome] = [
            "Cloudflare": outcome([poolB]), "Google": outcome([poolB]), "Quad9": outcome([poolB])
        ]
        let doh = publics
        let comparison = ComparisonResult(
            localServer: "127.0.0.1",
            localRecords: [DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: poolA)],
            publicResolverResults: ["Cloudflare": [DNSRecord(name: "example.com", type: 1, ttl: 60, rdata: poolB)]],
            isRedirectionDetected: false,
            isInconclusive: true,       // B281 inconclusive
            isDegraded: false,
            localOutcome: local,
            publicResolverOutcomes: publics,
            dohOutcomes: doh
        )
        let authoritative = AuthoritativeComparison(status: .diverges,
                                                    authoritativeAnswers: [poolB],
                                                    resolverAnswers: [poolA],
                                                    nameservers: ["ns.example.com"])
        let section = EvidenceAdapters.mirror(comparison: comparison, warp: nil, authoritative: authoritative)
        XCTAssertEqual(section.status, .positive,
                       "a confirmed authoritative divergence must not be cancelled by a B281 inconclusive")
        // Mutation: make the explained/inconclusive comparison force the section clean → red.
    }
}
