using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sentry;
using Syxx.Kit;

namespace Syxx.Telemetry;

/// <summary>
/// The bridge that makes <see cref="SyxxDiagnostics"/> actually report on Windows. Twin of macOS
/// <c>SentryDiagnosticSink.swift</c>. Shared by the WinUI client, the CLI, and the Avalonia
/// client — one implementation so the three cannot drift.
///
/// <para>WHY THIS EXISTS. <c>Syxx.Kit</c> captures diagnostics through a vendor-neutral interface so
/// the core does not link Sentry — the right shape, but until something *implements* that interface
/// and installs it, every <c>SyxxDiagnostics.Capture</c> in the engine finds a null sink and drops
/// silently. The core was instrumented and the wire was never connected. macOS got its bridge
/// first; this is the Windows half, and without it the platform that ships second reports
/// nothing.</para>
///
/// <para>CONSENT. Installed only via <see cref="Install"/>, behind the same <c>TelemetryGate</c>
/// that gates Sentry and OTel init. One gate, three consumers — a second gate is a second thing to
/// forget.</para>
///
/// <para>REDACTION. <c>DiagnosticEvent.Underlying</c> is documented as caller-redacted, but a
/// queried domain reaching the issue tracker is not a bug report — it is precisely the harm this
/// product exists to prevent, and it would arrive looking like ordinary telemetry. The sink
/// re-strips rather than trusting the contract.</para>
/// </summary>
public sealed class SentryDiagnosticSink : IDiagnosticSink
{
    private readonly string _surface;
    private readonly string _platform;

    private SentryDiagnosticSink(string surface, string platform)
    {
        _surface = surface;
        _platform = platform;
    }

    /// Install if and only if consent allows. Returns whether reporting is now live, so callers can
    /// surface the OBSERVED state rather than the toggle position.
    public static bool Install(string surface, string platform, bool? ifAllowed = null)
    {
        if (!(ifAllowed ?? TelemetryGate.IsAllowed)) { return false; }
        SyxxDiagnostics.Install(new SentryDiagnosticSink(surface, platform));
        return true;
    }

    /// Consent withdrawn: detach and drop.
    public static void Remove() => SyxxDiagnostics.Uninstall();

    public void Capture(DiagnosticEvent evt)
    {
        var e = new SentryEvent
        {
            Level = evt.Severity switch
            {
                DiagnosticSeverity.Fatal => SentryLevel.Fatal,
                DiagnosticSeverity.Error => SentryLevel.Error,
                _ => SentryLevel.Warning,
            },
            Message = $"{evt.ErrorClass}: {evt.Component}",
        };

        // Explicit fingerprint, always. Sentry's default stack-based grouping would merge the
        // thirteen distinct MatrixEngineException kinds into one issue (they all throw from the
        // same line), while message-based grouping would shatter them into hundreds (the messages
        // interpolate diagnosis and indicator ids). Neither default is usable here.
        e.SetFingerprint(evt.Fingerprint);

        e.SetTag("syxx.error_class", evt.ErrorClass);
        e.SetTag("syxx.component", evt.Component);
        e.SetTag("syxx.impact", evt.Impact.ToString());
        e.SetTag("syxx.section", evt.Section?.ToString() ?? "none");
        // Surface and platform are supplied by the installing app, not assumed: this same sink
        // serves the GUI, the CLI, and Linux.
        e.SetTag("syxx.surface", _surface);
        e.SetTag("syxx.platform", _platform);
        // Join key to the result-provenance event and the OTel span: one query returns the whole
        // causal chain — which failure, which section, which result.
        e.SetTag("syxx.run_id", SyxxDiagnostics.Run?.Id ?? "none");

        if (evt.Tags is not null)
        {
            // B224: redact tag values — tags are the most exposed Sentry field (indexed, searchable,
            // shown in issue lists). The defence-in-depth argument for Extra applies with more force here.
            foreach (var kv in evt.Tags) { e.SetTag(kv.Key, Redact(kv.Value)); }
        }
        if (evt.Extra is not null)
        {
            foreach (var kv in evt.Extra) { e.SetExtra(kv.Key, Redact(kv.Value)); }
        }
        if (evt.Underlying is not null) { e.SetExtra("underlying", Redact(evt.Underlying)); }

        SentrySdk.CaptureEvent(e);
    }

    public void Breadcrumb(string category, string message, DiagnosticSeverity level,
                           IReadOnlyDictionary<string, string>? data)
    {
        var redacted = data is null ? null : new Dictionary<string, string>();
        if (data is not null)
        {
            foreach (var kv in data) { redacted![kv.Key] = Redact(kv.Value); }
        }
        SentrySdk.AddBreadcrumb(
            message: Redact(message),
            category: category,
            data: redacted,
            level: level switch
            {
                DiagnosticSeverity.Fatal => BreadcrumbLevel.Fatal,
                DiagnosticSeverity.Error => BreadcrumbLevel.Error,
                _ => BreadcrumbLevel.Warning,
            });
    }

    /// Consent withdrawal must DISCARD, never flush — flushing here transmits exactly the data the
    /// user just refused. The .NET SDK's offline cache is opt-in and deliberately NOT enabled, so
    /// disposing without a flush leaves nothing on disk to leak later.
    public void DiscardBuffered() => SentrySdk.Close();

    // ── Redaction ───────────────────────────────────────────────────────────────────────────────

    /// Delegates to <see cref="DiagnosticRedaction"/> in Syxx.Kit. The implementation lives there,
    /// not here, for two reasons: the CLI and the Avalonia client need identical behaviour (three
    /// copies drift, and the drift is invisible — the weakest one leaks and nothing fails), and
    /// SyxxUI cannot be referenced from the test project, so redaction implemented in this file
    /// would be redaction nobody can test.
    public static string Redact(string s) => DiagnosticRedaction.Apply(s);
}
