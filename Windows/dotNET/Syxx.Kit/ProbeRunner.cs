using System.Diagnostics;
using System.Globalization;

namespace Syxx.Kit;

/// <summary>
/// Runs a single probe with timeout enforcement and budget accounting.
/// </summary>
/// <remarks>
/// <para>
/// <b>Timeout enforcement</b> — uses <c>Task.WhenAny</c> to race the operation against a
/// delay of <c>budget.PerProbeTimeout</c>. If the delay wins, the operation's task is
/// orphaned (it continues in the background but its result is discarded). The
/// <c>CancellationToken</c> overload propagates cancellation into the operation (F006).
/// </para>
/// <para>
/// <b>Budget accounting</b> — records actual wall-clock time for both success and timeout (F006).
/// <c>ProbesRun</c> is always incremented, even on timeout or skipped execution.
/// </para>
/// <para>
/// <b>Return semantics</b> — returns the operation's value on success; <c>null</c> on
/// budget exhausted, timeout, or if the operation itself returns <c>null</c>.
/// No new exceptions are thrown.
/// </para>
/// </remarks>
public static class ProbeRunner
{
    /// <summary>
    /// Run <paramref name="operation"/> under <paramref name="budget"/>.
    /// </summary>
    /// <typeparam name="T">Reference type returned by the operation.</typeparam>
    /// <param name="label">Human-readable label for debug logging (e.g. "latency", "wildcard").</param>
    /// <param name="budget">Mutable budget — the caller sees updated <c>Elapsed</c>/<c>ProbesRun</c>.</param>
    /// <param name="operation">The probe to run. Does not accept a <c>CancellationToken</c>.</param>
    public static async Task<T?> RunAsync<T>(
        string label,
        ProbeBudget budget,
        Func<Task<T?>> operation) where T : class
    {
        return await RunInternalAsync(label, budget, _ => operation(), supportsCancellation: false);
    }

    /// <summary>
    /// Run <paramref name="operation"/> under <paramref name="budget"/>, passing a
    /// <c>CancellationToken</c> that fires after <c>budget.PerProbeTimeout</c>.
    /// </summary>
    /// <typeparam name="T">Reference type returned by the operation.</typeparam>
    /// <param name="label">Human-readable label for debug logging.</param>
    /// <param name="budget">Mutable budget.</param>
    /// <param name="operation">The probe to run. Receives a <c>CancellationToken</c>.</param>
    public static async Task<T?> RunAsync<T>(
        string label,
        ProbeBudget budget,
        Func<CancellationToken, Task<T?>> operation) where T : class
    {
        // The token handed down by RunInternalAsync already carries the deadline (and, per C058,
        // the grace window beyond it). A second linked CTS here would be a duplicate copy of that
        // policy which could silently disagree with it — it previously cancelled at
        // PerProbeTimeout, which would have cut the grace drain off before it could drain.
        return await RunInternalAsync(label, budget, operation, supportsCancellation: true);
    }

    // ── Shared implementation ──

    /// <summary>
    /// C058: when the deadline fires, the transport may still complete moments later — a UDP retry
    /// issued at 2s arriving at 2.6s, a DoH request at 5.1s. Rather than discard a late-but-valid
    /// answer and report a fabricated absence, wait this much longer for the in-flight operation.
    /// Matches the macOS <c>ProbeRunner.swift</c> grace deadline exactly. The effective hard cap on
    /// a probe is therefore <c>PerProbeTimeout + GraceDrain</c>, and cancellation is armed for that
    /// total so a cooperative probe is not torn down during the window meant to recover its answer.
    /// </summary>
    private static readonly TimeSpan GraceDrain = TimeSpan.FromSeconds(1.5);

    private static async Task<T?> RunInternalAsync<T>(
        string label,
        ProbeBudget budget,
        Func<CancellationToken, Task<T?>> operation,
        bool supportsCancellation) where T : class
    {
        // 1. Budget gate
        if (budget.IsExhausted)
        {
            DebugLog(label, "budget exhausted — skipping");
            // Emit a zero-duration span rather than returning silently, so a SKIPPED probe is
            // distinguishable on the wire from one that was never planned. Parity with macOS
            // ProbeRunner.swift.
            SyxxTracer.EmitUntested($"probe.{label}",
                budget.ProbesRun >= budget.MaxProbes ? "max_probes_reached" : "budget_exhausted",
                ("probe.label", label),
                ("probe.budget_elapsed", budget.Elapsed.TotalSeconds),
                ("probe.budget_limit", budget.TotalTimeout.TotalSeconds));
            return null;
        }

        var sw = Stopwatch.StartNew();
        T? result;
        // Which cause produced a null result. "timeout" was previously asserted for all three,
        // so a swallowed exception was reported as a measured timeout — a fabricated cause.
        // C# can tell them apart (the WhenAny winner, and separate catch arms); macOS currently
        // cannot, which is why its reason token is deliberately the weaker
        // "probe_deadline_or_error". Do not "align" them by making this one vaguer.
        string? failureReason = null;
        // C058: set when the deadline fired but the grace drain recovered a real answer. Reported
        // as `late_but_valid` — a real (late) answer is preferred over a fabricated absence, and
        // the tag says plainly that it arrived past the deadline.
        bool lateButValid = false;

        // Wrap in an OTel span for observability — parity with macOS ProbeRunner.swift
        using var span = SyxxTracer.StartSpan($"probe.{label}", ActivityKind.Internal,
            ("probe.label", label),
            ("probe.timeout_s", budget.PerProbeTimeout.TotalSeconds),
            ("probe.budget_elapsed", budget.Elapsed.TotalSeconds),
            ("probe.budget_remaining", Math.Max(0, (budget.TotalTimeout - budget.Elapsed).TotalSeconds)));

        try
        {
            if (supportsCancellation)
            {
                // Operation receives its own CancellationToken; the runner just forwards.
                // C058: armed for PerProbeTimeout + GraceDrain, not PerProbeTimeout. Cancelling at
                // the deadline would tear the operation down at the exact moment the grace drain
                // starts waiting for it, making the drain inert for cooperative probes — which are
                // the ones it exists to recover. macOS's task group only calls `cancelAll()` when
                // the group closure returns, i.e. after the grace window, so this matches it.
                using var cts = new CancellationTokenSource(budget.PerProbeTimeout + GraceDrain);
                // BACKSTOP. This branch previously relied entirely on the operation honoring the
                // token, so a non-cooperative probe ran past PerProbeTimeout unbounded and the span
                // never ended — macOS's task-group deadline applies unconditionally, so this was
                // also a parity gap. Race the token against a real timer.
                var opTask = operation(cts.Token);
                var completed = await Task.WhenAny(opTask, Task.Delay(budget.PerProbeTimeout)).ConfigureAwait(false);
                if (completed == opTask)
                {
                    result = await opTask.ConfigureAwait(false);
                    // A COOPERATIVE probe returns null on cancellation rather than throwing
                    // (DNSResolver.QueryUDPDetailedAsync returns a status, per F006). Without this
                    // check that lands in "operation_returned_nil" — asserting it ran to
                    // completion and found nothing, about a probe that was cut off at the deadline.
                    if (result == null && cts.IsCancellationRequested) { failureReason = "probe_deadline"; }
                }
                else
                {
                    (result, failureReason, lateButValid) = await DrainAsync(opTask).ConfigureAwait(false);
                }
            }
            else
            {
                // Race the operation against a timeout delay.
                var task = operation(CancellationToken.None);
                var timeout = Task.Delay(budget.PerProbeTimeout);
                var completed = await Task.WhenAny(task, timeout);

                if (completed == task)
                {
                    result = await task;       // may re-throw if the task faulted
                }
                else
                {
                    // The deadline task won. Before calling it an absence, give the in-flight
                    // operation the C058 grace window to deliver a late answer (see DrainAsync).
                    (result, failureReason, lateButValid) = await DrainAsync(task).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            result = null;
            failureReason = "probe_deadline";
        }
        catch
        {
            // A thrown exception is NOT a measured timeout. Naming it one fabricates a cause.
            result = null;
            failureReason = "transport_error";
        }

        sw.Stop();

        if (result == null)
        {
            // `null` with no recorded cause means the operation returned null of its own accord:
            // it ran and produced nothing, which is not the same as never finishing.
            var reason = failureReason ?? "operation_returned_nil";
            // `probe.result` MIRRORS the reason so the two tags cannot drift. They previously
            // disagreed — "no_result" on this tag against "operation_returned_null" on the outcome
            // — which is exactly the drift the macOS twin's comment guards against, and it filed
            // one condition under two names.
            span?.SetTag("probe.result", reason);
            if (reason == "operation_returned_nil")
            {
                // Unclassified, NOT Inconclusive: nothing prevented the measurement. The operation
                // ran to completion and chose to return nothing, and only the probe itself is
                // competent to say what that means. Calling it Inconclusive claims a
                // could-not-measure that did not happen. (macOS: `.unclassified`.)
                SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Unclassified, reason);
            }
            else
            {
                SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Inconclusive, reason, evidence: false);
            }
        }
        else if (lateButValid)
        {
            // C058: the deadline fired, but the grace drain recovered a real answer. Recorded as a
            // result, not an absence — and named so the lateness is not hidden.
            span?.SetTag("probe.result", "late_but_valid");
            SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Unclassified, "late_but_valid");
        }
        else
        {
            // NOT Success. T is opaque here and many probe results carry their own tri-state
            // (a DNSResponse with Status == Timeout is a non-null object), so scoring non-null as
            // success with evidence:true relocates the very defect this change set out to remove.
            // The runner's competence ends at "the deadline won / it threw".
            SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Unclassified, "runner_cannot_classify");
        }

        // 3. Budget accounting — real wall-clock elapsed for both success and timeout (F006)
        budget.Add(sw.Elapsed);

        // 4. Debug logging (gated by SYXX_DEBUG_PROBES)
        DebugLog(label, sw.Elapsed, budget.ProbesRun, budget.MaxProbes, budget.Elapsed, budget.TotalTimeout, result != null);

        return result;
    }

    /// <summary>
    /// C058 grace drain. The per-probe deadline has already fired; give the in-flight operation
    /// <see cref="GraceDrain"/> longer to deliver an answer before recording an absence.
    /// </summary>
    /// <returns>
    /// The recovered value with <c>lateButValid</c> set, or <c>(null, "probe_deadline", false)</c>
    /// if the grace window expired or the operation drained to null. A fault is deliberately
    /// re-thrown so the caller's catch arms classify it as <c>transport_error</c> — an operation
    /// that threw during the drain did not time out, and must not borrow the deadline's label.
    /// </returns>
    private static async Task<(T? Result, string? Reason, bool LateButValid)> DrainAsync<T>(Task<T?> opTask)
        where T : class
    {
        var drained = await Task.WhenAny(opTask, Task.Delay(GraceDrain)).ConfigureAwait(false);
        if (drained != opTask)
        {
            // Grace expired with the operation still in flight. Orphaned, as before.
            return (null, "probe_deadline", false);
        }

        var value = await opTask.ConfigureAwait(false);   // may re-throw if the task faulted
        // A drain that yields null recovered nothing: the deadline remains the honest cause.
        return value == null ? (null, "probe_deadline", false) : (value, null, true);
    }

    // ── Debug logging ──

    private static void DebugLog(string label, TimeSpan duration, int probeIndex, int maxProbes, TimeSpan elapsed, TimeSpan totalTimeout, bool succeeded)
    {
        if (Environment.GetEnvironmentVariable("SYXX_DEBUG_PROBES") is null) return;
        var icon = succeeded ? "✅" : "⏰";
        Console.WriteLine(
            $"[{label}] {icon} {duration.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}ms " +
            $"(probe {probeIndex}/{maxProbes}, " +
            $"elapsed {elapsed.TotalSeconds.ToString("F2", CultureInfo.InvariantCulture)}s/{totalTimeout.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s)");
    }

    private static void DebugLog(string label, string message)
    {
        if (Environment.GetEnvironmentVariable("SYXX_DEBUG_PROBES") is null) return;
        Console.WriteLine($"[{label}] ⏭️ {message}");
    }
}
