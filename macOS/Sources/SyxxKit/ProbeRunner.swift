import Foundation

/// Runs a single probe with timeout enforcement and budget accounting.
///
/// ## Timeout enforcement
/// Uses `withThrowingTaskGroup` with a timeout task that returns `nil` after `perProbeTimeout`.
/// Cancellation propagates to the transport layer via `Task.isCancelled` checks inside
/// `DNSResolver.queryUDPDetailed`'s retry loop (**F006**), so a hung probe is torn down at the
/// deadline rather than running ~4s past it.
///
/// ## Budget accounting
/// - Success: `elapsed += actual wall-clock duration`
/// - Timeout: `elapsed += actual wall-clock duration` (real elapsed, not capped at perProbeTimeout)
/// - `probesRun` is always incremented, even on timeout or skipped execution.
///
/// ## Return semantics
/// - Returns the operation's value on success.
/// - Returns `nil` on: budget exhausted, timeout, or if the operation itself returns nil.
/// - No new throws — errors are classified internally as `nil`.
/// Race sentinel for `ProbeRunner`. It exists so the causes of "no result" stay distinguishable:
/// with a bare `Output?` as the task-group element they collapse, and when `Output` is ITSELF
/// Optional (ProbePlan's `vantage`/`warp` closures return `ComparisonResult?`) an `output == nil`
/// test reads only the outer level — so an operation that deliberately returned nil scored as a
/// success while its section recorded nothing. Must be file-scope: Swift cannot nest a generic
/// type inside a closure.
enum ProbeRace<Output: Sendable>: Sendable {
    case produced(Output?)
    case deadline
}

/// C066: typed probe outcome that preserves the four-state distinction the runner already
/// records on the span but previously collapsed into a single `nil` at the API boundary.
/// `untested` = budget exhausted / max probes reached (we never ran it).
/// `deadline` = the per-probe timeout fired.
/// `transportError` = the operation threw.
/// `produced` = the operation returned a value (which may itself be nil for Optional outputs).
public enum ProbeOutcome<Output: Sendable>: Sendable {
    case produced(Output)
    case untested(reason: String)
    case deadline
    case transportError(String?)

    /// Extracts the produced value, or nil for any non-produced outcome.
    public var value: Output? {
        if case .produced(let v) = self { return v }
        return nil
    }
}

public enum ProbeRunner {
    /// Run `operation` under budget `budget`, returning the output or `nil` on timeout/exhaustion.
    ///
    /// - Parameters:
    ///   - label: Human-readable label for debug logging (e.g. `"latency"`, `"wildcard"`).
    ///   - budget: Mutable budget — use `inout` so the caller sees updated `elapsed`/`probesRun`.
    ///   - operation: The probe to run. Must be `async` but need not be throwing.
    /// - Returns: The operation's output, or `nil` if budget was exhausted or a timeout occurred.
    public static func run<Output: Sendable>(
        label: String,
        budget: inout ProbeBudget,
        operation: @escaping @Sendable () async -> Output
    ) async -> Output? {
        // 1. Budget gate
        guard !budget.isExhausted else {
            debugLog(label: label, message: "budget exhausted — skipping")
            // Emit a zero-duration span rather than returning silently. A probe that was
            // SKIPPED must be distinguishable on the wire from one that was never planned;
            // otherwise `untested` — the state meaning "we did not look" — has no
            // representation in telemetry and reads as "not applicable".
            await SyxxTracer.span("probe.\(label)", kind: .internal, attributes: [
                "probe.label": .string(label),
                "probe.budget_elapsed": .double(budget.elapsed),
                "probe.budget_limit": .double(budget.totalTimeout)
            ]) { span in
                SyxxTracer.setOutcome(span, .untested,
                                      reason: budget.probesRun >= budget.maxProbes
                                            ? "max_probes_reached" : "budget_exhausted",
                                      evidence: false)
            }
            return nil
        }

        // 2. Run with timeout — wrapped in an OTel span for observability
        let clock = ContinuousClock()
        let start = clock.now
        let perProbeTimeout = budget.perProbeTimeout  // capture value, not inout reference

        let result: Output? = await SyxxTracer.span("probe.\(label)", kind: .internal, attributes: [
            "probe.label": .string(label),
            "probe.timeout_s": .double(perProbeTimeout),
            "probe.budget_elapsed": .double(budget.elapsed),
            "probe.budget_remaining": .double(max(0, budget.totalTimeout - budget.elapsed))
        ]) { span in
            // `ProbeRace` exists so the three causes of "no result" stay distinguishable. With a
            // bare `Output?` as the group element they collapse: when `Output` is ITSELF Optional
            // (ProbePlan's `vantage` and `warp` closures return `ComparisonResult?`), `output == nil`
            // reads only the outer level, so an operation that deliberately returned nil scored as
            // a success — while its section recorded nothing. The sentinel also lets a deadline win
            // be told apart from a thrown error, which `try?` alone erases.
            var threw = false
            var raced: ProbeRace<Output>?
            do {
                raced = try await withThrowingTaskGroup(of: ProbeRace<Output>.self) { group in
                    group.addTask { .produced(await operation()) }
                    group.addTask {
                        try await Task.sleep(for: .seconds(perProbeTimeout))
                        return .deadline
                    }
                    defer { group.cancelAll() }

                    let first = try await group.next()
                    // C058 Fix 2: when the deadline fires first, the transport may still
                    // complete moments later (a UDP retry at 2s arrives at 2.6s, a DoH request
                    // at 5.1s). Instead of discarding the late-but-valid result, add a grace
                    // deadline and wait for the operation to finish. A real (late) answer is
                    // preferred over a fabricated absence; the span records `late_but_valid`.
                    if case .deadline = first {
                        let gracePeriod: TimeInterval = 1.5
                        group.addTask {
                            try await Task.sleep(for: .seconds(gracePeriod))
                            return .deadline  // grace expired
                        }
                        if let second = try await group.next() {
                            if case .produced(let value) = second, value != nil {
                                return .produced(value)  // late but valid
                            }
                        }
                    }
                    return first
                }
            } catch {
                threw = true
            }

            let output: Output?
            let reason: String?
            // C058 Fix 2: detect late-but-valid — the deadline fired but the grace drain
            // recovered a real answer. The elapsed time exceeds perProbeTimeout, distinguishing
            // it from a normal completion.
            let elapsedSoFar = start.duration(to: clock.now).asTimeInterval
            let isLateButValid = elapsedSoFar > perProbeTimeout
            switch raced {
            case .produced(let value):
                output = value
                if value == nil {
                    reason = "operation_returned_nil"
                } else if isLateButValid {
                    reason = "late_but_valid"
                } else {
                    reason = nil
                }
            case .deadline:
                output = nil
                reason = "probe_deadline"
            case nil:
                output = nil
                reason = threw ? "transport_error" : "no_result"
            }

            if let reason {
                // Distinguishable causes, never a fabricated one. `probe.result` mirrors the
                // reason so the two tags cannot drift.
                span.setAttribute(key: "probe.result", value: reason)
                if reason == "operation_returned_nil" || reason == "late_but_valid" {
                    // A real result (nil or late-but-recovered): the probe classifies itself.
                    SyxxTracer.setOutcome(span, .unclassified, reason: reason)
                } else {
                    SyxxTracer.setOutcome(span, .inconclusive, reason: reason, evidence: false)
                }
            } else {
                // NOT `.success`. `Output` is opaque here, and many probe results carry their own
                // tri-state — `DNSResolver.queryUDPDetailed` returns a NON-OPTIONAL DNSResponse,
                // so a timeout arrives as a non-nil value. Scoring that `success, evidence: true`
                // is the original "reports success having measured nothing" defect relocated from
                // the tracer into the runner, with a fabricated evidence claim on top. The runner's
                // competence ends at "the deadline won / it threw"; the probe must classify itself.
                SyxxTracer.setOutcome(span, .unclassified, reason: "runner_cannot_classify")
            }
            return output
        }

        // 3. Budget accounting — always use real wall-clock elapsed (F006: cap removed)
        let elapsedDuration = start.duration(to: clock.now)
        let accountedDuration = elapsedDuration.asTimeInterval
        budget.add(duration: accountedDuration)

        // 4. Debug logging (gated by SYXX_DEBUG_PROBES)
        debugLog(
            label: label,
            durationMs: accountedDuration * 1000,
            probeIndex: budget.probesRun,
            maxProbes: budget.maxProbes,
            elapsed: budget.elapsed,
            totalTimeout: budget.totalTimeout,
            succeeded: result != nil
        )

        return result
    }

    /// C066: typed variant of `run` that returns a `ProbeOutcome` instead of `Output?`.
    /// Preserves the distinction between untested (budget exhausted), deadline (timeout),
    /// transport error (threw), and produced (the operation returned a value).
    /// Callers (ProbePlan) can use this to record WHY a probe produced no result in
    /// SectionInputs, rather than letting Synthesis guess from a bare nil.
    public static func runTyped<Output: Sendable>(
        label: String,
        budget: inout ProbeBudget,
        operation: @escaping @Sendable () async -> Output
    ) async -> ProbeOutcome<Output> {
        // 1. Budget gate — return untested, not nil
        guard !budget.isExhausted else {
            debugLog(label: label, message: "budget exhausted — skipping")
            let reason = budget.probesRun >= budget.maxProbes ? "max_probes_reached" : "budget_exhausted"
            await SyxxTracer.span("probe.\(label)", kind: .internal, attributes: [
                "probe.label": .string(label),
                "probe.budget_elapsed": .double(budget.elapsed),
                "probe.budget_limit": .double(budget.totalTimeout)
            ]) { span in
                SyxxTracer.setOutcome(span, .untested, reason: reason, evidence: false)
            }
            return .untested(reason: reason)
        }

        // 2. Run with timeout — same logic as run(), but return typed outcome
        let clock = ContinuousClock()
        let start = clock.now
        let perProbeTimeout = budget.perProbeTimeout

        let outcome: ProbeOutcome<Output> = await SyxxTracer.span("probe.\(label)", kind: .internal, attributes: [
            "probe.label": .string(label),
            "probe.timeout_s": .double(perProbeTimeout),
            "probe.budget_elapsed": .double(budget.elapsed),
            "probe.budget_remaining": .double(max(0, budget.totalTimeout - budget.elapsed))
        ]) { span in
            var threw = false
            var threwMessage: String?
            var raced: ProbeRace<Output>?
            do {
                raced = try await withThrowingTaskGroup(of: ProbeRace<Output>.self) { group in
                    group.addTask { .produced(await operation()) }
                    group.addTask {
                        try await Task.sleep(for: .seconds(perProbeTimeout))
                        return .deadline
                    }
                    defer { group.cancelAll() }
                    let first = try await group.next()
                    if case .deadline = first {
                        let gracePeriod: TimeInterval = 1.5
                        group.addTask {
                            try await Task.sleep(for: .seconds(gracePeriod))
                            return .deadline
                        }
                        if let second = try await group.next() {
                            if case .produced(let value) = second, value != nil {
                                return .produced(value)
                            }
                        }
                    }
                    return first
                }
            } catch {
                threw = true
                threwMessage = error.localizedDescription
            }

            switch raced {
            case .produced(let value):
                if let value {
                    return .produced(value)
                } else {
                    // C066: the operation returned nil. For Optional Output types this is a
                    // valid result (the probe chose to return nil); for non-Optional types
                    // this shouldn't happen. Either way, the span records the observation
                    // and the outcome is transportError so callers can distinguish it from
                    // a produced non-nil value.
                    span.setAttribute(key: "probe.result", value: "operation_returned_nil")
                    SyxxTracer.setOutcome(span, .unclassified, reason: "operation_returned_nil")
                    return .transportError("operation_returned_nil")
                }
            case .deadline:
                span.setAttribute(key: "probe.result", value: "probe_deadline")
                SyxxTracer.setOutcome(span, .inconclusive, reason: "probe_deadline", evidence: false)
                return .deadline
            case nil:
                span.setAttribute(key: "probe.result", value: threw ? "transport_error" : "no_result")
                SyxxTracer.setOutcome(span, .inconclusive, reason: threw ? "transport_error" : "no_result", evidence: false)
                return .transportError(threwMessage)
            }
        }

        // 3. Budget accounting
        let elapsedDuration = start.duration(to: clock.now)
        budget.add(duration: elapsedDuration.asTimeInterval)

        debugLog(
            label: label,
            durationMs: elapsedDuration.asTimeInterval * 1000,
            probeIndex: budget.probesRun,
            maxProbes: budget.maxProbes,
            elapsed: budget.elapsed,
            totalTimeout: budget.totalTimeout,
            succeeded: outcome.value != nil
        )

        return outcome
    }
}

// MARK: - Debug logging

private extension ProbeRunner {
    static func debugLog(
        label: String,
        durationMs: Double,
        probeIndex: Int,
        maxProbes: Int,
        elapsed: TimeInterval,
        totalTimeout: TimeInterval,
        succeeded: Bool
    ) {
        guard ProcessInfo.processInfo.environment["SYXX_DEBUG_PROBES"] != nil else { return }
        let icon = succeeded ? "✅" : "⏰"
        print("[\(label)] \(icon) \(String(format: "%.1f", durationMs))ms "
            + "(probe \(probeIndex)/\(maxProbes), "
            + "elapsed \(String(format: "%.2f", elapsed))s/\(String(format: "%.0f", totalTimeout))s)")
    }

    static func debugLog(label: String, message: String) {
        guard ProcessInfo.processInfo.environment["SYXX_DEBUG_PROBES"] != nil else { return }
        print("[\(label)] ⏭️ \(message)")
    }
}

// MARK: - Duration helpers

private extension Duration {
    /// Convenience: express a `Swift.Duration` as a `TimeInterval` (fractional seconds).
    var asTimeInterval: TimeInterval {
        let components = self.components
        return TimeInterval(components.seconds) + TimeInterval(components.attoseconds) / 1_000_000_000_000_000_000
    }
}
