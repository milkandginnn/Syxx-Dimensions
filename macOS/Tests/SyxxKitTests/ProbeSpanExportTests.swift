import XCTest
import OpenTelemetryApi
import OpenTelemetrySdk
@testable import SyxxKit

/// Reads the spans SyxxKit actually EXPORTS.
///
/// `ProbeRunnerOutcomeHonestyTests` asserts on the runner's return value, which is why the S1
/// mutation (scoring any non-nil result as `success, evidence: true`) stayed green there: a
/// fabricated span attribute is invisible to a caller checking the returned object. This file
/// closes that hole by registering a real `TracerProviderSdk` with an in-process exporter.
///
/// `SyxxTracer.resetTracerForTesting()` is mandatory in setUp: the tracer memo otherwise pins the
/// process to whichever provider the first span in the whole suite happened to see, which makes
/// every assertion here silently vacuous depending on test order.
final class ProbeSpanExportTests: XCTestCase {

    private final class Collector: SpanExporter, @unchecked Sendable {
        private let lock = NSLock()
        private var spans: [SpanData] = []
        func export(spans s: [SpanData], explicitTimeout: TimeInterval?) -> SpanExporterResultCode {
            lock.lock(); spans.append(contentsOf: s); lock.unlock(); return .success
        }
        func flush(explicitTimeout: TimeInterval?) -> SpanExporterResultCode { .success }
        func shutdown(explicitTimeout: TimeInterval?) {}
        func finished(named: String) -> SpanData? {
            lock.lock(); defer { lock.unlock() }
            return spans.first { $0.name == named }
        }
        var allNames: [String] {
            lock.lock(); defer { lock.unlock() }
            return spans.map { $0.name }
        }
        func clear() { lock.lock(); spans.removeAll(); lock.unlock() }
    }

    /// ONE provider, registered once. `OpenTelemetry.registerTracerProvider` is a one-shot
    /// singleton: a second call is ignored, so re-registering per test silently left every test
    /// after the first reading an unwired collector and asserting against zero spans. That is the
    /// vacuous-pass shape this suite exists to catch, so it must not live in the suite itself.
    private static let collector = Collector()
    private static let registerOnce: Void = {
        OpenTelemetry.registerTracerProvider(tracerProvider:
            TracerProviderBuilder()
                .add(spanProcessor: SimpleSpanProcessor(spanExporter: ProbeSpanExportTests.collector))
                .build())
    }()

    private var collector: Collector { Self.collector }

    override func setUp() {
        super.setUp()
        _ = Self.registerOnce
        SyxxTracer.resetTracerForTesting()   // the memo would otherwise pin a stale provider
        Self.collector.clear()               // isolate: each test asserts only on its own spans
    }

    /// Wait briefly for the span to reach the exporter. Export is dispatched off the calling
    /// thread, so the two fastest tests here (a skipped probe and a bare span, both ~1ms with no
    /// suspension point) asserted before it landed and read as "no span emitted" — a false red
    /// that would have been "fixed" by deleting the assertion. Poll instead of sleeping a fixed
    /// interval so the tests stay fast and are not timing-fragile on a loaded machine.
    private func awaitSpan(_ name: String, timeout: TimeInterval = 2.0) async -> SpanData? {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if let s = collector.finished(named: name) { return s }
            try? await Task.sleep(for: .milliseconds(5))
        }
        return collector.finished(named: name)
    }

    private func attr(_ span: SpanData?, _ key: String) -> String? {
        guard let v = span?.attributes[key] else { return nil }
        return v.description
    }

    private func budget(perProbe: TimeInterval = 2.0, maxProbes: Int = 8) -> ProbeBudget {
        ProbeBudget(totalTimeout: 10.0, perProbeTimeout: perProbe, maxProbes: maxProbes)
    }

    /// A value type carrying its own tri-state — the S1 shape. `DNSResolver.queryUDPDetailed`
    /// returns a NON-OPTIONAL `DNSResponse`, so a timeout arrives here as a non-nil object.
    private struct TriStateResult: Sendable { let timedOut: Bool }

    // MARK: - S1: the regression the return-value tests cannot see

    /// THE S1 GUARD. A non-nil result the runner cannot interpret must be exported `unclassified`
    /// with NO evidence claim and NOT status Ok.
    /// Failure case: restore `setOutcome(span, .success, evidence: true)` on the non-nil branch →
    /// outcome becomes `success`, evidence becomes `true`, status becomes Ok → red on all three.
    func testOpaqueNonNilResult_isExportedUnclassifiedNotSuccess() async {
        var b = budget()
        _ = await ProbeRunner.run(label: "s1", budget: &b) { TriStateResult(timedOut: true) }

        let span = await awaitSpan("probe.s1")
        XCTAssertNotNil(span, "the probe must emit a span")
        XCTAssertEqual(attr(span, "syxx.outcome"), "unclassified")
        XCTAssertNotEqual(attr(span, "syxx.outcome"), "success",
                          "a result carrying its own timeout must never export as a success")
        XCTAssertNotEqual(attr(span, "syxx.evidence"), "true",
                          "the runner must not fabricate an evidence claim about a value it cannot read")
        XCTAssertNotEqual(span?.status, .ok)
    }

    // MARK: - Untested vs Inconclusive, on the wire

    /// Failure case: delete the untested emit in the budget gate → no span → red.
    func testSkippedProbe_exportsUntestedSpan() async {
        var b = budget(maxProbes: 0)
        _ = await ProbeRunner.run(label: "gated", budget: &b) { "x" }

        let span = await awaitSpan("probe.gated")
        XCTAssertNotNil(span, "a SKIPPED probe must still be visible on the wire")
        XCTAssertEqual(attr(span, "syxx.outcome"), "untested")
        XCTAssertEqual(attr(span, "syxx.attempted"), "false")
        XCTAssertEqual(span?.status, .unset, "untested is not an error — nothing failed")
    }

    /// Failure case: map untested onto the inconclusive arm → statuses match → red.
    func testUntestedAndInconclusive_exportDifferently() async {
        var g = budget(maxProbes: 0)
        _ = await ProbeRunner.run(label: "u", budget: &g) { "x" }

        var d = budget(perProbe: 0.05)
        _ = await ProbeRunner.run(label: "i", budget: &d) {
            try? await Task.sleep(for: .seconds(2)); return "never"
        }

        let untested = await awaitSpan("probe.u")
        let inconclusive = await awaitSpan("probe.i")
        XCTAssertEqual(attr(untested, "syxx.outcome"), "untested")
        XCTAssertEqual(attr(inconclusive, "syxx.outcome"), "inconclusive")
        XCTAssertEqual(attr(untested, "syxx.attempted"), "false")
        XCTAssertEqual(attr(inconclusive, "syxx.attempted"), "true")
        XCTAssertNotEqual(untested?.status, inconclusive?.status)
    }

    /// Could-not-measure must be as loud as a crash.
    /// Failure case: map inconclusive to Unset or Ok → red.
    func testDeadlineExport_isErrorNotOk() async {
        var b = budget(perProbe: 0.05)
        _ = await ProbeRunner.run(label: "slow", budget: &b) {
            try? await Task.sleep(for: .seconds(2)); return "never"
        }

        let span = await awaitSpan("probe.slow")
        XCTAssertEqual(span?.status.name, "error")
        XCTAssertNotEqual(span?.status, .ok)
        XCTAssertEqual(attr(span, "syxx.outcome.reason"), "probe_deadline")
        XCTAssertEqual(attr(span, "syxx.evidence"), "false")
    }

    // MARK: - Consent withdrawal must actually stop the flow

    /// Withdrawing consent must stop spans being RECORDED, not merely re-point a provider.
    ///
    /// The previous implementation only called `registerTracerProvider(DefaultTracerProvider)`.
    /// That is a no-op twice over: registration is a ONE-SHOT singleton (verified — it is why this
    /// suite registers once and clears between tests), and the SyxxKit tracer memo would keep the
    /// old provider's live BatchSpanProcessor exporting regardless. So telemetry continued after
    /// opt-out. Failure case: revert `discardOnConsentWithdrawal` to provider-swapping only, or
    /// delete the `isDisabled` gate in `SyxxTracer.tracer` → a span is recorded → red.
    func testDisabledTracer_recordsNothing() async {
        SyxxTracer.disable()
        defer { SyxxTracer.enable(); SyxxTracer.resetTracerForTesting() }

        var b = budget()
        _ = await ProbeRunner.run(label: "after-optout", budget: &b) { "value" }

        // Poll deliberately: a false pass here would mean "we asserted before the span landed",
        // so wait the full window and require it to STILL be absent.
        let span = await awaitSpan("probe.after-optout", timeout: 0.4)
        XCTAssertNil(span, "no span may be recorded once consent is withdrawn")
        XCTAssertTrue(SyxxTracer.isDisabled)
    }

    /// Re-granting consent resumes recording, and does NOT backfill the disabled period.
    func testReEnabling_resumesWithoutBackfilling() async {
        SyxxTracer.disable()
        var b1 = budget()
        _ = await ProbeRunner.run(label: "silent", budget: &b1) { "value" }

        SyxxTracer.enable()
        SyxxTracer.resetTracerForTesting()
        var b2 = budget()
        _ = await ProbeRunner.run(label: "audible", budget: &b2) { "value" }

        let resumed = await awaitSpan("probe.audible")
        XCTAssertNotNil(resumed, "recording must resume")
        XCTAssertNil(collector.finished(named: "probe.silent"),
                     "data gathered without consent stays unsent even after consent returns")
    }

    // MARK: - S3: an uninstrumented span must say so

    /// A span whose body never classifies itself is stamped `unrecorded`, so "nobody measured this"
    /// cannot masquerade as a deliberate Unset (which is what `failure` and `untested` also are).
    /// Failure case: drop the stamp in `finalize` → outcome is nil → red.
    func testSpanWithNoOutcome_isStampedUnrecorded() async {
        await SyxxTracer.span("probe.bare") { _ in }

        let span = await awaitSpan("probe.bare")
        XCTAssertEqual(attr(span, "syxx.outcome"), "unrecorded",
                       "an uninstrumented span must self-report, not read as fine")
    }
}
