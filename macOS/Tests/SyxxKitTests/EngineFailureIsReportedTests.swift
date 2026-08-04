import XCTest
@testable import SyxxKit

/// The determinative engine's catch returns nil so no result is fabricated — correct, and honest
/// to the USER. But it was also silent to US: a user got "the engine could not evaluate this run"
/// and nobody ever learned why. A missing determination is how a result goes missing.
///
/// These tests pin that the failure is REPORTED, using a recording sink rather than a live Sentry.
/// That is the point of the vendor-neutral `DiagnosticSink`: reporting becomes a testable
/// behaviour instead of something you can only verify by watching a dashboard.
///
/// Twin: Windows/dotNET/Syxx.Tests/EngineFailureIsReportedTests.cs — same scenarios, same asserts.
final class EngineFailureIsReportedTests: XCTestCase {

    private final class RecordingSink: DiagnosticSink, @unchecked Sendable {
        var events: [DiagnosticEvent] = []
        var discards = 0
        func capture(_ event: DiagnosticEvent) { events.append(event) }
        func breadcrumb(category: String, message: String,
                        level: DiagnosticSeverity, data: [String: String]) {}
        func discardBuffered() { discards += 1 }
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

    /// A duplicate section entry makes `EvidenceBundle.validate()` throw, which is the real path to
    /// a nil determination. Failure case: delete the capture in the engine's catch → red.
    func testEngineFailure_isCapturedWithNoDeterminationImpact() {
        let dup = [
            SectionStatusEntry(section: .prophet, status: .positive, completeness: 1.0),
            SectionStatusEntry(section: .prophet, status: .positive, completeness: 1.0)
        ]

        let result = SynthesisEngine.runDeterminativeEngine(evidence: [], sectionStatuses: dup)

        XCTAssertNil(result, "still no fabricated result")
        XCTAssertEqual(sink.events.count, 1)
        XCTAssertEqual(sink.events.first?.errorClass, "engine.no_determination")
        XCTAssertEqual(sink.events.first?.impact, .noDetermination)
        XCTAssertEqual(sink.events.first?.severity, .error)
    }

    /// Grouping must key on the error VARIANT. `MatrixEngineError.description` interpolates section
    /// and indicator ids, so fingerprinting on it would shard one bug across hundreds of issues.
    func testFingerprint_usesTheErrorVariantNotTheInterpolatedMessage() {
        let dup = [
            SectionStatusEntry(section: .mirror, status: .positive, completeness: 1.0),
            SectionStatusEntry(section: .mirror, status: .positive, completeness: 1.0)
        ]

        SynthesisEngine.runDeterminativeEngine(evidence: [], sectionStatuses: dup)

        let fingerprint = sink.events.first?.fingerprint ?? []
        XCTAssertEqual(fingerprint.count, 2)
        XCTAssertEqual(fingerprint.first, "engine.no_determination")
        XCTAssertEqual(fingerprint.last, "duplicateSectionInBundle")
        XCTAssertFalse(fingerprint.contains { $0.lowercased().contains("mirror") },
                       "the section id is in the message but must not be in the grouping key")
    }

    /// A successful evaluation must report nothing — capturing unconditionally would drown the
    /// issue stream in non-events.
    func testSuccessfulEvaluation_reportsNothing() {
        let ok = [SectionStatusEntry(section: .prophet, status: .untested, completeness: 0.0)]

        SynthesisEngine.runDeterminativeEngine(evidence: [], sectionStatuses: ok)

        XCTAssertTrue(sink.events.isEmpty)
    }

    /// Consent withdrawal must detach the sink AND discard, not flush: flushing on opt-out
    /// transmits exactly the data the user just refused.
    func testUninstall_detachesAndDiscards_soNothingIsSentAfterOptOut() {
        SyxxDiagnostics.uninstall()
        XCTAssertFalse(SyxxDiagnostics.isActive)
        XCTAssertEqual(sink.discards, 1)

        let dup = [
            SectionStatusEntry(section: .prophet, status: .positive, completeness: 1.0),
            SectionStatusEntry(section: .prophet, status: .positive, completeness: 1.0)
        ]
        SynthesisEngine.runDeterminativeEngine(evidence: [], sectionStatuses: dup)

        XCTAssertTrue(sink.events.isEmpty, "opted out: nothing captured at all")
    }
}
