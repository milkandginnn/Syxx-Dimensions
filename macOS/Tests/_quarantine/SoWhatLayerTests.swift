import XCTest
@testable import SyxxKit

// Phase A twins (A1 ResultAction, A2 PlainExplanation, A3 ReportCard, A6 TrayIndicator) — the
// macOS side of the Windows Syxx.Kit implementations. The honesty gates are the point: an
// unfinished run must never read as safe on ANY surface.
//
// These mirror Windows ResultActionTests / PlainExplanationTests / ReportCardTests /
// TrayIndicatorTests. Where a string is asserted it is asserted VERBATIM, because the parity rule
// makes the twin's wording part of the contract — a drift in copy between platforms is a defect.
final class SoWhatLayerTests: XCTestCase {

    private func make(
        _ diagnosis: Hypothesis,
        conclusivity: Conclusivity = .conclusive,
        confidence: Int = 90,
        referral: Bool = false,
        evidence: [EvidenceItem] = [],
        statuses: [SectionStatusEntry]? = nil
    ) -> Conclusion {
        // `negative` = the check RAN and found nothing (clean-support). `positive` means a THREAT
        // was found and gates clean out entirely — it must not be the default for a clean fixture.
        let st = statuses ?? [SectionStatusEntry(section: .prophet, status: .negative)]
        return Conclusion(
            diagnosis: diagnosis, conclusivity: conclusivity, confidence: confidence,
            probabilities: [], evidence: evidence,
            nextSteps: [], referralTriggered: referral,
            referral: referral ? Referral(reason: "low", subreddits: ["r/AskNetSec"], prefilledPost: "") : nil,
            generatedAt: Date(timeIntervalSince1970: 0),
            sectionStatuses: st)
    }

    /// Swift derives `supports`/`weight` from the evidenceId via WeightRubric, so these fixtures
    /// exercise the REAL weight table — a test cannot invent a support set the engine disagrees with.
    private func ev(_ id: EvidenceId, _ obs: String) -> EvidenceItem {
        EvidenceItem(section: .prophet, evidenceId: id, observation: obs, rationale: "r")
    }

    // MARK: - A1

    func testInconclusiveNeverReadsAsSafe() {
        let a = ResultActions.For(make(.clean, conclusivity: .inconclusive, confidence: 0))
        XCTAssertEqual(a.severity, .unknown)
        XCTAssertTrue(a.qualified)
        XCTAssertFalse(a.meaning.lowercased().contains("no tampering"))
        XCTAssertFalse(a.meaning.lowercased().contains("no action needed"))
        XCTAssertTrue(a.primaryAction.lowercased().contains("real result"))
    }

    func testCleanButHardGatedIsPresentedAsUnfinishedNotSafe() {
        let gated = [SectionStatusEntry(section: .prophet, status: .negative),
                     SectionStatusEntry(section: .mirror, status: .untested)]
        let a = ResultActions.For(make(.clean, statuses: gated))

        XCTAssertEqual(a.severity, .unknown)          // NOT .clean
        XCTAssertTrue(a.meaning.contains("NOT a clean result"))
        // Mutation guard: the gate must actually change the output vs a complete clean run.
        XCTAssertNotEqual(ResultActions.For(make(.clean)).meaning, a.meaning)
    }

    func testThreatUnderReferralIsQualifiedButActionNotSoftened() {
        let confident = ResultActions.For(make(.spoofing))
        let referred = ResultActions.For(make(.spoofing, confidence: 40, referral: true))

        XCTAssertFalse(confident.qualified)
        XCTAssertTrue(referred.qualified)
        XCTAssertTrue(referred.meaning.contains(ResultActions.confidenceCaveat))
        // The recommended action stays identical — a limited-confidence threat is still acted on.
        XCTAssertEqual(confident.primaryAction, referred.primaryAction)
    }

    func testEveryThreatExplainsAndDirectsNeverReassures() {
        for h in [Hypothesis.spoofing, .censorship, .interception_transport, .interception_onPath,
                  .contentTampering, .privacyLeak, .localCompromise] {
            let a = ResultActions.For(make(h))
            XCTAssertGreaterThan(a.meaning.count, 40, "\(h) meaning too thin to explain anything")
            XCTAssertGreaterThan(a.primaryAction.count, 20, "\(h) action too thin to act on")
            XCTAssertFalse(a.meaning.lowercased().contains("no tampering"), "\(h) must not reassure")
            XCTAssertNotEqual(a.severity, .clean)
        }
    }

    // MARK: - A2

    func testExplanationIsBuiltFromEvidenceSupportingTheResult() {
        let c = make(.spoofing, evidence: [
            ev(.authoritativeDiverges, "resolver returned an address no trusted vantage point agrees with"),
            ev(.dnsLeakConfirmed, "a privacy-leak detail"),
        ])
        let e = PlainExplanations.For(c)
        XCTAssertTrue(e.because.contains { $0.contains("no trusted vantage point") })
        XCTAssertFalse(e.because.contains { $0.contains("privacy-leak detail") })
    }

    func testExplanationSurfacesGapsAndNeverSuppressesDespite() {
        let c = make(.spoofing,
                     evidence: [ev(.authoritativeDiverges, "answers diverge from authoritative"),
                                ev(.encryptedDnsBlocked, "transport looks downgraded too")],
                     statuses: [SectionStatusEntry(section: .prophet, status: .positive),
                                SectionStatusEntry(section: .witness, status: .untested)])
        let e = PlainExplanations.For(c)
        XCTAssertTrue(e.despite.contains { $0.contains("transport looks downgraded") })
        XCTAssertTrue(e.despite.contains { $0.contains("Witness") && $0.contains("did not run") })
        XCTAssertTrue(e.narrative.contains("That said,"))
    }

    func testInconclusiveExplainedAsUnfinishedNotClean() {
        let e = PlainExplanations.For(make(.clean, conclusivity: .inconclusive, confidence: 0))
        XCTAssertTrue(e.summary.lowercased().contains("unfinished"))
    }

    // MARK: - A3

    func testInconclusiveIsUngradedNeverALetter() {
        let card = ReportCards.For(make(.clean, conclusivity: .inconclusive, confidence: 0))
        XCTAssertEqual(card.grade, .ungraded)
        XCTAssertEqual(card.gradeLabel, "N/A")
        XCTAssertEqual(card.severity, .unknown)
    }

    func testCleanButHardGatedIsUngradedNotAnA() {
        let gated = [SectionStatusEntry(section: .prophet, status: .negative),
                     SectionStatusEntry(section: .mirror, status: .untested)]
        let card = ReportCards.For(make(.clean, statuses: gated))
        XCTAssertEqual(card.grade, .ungraded)                       // NOT .a
        XCTAssertTrue(card.topReasons.contains { $0.contains("Mirror") && $0.contains("did not run") })
        // Mutation guard: a complete clean run DOES grade A, so the gate is what changed it.
        XCTAssertEqual(ReportCards.For(make(.clean)).grade, .a)
    }

    func testThreatsGradeBySeverity() {
        XCTAssertEqual(ReportCards.For(make(.spoofing, evidence: [ev(.authoritativeDiverges, "o")])).grade, .f)
        XCTAssertEqual(ReportCards.For(make(.localCompromise, evidence: [ev(.exfilLikely, "o")])).grade, .f)
        XCTAssertEqual(ReportCards.For(make(.censorship, evidence: [ev(.nxCanaryHijack, "o")])).grade, .d)
        XCTAssertEqual(ReportCards.For(make(.privacyLeak, evidence: [ev(.dnsLeakConfirmed, "o")])).grade, .c)
    }

    func testPositiveSectionIsNeverDescribedAsFoundNothingWrong() {
        // `positive` = a THREAT was found. Listing it as "found nothing wrong" inverts its meaning.
        let c = make(.clean, statuses: [SectionStatusEntry(section: .prophet, status: .negative),
                                        SectionStatusEntry(section: .mirror, status: .positive)])
        let card = ReportCards.For(c)
        XCTAssertFalse(card.topReasons.contains { $0.contains("Mirror") && $0.contains("found nothing wrong") })
    }

    // MARK: - A6

    func testNeverScannedIsUnknownNeverGreen() {
        let t = TrayIndicators.For(nil)
        XCTAssertEqual(t.severity, .unknown)
        XCTAssertNotEqual(t.severity, .clean)
        XCTAssertFalse(t.attention)
        XCTAssertFalse(TrayIndicators.glyphFor(.unknown).contains("check"))
    }

    func testCleanButHardGatedTrayIsNeverGreen() {
        // The tray's OWN honesty gate. This is the one case where reading `result.severity`
        // directly instead of the card diverges: the result says .clean, the card says .unknown.
        let gated = [SectionStatusEntry(section: .prophet, status: .negative),
                     SectionStatusEntry(section: .mirror, status: .untested)]
        let t = TrayIndicators.For(make(.clean, statuses: gated))

        XCTAssertEqual(t.severity, .unknown)
        XCTAssertNotEqual(t.severity, .clean)
        // Mutation guard: a complete clean run DOES go green, so the gate is what changed it.
        XCTAssertEqual(TrayIndicators.For(make(.clean)).severity, .clean)
    }

    func testTrayNeverDisagreesWithTheReportCard() {
        // The anti-divergence invariant: the tray is DERIVED from the card, so a future edit giving
        // the tray its own severity mapping turns this red.
        //
        // The hard-gated CLEAN fixture is load-bearing and must not be dropped: for every complete
        // fixture the card's severity and `result.severity` happen to AGREE, so a loop over
        // Hypothesis.allCases alone cannot discriminate and passes even when the tray is rewired to
        // read the result directly. Only when the Ungraded gate fires do the two mappings differ
        // (card .unknown vs result .clean) — that is the case with teeth.
        var fixtures: [(String, Conclusion)] = Hypothesis.allCases.map { ("\($0)", make($0)) }
        fixtures.append(("clean+hard-gated", make(.clean, statuses: [
            SectionStatusEntry(section: .prophet, status: .negative),
            SectionStatusEntry(section: .mirror, status: .untested),
        ])))

        for (name, c) in fixtures {
            XCTAssertEqual(ReportCards.For(c).severity, TrayIndicators.For(c).severity,
                           "tray diverged from the report card for \(name)")
        }
    }

    func testUnknownDoesNotCryWolfButThreatsDo() {
        XCTAssertFalse(TrayIndicators.For(nil).attention)
        XCTAssertFalse(TrayIndicators.For(make(.clean, conclusivity: .inconclusive)).attention)
        XCTAssertFalse(TrayIndicators.For(make(.clean)).attention)   // good news must not bounce
        XCTAssertTrue(TrayIndicators.For(make(.spoofing)).attention)
    }

    // MARK: - Cross-platform parity

    func testCopyIsVerbatimIdenticalToTheWindowsTwin() {
        // The parity rule makes wording part of the contract. These literals are copied from
        // Windows Syxx.Kit/Synthesis/ResultAction.cs + ReportCard.cs; if either side is reworded
        // without the other, this fails — which is the point.
        XCTAssertEqual(ResultActions.confidenceCaveat,
            "Confidence is limited; treat this as a strong signal to act on, not a certainty.")
        XCTAssertEqual(ResultActions.For(make(.clean, conclusivity: .inconclusive)).primaryAction,
            "Run the checks that were skipped or failed, then synthesize again for a real result.")
        XCTAssertEqual(ReportCards.For(make(.clean, conclusivity: .inconclusive)).headline,
            "Not enough evidence to grade this network yet: finish the checks for a real grade.")
        XCTAssertEqual(TrayIndicators.neverScannedTooltip,
            "Syxx has not checked this network yet: run a scan.")
    }
}
