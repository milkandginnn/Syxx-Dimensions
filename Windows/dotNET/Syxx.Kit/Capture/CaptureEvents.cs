using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Syxx.Kit;

// ──────────────────────────────────────────────────────────────────────────
// MARK: CaptureEvent — the IPC contract every capture surface emits
// ──────────────────────────────────────────────────────────────────────────
//
// AUGUST 2026 (Checkpoint B of the capture refactor). Once the daemonrefactor lands, three
// surfaces will produce events from the SAME underlying record stream:
//   1. The Linux systemd daemon (syxx-capture.service, CAP_NET_RAW, unix-domain socket).
//   2. The macOS launchd daemon (a privileged stand-in until the Apple Developer ID lands and
//      the real XPC helper can ship; the IPC contract is identical so the shed-to-XPC step
//      changes transport only, not payload).
//   3. The cage CLI fallback (Windows/SyxxUI UAC-over-the-shoulder tail; Linux CageViewModel
//      when the daemon socket isn't there; macOS when the launchd job is unloadable).
//
// All three will route through this taxonomy so the GUI, the CLI report, and the animated
// tray glyph consume one shape, regardless of which surface produced it.
//
// HONESTY rules baked in (the whole module is a Honesty Protocol §4 surface):
//   1. Events are derived from records the engine ACTUALLY captured. No event asserts a cause
//      the records do not establish — "37 of 42 responses were NXDOMAIN" is the event; "you
//      are infected with DGA malware" is not.
//   2. Insufficient samples ⇒ NO event emitted. A window with 3 NXDOMAIN responses out of 3
//      is not a flood — it is too little data. The classifier returns an empty list rather
//      than a reassuring "no flood" event (the absence of a Critical event is not the presence
//      of a Clean one — the consumer renders "no signal yet" honestly).
//   3. The EncryptedDns* events come from NetworkKeyState.EncryptedDNSStatus(), NOT from the
//      port-53 capture (which only sees plaintext by definition). The daemon polls that on a
//      separate cadence and emits on transition; the helper below is pure.
//   4. Per-event message wording is pending owner approval (project precedent: macOS twin
//      CollectorFailure.Reason was approved 2026-07-29; the wording here follows the same
//      "sentence carries no cause it has not established" rule, but is NOT yet signed off).
//   5. Thresholds are public constants so tests pin them and an owner can review them in one
//      place. They are deliberately conservative: false-acquittal (failing to flag a real
//      symptom) is worse than false-alarm (flagging a symptom that turns out benign), so the
//      bar for Classification is "enough samples to argue this is a pattern, not noise."
// ──────────────────────────────────────────────────────────────────────────

/// <summary>
/// The kind of event the capture pipeline can produce. The taxonomy is deliberately small —
/// every kind maps to a distinct user-visible outcome and a distinct glyph color, so adding a
/// kind without a clear surface need is how the contract gets bloated.
/// </summary>
public enum CaptureEventKind
{
    /// <summary>The system resolver is configured for an encrypted DNS transport (DoH/DoT/DoQ).
    /// Emitted on transition from No/Unknown → Yes. Glyph color: success/teal.</summary>
    EncryptedDnsDetected,

    /// <summary>The system resolver was configured for encrypted DNS but is no longer. Emitted on
    /// transition Yes → No/Unknown. Glyph color: amber (a regression, not necessarily a leak).</summary>
    EncryptedDnsLost,

    /// <summary>Plaintext DNS was observed on port 53 while encrypted DNS was expected. The
    /// glyph/danger signal. Critical because it is the exact condition this product exists to
    /// catch: queries left the protected transport.</summary>
    PlainTextLeak,

    /// <summary>Abnormal NXDOMAIN rate over the window — a symptom of DGA / malware tunneling /
    /// resolver sinkholing / NXDOMAIN-based data exfiltration. NOT a conclusion: the event
    /// surfaces the symptom; the user investigates.</summary>
    NXDomainFlood,

    /// <summary>Abnormal SERVFAIL rate over the window — a symptom of upstream resolver failure /
    /// misconfiguration / DNSSEC validation breakage / spoofed responses that fail validation.
    /// NOT a conclusion.</summary>
    ServFailSpike,

    /// <summary>Outbound queries are not getting responses back at a normal rate — a symptom of
    /// packet loss, upstream blocking, or a dead resolver. NOT a conclusion.</summary>
    ResponseLossHigh,

    /// <summary>The capture itself is degraded (v4-only or v6-only). Emitted by the daemon on
    /// socket-open failure of one family; the cage CLI emits equivalent copy to its console.</summary>
    CaptureDegraded,
}

/// <summary>
/// Four severity tiers matching the project's <c>Severity</c> brush mapping one-for-one (the
/// glyph and the report colorize from this). <c>Critical</c> is reserved for the
/// <see cref="CaptureEventKind.PlainTextLeak"/> event — the single condition this product
/// exists to catch. <c>Clean</c> is intentionally absent: a "clean" capture event is the absence
/// of an event, never an event itself (Honesty rule 2 above).
/// </summary>
public enum CaptureEventSeverity { Info, Warning, High, Critical }

/// <summary>
/// One event in the capture stream. The record is deliberately small so it serializes cheaply
/// over the daemon's unix-domain socket — the daemon emits these in volume.
/// </summary>
public sealed record CaptureEvent(
    CaptureEventKind Kind,
    CaptureEventSeverity Severity,
    string Message,
    DateTime Timestamp,
    IReadOnlyDictionary<string, string>? Data = null);

/// <summary>
/// Static thresholds for the windowed classifier. Public so tests pin them and an owner can
/// review them in one place. Defaults are conservative on purpose: false-acquittal (missing a
/// real symptom) is worse than false-alarm (flagging one that turns out benign) — the latter
/// is what the user's investigation is for.
/// </summary>
public static class CaptureThresholds
{
    /// <summary>Window length the classifier is asked to evaluate. 30s balances "fast enough to
    /// surface an acute issue" against "long enough to accumulate statistical samples".</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(30);

    /// <summary>Minimum NXDOMAIN count AND NXDOMAIN ratio of all responses for NXDomainFlood.
    /// 10 absolute samples is the floor below which 100% NXDOMAIN could be banal (a single app
    /// querying a stale hostname on startup). The 50% ratio bar is high because a working resolver
    /// legitimately returns NXDOMAIN for misspelled names.</summary>
    public const int NXDomainFloodMinCount = 10;
    public const double NXDomainFloodMinRatio = 0.50;

    /// <summary>Minimum SERVFAIL count AND SERVFAIL ratio for ServFailSpike. 5 absolute samples
    /// + 25% ratio — SERVFAIL is rarer than NXDOMAIN in normal operation so the floor is lower,
    /// but the ratio bar catches a resolver that is failing a quarter of its queries.</summary>
    public const int ServFailSpikeMinCount = 5;
    public const double ServFailSpikeMinRatio = 0.25;

    /// <summary>Minimum outbound-query count AND max response/query ratio for ResponseLossHigh.
    /// 10 outbound queries is the floor at which a "50% response rate" reading is meaningful;
    /// below it, a single dropped response would dominate the ratio. 0.5 ratio means at least
    /// half the queries never got an answer back — this is the symptom the user described:
    /// packet loss visible from the app side.</summary>
    public const int ResponseLossMinOutbound = 10;
    public const double ResponseLossMaxRatio = 0.5;
}

/// <summary>
/// Pure, offline-testable windowed classifier. Given a window of
/// <see cref="DNSCaptureRecord"/>s and the value of NetworkKeyState.EncryptedDNSStatus() at the
/// end of the window, returns the events the window established. Callers (daemon poller, CLI
/// report pass, GUI row stream) call this on their cadence and feed the returned events through
/// <see cref="CaptureEventGate"/> for dedupe.
/// </summary>
public static class CaptureEventClassifier
{
    /// <summary>
    /// Classify a window of records. Pure: no I/O, no clock — the caller supplies
    /// <paramref name="now"/> so tests are deterministic. Returns zero or more events; an empty
    /// list is honest ("no signal this window") and MUST NOT be rendered as a "clean" result —
    /// that is <see cref="CaptureEventKind"/>-level absence, never a Clean event.
    ///
    /// <para><b>encryptedDnsExpected</b> governs <see cref="CaptureEventKind.PlainTextLeak"/>:
    /// plaintext DNS observed while encrypted DNS is configured is the leak this product exists
    /// to catch (Critical). Plaintext DNS observed while encrypted DNS is NOT configured is
    /// normal operation — the event is NOT emitted; the absence of an EncryptedDns* event is
    /// the consumer's signal that encrypted DNS is not in play.</para>
    /// </summary>
    public static IReadOnlyList<CaptureEvent> ClassifyWindow(
        IReadOnlyList<DNSCaptureRecord> records,
        DateTime now,
        TimeSpan? window = null,
        bool encryptedDnsExpected = false)
    {
        var span = window ?? CaptureThresholds.DefaultWindow;
        var cutoff = now - span;
        var events = new List<CaptureEvent>();

        // Filter to records inside the window AND parseable. An unparseable timestamp is excluded
        // (we cannot honestly place it). This is the same D101-invariant-culture policy the cage
        // CLI uses for record WRITES — the round-trip is preserved.
        var inWindow = new List<DNSCaptureRecord>(records.Count);
        foreach (var r in records)
        {
            var ts = ParseTimestamp(r.Timestamp);
            if (ts is DateTime d && d >= cutoff && d <= now) inWindow.Add(r);
        }

        if (inWindow.Count == 0) return events;   // no samples ⇒ no events ⇒ no claim

        // ── Outbound plaintext UDP/53 ⇒ PlainTextLeak (only when encrypted DNS is expected) ──
        // Direction word-match parity with DNSCapture / DNSCaptureReportGenerator. The cage CLI
        // writes "Outbound Query" / "Inbound Response" — match the same substrings here so the
        // classifier does not drift from the producer.
        bool anyOutboundPlaintext = inWindow.Any(r =>
            r.Network.Direction.Contains("Outbound", StringComparison.OrdinalIgnoreCase));
        if (anyOutboundPlaintext && encryptedDnsExpected)
        {
            // PENDING OWNER APPROVAL (2026-08-04): wording carries no cause the records did not
            // establish — the symptom is the leak itself, not why it happened.
            events.Add(new CaptureEvent(
                CaptureEventKind.PlainTextLeak,
                CaptureEventSeverity.Critical,
                "Plaintext DNS queries observed on port 53 while encrypted DNS was expected. " +
                "These queries left the protected transport.",
                now,
                new Dictionary<string, string>
                {
                    ["window_seconds"] = span.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture),
                }));
        }

        // ── rcode tallies over responses (inbound) ──
        var inbound = inWindow.Where(r => r.Network.Direction.Contains("Inbound", StringComparison.OrdinalIgnoreCase)).ToList();
        int totalResponses = inbound.Count;
        int nxdomain = 0, servfail = 0;
        foreach (var r in inbound)
        {
            var rc = (r.Packet.Rcode ?? "").Trim().ToLowerInvariant();
            if (rc == "nxdomain" || rc == "nxdomain-records" || rc == "3") nxdomain++;
            else if (rc == "servfail" || rc == "2") servfail++;
        }

        // ── NXDomainFlood ──
        if (totalResponses >= CaptureThresholds.NXDomainFloodMinCount
            && nxdomain >= CaptureThresholds.NXDomainFloodMinCount
            && (double)nxdomain / totalResponses >= CaptureThresholds.NXDomainFloodMinRatio)
        {
            double ratio = (double)nxdomain / totalResponses;
            events.Add(new CaptureEvent(
                CaptureEventKind.NXDomainFlood,
                CaptureEventSeverity.High,
                // PENDING OWNER APPROVAL — symptom is a ratio, NOT a diagnosis.
                $"{nxdomain} of {totalResponses} responses ({(ratio * 100.0).ToString("F0", CultureInfo.InvariantCulture)}%) " +
                $"were NXDOMAIN over the last {span.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s. " +
                "This is a symptom, not a conclusion — investigate the flagged processes.",
                now,
                new Dictionary<string, string>
                {
                    ["nxdomain_count"] = nxdomain.ToString(CultureInfo.InvariantCulture),
                    ["total_responses"] = totalResponses.ToString(CultureInfo.InvariantCulture),
                    ["ratio"] = ratio.ToString("F3", CultureInfo.InvariantCulture),
                }));
        }

        // ── ServFailSpike ──
        if (totalResponses >= CaptureThresholds.ServFailSpikeMinCount
            && servfail >= CaptureThresholds.ServFailSpikeMinCount
            && (double)servfail / totalResponses >= CaptureThresholds.ServFailSpikeMinRatio)
        {
            double ratio = (double)servfail / totalResponses;
            events.Add(new CaptureEvent(
                CaptureEventKind.ServFailSpike,
                CaptureEventSeverity.High,
                $"{servfail} of {totalResponses} responses ({(ratio * 100.0).ToString("F0", CultureInfo.InvariantCulture)}%) " +
                $"were SERVFAIL over the last {span.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s. " +
                "Upstream resolver failure, misconfiguration, or DNSSEC validation breakage can produce this.",
                now,
                new Dictionary<string, string>
                {
                    ["servfail_count"] = servfail.ToString(CultureInfo.InvariantCulture),
                    ["total_responses"] = totalResponses.ToString(CultureInfo.InvariantCulture),
                    ["ratio"] = ratio.ToString("F3", CultureInfo.InvariantCulture),
                }));
        }

        // ── ResponseLossHigh ──
        int outbound = inWindow.Count(r => r.Network.Direction.Contains("Outbound", StringComparison.OrdinalIgnoreCase));
        if (outbound >= CaptureThresholds.ResponseLossMinOutbound
            && totalResponses > 0   // guard: zero responses ⇒ the fallthrough below
            && (double)totalResponses / outbound <= CaptureThresholds.ResponseLossMaxRatio)
        {
            double ratio = (double)totalResponses / outbound;
            events.Add(new CaptureEvent(
                CaptureEventKind.ResponseLossHigh,
                CaptureEventSeverity.Warning,
                $"{outbound} outbound queries, {totalResponses} responses " +
                $"({(ratio * 100.0).ToString("F0", CultureInfo.InvariantCulture)}% reply rate) over the last " +
                $"{span.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}s. " +
                "Queries were sent but most got no reply back — packet loss, upstream blocking, or a dead resolver can produce this.",
                now,
                new Dictionary<string, string>
                {
                    ["outbound_count"] = outbound.ToString(CultureInfo.InvariantCulture),
                    ["response_count"] = totalResponses.ToString(CultureInfo.InvariantCulture),
                    ["ratio"] = ratio.ToString("F3", CultureInfo.InvariantCulture),
                }));
        }

        return events;
    }

    /// <summary>
    /// Pure helper for the daemon's encrypted-DNS poller. Given the previous and current
    /// <see cref="EncryptedDNSStatus"/> (read from <see cref="NetworkKeyState.EncryptedDNSStatus"/>),
    /// returns an event ON TRANSITION ONLY — No→Yes produces <see cref="CaptureEventKind.EncryptedDnsDetected"/>,
    /// Yes→No/Unknown produces <see cref="CaptureEventKind.EncryptedDnsLost"/>. No transition
    /// is an honest null: the consumer MUST treat null as "no change" rather than "clean".
    /// </summary>
    public static CaptureEvent? EncryptedDnsTransitionEvent(
        EncryptedDNSStatus? previous, EncryptedDNSStatus current, DateTime now)
    {
        bool wasYes = previous?.State == ConditionState.Yes;
        bool isYes = current.State == ConditionState.Yes;

        if (wasYes && !isYes)
        {
            return new CaptureEvent(
                CaptureEventKind.EncryptedDnsLost,
                CaptureEventSeverity.Warning,
                "Encrypted DNS transport was configured but is no longer reported. " +
                "Queries will leave in plaintext unless this is restored.",
                now,
                new Dictionary<string, string>
                {
                    ["previous_state"] = previous?.State.ToString() ?? "null",
                    ["current_state"] = current.State.ToString(),
                    ["current_evidence"] = current.Evidence ?? "",
                });
        }
        if (!wasYes && isYes)
        {
            return new CaptureEvent(
                CaptureEventKind.EncryptedDnsDetected,
                CaptureEventSeverity.Info,
                "Encrypted DNS transport detected (DoH/DoT/DoQ). Queries on this transport are protected.",
                now,
                new Dictionary<string, string>
                {
                    ["current_state"] = current.State.ToString(),
                    ["current_evidence"] = current.Evidence ?? "",
                });
        }
        return null;
    }

    /// <summary>
    /// Construct a <see cref="CaptureEventKind.CaptureDegraded"/> event for the daemon to emit on
    /// socket-open failure of one family. Pure — the daemon supplies the <see cref="OpenSocketResult"/>;
    /// the helper formats the honest state into an event without re-deriving it.
    /// </summary>
    public static CaptureEvent? DegradedEvent(OpenSocketResult sockets, DateTime now)
    {
        if (sockets is { Failure: null } || sockets.BothFailed) return null;   // nothing or hard-fail
        var msg = sockets.V4Only
            ? "Capture degraded — IPv6 raw socket could not be opened. IPv6 DNS traffic is invisible this run."
            : "Capture degraded — IPv4 raw socket could not be opened. IPv4 DNS traffic is invisible this run.";
        return new CaptureEvent(
            CaptureEventKind.CaptureDegraded,
            CaptureEventSeverity.Warning,
            msg,
            now,
            new Dictionary<string, string>
            {
                ["degraded_family"] = sockets.V4Only ? "ipv6" : "ipv4",
                ["reason"] = sockets.Failure?.Message ?? "",
            });
    }

    // ──────────────────────────────────────────────────────────────────────
    // MARK: - Timestamp parser (D101 parity — matches the cage CLI write format)
    // ──────────────────────────────────────────────────────────────────────

    /// <summary>Parse the cage CLI's record timestamp ("yyyy-MM-dd HH:mm:ss.fff" or without
    /// millis) using invariant culture so a non-Gregorian host locale does not break the
    /// classifier. Null on an unparseable stamp, so the record contributes no window sample
    /// rather than a fabricated one.</summary>
    public static DateTime? ParseTimestamp(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var withMs))
            return withMs;
        if (DateTime.TryParseExact(s, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var noMs))
            return noMs;
        return null;
    }
}

/// <summary>
/// Per-kind cooldown gate. The daemon calls <see cref="CaptureEventClassifier.ClassifyWindow"/>
/// once a second and would otherwise re-emit the same NXDomainFlood event every tick until the
/// flood subsides. The gate emits an event only when its kind has not been seen in the
/// cooldown window. Pure to the outside: feed it events, it returns the subset that should fire.
/// </summary>
public sealed class CaptureEventGate
{
    /// <summary>Default cooldown per kind. Tuned so a sustained flood surfaces once at the start
    /// of the episode and once on recovery — not every second for 30s. Tests pin these.</summary>
    public static readonly TimeSpan DefaultCooldown = TimeSpan.FromSeconds(30);

    private readonly Dictionary<CaptureEventKind, DateTime> _lastEmitted = new();
    private readonly TimeSpan _cooldown;

    public CaptureEventGate(TimeSpan? cooldown = null) => _cooldown = cooldown ?? DefaultCooldown;

    /// <summary>
    /// Returns the events from <paramref name="events"/> that should fire (their kind has not
    /// been seen in the cooldown). Updates internal state for the returned events. Pure to the
    /// outside — same input at the same instant produces the same output.
    /// </summary>
    public IReadOnlyList<CaptureEvent> Filter(IEnumerable<CaptureEvent> events, DateTime now)
    {
        var fire = new List<CaptureEvent>();
        foreach (var ev in events)
        {
            if (_lastEmitted.TryGetValue(ev.Kind, out var last) && (now - last) < _cooldown)
                continue;   // still in cooldown — suppress without inventing a "we are still in flood" event
            _lastEmitted[ev.Kind] = now;
            fire.Add(ev);
        }
        return fire;
    }

    /// <summary>For tests / diagnostics: the last-emitted instant for a kind. Null if never
    /// emitted.</summary>
    public DateTime? LastEmitted(CaptureEventKind kind) =>
        _lastEmitted.TryGetValue(kind, out var v) ? v : null;
}