import XCTest
@testable import SyxxKit

/// Realistic MULTI-SIGNAL runs through the full engine (adapters → softmax → gating → confidence →
/// referral), exercising the newly-wired Workstream H datapoints in combination. Unit tests prove
/// each adapter in isolation; these prove the datapoints compose into sane results — and that the
/// dampeners (captive portal, encrypted DNS) and the A1 quality-only rule survive integration.
final class SynthesisScenarioTests: XCTestCase {

    /// A fully-benign run with all six sections having actually run (the clean baseline every
    /// scenario mutates). Ph3.2: provides all datapoints so section completeness reaches 1.0,
    /// allowing a clean result. Mirrors `SynthesizerTests.testFullyCleanRun`.
    private func cleanBaseline(encryptedDNS: TriState = .yes,
                               captivePortal: TriState = .no) -> SectionInputs {
        var inputs = SectionInputs()
        inputs.resolution = DNSResponse(status: .success, records: [DNSRecord(name: "a", type: 1, ttl: 60, rdata: "1.1.1.1")])
        inputs.dnssec = .recordsPresentUnverified
        inputs.nxCanary = DNSCanaries.CanaryResult(section: .prophet, status: .negative, evidence: nil, detail: "")
        inputs.dnssecCanary = DNSCanaries.CanaryResult(section: .prophet, status: .negative, evidence: nil, detail: "")
        inputs.leak = LeakTestResult(result: .notDetected)
        inputs.exfil = []
        inputs.drifts = []
        inputs.configChanges = []
        // Ph3.2: all clean datapoints so completeness reaches 1.0.
        inputs.latency = ResolverLatency(status: .fast, sampleCount: 3, successCount: 3, medianMs: 20)
        inputs.stability = AnswerStability(status: .stable, usableCount: 3)
        inputs.edns0 = EDNS0Capability(status: .supported, udpPayloadSize: 1232)
        inputs.stubEntropy = StubEntropyAudit(status: .wellRandomized, sampleCount: 12)
        inputs.ecs = ECSLeak(status: .notForwarded)
        inputs.encryptedTransport = EncryptedTransport(status: .working, endpoint: "cloudflare-dns.com")
        inputs.dualStack = DualStackAudit(status: .dualStack)
        inputs.authoritative = AuthoritativeComparison(status: .matches)
        inputs.svcb = SVCBFinding(status: .absent, presentAt: [], absentAt: [])
        inputs.ttlAnomaly = TTLAnomaly(status: .independentCaching)
        inputs.cnameChain = CNAMEChainAudit(status: .consistent)
        inputs.network = NetworkStateReport(
            dnsServers: ["192.168.1.1"], activeInterfaces: ["en0"], vpnActive: false, captivePortalDetected: captivePortal == .yes,
            vpn: VPNStatus(state: .no, tunnelInterfaces: [], evidence: ""),
            captivePortal: CaptivePortalStatus(state: captivePortal, evidence: ""),
            encryptedDNS: EncryptedDNSStatus(state: encryptedDNS, evidence: ""),
            scopedResolvers: [], gateway: nil)
        // A clean run means the vantage points AGREED ON SOMETHING — not that nothing was
        // observed. This fixture previously passed empty localRecords/publicResolverResults, which
        // only reached `.clean` because EvidenceAdapters.mirror had no record-count guard and
        // treated an empty comparison as clean support. That guard now exists (ROADMAP TOP-TEN #1),
        // so the baseline states its evidence explicitly instead of depending on the gap.
        let agreed = DNSRecord(name: "a", type: 1, ttl: 60, rdata: "1.1.1.1")
        inputs.comparison = ComparisonResult(
            localServer: "192.168.1.1", localRecords: [agreed],
            publicResolverResults: ["Cloudflare": [agreed]],
            isRedirectionDetected: false, isInconclusive: false, isDegraded: false)
        return inputs
    }

    // MARK: spoofing — authoritative ground truth + unstable answers corroborate

    func testSpoofing_authoritativeDivergesPlusUnstableAnswers() {
        var inputs = cleanBaseline()
        inputs.authoritative = AuthoritativeComparison(status: .diverges,
            authoritativeAnswers: ["1.1.1.1"], resolverAnswers: ["203.0.113.9"], nameservers: ["ns1.example.com"])
        inputs.stability = AnswerStability(status: .unstable, sampleCount: 3, usableCount: 3, distinctAnswerSets: 3)
        let c = Synthesizer.conclude(from: inputs)
        // Authoritative divergence must NEVER read as censorship (a block), and never as clean (B020).
        // With only divergence + instability the committed threat belief sits below the 0.40 name-floor,
        // so the honest result is inconclusive (or a threat name if belief clears the floor) — the point
        // is that it is not the mis-seeded censorship it used to be.
        XCTAssertNotEqual(c.diagnosis, .censorship, "authoritative divergence must not read as censorship, got \(c.diagnosis)")
        XCTAssertNotEqual(c.diagnosis, .clean)
        XCTAssertTrue([.clean, .spoofing, .interception_onPath, .interception_transport].contains(c.diagnosis), "got \(c.diagnosis)")
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .authoritativeDiverges })
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .answerUnstable })
    }

    // MARK: dampener — a captive portal is a benign explanation for on-path divergence

    func testCaptivePortalDampensAuthoritativeDivergence() {
        var inputs = cleanBaseline(encryptedDNS: .unknown, captivePortal: .yes)
        inputs.authoritative = AuthoritativeComparison(status: .diverges,
            authoritativeAnswers: ["1.1.1.1"], resolverAnswers: ["203.0.113.9"], nameservers: ["ns1.example.com"])
        let c = Synthesizer.conclude(from: inputs)
        // The captive-portal dampener (-2.0 vs spoofing/onPath/transport) must neutralise the on-path
        // reads, so the engine does not accuse spoofing/interception when a portal explains it.
        XCTAssertFalse([.spoofing, .interception_onPath, .interception_transport].contains(c.diagnosis), "portal should dampen on-path reads, got \(c.diagnosis)")
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .captivePortalActive })
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .authoritativeDiverges })  // still surfaced for review
    }

    // MARK: B021 — belief is 0 for a threat with no committed evidence (honest "evidence strength")

    func testBeliefIsZeroForUnevidencedThreat() {
        // Portal scenario: contentTampering has NO evidence, yet the pignistic distribution spreads
        // clean-support set-mass onto it. Belief (committed Bel) must read 0 for it — the honest
        // "evidence points here" signal — even though Likelihood/BetP may show a nonzero share.
        var i = cleanBaseline(encryptedDNS: .unknown, captivePortal: .yes)
        i.authoritative = AuthoritativeComparison(status: .diverges,
            authoritativeAnswers: ["1.1.1.1"], resolverAnswers: ["203.0.113.9"], nameservers: ["ns1.example.com"])
        let c = Synthesizer.conclude(from: i)
        let ctBelief = c.belief.first { $0.hypothesis == .contentTampering }?.probability ?? -1
        XCTAssertEqual(ctBelief, 0, accuracy: 1e-9, "unevidenced threat must have zero committed belief")
        XCTAssertTrue(c.conclusivity == .inconclusive || c.conclusivity == .untested)
        XCTAssertEqual(c.confidence, 0, "an inconclusive result names no conclusion → confidence 0 (B022)")
    }

    func testEmptyRunHasZeroBeliefEverywhere() {
        // The property the naive "zero + renormalize" fix would have broken: on no evidence, belief
        // is 0 for EVERY hypothesis (including clean) — nothing is claimed.
        let c = Synthesizer.conclude(from: SectionInputs())
        for hp in c.belief {
            XCTAssertEqual(hp.probability, 0, accuracy: 1e-9, "empty run must claim zero belief in \(hp.hypothesis)")
        }
    }

    // MARK: interception_transport — forced-cleartext + SVCB stripping stack toward transport downgrade

    func testInterception_encryptedBlockedPlusUnencryptedPlusSvcbStripped() {
        var inputs = cleanBaseline(encryptedDNS: .no)   // system DNS cleartext → dnsUnencrypted
        inputs.encryptedTransport = EncryptedTransport(status: .blocked, endpoint: "cloudflare-dns.com")
        inputs.svcb = SVCBFinding(status: .divergent, presentAt: ["1.1.1.1"], absentAt: ["8.8.8.8"])
        let c = Synthesizer.conclude(from: inputs)
        XCTAssertEqual(c.diagnosis, .interception_transport, "got \(c.diagnosis)")
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .encryptedDnsBlocked })
        XCTAssertTrue(c.evidence.contains { $0.evidenceId == .svcbStripped })
        XCTAssertNotEqual(c.diagnosis, .clean)
    }

    // MARK: A1 — latency is a quality overlay: never a THREAT (contributes no threat mass), and under the
    // P1.2 critical-section gate a slow resolver degrades PROPHET (a critical section), so an otherwise-clean
    // run reads honest-INCONCLUSIVE rather than "clean, low confidence". This supersedes the pre-P1.2 "stays
    // clean" expectation: a degraded critical section is not a complete clean bill of health, so we never
    // overclaim clean on it. (The confidence-only quality-caveat distinction that would let latency lower
    // confidence WITHOUT forbidding clean is the deferred Phase-3 weight-free QualityCaveat channel — NE5/P1.8.)
    // FLAGGED to the planner: this is the A1-invariant ⇄ P1.2/P1.3 interaction; confirm latency-slow →
    // inconclusive is the intended Phase-1 posture until the QualityCaveat channel restores the distinction.

    func testLatencyIsQualityOnly_neverAThreat_andCriticalDegradedForbidsClean() {
        let clean = Synthesizer.conclude(from: cleanBaseline())
        XCTAssertEqual(clean.diagnosis, .clean)

        var slow = cleanBaseline()
        slow.latency = ResolverLatency(status: .slow, sampleCount: 4, successCount: 4, medianMs: 220)
        let slowC = Synthesizer.conclude(from: slow)

        // A slow resolver degrades PROPHET (critical) → P1.2 forbids clean → honest inconclusive, never clean.
        XCTAssertEqual(slowC.sectionStatuses.first { $0.section == .prophet }?.status, .degraded)
        let isClean = slowC.diagnosis == .clean && slowC.conclusivity == .conclusive
        XCTAssertFalse(isClean, "a degraded critical section is not a clean bill of health")
        XCTAssertTrue(slowC.conclusivity == .inconclusive)
        XCTAssertTrue(slowC.referralTriggered)
        // The A1 core still holds: latency contributes NO threat evidence and never names a threat.
        let threatIds: Set<EvidenceId> = [.authoritativeDiverges, .svcbStripped, .answerUnstable, .encryptedDnsBlocked]
        XCTAssertFalse(slowC.evidence.contains { threatIds.contains($0.evidenceId) })
    }

    // MARK: hard gate — a lone positive datapoint with the rest untested can never read clean

    func testAuthoritativeDivergesAloneIsGatedFromClean() {
        var inputs = SectionInputs()   // five sections untested
        inputs.authoritative = AuthoritativeComparison(status: .diverges,
            authoritativeAnswers: ["1.1.1.1"], resolverAnswers: ["203.0.113.9"], nameservers: ["ns1.example.com"])
        let c = Synthesizer.conclude(from: inputs)
        XCTAssertNotEqual(c.diagnosis, .clean)
        XCTAssertTrue(c.hasHardGatedSection)
        XCTAssertTrue(c.referralTriggered)
    }
}
