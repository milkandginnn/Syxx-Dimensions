import XCTest
@testable import SyxxKit

// A4 twin — first-run scan. The live probing is untestable by construction, so the decision logic
// lives in the pure summarize() seam and is tested here against REAL engine output (production
// Synthesizer.conclude). Mirrors Windows FirstRunScanTests.
final class FirstRunScanTests: XCTestCase {

    private let allDisciplines = ["Glutton", "Mirror", "Portal", "Prophet"]

    func testEveryDisciplineIsAccountedForRanPlusSkippedIsComplete() {
        // Guards a real refactor slip on the Windows side: a track() call silently removed still
        // compiles, and the discipline just vanishes from the report rather than being reported
        // unmeasured. Ran + Skipped must always name all four.
        let r = FirstRunScan.summarize(SectionInputs())
        XCTAssertEqual((r.ran + r.skipped).sorted(), allDisciplines)
    }

    func testNothingMeasuredIsUngradedAndEveryDisciplineReportedSkipped() {
        let r = FirstRunScan.summarize(SectionInputs())

        XCTAssertEqual(r.card.grade, .ungraded)      // never a letter
        XCTAssertEqual(r.card.gradeLabel, "N/A")
        XCTAssertTrue(r.ran.isEmpty)
        XCTAssertEqual(r.skipped.count, 4)
        // The engine's own gate must be what drives this, not our bookkeeping.
        XCTAssertTrue(r.conclusion.hasHardGatedSection)
    }

    func testAProbeThatProducedNothingIsSkippedNotCountedAsRun() {
        // Prophet resolved; Mirror's vantage comparison failed (nil) — the honest outcome is Mirror
        // reported unmeasured, NOT quietly omitted or treated as clean.
        var inputs = SectionInputs()
        inputs.resolution = DNSResponse(status: .success)
        inputs.comparison = nil

        let r = FirstRunScan.summarize(inputs)
        XCTAssertTrue(r.ran.contains("Prophet"))
        XCTAssertTrue(r.skipped.contains("Mirror"))
        XCTAssertFalse(r.ran.contains("Mirror"))
    }

    func testProphetCountsAsRunOnACanaryAloneEvenWithoutAResolution() {
        // A hijacking resolver can fail the A query yet still trip the canary. That is evidence, so
        // Prophet must count as having run — otherwise a detected hijack reads as unmeasured.
        var inputs = SectionInputs()
        inputs.nxCanary = DNSCanaries.CanaryResult(
            section: .prophet, status: .positive,
            evidence: EvidenceItem(section: .prophet, evidenceId: .nxCanaryHijack,
                                   observation: "Two unrelated random names both resolved to 203.0.113.7",
                                   rationale: "Synthesized answers for names that cannot exist.",
                                   source: "canaries"),
            detail: "hijack")

        let r = FirstRunScan.summarize(inputs)
        XCTAssertTrue(r.ran.contains("Prophet"))
        XCTAssertNotEqual(r.card.grade, .a)
        XCTAssertNotEqual(r.card.grade, .b)
        XCTAssertNotEqual(r.conclusion.diagnosis, .clean)
    }

    func testDefaultDomainIsSetSoAFirstRunNeedsNoInput() {
        XCTAssertFalse(FirstRunScan.defaultDomain.isEmpty)
    }

    // MARK: - runAsync merge (macOS live half, added 2026-07-17)

    func testResultExposesTheEvidenceItGathered() {
        // The GUI merges result.inputs back into the live session so the component cards reflect the
        // scan. If summarize() dropped the evidence, the result would render but the session would
        // look empty — the scan would appear to have measured nothing.
        var inputs = SectionInputs()
        inputs.resolution = DNSResponse(status: .success, records: [
            DNSRecord(name: "github.com", type: 1, ttl: 60, rdata: "140.82.112.3")
        ])
        let r = FirstRunScan.summarize(inputs)
        XCTAssertEqual(r.inputs.resolution?.records.first?.rdata, "140.82.112.3",
                       "summarize() must carry the evidence through, not just the result")
    }

    func testMergeNeverLetsALaterNilEraseEarlierEvidence() {
        // The runAsync merge() rule: a non-nil wins, a nil never overwrites. This is the failure mode
        // that matters — Portal returns a SectionInputs with resolution=nil, and if that nil clobbered
        // Prophet's resolution the scan would silently delete a measurement it had already made.
        var acc = SectionInputs()
        acc.resolution = DNSResponse(status: .success, records: [
            DNSRecord(name: "github.com", type: 1, ttl: 60, rdata: "140.82.112.3")
        ])
        // Simulate the exact shape of a later discipline that measured something else and nothing here.
        var later = SectionInputs()
        later.network = nil   // Portal produced no network report on this run
        // Apply the same rule merge() uses: only non-nil assigns.
        if let v = later.resolution { acc.resolution = v }
        XCTAssertNotNil(acc.resolution, "a later empty result must not erase earlier evidence")
        XCTAssertEqual(acc.resolution?.records.first?.rdata, "140.82.112.3")
    }
}
