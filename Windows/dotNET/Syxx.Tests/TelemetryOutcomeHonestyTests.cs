using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Telemetry must obey the same honesty rules as the product it observes.
///
/// THE DEFECT CLASS: a span that returns without throwing is not evidence of success. A probe that
/// timed out, one whose operation threw, and one that genuinely succeeded all return normally —
/// marking them all OK exports "we measured nothing" as "everything is fine", which is the same
/// failure this codebase exists to refuse, reproduced inside the observability layer. The macOS
/// twin did exactly that (`SyxxTracer.swift` assigned `.ok` unconditionally after every body).
///
/// These tests pin four things a future refactor must not undo:
///   1. Untested is emitted, not omitted — a skipped probe is distinguishable from an unplanned one.
///   2. Untested and Inconclusive produce DIFFERENT status and different attributes.
///   3. Inconclusive is Error (could-not-measure must be as loud as a crash), not Ok.
///   4. A thrown exception is not relabelled as a measured timeout.
/// </summary>
public class TelemetryOutcomeHonestyTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _captured = new();

    public TelemetryOutcomeHonestyTests()
    {
        // Without a listener, StartActivity returns null and every span silently vanishes —
        // which is itself the Windows CLI's current production state (no TracerProvider).
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "SyxxKit",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { lock (_captured) { _captured.Add(a); } },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private Activity Single(string name)
    {
        lock (_captured) { return _captured.Single(a => a.OperationName == name); }
    }

    /// Read via GetTagItem, not `Tags`: `Tags` exposes only string-valued tags, so the bool
    /// `syxx.attempted` / `syxx.evidence` flags would silently read as null there.
    private static string? Tag(Activity a, string key)
        => a.GetTagItem(key)?.ToString();

    // ── 1 & 2: Untested is emitted, and is distinct from Inconclusive ──

    /// A budget-exhausted probe must leave a trace. Failure case: delete the EmitUntested call in
    /// ProbeRunner's budget gate → no span → `Single` throws → red.
    [Fact]
    public async Task SkippedProbe_EmitsAnUntestedSpan_RatherThanNothing()
    {
        var budget = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), maxProbes: 0);

        var result = await ProbeRunner.RunAsync<string>("skipme", budget, () => Task.FromResult<string?>("x"));

        Assert.Null(result);
        var span = Single("probe.skipme");
        Assert.Equal("untested", Tag(span, "syxx.outcome"));
        Assert.Equal("False", Tag(span, "syxx.attempted"));
        Assert.Equal("max_probes_reached", Tag(span, "syxx.outcome.reason"));
        // Untested is NOT an error — nothing failed; nothing ran.
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    /// The distinction the whole exercise is about. Failure case: map Untested onto the
    /// Inconclusive arm of SetOutcome → identical status and attempted flag → red.
    [Fact]
    public async Task UntestedAndInconclusive_AreNotInterchangeable()
    {
        var exhausted = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), maxProbes: 0);
        await ProbeRunner.RunAsync<string>("gated", exhausted, () => Task.FromResult<string?>("x"));

        var live = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), maxProbes: 4);
        await ProbeRunner.RunAsync<string>("stalled", live, async () =>
        {
            await Task.Delay(400).ConfigureAwait(false);
            return "never";
        });

        var untested = Single("probe.gated");
        var inconclusive = Single("probe.stalled");

        Assert.Equal("untested", Tag(untested, "syxx.outcome"));
        Assert.Equal("inconclusive", Tag(inconclusive, "syxx.outcome"));
        Assert.NotEqual(Tag(untested, "syxx.outcome"), Tag(inconclusive, "syxx.outcome"));
        // "Did not run" vs "ran and did not resolve" — the attempted flag is the discriminator.
        Assert.Equal("False", Tag(untested, "syxx.attempted"));
        Assert.Equal("True", Tag(inconclusive, "syxx.attempted"));
        Assert.NotEqual(untested.Status, inconclusive.Status);
    }

    // ── 3: a probe that could not measure must never export as OK ──

    /// Failure case: assign Ok unconditionally after the body (the macOS bug) → red.
    [Fact]
    public async Task TimedOutProbe_IsErrorNotOk()
    {
        var budget = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), maxProbes: 4);

        var result = await ProbeRunner.RunAsync<string>("slow", budget, async () =>
        {
            await Task.Delay(400).ConfigureAwait(false);
            return "never";
        });

        Assert.Null(result);
        var span = Single("probe.slow");
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        Assert.NotEqual(ActivityStatusCode.Ok, span.Status);
        Assert.Equal("inconclusive", Tag(span, "syxx.outcome"));
        Assert.Equal("False", Tag(span, "syxx.evidence"));
    }

    /// A resolved probe is UNCLASSIFIED, not Success — and deliberately so.
    ///
    /// The runner receives an opaque T. Several probe results carry their own tri-state
    /// (`DNSResolver.QueryUDPDetailedAsync` returns a non-null DNSResponse whose Status may be
    /// Timeout), so "returned non-null" is no more evidence of success than "did not throw" was.
    /// Scoring it Success with evidence:true relocated the original defect from the tracer into
    /// the runner and added a fabricated evidence claim. Only the probe can classify its own
    /// result; until each call site supplies a classifier, the span says so.
    ///
    /// Failure case: restore `SetOutcome(Success, evidence: true)` on the non-null branch → red.
    [Fact]
    public async Task ResolvedProbe_IsUnclassifiedNotSuccess()
    {
        var budget = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), maxProbes: 4);

        var result = await ProbeRunner.RunAsync<string>("fast", budget, () => Task.FromResult<string?>("answer"));

        Assert.Equal("answer", result);   // the value itself is passed through untouched
        var span = Single("probe.fast");
        Assert.Equal("unclassified", Tag(span, "syxx.outcome"));
        Assert.Equal("runner_cannot_classify", Tag(span, "syxx.outcome.reason"));
        // Unjudged must not render green in a collector's default view...
        Assert.NotEqual(ActivityStatusCode.Ok, span.Status);
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
        // ...and must never carry a fabricated evidence claim.
        Assert.NotEqual("True", Tag(span, "syxx.evidence"));
        // It DID run, which is what separates this from untested.
        Assert.Equal("True", Tag(span, "syxx.attempted"));
    }

    // ── 4: a thrown exception is not a measured timeout ──

    /// Failure case: collapse the catch arms back to a single `result = null` labelled "timeout"
    /// → reason becomes probe_deadline → red. A fabricated cause is worse than no cause.
    [Fact]
    public async Task ThrownException_IsReportedAsTransportError_NotAsTimeout()
    {
        var budget = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), maxProbes: 4);

        var result = await ProbeRunner.RunAsync<string>("boom", budget,
            () => throw new InvalidOperationException("socket exploded"));

        Assert.Null(result);
        var span = Single("probe.boom");
        Assert.Equal("transport_error", Tag(span, "syxx.outcome.reason"));
        Assert.NotEqual("probe_deadline", Tag(span, "syxx.outcome.reason"));
        Assert.Equal("inconclusive", Tag(span, "syxx.outcome"));
    }

    // ── The cancellation overload: previously had NO telemetry coverage at all ──

    /// A NON-COOPERATIVE probe (ignores the token) must still be cut off. Before the backstop this
    /// branch awaited the operation directly, so it ran unbounded past PerProbeTimeout and the span
    /// never ended — while macOS's task-group deadline applied unconditionally.
    /// Failure case: remove the Task.WhenAny backstop → this hangs ~2s and returns "never" → red.
    [Fact]
    public async Task NonCooperativeProbe_OnCancellationOverload_IsStillCutOffAtTheDeadline()
    {
        var budget = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), maxProbes: 4);

        var sw = Stopwatch.StartNew();
        var result = await ProbeRunner.RunAsync<string>("uncoop", budget, async _ =>
        {
            await Task.Delay(2000).ConfigureAwait(false);   // deliberately ignores the token
            return "never";
        });
        sw.Stop();

        Assert.Null(result);
        Assert.True(sw.ElapsedMilliseconds < 1500,
                    $"the deadline must bound a non-cooperative probe; took {sw.ElapsedMilliseconds}ms");
        var span = Single("probe.uncoop");
        Assert.Equal("probe_deadline", Tag(span, "syxx.outcome.reason"));
        Assert.Equal("inconclusive", Tag(span, "syxx.outcome"));
    }

    /// A COOPERATIVE probe returns null on cancellation instead of throwing. That must be reported
    /// as a deadline, not as "it ran to completion and produced nothing".
    /// Failure case: drop the `cts.IsCancellationRequested` check → reason becomes
    /// operation_returned_null → red. A probe cut off at the deadline did NOT run to completion.
    [Fact]
    public async Task CooperativeProbe_ReturningNullOnCancel_IsADeadlineNotAnEmptyResult()
    {
        var budget = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(50), maxProbes: 4);

        var result = await ProbeRunner.RunAsync<string>("coop", budget, async ct =>
        {
            try { await Task.Delay(2000, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return null; }   // cooperative: returns, not throws
            return "never";
        });

        Assert.Null(result);
        var span = Single("probe.coop");
        Assert.Equal("probe_deadline", Tag(span, "syxx.outcome.reason"));
        Assert.NotEqual("operation_returned_null", Tag(span, "syxx.outcome.reason"));
    }

    /// Elapsed-time exhaustion, not just probe-count — the arm a mutation hardcoding the reason to
    /// "max_probes_reached" previously survived.
    /// Failure case: hardcode either reason token → red.
    [Fact]
    public async Task BudgetExhaustedByElapsedTime_ReportsTheTimeReasonNotTheCountReason()
    {
        var budget = new ProbeBudget(TimeSpan.FromMilliseconds(10), TimeSpan.FromSeconds(1), maxProbes: 8);
        budget.Add(TimeSpan.FromSeconds(1));   // push elapsed past TotalTimeout

        var result = await ProbeRunner.RunAsync<string>("timegated", budget, () => Task.FromResult<string?>("x"));

        Assert.Null(result);
        var span = Single("probe.timegated");
        Assert.Equal("budget_exhausted", Tag(span, "syxx.outcome.reason"));
        Assert.NotEqual("max_probes_reached", Tag(span, "syxx.outcome.reason"));
    }

    /// An operation that returns null of its own accord ran to completion — it is neither a
    /// deadline nor an error, and must not borrow either label.
    [Fact]
    public async Task OperationReturningNull_IsNotLabelledADeadlineOrAnError()
    {
        var budget = new ProbeBudget(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), maxProbes: 4);

        var result = await ProbeRunner.RunAsync<string>("empty", budget, () => Task.FromResult<string?>(null));

        Assert.Null(result);
        var span = Single("probe.empty");
        Assert.Equal("operation_returned_null", Tag(span, "syxx.outcome.reason"));
        Assert.Equal("no_result", Tag(span, "probe.result"));
    }
}
