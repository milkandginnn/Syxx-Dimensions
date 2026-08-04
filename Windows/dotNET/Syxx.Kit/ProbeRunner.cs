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
        return await RunInternalAsync(label, budget, async ct =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(budget.PerProbeTimeout);
            return await operation(cts.Token);
        }, supportsCancellation: true);
    }

    // ── Shared implementation ──

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
                using var cts = new CancellationTokenSource(budget.PerProbeTimeout);
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
                    // check that lands in "operation_returned_null" — asserting it ran to
                    // completion and found nothing, about a probe that was cut off at the deadline.
                    if (result == null && cts.IsCancellationRequested) { failureReason = "probe_deadline"; }
                }
                else
                {
                    result = null;
                    failureReason = "probe_deadline";
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
                    result = null;             // the deadline task won — a genuine timeout
                    failureReason = "probe_deadline";
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
            span?.SetTag("probe.result", failureReason ?? "no_result");
            SyxxTracer.SetOutcome(span, SyxxTracer.Outcome.Inconclusive,
                                  failureReason ?? "operation_returned_null", evidence: false);
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
