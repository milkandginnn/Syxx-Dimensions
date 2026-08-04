import XCTest
@testable import SyxxKit

/// macOS twin of `Windows/dotNET/Syxx.Tests/TelemetryOutcomeHonestyTests.cs`.
///
/// Its absence is what let two defects ship in the "fix" for the original telemetry-honesty bug:
///
///   S1 — the runner scored ANY non-nil result as `success` with `evidence: true`. That is no
///        stronger than the "did not throw" test it replaced, because several probes return a
///        value carrying their own tri-state: `DNSResolver.queryUDPDetailed` returns a
///        NON-OPTIONAL `DNSResponse`, so a timeout arrives as a non-nil object and was exported
///        as a successful measurement — with a fabricated evidence claim added on top.
///
///   S2 — `output == nil` reads only the OUTER optional. Where `Output` is itself Optional
///        (`ProbePlan`'s `vantage` and `warp` closures return `ComparisonResult?`), an operation
///        that deliberately returned nil was also scored a success, while its section recorded
///        nothing. Windows got this right, so it was a parity break too.
///
/// These assert on the runner's OBSERVABLE classification rather than on span plumbing, so they
/// hold regardless of whether a tracer provider is registered (SyxxTracer memoizes a no-op tracer
/// when none is, which would otherwise make span-reading tests order-dependent).
final class ProbeRunnerOutcomeHonestyTests: XCTestCase {

    /// A value type that carries its own tri-state — the S1 shape.
    private struct TriStateResult: Sendable {
        enum Status: String, Sendable { case ok, timedOut }
        let status: Status
    }

    private func budget(perProbe: TimeInterval = 2.0,
                        total: TimeInterval = 10.0,
                        maxProbes: Int = 8) -> ProbeBudget {
        ProbeBudget(totalTimeout: total, perProbeTimeout: perProbe, maxProbes: maxProbes)
    }

    // MARK: - S2: the double-optional collapse

    /// An operation that returns nil ran to completion and yielded nothing — it is neither a
    /// deadline nor an error, and the runner must not treat the outer nil as "no result from a
    /// timeout". Failure case: revert `ProbeRace` to a bare `Output?` element → the nil produced
    /// by the operation becomes indistinguishable from the deadline sentinel → red.
    func testOperationReturningNil_isDistinguishedFromADeadline() async {
        var b = budget()
        let out: String?? = await ProbeRunner.run(label: "returns-nil", budget: &b) {
            nil as String?
        }

        // The operation ran, so the budget must record a probe — a deadline would too, but a
        // SKIPPED probe would not, which is the third case this separates.
        XCTAssertEqual(b.probesRun, 1)
        // Outer optional is present (the race produced a value); inner is nil (that value was nil).
        XCTAssertNotNil(out, "the race must report that the operation PRODUCED a value")
        XCTAssertNil(out ?? nil, "and that produced value must still be nil")
    }

    /// The deadline path must be reachable and distinct. Failure case: remove the sleep task from
    /// the group → the slow operation completes and this returns non-nil → red.
    func testDeadlineWin_returnsNilAndIsCountedAsARun() async {
        var b = budget(perProbe: 0.05)
        let out: String? = await ProbeRunner.run(label: "slow", budget: &b) {
            try? await Task.sleep(for: .seconds(2))
            return "never"
        }

        XCTAssertNil(out)
        XCTAssertEqual(b.probesRun, 1, "a probe that hit its deadline still RAN")
    }

    // MARK: - S1: an opaque non-nil result is not evidence of success

    /// The runner must not claim success for a value it cannot interpret. This test pins the
    /// contract at the seam: the runner returns the value untouched and does not inspect it, so a
    /// result whose own status says `timedOut` is passed through unjudged rather than relabelled.
    ///
    /// SCOPE — this test asserts on the RETURN VALUE only, and a fabricated span attribute is
    /// invisible to a caller checking the returned object. The span side is covered by
    /// `ProbeSpanExportTests.testOpaqueNonNilResult_isExportedUnclassifiedNotSuccess`, which reads
    /// what is actually exported; mutation-checked at 4 failures when the defect is reintroduced.
    /// Keep both: this one pins the pass-through contract, that one pins the wire format.
    func testResultCarryingItsOwnTimeout_isReturnedUnjudged() async {
        var b = budget()
        let out: TriStateResult? = await ProbeRunner.run(label: "tri", budget: &b) {
            TriStateResult(status: .timedOut)
        }

        XCTAssertNotNil(out, "the runner must pass the value through, not swallow it")
        XCTAssertEqual(out?.status, .timedOut,
                       "the probe's own tri-state is the authority; the runner must not overwrite it")
        XCTAssertEqual(b.probesRun, 1)
    }

    // MARK: - Untested is emitted, not silence

    /// A budget-exhausted probe must not run the operation, and must still count as observed.
    /// Failure case: delete the budget gate → the operation runs → `ran` becomes true → red.
    func testBudgetExhausted_skipsWithoutRunningTheOperation() async {
        var b = budget(maxProbes: 0)
        let ran = LockedFlag()

        let out: String? = await ProbeRunner.run(label: "gated", budget: &b) {
            await ran.set()
            return "should not happen"
        }

        XCTAssertNil(out)
        let didRun = await ran.value
        XCTAssertFalse(didRun, "an exhausted budget must SKIP the probe, not run it")
    }

    /// Exhaustion by elapsed time, not just probe count — the arm the Windows suite leaves
    /// unexercised (a mutation hardcoding the reason to `max_probes_reached` survived there).
    func testBudgetExhaustedByElapsedTime_alsoSkips() async {
        var b = ProbeBudget(totalTimeout: 0.01, perProbeTimeout: 1.0, maxProbes: 8)
        b.add(duration: 0.5)   // push elapsed past totalTimeout
        XCTAssertTrue(b.isExhausted, "fixture precondition: the budget must be time-exhausted")

        let ran = LockedFlag()
        let out: String? = await ProbeRunner.run(label: "time-gated", budget: &b) {
            await ran.set()
            return "should not happen"
        }

        XCTAssertNil(out)
        let didRun = await ran.value
        XCTAssertFalse(didRun)
    }

    /// A genuinely resolved probe still returns its value — the fix must not suppress real results.
    func testResolvedProbe_returnsItsValue() async {
        var b = budget()
        let out: String? = await ProbeRunner.run(label: "fast", budget: &b) { "answer" }

        XCTAssertEqual(out, "answer")
        XCTAssertEqual(b.probesRun, 1)
    }
}

/// Minimal actor flag — the probe closure is `@Sendable`, so a captured `var` will not compile.
private actor LockedFlag {
    private(set) var value = false
    func set() { value = true }
}
