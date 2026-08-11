import XCTest
@testable import SyxxKit

/// End-to-end checks of the adapters + orchestrator: real section results flow through to a
/// `Conclusion`. Offline (no network).
final class SynthesizerTests: XCTestCase {

    func testAllNilInputs_isUntested_hardGated() {
        let c = Synthesizer.conclude(from: SectionInputs())
        XCTAssertEqual(c.determination?.state, .untested)
        XCTAssertTrue(c.hasHardGatedSection)         // all six sections untested
        // REMOVED: c.referralTriggered — referral concept removed in matrix engine migration (2026-07-21)
        XCTAssertEqual(c.sectionStatuses.count, 6)   // every section is represented
    }

    func testConfirmedLeak_yieldsThreatDetected_neverClean() {
        var inputs = SectionInputs()
        inputs.leak = LeakTestResult(result: .confirmed, observedResolvers: ["8.8.8.8"])
        let c = Synthesizer.conclude(from: inputs)
        // The matrix engine must reach .threatDetected for a confirmed leak — never .clean.
        XCTAssertNotEqual(c.determination?.state, .clean)
        XCTAssertEqual(c.sectionStatuses.first { $0.section == .glutton }?.status, .positive)
    }

    func testPositiveNxCanary_marksProphetPositive_andBlocksClean() {
        var inputs = SectionInputs()
        inputs.nxCanary = DNSCanaries.CanaryResult(
            section: .prophet, status: .positive,
            evidence: EvidenceItem(section: .prophet, evidenceId: .nxCanaryHijack, observation: "shared hijack IP", rationale: "x"),
            detail: "")
        let c = Synthesizer.conclude(from: inputs)
        XCTAssertEqual(c.sectionStatuses.first { $0.section == .prophet }?.status, .positive)
        XCTAssertNotEqual(c.determination?.state, .clean)
    }

    func testFullyCleanRun_isClean_noReferral() {
        var inputs = SectionInputs()
        inputs.resolution = DNSResponse(status: .success, records: [DNSRecord(name: "a", type: 1, ttl: 60, rdata: "1.1.1.1")])
        inputs.dnssec = .recordsPresentUnverified
        inputs.nxCanary = DNSCanaries.CanaryResult(section: .prophet, status: .negative, evidence: nil, detail: "")
        inputs.dnssecCanary = DNSCanaries.CanaryResult(section: .prophet, status: .negative, evidence: nil, detail: "")
        inputs.comparison = nil     // set below
        inputs.leak = LeakTestResult(result: .notDetected)
        inputs.exfil = []           // ran, nothing suspicious
        inputs.drifts = []          // ran, no drift
        inputs.configChanges = []
        // Ph3.2: provide all datapoints to reach full 1.0 completeness so clean result is reachable.
        inputs.latency = ResolverLatency(status: .fast, sampleCount: 3, successCount: 3, medianMs: 20)
        inputs.stability = AnswerStability(status: .stable, usableCount: 3)
        inputs.edns0 = EDNS0Capability(status: .supported, udpPayloadSize: 1232)
        inputs.stubEntropy = StubEntropyAudit(status: .wellRandomized, sampleCount: 12)
        inputs.ecs = ECSLeak(status: .notForwarded)
        inputs.encryptedTransport = EncryptedTransport(status: .working, endpoint: "cloudflare-dns.com")
        inputs.dualStack = DualStackAudit(status: .dualStack)
        // Mirror clean datapoints to ensure full coverage
        inputs.authoritative = AuthoritativeComparison(status: .matches)
        inputs.svcb = SVCBFinding(status: .absent, presentAt: [], absentAt: [])
        inputs.ttlAnomaly = TTLAnomaly(status: .independentCaching)
        inputs.cnameChain = CNAMEChainAudit(status: .consistent)
        inputs.network = NetworkStateReport(
            dnsServers: ["192.168.1.1"], activeInterfaces: ["en0"], vpnActive: false, captivePortalDetected: false,
            vpn: VPNStatus(state: .no, tunnelInterfaces: [], evidence: ""),
            captivePortal: CaptivePortalStatus(state: .no, evidence: ""),
            encryptedDNS: EncryptedDNSStatus(state: .yes, evidence: ""),
            scopedResolvers: [], gateway: nil)
        // warp stays nil → Mirror runs on the comparison alone (negative, no redirection).
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
        let c = Synthesizer.conclude(from: inputs)
        XCTAssertEqual(c.determination?.state, .clean)
        XCTAssertFalse(c.hasHardGatedSection)
    }
}
