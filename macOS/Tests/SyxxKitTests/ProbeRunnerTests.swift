import XCTest
@testable import SyxxKit

final class ProbeRunnerTests: XCTestCase {
    /// Sendable-safe mutable boolean for verifying operation was/wasn't called
    /// from inside a `@Sendable` closure.
    private final class CallFlag: @unchecked Sendable {
        var called = false
    }

    // ── Helpers ──

    /// An empty, non-exhausted budget suitable for testing success paths.
    private func makeBudget() -> ProbeBudget {
        ProbeBudget(totalTimeout: 60, perProbeTimeout: 5, maxProbes: 10)
    }

    // ── Exhaustion gates ──

    func test_run_exhaustedBudget_returnsNil() async {
        var budget = ProbeBudget(totalTimeout: 0, perProbeTimeout: 5, maxProbes: 10)
        XCTAssertTrue(budget.isExhausted)  // elapsed >= totalTimeout

        let flag = CallFlag()
        let result = await ProbeRunner.run(label: "test", budget: &budget) {
            flag.called = true
            return "should-not-run"
        }
        XCTAssertNil(result)
        XCTAssertFalse(flag.called, "operation must not be called when budget is already exhausted")
    }

    func test_run_maxProbesExceeded_returnsNil() async {
        var budget = ProbeBudget(totalTimeout: 60, perProbeTimeout: 5, maxProbes: 1)
        // Run one probe to exhaust maxProbes
        let r1 = await ProbeRunner.run(label: "test", budget: &budget) { "first" }
        XCTAssertEqual(r1, "first")
        XCTAssertTrue(budget.isExhausted)  // probesRun(1) >= maxProbes(1)

        let flag = CallFlag()
        let r2 = await ProbeRunner.run(label: "test", budget: &budget) {
            flag.called = true
            return "second"
        }
        XCTAssertNil(r2)
        XCTAssertFalse(flag.called, "operation must not be called when probesRun >= maxProbes")
    }

    // ── Success path ──

    func test_run_success_returnsValue() async {
        var budget = makeBudget()
        let result = await ProbeRunner.run(label: "test", budget: &budget) {
            "hello"
        }
        XCTAssertEqual(result, "hello")
    }

    func test_run_success_recordsTiming() async {
        var budget = makeBudget()
        XCTAssertEqual(budget.probesRun, 0)
        XCTAssertEqual(budget.elapsed, 0)

        _ = await ProbeRunner.run(label: "test", budget: &budget) { "value" }

        XCTAssertEqual(budget.probesRun, 1)
        XCTAssertGreaterThan(budget.elapsed, 0, "elapsed should be > 0 after a successful probe")
    }

    // ── Timeout path ──

    func test_run_timeout_returnsNil() async {
        // Use an extremely short perProbeTimeout and a probe that sleeps far past it.
        var budget = ProbeBudget(totalTimeout: 60, perProbeTimeout: 0.010, maxProbes: 10)

        let result = await ProbeRunner.run(label: "test", budget: &budget) {
            try? await Task.sleep(for: .seconds(10))
            return "too-late"
        }
        XCTAssertNil(result, "should return nil when the probe exceeds perProbeTimeout")
    }

    func test_run_timeout_accountsCorrectly() async {
        // F006: elapsed now records real wall-clock time, not the perProbeTimeout cap.
        var budget = ProbeBudget(totalTimeout: 60, perProbeTimeout: 0.010, maxProbes: 10)

        _ = await ProbeRunner.run(label: "test", budget: &budget) {
            try? await Task.sleep(for: .seconds(10))
            return "too-late"
        }

        // elapsed >= perProbeTimeout (timeout fired at ~10ms), probesRun incremented.
        XCTAssertEqual(budget.probesRun, 1)
        XCTAssertGreaterThanOrEqual(budget.elapsed, 0.009, "elapsed must be at least the perProbeTimeout duration")
        // Real elapsed should be close to perProbeTimeout, not 10s (the operation was cancelled).
        XCTAssertLessThanOrEqual(budget.elapsed, 1.0, "elapsed must not be the full probe sleep duration")
    }

    func test_run_timeout_realElapsedNotCapped() async {
        // Mutation probe: if elapsed were still capped at perProbeTimeout, this test would fail
        // because the measured elapsed after TWO timed-out probes would never exceed 2×perProbeTimeout.
        // With real elapsed, the sum grows proportionally to actual wall time per probe.
        var budget = ProbeBudget(totalTimeout: 60, perProbeTimeout: 0.010, maxProbes: 10)

        for _ in 0..<2 {
            _ = await ProbeRunner.run(label: "test", budget: &budget) {
                try? await Task.sleep(for: .seconds(10))
                return "too-late"
            }
        }

        // Two timed-out probes: elapsed should be at least 2 × perProbeTimeout.
        XCTAssertGreaterThanOrEqual(budget.elapsed, 0.018, "two timed-out probes must accumulate real elapsed time")
        XCTAssertEqual(budget.probesRun, 2)
    }

    // ── Exhaustion between probes ──

    func test_run_exhaustion_betweenProbes_skipsSubsequentProbe() async {
        // maxProbes=1 so the first probe exhausts the budget.
        var budget = ProbeBudget(totalTimeout: 60, perProbeTimeout: 5, maxProbes: 1)

        let r1 = await ProbeRunner.run(label: "test", budget: &budget) { "first" }
        XCTAssertEqual(r1, "first")
        XCTAssertTrue(budget.isExhausted)

        // Second probe should be skipped without executing.
        let flag = CallFlag()
        let r2 = await ProbeRunner.run(label: "test", budget: &budget) {
            flag.called = true
            return "second"
        }
        XCTAssertNil(r2)
        XCTAssertFalse(flag.called)
    }

    // ── ProbeBudget value correctness ──

    func test_budget_add_incrementsBoth() {
        var budget = ProbeBudget(totalTimeout: 60, perProbeTimeout: 5, maxProbes: 10)
        budget.add(duration: 1.5)
        XCTAssertEqual(budget.probesRun, 1)
        XCTAssertEqual(budget.elapsed, 1.5, accuracy: 0.001)

        budget.add(duration: 2.5)
        XCTAssertEqual(budget.probesRun, 2)
        XCTAssertEqual(budget.elapsed, 4.0, accuracy: 0.001)
    }

    func test_budget_isExhausted_onProbesRun() {
        var budget = ProbeBudget(totalTimeout: 60, perProbeTimeout: 5, maxProbes: 2)
        budget.add(duration: 0.1)
        XCTAssertFalse(budget.isExhausted)
        budget.add(duration: 0.1)
        XCTAssertTrue(budget.isExhausted)  // probesRun(2) >= maxProbes(2)
    }

    func test_budget_isExhausted_onElapsed() {
        var budget = ProbeBudget(totalTimeout: 2, perProbeTimeout: 5, maxProbes: 100)
        budget.add(duration: 1.5)
        XCTAssertFalse(budget.isExhausted)
        budget.add(duration: 1.0)
        XCTAssertTrue(budget.isExhausted)  // elapsed(2.5) >= totalTimeout(2)
    }

    // ── Factory defaults (F009 item 3 — macOS parity with Windows Default*_HasExpectedValues) ──

    func test_defaultProphet_HasExpectedValues() {
        let b = ProbeBudget.defaultProphet
        XCTAssertEqual(b.totalTimeout, 30)
        XCTAssertEqual(b.perProbeTimeout, 2.5)
        XCTAssertEqual(b.maxProbes, 15)
    }

    func test_defaultMirror_HasExpectedValues() {
        let b = ProbeBudget.defaultMirror
        XCTAssertEqual(b.totalTimeout, 30)
        XCTAssertEqual(b.perProbeTimeout, 2.5)
        XCTAssertEqual(b.maxProbes, 10)
    }

    func test_defaultPortal_HasExpectedValues() {
        let b = ProbeBudget.defaultPortal
        XCTAssertEqual(b.totalTimeout, 15)
        XCTAssertEqual(b.perProbeTimeout, 2.5)
        XCTAssertEqual(b.maxProbes, 5)
    }

    func test_defaultGlutton_HasExpectedValues() {
        let b = ProbeBudget.defaultGlutton
        XCTAssertEqual(b.totalTimeout, 20)
        XCTAssertEqual(b.perProbeTimeout, 2.5)
        XCTAssertEqual(b.maxProbes, 5)
    }
}
