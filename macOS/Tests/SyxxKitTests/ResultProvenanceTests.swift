import XCTest
@testable import SyxxKit

/// A fleet-wide rise in inconclusive results was uninvestigable: you could not tell whether one
/// resolver started dropping queries, an OS release broke a platform API, or the matrix spec
/// regressed. Errors were recorded (eventually) and results were recorded, but nothing linked
/// them.
///
/// These tests pin the link, and the standing invariant that comes with it: a section that is
/// untested/invalidated/inconclusive with NO diagnostic recorded against it means some code path
/// degraded a measurement silently. That is exactly the defect class this project exists to
/// prevent, and unlike a code audit it is detectable mechanically on every run.
final class ResultProvenanceTests: XCTestCase {

    private final class RecordingSink: DiagnosticSink, @unchecked Sendable {
        var events: [DiagnosticEvent] = []
        var crumbs: [(String, DiagnosticSeverity, [String: String])] = []
        func capture(_ event: DiagnosticEvent) { events.append(event) }
        func breadcrumb(category: String, message: String,
                        level: DiagnosticSeverity, data: [String: String]) {
            crumbs.append((category, level, data))
        }
        func discardBuffered() {}
    }

    private var sink = RecordingSink()

    override func setUp() {
        super.setUp()
        sink = RecordingSink()
        SyxxDiagnostics.install(sink)
    }

    override func tearDown() {
        SyxxDiagnostics.uninstall()
        super.tearDown()
    }

    private func conclusion(_ statuses: [SectionStatusEntry],
                            determination: DeterminativeResult?) -> Conclusion {
        Conclusion(generatedAt: Date(timeIntervalSince1970: 0),
                   sectionStatuses: statuses,
                   determination: determination)
    }

    /// The five-state distinction must survive onto the wire. A nil determination is
    /// "the engine could not run" — categorically NOT `.inconclusive`.
    /// Failure case: coerce nil to "inconclusive" → red.
    func testNilDeterminationReportsNoDeterminationNotInconclusive() {
        Synthesizer.reportResultProvenance(
            conclusion([], determination: nil))

        let data = sink.crumbs.first?.2 ?? [:]
        XCTAssertEqual(data["syxx.result.state"], "no_determination")
        XCTAssertNotEqual(data["syxx.result.state"], "inconclusive",
                          "engine-could-not-run must not be reported as an ambiguous finding")
    }

    /// Untested and inconclusive must stay distinct on the wire too.
    func testUntestedAndInconclusiveAreDistinctOnTheWire() {
        for state in [DeterminativeState.untested, .inconclusive, .clean, .threatDetected] {
            sink.crumbs.removeAll()
            let det = DeterminativeResult(state: state, diagnoses: [], suppressed: [],
                                          explanation: "")
            Synthesizer.reportResultProvenance(conclusion([], determination: det))
            XCTAssertEqual(sink.crumbs.first?.2["syxx.result.state"], state.rawValue)
        }
    }

    /// THE INVARIANT: a blocking section with no diagnostic recorded against it is a silent
    /// degradation, and is reported as its own issue.
    /// Failure case: delete the unattributed loop → red.
    func testUnattributedDegradationIsItsOwnIssue() {
        SyxxDiagnostics.$run.withValue(RunContext()) {
            let statuses = [SectionStatusEntry(section: .cage, status: .untested, completeness: 0)]
            Synthesizer.reportResultProvenance(
                conclusion(statuses,
                           determination: DeterminativeResult(state: .inconclusive, diagnoses: [],
                                                              suppressed: [], explanation: "")))
        }

        let evt = sink.events.first { $0.errorClass == "result.unattributed_degradation" }
        XCTAssertNotNil(evt, "a degraded section nobody explained must be reported")
        XCTAssertEqual(evt?.tags["syxx.section"], "cage")
        XCTAssertEqual(sink.crumbs.first?.2["syxx.attribution_complete"], "false")
    }

    /// The converse: when a diagnostic WAS recorded for that section, the degradation is
    /// attributed and must NOT be flagged. Otherwise the invariant would cry wolf on every run.
    func testAttributedDegradationIsNotFlagged() {
        SyxxDiagnostics.$run.withValue(RunContext()) {
            SyxxDiagnostics.capture(DiagnosticEvent(
                errorClass: "capture.permission_denied",
                component: "DNSCapture",
                section: .cage,
                impact: .untestedSection,
                severity: .warning,
                fingerprint: ["capture.permission_denied"]))

            let statuses = [SectionStatusEntry(section: .cage, status: .untested, completeness: 0)]
            Synthesizer.reportResultProvenance(
                conclusion(statuses,
                           determination: DeterminativeResult(state: .inconclusive, diagnoses: [],
                                                              suppressed: [], explanation: "")))
        }

        XCTAssertNil(sink.events.first { $0.errorClass == "result.unattributed_degradation" },
                     "an explained degradation must not be flagged as silent")
        XCTAssertEqual(sink.crumbs.first?.2["syxx.attribution_complete"], "true")
    }

    /// Abstentions are the interesting case and are recorded at error level; a decided run is
    /// routine. Failure case: report everything at the same level → the signal is lost.
    func testAbstentionIsLouderThanADecidedResult() {
        Synthesizer.reportResultProvenance(
            conclusion([], determination: DeterminativeResult(state: .clean, diagnoses: [],
                                                              suppressed: [], explanation: "")))
        XCTAssertEqual(sink.crumbs.first?.1, .warning)

        sink.crumbs.removeAll()
        Synthesizer.reportResultProvenance(conclusion([], determination: nil))
        XCTAssertEqual(sink.crumbs.first?.1, .error)
    }
}
