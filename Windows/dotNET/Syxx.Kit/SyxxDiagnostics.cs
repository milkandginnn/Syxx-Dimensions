using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Linq;

namespace Syxx.Kit;

/// <summary>
/// Vendor-neutral diagnostic sink for the core library. Twin of macOS
/// <c>SyxxKit/SyxxDiagnostics.swift</c> — same shape, same vocabulary, same rules.
///
/// <para>WHY THIS EXISTS: every honesty-critical failure site — the determinative engine,
/// LicenseManager, DNSCapture, ProbePlan — lives in Syxx.Kit, and Syxx.Kit does not (and should
/// not) reference Sentry; only the CLI and the GUI projects do. A capture call written directly
/// against a vendor SDK would not compile here. This indirection is also the single consent gate:
/// the sink is installed only when the user has agreed, so no per-call-site consent check exists
/// to forget.</para>
///
/// <para>Because Ubuntu's Avalonia client project-references this assembly, every capture written
/// here fires on Linux too — the first ship platform, which today has no telemetry at all.</para>
///
/// <para>THE RULE IT ENFORCES: a failure that changed what the product could conclude must never
/// be silent. A bare <c>catch { return null; }</c> on a measurement path is how a result goes
/// missing with nobody told.</para>
/// </summary>
public enum DiagnosticSeverity { Warning, Error, Fatal }

/// <summary>
/// What this failure did to the measurement. An error is only interesting because of this.
/// Each member names the <c>SectionStatus</c> the adapter is expected to assign, so a mismatch
/// between what we report and what the engine records is itself detectable.
/// </summary>
public enum MeasurementImpact
{
    /// Logged; the measurement is unaffected. Warning-level, never an issue.
    None,
    DegradedSection,        // -> SectionStatus.Degraded
    InconclusiveSection,    // -> SectionStatus.Inconclusive
    InvalidatedSection,     // -> SectionStatus.Invalidated
    UntestedSection,        // -> SectionStatus.Untested
    NoDetermination,        // -> Conclusion.Determination == null
}

/// <summary>One diagnostic occurrence. Construct at the failure site, where the cause is known.</summary>
/// <param name="Underlying">
/// Error description. MUST NOT contain a hostname, an IP, a license key, or a path under the
/// user's home directory — this product's users are people whose DNS is being surveilled, and
/// their query names are the last thing that should leave the machine. Callers redact.
/// </param>
/// <param name="Fingerprint">
/// Stable grouping key. Use closed-set discriminants (an error *variant*), never interpolated
/// text — an interpolated id shards one bug into hundreds of issues.
/// </param>
public sealed record DiagnosticEvent(
    string ErrorClass,
    string Component,
    Section? Section,
    MeasurementImpact Impact,
    DiagnosticSeverity Severity,
    string? Underlying,
    IReadOnlyList<string> Fingerprint,
    IReadOnlyDictionary<string, string>? Tags = null,
    IReadOnlyDictionary<string, string>? Extra = null);

/// <summary>Implemented in the app layer, where a vendor SDK is referenced.
/// Absence of a sink is the OFF state.</summary>
public interface IDiagnosticSink
{
    void Capture(DiagnosticEvent evt);
    void Breadcrumb(string category, string message, DiagnosticSeverity level,
                    IReadOnlyDictionary<string, string> data);
    /// <summary>Drop everything not yet transmitted WITHOUT sending it. Called on consent
    /// withdrawal — flushing here would transmit exactly the data the user just refused.</summary>
    void DiscardBuffered();
}

/// <summary>Per-run attribution ledger: which sections reported which failures during this scan.
/// Read once at synthesis to detect a degraded section that nobody explained.</summary>
public sealed class RunContext
{
    public string Id { get; }
    private readonly ConcurrentDictionary<Section, ConcurrentDictionary<string, byte>> _bySection = new();

    public RunContext(string? id = null) => Id = id ?? Guid.NewGuid().ToString();

    internal void Record(Section? section, string errorClass)
    {
        if (section is not { } s) { return; }
        _bySection.GetOrAdd(s, _ => new ConcurrentDictionary<string, byte>())[errorClass] = 0;
    }

    public IReadOnlyCollection<string> ErrorClassesFor(Section section)
        => _bySection.TryGetValue(section, out var set) ? (IReadOnlyCollection<string>)set.Keys : Array.Empty<string>();

    public IReadOnlyCollection<string> AllErrorClasses
    {
        get
        {
            var all = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in _bySection.Values) { foreach (var k in set.Keys) { all.Add(k); } }
            return all;
        }
    }
}

public static class SyxxDiagnostics
{
    /// <summary>Ambient run id, flowing across async/await and thread-pool hops so a failure deep
    /// in a probe can be joined to the result it produced.</summary>
    private static readonly AsyncLocal<RunContext?> _run = new();
    public static RunContext? Run { get => _run.Value; set => _run.Value = value; }

    private static IDiagnosticSink? _sink;

    /// <summary>Install the platform sink. Call ONLY when consent has been affirmatively granted.</summary>
    public static void Install(IDiagnosticSink sink) => Interlocked.Exchange(ref _sink, sink);

    /// <summary>Detach and discard. The exchange clears the sink FIRST so a capture racing in from
    /// a scan thread finds null, THEN we discard whatever the vendor buffered.</summary>
    public static void Uninstall()
    {
        var old = Interlocked.Exchange(ref _sink, null);
        old?.DiscardBuffered();
    }

    /// <summary>True when a sink is installed. For honest UI: report observed state, never the toggle.</summary>
    public static bool IsActive => Volatile.Read(ref _sink) is not null;

    public static void Capture(DiagnosticEvent evt)
    {
        // Record attribution even with no sink: the unattributed-degradation check is a local
        // honesty invariant and must hold whether or not the user shares telemetry.
        Run?.Record(evt.Section, evt.ErrorClass);
        Volatile.Read(ref _sink)?.Capture(evt);
    }

    public static void Breadcrumb(string category, string message,
                                  DiagnosticSeverity level = DiagnosticSeverity.Warning,
                                  IReadOnlyDictionary<string, string>? data = null)
        => Volatile.Read(ref _sink)?.Breadcrumb(category, message, level,
               data ?? new Dictionary<string, string>());
}

/// <summary>
/// Telemetry redaction. ONE list, applied to every channel — spans, breadcrumbs, and Sentry events
/// alike. If it lived in only one place the leak would simply move channels.
/// Twin of macOS <c>TelemetryRedaction</c> in SyxxDiagnostics.swift.
///
/// <para>THE RULE: a DNS query stream IS a browsing history. Syxx's users are people whose DNS is
/// being surveilled; exporting the names they look up to any endpoint — ours included — would make
/// this product a second surveillance channel. Domains are never emitted verbatim unless they are
/// Syxx's OWN constants, which carry no user information.</para>
/// </summary>
public static class TelemetryRedaction
{
    /// Domains Syxx itself chose. Safe to name exactly: they say nothing about the user.
    private static readonly HashSet<string> SyxxOwnedDomains =
        new(StringComparer.OrdinalIgnoreCase) { "github.com" };

    private static readonly HashSet<string> KnownPublicResolvers = new(StringComparer.Ordinal)
    {
        "8.8.8.8", "8.8.4.4", "1.1.1.1", "1.0.0.1", "9.9.9.9", "149.112.112.112",
        "208.67.222.222", "208.67.220.220", "94.140.14.14", "76.76.2.0",
    };

    public static bool IsSyxxOwned(string domain) => SyxxOwnedDomains.Contains(domain);

    /// <summary>Attributes describing a queried name WITHOUT disclosing it. Shape is enough to
    /// diagnose malformed-name and long-name failures; the name itself is not.</summary>
    public static List<(string, object?)> ForDomain(string domain)
    {
        if (SyxxOwnedDomains.Contains(domain))
        {
            return new List<(string, object?)>
            {
                ("dns.target_class", "syxxOwned"),
                ("dns.target", domain),   // our own constant — safe verbatim
            };
        }
        var isIdn = domain.Contains("xn--", StringComparison.OrdinalIgnoreCase)
                    || domain.Any(c => c > 127);
        return new List<(string, object?)>
        {
            ("dns.target_class", "userSupplied"),
            ("dns.name.labels", domain.Split('.').Length),
            ("dns.name.length", domain.Length),
            ("dns.name.is_idn", isIdn),
        };
    }

    /// <summary>Public resolvers are a meaningful, non-identifying dimension; a home, ISP, or
    /// corporate resolver is identifying and is reduced to a class token.</summary>
    public static List<(string, object?)> ForResolver(string address)
    {
        if (KnownPublicResolvers.Contains(address))
        {
            return new List<(string, object?)>
            {
                ("dns.server", address),
                ("dns.server_class", "public"),
            };
        }
        string cls;
        if (address.StartsWith("127.", StringComparison.Ordinal) || address == "::1") { cls = "loopback"; }
        else if (address.StartsWith("10.", StringComparison.Ordinal)
                 || address.StartsWith("192.168.", StringComparison.Ordinal)
                 || address.StartsWith("169.254.", StringComparison.Ordinal)
                 // B224: RFC1918 172.16.0.0/12 covers 172.16.x.x–172.31.x.x, not just 172.16.x.x.
                 // String prefix "172.16." also falsely matched the public 172.160.0.0/16 range.
                 || (address.StartsWith("172.", StringComparison.Ordinal)
                     && System.Net.IPAddress.TryParse(address, out var ip172)
                     && ip172.GetAddressBytes() is var b172 && b172.Length == 4
                     && b172[1] >= 16 && b172[1] <= 31)) { cls = "private"; }
        else { cls = "unknown"; }
        return new List<(string, object?)> { ("dns.server_class", cls) };
    }

    /// <summary>Full attribute set for a DNS span with the queried name redacted.</summary>
    public static (string, object?)[] SpanAttrs(string domain, ushort type, string transport,
                                                string? resolver = null, int? port = null,
                                                string? dohEndpoint = null)
    {
        var attrs = new List<(string, object?)>
        {
            ("dns.type", (int)type),
            ("dns.transport", transport),
        };
        attrs.AddRange(ForDomain(domain));
        if (resolver is not null) { attrs.AddRange(ForResolver(resolver)); }
        if (port is not null) { attrs.Add(("dns.port", port.Value)); }
        if (dohEndpoint is not null) { attrs.Add(("dns.server", dohEndpoint)); }
        return attrs.ToArray();
    }
}

/// <summary>
/// The single consent gate for ALL telemetry channels — Sentry, OTel, and breadcrumbs.
/// Twin of macOS <c>TelemetryConsent</c>/<c>TelemetryGate</c> in SyxxDiagnostics.swift.
///
/// <para>WHY TRI-STATE: <c>TelemetryEnabled</c> is a bool defaulting to <c>true</c>, so "no answer"
/// and "yes" were the same value. A user who opted out and then had their settings file truncated
/// was silently opted back in. <c>Unknown</c> exists so missing data can behave as
/// <c>Denied</c> — the rule the result engine applies to evidence, applied to consent.</para>
///
/// <para>Because Ubuntu's Avalonia client project-references this assembly, this gate governs
/// Linux too — the first ship platform.</para>
/// </summary>
public enum TelemetryConsent
{
    Granted,   // user affirmatively enabled
    Denied,    // user affirmatively disabled
    Unknown,   // never asked, OR settings exist but will not decode
}

public static class TelemetryConsentExtensions
{
    /// <summary>The only question callers should ask. <c>Unknown</c> is NOT consent.</summary>
    public static bool AllowsTelemetry(this TelemetryConsent c) => c == TelemetryConsent.Granted;
}

public static class TelemetryGate
{
    /// <summary>
    /// Resolve consent from persisted settings and the environment override.
    /// <c>SYXX_TELEMETRY=0</c> always wins: a CI run or a scripted invocation must be able to
    /// guarantee silence without touching the user's settings file.
    /// </summary>
    public static TelemetryConsent CurrentConsent(SyxxSettings? settings = null,
                                                  string? envOverride = null)
    {
        var over = envOverride ?? Environment.GetEnvironmentVariable("SYXX_TELEMETRY");
        if (over == "0") { return TelemetryConsent.Denied; }
        if (settings is not null)
        {
            // Caller supplied settings — use the explicit boolean.
            return settings.TelemetryEnabled ? TelemetryConsent.Granted : TelemetryConsent.Denied;
        }
        // B222: use LoadWithStatus so absent/corrupt files resolve to Unknown, not Granted.
        // "Never asked" and "explicitly enabled" must not be the same value.
        var loadStatus = SyxxSettingsStore.LoadWithStatus();
        if (loadStatus is SettingsLoadStatus.LoadedStatus loaded)
        {
            return loaded.Settings.TelemetryEnabled ? TelemetryConsent.Granted : TelemetryConsent.Denied;
        }
        return TelemetryConsent.Unknown;
    }

    /// <summary>Whether any exporter may be registered right now.</summary>
    public static bool IsAllowed => CurrentConsent().AllowsTelemetry();
}
