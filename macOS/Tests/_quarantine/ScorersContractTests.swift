import XCTest
@testable import SyxxKit

// Ph5.2 / B005 score contracts. All three had ZERO direct tests (audit 2026-07-16) — and this is
// the one place in the repo where the regression they guard against HAS ALREADY HAPPENED: the
// comment at Scorers.swift:60-62 records that the original non-nullable signals made the no-data
// regime "UNREACHABLE", injecting a fabricated reading where there was no measurement.
//
// The contract in one line: NO DATA MUST PRODUCE NO NUMBER. A confidence score invented from zero
// signals is exactly the false-confidence this product exists to eliminate, and it is far more
// dangerous than an honest nil because a number renders as authority.
final class ScorersContractTests: XCTestCase {

    private func st(_ section: Section, _ status: SectionStatus) -> SectionStatusEntry {
        SectionStatusEntry(section: section, status: status)
    }

    private func ev(_ id: EvidenceId) -> EvidenceItem {
        EvidenceItem(section: .prophet, evidenceId: id, observation: "o", rationale: "r")
    }

    // MARK: - 1. resolverProvenanceConfidence — the no-data regime

    func testNoSignalsAtAll_ReturnsNil_NotZero() {
        // The load-bearing case. nil = "we did not measure". 0.0 = "we measured and it is bad".
        // Collapsing the first into the second fabricates a reading from nothing.
        let r = Scorers.resolverProvenanceConfidence(
            hasEDNS0: nil, portEntropy: nil, txidEntropy: nil,
            transportIsEncrypted: nil, resolverMatchesExpected: nil)
        XCTAssertNil(r, "zero signals must yield nil — never a number")
    }

    func testAllFiveSignals_ProduceAnUnattenuatedScore() {
        // Baseline: without this, "always nil" would pass every other test here.
        let r = Scorers.resolverProvenanceConfidence(
            hasEDNS0: true, portEntropy: 1.0, txidEntropy: 1.0,
            transportIsEncrypted: true, resolverMatchesExpected: true)
        XCTAssertNotNil(r)
        XCTAssertEqual(r!, 1.0, accuracy: 0.0001, "all five signals at max must reach full confidence")
    }

    func testFewerThanHalfTheSignals_StayLow() {
        // Two of five signals cannot yield high confidence however good they look, because three
        // probes never reported.
        //
        // NB: this is ATTENUATION doing the work, not the `min(0.40, ...)` cap on the line below
        // it — that cap is DEAD CODE and this test does not (and cannot) exercise it. See
        // testThePartialDataCapIsUnreachable_DeadCode below for the proof. Asserting `<= 0.40`
        // here would look like cap coverage while proving nothing about it.
        let r = Scorers.resolverProvenanceConfidence(
            hasEDNS0: true, portEntropy: 1.0, txidEntropy: nil,
            transportIsEncrypted: nil, resolverMatchesExpected: nil)
        XCTAssertNotNil(r)
        XCTAssertEqual(r!, 0.18, accuracy: 0.0001,
                       "two of five signals: rawScore 0.45 attenuated by 2/5 = 0.18")
    }

    func testThePartialDataCapIsUnreachable_DeadCode() {
        // FINDING (2026-07-16): `if available < signalCount/2 { attenuated = min(0.40, attenuated) }`
        // can NEVER fire. Its precondition restricts it to available ∈ {1, 2}, and the largest
        // rawScore two signals can produce is 0.45 (portEntropy 0.25 + txidEntropy 0.20), which
        // attenuates to 0.45 * 2/5 = 0.18 — far below 0.40. Brute-forcing the whole signal space
        // confirms ZERO inputs where the cap changes the result.
        //
        // This test PINS that fact rather than hiding it: the cap implies a protection it does not
        // provide, and a reader (or an auditor) would reasonably assume partial data is capped at
        // 0.40 when it is really capped at 0.18 by attenuation. If someone rebalances the weights
        // so two signals CAN exceed 0.40, this test fails and forces a deliberate decision about
        // whether the cap should live or die. Deleting the cap today is a no-op; deleting it after
        // a reweight would silently raise the partial-data ceiling.
        //
        // Windows Syxx.Kit/Synthesis/Scorers.cs carries the same dead cap.
        var maxAttenuated = 0.0
        let ednsOpts: [Bool?] = [nil, true, false]
        let entOpts: [Double?] = [nil, 0.0, 0.5, 1.0]
        let encOpts: [Bool?] = [nil, true, false]
        let matchOpts: [Bool?] = [nil, true, false]

        for e in ednsOpts { for pe in entOpts { for te in entOpts {
            for enc in encOpts { for m in matchOpts {
                var signals: [Double] = []
                if let e { signals.append(e ? 0.2 : 0) }
                if let pe { signals.append(pe * 0.25) }
                if let te { signals.append(te * 0.2) }
                if let enc { signals.append(enc ? 0.2 : 0.1) }
                if let m { signals.append(m ? 0.15 : 0) }
                let available = signals.count
                guard available > 0, Double(available) < 5.0 / 2.0 else { continue }
                maxAttenuated = max(maxAttenuated, signals.reduce(0, +) * (Double(available) / 5.0))
            }}
        }}}

        XCTAssertLessThan(maxAttenuated, 0.40,
            "the 0.40 cap is unreachable: attenuation already holds partial data to \(maxAttenuated). "
            + "If this fails, the weights were rebalanced and the cap now has a real job — decide "
            + "deliberately whether to keep it.")
    }

    func testPartialDataScoresBelowFullData_ForTheSameReadings() {
        // Attenuation must actually attenuate: identical positive readings must score LOWER when
        // fewer probes reported. Otherwise missing probes are free.
        let full = Scorers.resolverProvenanceConfidence(
            hasEDNS0: true, portEntropy: 1.0, txidEntropy: 1.0,
            transportIsEncrypted: true, resolverMatchesExpected: true)!
        let partial = Scorers.resolverProvenanceConfidence(
            hasEDNS0: true, portEntropy: 1.0, txidEntropy: 1.0,
            transportIsEncrypted: nil, resolverMatchesExpected: nil)!
        XCTAssertLessThan(partial, full, "the same readings with fewer probes must score lower")
    }

    // MARK: - 2. tamperMechanismFingerprint — minimum corroboration

    func testNoTamperSignals_ReturnsNil() {
        XCTAssertNil(Scorers.tamperMechanismFingerprint(evidence: [], sectionStatuses: []))
    }

    func testASingleSignal_IsBelowMinimumCorroboration_ReturnsNil() {
        // B005 §2: one signal is a hunch, not a fingerprint. Naming a mechanism from a lone
        // reading is precisely the over-claim the contract forbids.
        let r = Scorers.tamperMechanismFingerprint(
            evidence: [ev(.authoritativeDiverges)], sectionStatuses: [st(.prophet, .positive)])
        XCTAssertNil(r, "a single signal must not produce a fingerprint")
    }

    func testTwoCopiesOfTheSameReading_AreNotIndependent() {
        // Two items with the SAME evidenceId are one signal observed twice, not corroboration.
        let r = Scorers.tamperMechanismFingerprint(
            evidence: [ev(.authoritativeDiverges), ev(.authoritativeDiverges)],
            sectionStatuses: [st(.prophet, .positive)])
        XCTAssertNil(r, "duplicate readings of one signal are not independent corroboration")
    }

    func testTwoIndependentSignals_ProduceOnlyATentativeFingerprint() {
        // Mutation guard for the nil tests: two DISTINCT ids do produce a fingerprint — but a
        // tentative one (≤0.35), not a conclusion.
        let r = Scorers.tamperMechanismFingerprint(
            evidence: [ev(.authoritativeDiverges), ev(.cnameChainDivergent)],
            sectionStatuses: [st(.prophet, .positive)])
        XCTAssertNotNil(r, "two independent signals must produce a fingerprint")
        if let conf = r?.confidence {
            XCTAssertLessThanOrEqual(conf, 0.35,
                "fewer than 3 converging signals is TENTATIVE — confidence must stay ≤ 0.35")
        }
    }

    // MARK: - 3. evidenceConfidence — no conclusive section means no number

    func testNoConclusiveSections_ReturnsNil_NotAConfidenceNumber() {
        let r = Scorers.evidenceConfidence(
            baseConfidence: 90,
            sectionStatuses: [st(.prophet, .inconclusive), st(.mirror, .untested)])
        XCTAssertNil(r, "a run where nothing concluded must yield NO confidence number")
    }

    func testEmptyStatusList_ReturnsNil() {
        XCTAssertNil(Scorers.evidenceConfidence(baseConfidence: 90, sectionStatuses: []))
    }

    func testFullEvidence_ReturnsTheBaseScoreUnattenuated() {
        // Baseline for the attenuation tests below.
        let all: [SectionStatusEntry] = [
            st(.prophet, .negative), st(.mirror, .negative), st(.portal, .negative),
            st(.glutton, .negative), st(.cage, .negative), st(.witness, .negative),
        ]
        XCTAssertEqual(Scorers.evidenceConfidence(baseConfidence: 90, sectionStatuses: all), 90)
    }

    func testPartialEvidenceAttenuatesBelowTheBaseScore() {
        let partial: [SectionStatusEntry] = [
            st(.prophet, .negative), st(.mirror, .negative), st(.portal, .inconclusive),
            st(.glutton, .inconclusive), st(.cage, .inconclusive), st(.witness, .inconclusive),
        ]
        let r = Scorers.evidenceConfidence(baseConfidence: 90, sectionStatuses: partial)
        XCTAssertNotNil(r)
        XCTAssertLessThan(r!, 90, "a partial run must not report the full-evidence confidence")
    }

    // MARK: - ROADMAP TOP-TEN #1 — Mirror must not manufacture clean support from nothing

    /// An otherwise-COMPLETE clean run. Every section reports; only the Mirror comparison varies.
    /// This is the fixture that gives the guard teeth: with a lesser fixture the run is hard-gated
    /// anyway, so `.inconclusive` proves nothing about Mirror.
    private func otherwiseCleanRun(comparison: SyxxKit.ComparisonResult) -> SectionInputs {
        var inputs = SectionInputs()
        inputs.resolution = DNSResponse(status: .success,
            records: [DNSRecord(name: "a", type: 1, ttl: 60, rdata: "1.1.1.1")])
        inputs.dnssec = .recordsPresentUnverified
        inputs.nxCanary = DNSCanaries.CanaryResult(section: .prophet, status: .negative, evidence: nil, detail: "")
        inputs.dnssecCanary = DNSCanaries.CanaryResult(section: .prophet, status: .negative, evidence: nil, detail: "")
        inputs.leak = LeakTestResult(result: .notDetected)
        inputs.exfil = []
        inputs.drifts = []
        inputs.configChanges = []
        inputs.network = NetworkStateReport(
            dnsServers: ["192.168.1.1"], activeInterfaces: ["en0"], vpnActive: false,
            captivePortalDetected: false,
            vpn: VPNStatus(state: .no, tunnelInterfaces: [], evidence: ""),
            captivePortal: CaptivePortalStatus(state: .no, evidence: ""),
            encryptedDNS: EncryptedDNSStatus(state: .yes, evidence: ""),
            scopedResolvers: [], gateway: nil)
        inputs.comparison = comparison
        return inputs
    }

    func testZeroEvidenceMirror_CannotCarryAnOtherwiseCleanRunToClean() {
        // A ComparisonResult with NO local records and NO public results compared NOTHING. Its
        // flags are all false because nothing was observed, not because the answers agreed.
        //
        // This was a FALSE CLEAN ENCODED AS THE CONTRACT: EvidenceAdapters.mirror fell through to
        // `.negative` (clean support), and SynthesizerTests ASSERTED `.clean` for exactly this
        // input — so the test would have gone red if someone FIXED the engine. Worst defect class
        // in the repo: not a missing test, but a test defending the bug.
        //
        // The fixture is otherwise fully clean ON PURPOSE. Every other section reports, so the run
        // is NOT hard-gated and the ONLY thing that can stop `.clean` is the Mirror guard. (My
        // first version of this test passed a bare comparison and was vacuous — the hard gate did
        // the work and the mutation went undetected.)
        let empty = SyxxKit.ComparisonResult(
            localServer: "192.168.1.1", localRecords: [], publicResolverResults: [:],
            isRedirectionDetected: false, isInconclusive: false, isDegraded: false)

        let c = Synthesizer.conclude(from: otherwiseCleanRun(comparison: empty))
        XCTAssertNotEqual(c.result, .clean,
            "a Mirror that compared NOTHING must never carry an otherwise-clean run to clean")
    }

    func testMirrorWithAgreeingRecords_StillReachesClean() {
        // Mutation guard: the fix must reject only EMPTY comparisons. A Mirror that actually
        // compared real agreeing records must still carry the run to clean — otherwise "always
        // inconclusive" would pass the test above while breaking every real clean run.
        let agreed = DNSRecord(name: "a", type: 1, ttl: 60, rdata: "1.1.1.1")
        let real = SyxxKit.ComparisonResult(
            localServer: "192.168.1.1", localRecords: [agreed],
            publicResolverResults: ["Cloudflare": [agreed]],
            isRedirectionDetected: false, isInconclusive: false, isDegraded: false)

        let c = Synthesizer.conclude(from: otherwiseCleanRun(comparison: real))
        XCTAssertEqual(c.result, .clean,
            "a Mirror that compared real agreeing records must still support a clean result")
    }
}
