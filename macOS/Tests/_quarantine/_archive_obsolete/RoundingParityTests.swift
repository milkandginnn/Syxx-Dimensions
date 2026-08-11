import XCTest
@testable import SyxxKit

/// P0.1 — the cross-platform integer-percentage gate primitive (design §3 M4 D2; NE7 Δ5/Δ6). The C#
/// twin (`RoundingParityTests.cs`) asserts the IDENTICAL expected integers, so both suites green
/// proves the two platforms round a `.5` boundary the same way (away-from-zero, NOT C#'s default
/// banker's/ToEven, which straddled the 75% gate).
final class RoundingParityTests: XCTestCase {

    /// The design illustrates the straddle at 74.5, but 0.745 is not dyadic-exact (0.745·100 ≠ 74.5
    /// exactly), so we use the exactly-representable analogs: 0.125·100 == 12.5 and 0.625·100 == 62.5
    /// are EXACT `.5` boundaries where away-from-zero (odd result) and banker's-to-even DIVERGE.
    func testRoundPercent_awayFromZero_atExactHalfBoundary() {
        XCTAssertEqual(SynthesisEngine.roundPercent(0.125), 13, "12.5 must round to 13 (away-from-zero); banker's would give 12")
        XCTAssertEqual(SynthesisEngine.roundPercent(0.625), 63, "62.5 must round to 63 (away-from-zero); banker's would give 62")
    }

    func testRoundPercent_ordinaryValues() {
        XCTAssertEqual(SynthesisEngine.roundPercent(0.0), 0)
        XCTAssertEqual(SynthesisEngine.roundPercent(1.0), 100)
        XCTAssertEqual(SynthesisEngine.roundPercent(0.744), 74)
        XCTAssertEqual(SynthesisEngine.roundPercent(0.746), 75)
        // Just-below vs at the 75% referral gate.
        XCTAssertLessThan(SynthesisEngine.roundPercent(0.7449), 75)
    }

    /// The fold is bit-stable: synthesizing the same multi-support input repeatedly yields the same
    /// confidence and belief vector (the canonicalized ascending-key Dempster accumulation, P0.1).
    func testSynthesisIsDeterministicAcrossRepeatedRuns() {
        let ev = [
            EvidenceItem(section: .mirror, evidenceId: .redirection, observation: "o", rationale: "r"),
            EvidenceItem(section: .prophet, evidenceId: .dnssecCanaryStripped, observation: "o", rationale: "r", source: "canaries"),
        ]
        let statuses = Section.allCases.map { SectionStatusEntry(section: $0, status: $0 == .glutton ? .untested : .negative) }
        let a = SynthesisEngine.synthesize(evidence: ev, sectionStatuses: statuses)
        for _ in 0..<20 {
            let b = SynthesisEngine.synthesize(evidence: ev, sectionStatuses: statuses)
            XCTAssertEqual(a.confidence, b.confidence)
            XCTAssertEqual(a.diagnosis, b.diagnosis)
            XCTAssertEqual(a.belief.map { $0.probability }, b.belief.map { $0.probability })
            XCTAssertEqual(a.uncertainty, b.uncertainty)
            XCTAssertEqual(a.conflict, b.conflict)
        }
    }
}
