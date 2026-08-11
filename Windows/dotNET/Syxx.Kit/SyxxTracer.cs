using System.Diagnostics;

namespace Syxx.Kit;

/// <summary>
/// Lightweight OTel span helper for Syxx.Kit operations. Parity twin of macOS SyxxTracer.swift.
/// Creates spans via the registered ActivitySource("SyxxKit"), which the TracerProvider in
/// App.xaml.cs is configured to listen to. When no TracerProvider is registered (e.g. tests),
/// Activity creation is still safe — the ActivitySource API is a no-op without a listener.
/// </summary>
public static class SyxxTracer
{
    /// <summary>
    /// The ActivitySource that the TracerProvider in App.xaml.cs listens to.
    /// Must match the string passed to .AddSource("SyxxKit") in the provider builder.
    /// </summary>
    public static readonly ActivitySource Source = new("SyxxKit", SyxxConfig.Version);

    /// <summary>
    /// Start a new span (Activity) for a named operation. The caller MUST dispose the
    /// returned Activity (preferably via `using`). When no listener is registered, the
    /// returned Activity is a no-op instance.
    ///
    /// Usage:
    /// <code>
    /// using var span = SyxxTracer.StartSpan("dns.query.udp");
    /// span?.SetTag("dns.target_class", "userSupplied");   // NEVER the queried name
    /// var result = await PerformQueryAsync();
    /// span?.SetStatus(ActivityStatusCode.Ok);
    /// return result;
    /// </code>
    /// </summary>
    /// <param name="name">Span name, e.g. "dns.query.udp", "synthesis.evaluate".</param>
    /// <param name="kind">Span kind. Defaults to Internal.</param>
    /// <param name="tags">Optional tags to set on span creation.</param>
    public static Activity? StartSpan(
        string name,
        ActivityKind kind = ActivityKind.Internal,
        params (string Key, object? Value)[] tags)
    {
        var activity = Source.StartActivity(name, kind);
        if (activity != null)
        {
            foreach (var (key, value) in tags)
            {
                activity.SetTag(key, value);
            }
        }
        return activity;
    }

    /// <summary>
    /// The honest outcome of a measurement. Mirrors the macOS <c>SyxxTracer.Outcome</c> case-for-case
    /// and reuses the engine's own vocabulary so the wire format cannot drift from the product state:
    /// Success/Failure/Inconclusive are TriState's cases, Untested is DeterminativeState.Untested.
    /// </summary>
    public enum Outcome
    {
        Success,
        Failure,
        Inconclusive,
        Untested,
        /// The operation ran and returned a value this layer is NOT COMPETENT TO JUDGE.
        /// ProbeRunner receives an opaque T; many probe results carry their own tri-state
        /// (DNSResponse.Status == Timeout), so "returned non-null" is no more evidence of
        /// success than "did not throw" was. Claiming Success there relocates the defect.
        Unclassified,
    }

    /// <summary>
    /// Record the outcome AND derive the OTel status from it, in one call, so the two can never
    /// disagree. Every Syxx.Kit span should call this exactly once.
    /// <para>
    ///   Success      -> Ok.
    ///   Failure      -> Unset (a measured negative is the product working, not a telemetry error).
    ///   Inconclusive -> Error (could-not-measure must be as loud as a crash in the error rate).
    ///   Untested     -> Unset (nothing ran; <c>syxx.attempted=false</c> carries the distinction).
    /// </para>
    /// Never assign Ok merely because a call returned without throwing: a probe that timed out and
    /// one that succeeded both return normally, and marking both Ok is the "reports success having
    /// measured nothing" defect this project exists to refuse.
    /// </summary>
    public static void SetOutcome(Activity? span, Outcome outcome,
                                  string? reason = null, bool? evidence = null)
    {
        if (span == null) { return; }
        span.SetTag("syxx.outcome", outcome switch
        {
            Outcome.Success => "success",
            Outcome.Failure => "failure",
            Outcome.Inconclusive => "inconclusive",
            Outcome.Unclassified => "unclassified",
            _ => "untested",
        });
        span.SetTag("syxx.attempted", outcome != Outcome.Untested);
        if (evidence.HasValue) { span.SetTag("syxx.evidence", evidence.Value); }
        if (reason != null) { span.SetTag("syxx.outcome.reason", reason); }

        switch (outcome)
        {
            case Outcome.Success:
                span.SetStatus(ActivityStatusCode.Ok);
                break;
            case Outcome.Inconclusive:
                span.SetStatus(ActivityStatusCode.Error, reason ?? "inconclusive");
                break;
            default:
                // Failure / Untested / Unclassified -> Unset, never a fabricated Ok. An unjudged
                // result must not render green in a collector's default view.
                break;
        }
    }

    /// <summary>
    /// Classify a span from a DNS response status — the twin of the macOS `classify:` closure.
    /// A resolver that REPLIED produced a measurement, even when the answer is negative:
    /// NXDOMAIN/SERVFAIL/REFUSED are `Failure` (the product working), not telemetry errors.
    /// Only "we could not read an answer" is `Inconclusive`.
    /// </summary>
    public static void SetOutcomeFor(Activity? span, DNSResponseStatus status)
    {
        switch (status)
        {
            case DNSResponseStatus.Success:
            case DNSResponseStatus.NoData:
                SetOutcome(span, Outcome.Success, evidence: true);
                break;
            case DNSResponseStatus.NXDomain:
            case DNSResponseStatus.ServFail:
            case DNSResponseStatus.Refused:
                SetOutcome(span, Outcome.Failure, status.ToString(), evidence: true);
                break;
            default:
                SetOutcome(span, Outcome.Inconclusive, status.ToString(), evidence: false);
                break;
        }
    }

    /// <summary>
    /// Emit a zero-duration span recording that an operation did NOT run. A skipped probe must be
    /// distinguishable on the wire from one that was never planned; otherwise Untested — the state
    /// meaning "we did not look" — has no representation in telemetry and reads as "not applicable".
    /// </summary>
    public static void EmitUntested(string name, string reason,
                                    params (string Key, object? Value)[] tags)
    {
        using var span = StartSpan(name, ActivityKind.Internal, tags);
        SetOutcome(span, Outcome.Untested, reason, evidence: false);
    }

    /// <summary>
    /// Diagnostic breadcrumb convenience — delegates to <see cref="SyxxDiagnostics.Breadcrumb"/>.
    /// Exists so call sites that need a lightweight diagnostic log don't need to reach for the
    /// full diagnostics API. The Swift twin uses <c>SyxxDiagnostics.breadcrumb()</c> directly.
    /// </summary>
    public static void Log(string category, string message)
        => SyxxDiagnostics.Breadcrumb(category, message, DiagnosticSeverity.Warning);
}