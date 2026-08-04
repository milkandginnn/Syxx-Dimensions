using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace Syxx.Kit;

// A5 (ROADMAP "Implementation plan v2" — continuous monitoring). Turns Sentinel's snapshot/diff
// primitives into a monitoring TICK: compare this network's DNS-relevant configuration against the
// last known baseline, decide whether anything drifted, and raise alerts a user can act on.
//
// Built on the existing Trust/ primitives (NetworkConfigSnapshot.Capture, Sentinel.Diff,
// Sentinel.Save/LoadSnapshot) — this adds the decision + alert layer, not new capture logic.
//
// PURE/LIVE SPLIT (the A4 lesson): Evaluate() is pure and holds every honesty-relevant decision;
// TickAsync() only does the IO. The untestable part is kept as thin as possible.
//
// HONESTY CONTRACT (Protocol §1/§10):
//   * NO BASELINE IS NOT "STABLE". A first run has nothing to compare against — that is
//     `BaselineEstablished`, never `NoChange`. Reporting "no changes detected" when we have never
//     looked before is the monitoring equivalent of a false clean, and it is the single most
//     likely way this feature would lie.
//   * A drift is reported as a REASON TO RE-CHECK, not as a result. Sentinel sees configuration,
//     not tampering: a resolver change might be a coffee-shop DHCP lease or an attack, and this
//     layer must not pretend to know which. Only a re-run of the disciplines can say.
//   * Losing protection is escalated above gaining it: encrypted -> plaintext and VPN active ->
//     inactive are the transitions that expose a user, and they are Warning, not Info.
//
// Windows-parity twin owed on macOS (SyxxKit/Trust/ContinuousMonitor.swift).
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md:42).

/// What a monitoring tick concluded. `BaselineEstablished` is deliberately distinct from
/// `NoChange` — "we had nothing to compare against" must never render as "nothing changed".
public enum MonitorState { BaselineEstablished, NoChange, Changed }

/// A user-facing monitoring alert. Severity reuses the shared palette so a monitor alert and a
/// result badge cannot disagree about what amber means.
public sealed record MonitorAlert(Severity Severity, string Headline, string Detail);

/// The outcome of one tick.
public sealed record MonitorTick(
    MonitorState State,
    IReadOnlyList<ConfigChange> Changes,
    IReadOnlyList<MonitorAlert> Alerts,
    string Summary,
    bool RecheckRecommended);

public static class ContinuousMonitor
{
    /// The PURE decision: given the previous baseline (null on a first run) and the current
    /// snapshot, decide what drifted and what to tell the user. No IO, no clock, no network.
    public static MonitorTick Evaluate(NetworkConfigSnapshot? previous, NetworkConfigSnapshot current)
    {
        if (current is null) throw new ArgumentNullException(nameof(current));

        // Honesty gate — never report "no change" when there was nothing to compare against.
        if (previous is null)
        {
            return new MonitorTick(
                MonitorState.BaselineEstablished,
                Array.Empty<ConfigChange>(),
                Array.Empty<MonitorAlert>(),
                // C145: downgraded from "this network" — no network identity is recorded
                "Baseline recorded for your DNS configuration. Nothing has been compared yet — monitoring starts from now.",
                RecheckRecommended: false);
        }

        var changes = Sentinel.Diff(previous, current);
        if (changes.Count == 0)
        {
            return new MonitorTick(
                MonitorState.NoChange,
                changes,
                Array.Empty<MonitorAlert>(),
                // C145: downgraded from "this network's" — no network identity is recorded
                "No change to your DNS configuration since the last check.",
                RecheckRecommended: false);
        }

        var alerts = changes.Select(c => ToAlert(c, previous, current)).ToList();
        string what = string.Join("; ", changes.Select(c => c.Kind.Label()));
        return new MonitorTick(
            MonitorState.Changed,
            changes,
            alerts,
            // C145: downgraded from "This network's" — no network identity is recorded
            $"Your DNS configuration changed ({what}). A configuration change is not proof of tampering — re-run the checks to find out what it means.",
            RecheckRecommended: true);
    }

    /// Map a configuration change to an alert. A change that REMOVES protection is a warning; a
    /// change that adds it, or is merely different, is informational. Neither is a tamper result —
    /// Sentinel observes configuration, not honesty (that is what a re-check is for).
    private static MonitorAlert ToAlert(ConfigChange change, NetworkConfigSnapshot previous, NetworkConfigSnapshot current)
    {
        switch (change.Kind)
        {
            case ConfigChangeKind.EncryptedDNSChanged:
                bool lostEncryption = previous.DnsEncrypted && !current.DnsEncrypted;
                return new MonitorAlert(
                    lostEncryption ? Severity.Warning : Severity.Info,
                    // C145: downgraded from "on this network" — no network identity is recorded
                    lostEncryption
                        ? "Encrypted DNS is no longer in use."
                        : "Encrypted DNS is now in use.",
                    lostEncryption
                        ? "Your lookups are now visible to the network. Re-check, and turn encrypted DNS back on if you did not intend this."
                        : change.Detail);

            case ConfigChangeKind.VpnChanged:
                bool lostVpn = previous.VpnActive == ConditionState.Yes && current.VpnActive != ConditionState.Yes;
                return new MonitorAlert(
                    lostVpn ? Severity.Warning : Severity.Info,
                    lostVpn ? "Your VPN is no longer active." : "A VPN is now active.",
                    lostVpn
                        ? "Traffic that you may expect to be tunnelled is not. Re-check for leaks before trusting this connection."
                        : change.Detail);

            case ConfigChangeKind.ResolversChanged:
                return new MonitorAlert(
                    Severity.Warning,
                    // C145: downgraded from "on this network" — no network identity is recorded
                    "The DNS resolvers changed.",
                    $"{change.Detail}. This is normal when you join a new network, and it is also what a hijack looks like — re-run the checks to tell the difference.");

            default:
                return new MonitorAlert(Severity.Info, change.Kind.Label(), change.Detail);
        }
    }

    /// One live tick: load the stored baseline, capture the current configuration, decide, and
    /// persist the new baseline. IO only — the decision is Evaluate().
    /// <param name="persist">false to evaluate without moving the baseline (a dry run).</param>
    public static MonitorTick TickOnce(string? snapshotPath = null, bool persist = true)
    {
        // D081: Distinguish "corrupt file" from "no file" — a corrupt baseline must not be
        // silently treated as "first run" (which would overwrite the damaged file).
        var (loadResult, previous) = Sentinel.LoadSnapshot(snapshotPath);
        var current = NetworkConfigSnapshot.Capture();
        if (loadResult == AnswerLedger.TrustLoadResult.Corrupted)
        {
            // C144: Do NOT overwrite the corrupt baseline — it may be recoverable, or may be
            // evidence of tampering. Move it aside, then write a fresh baseline only after.
            if (persist)
            {
                var corruptPath = (snapshotPath ?? Sentinel.DefaultPath) + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                try { System.IO.File.Move(snapshotPath ?? Sentinel.DefaultPath, corruptPath); } catch { /* best-effort */ }
                Sentinel.SaveSnapshot(current, snapshotPath);
            }
            return new MonitorTick(
                MonitorState.BaselineEstablished,
                Array.Empty<ConfigChange>(),
                new[] { new MonitorAlert(Severity.Warning, "Baseline file corrupted",
                    $"The saved baseline ({snapshotPath ?? Sentinel.DefaultPath}) could not be read. " +
                    "The damaged file has been moved aside and a fresh baseline saved; previous configuration history may be recoverable from the .corrupt file.") },
                "Prior baseline was corrupted. Re-check recommended to confirm current configuration.",
                RecheckRecommended: true);
        }
        var tick = Evaluate(previous, current);
        // C153: Do NOT roll the baseline forward on a Changed tick. Keep comparing against the
        // last known-good snapshot so the alert persists across ticks until acknowledged.
        if (persist && tick.State != MonitorState.Changed)
            Sentinel.SaveSnapshot(current, snapshotPath);
        return tick;
    }

    // C136: macOS-parity sample domains — lightweight A-record lookups whose answers are
    // recorded in the AnswerLedger so that per-network DNS history and drift alerts work.
    private static readonly string[] SampleDomains = { "apple.com", "google.com", "cloudflare.com" };

    /// Async tick that also performs DNS sampling and records answers in the AnswerLedger,
    /// matching macOS ContinuousMonitor.tickOnce. The sync TickOnce is kept for backward
    /// compatibility but does not sample DNS.
    public static async Task<MonitorTick> TickOnceAsync(string? snapshotPath = null, bool persist = true)
    {
        var (loadResult, previous) = Sentinel.LoadSnapshot(snapshotPath);
        var current = NetworkConfigSnapshot.Capture();
        if (loadResult == AnswerLedger.TrustLoadResult.Corrupted)
        {
            // C144: Do NOT overwrite the corrupt baseline — move it aside first.
            if (persist)
            {
                var corruptPath = (snapshotPath ?? Sentinel.DefaultPath) + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                try { System.IO.File.Move(snapshotPath ?? Sentinel.DefaultPath, corruptPath); } catch { /* best-effort */ }
                Sentinel.SaveSnapshot(current, snapshotPath);
            }
            return new MonitorTick(
                MonitorState.BaselineEstablished,
                Array.Empty<ConfigChange>(),
                new[] { new MonitorAlert(Severity.Warning, "Baseline file corrupted",
                    $"The saved baseline ({snapshotPath ?? Sentinel.DefaultPath}) could not be read. " +
                    "The damaged file has been moved aside and a fresh baseline saved; previous configuration history may be recoverable from the .corrupt file.") },
                "Prior baseline was corrupted. Re-check recommended to confirm current configuration.",
                RecheckRecommended: true);
        }

        // C136: perform DNS sampling — query each sample domain against the first system
        // resolver and record the answer in the AnswerLedger. Drift alerts are raised when
        // an answer changes between ticks.
        var driftAlerts = await SampleDnsAndRecord(current.Resolvers);

        var tick = Evaluate(previous, current);

        // Merge DNS drift alerts into the tick's alerts.
        if (driftAlerts.Count > 0)
        {
            var allAlerts = tick.Alerts.ToList();
            allAlerts.AddRange(driftAlerts);
            string what = string.Join("; ", driftAlerts.Select(a => a.Headline));
            tick = tick with
            {
                Alerts = allAlerts,
                Summary = tick.Summary + $" DNS answer drift detected: {what}",
                RecheckRecommended = true
            };
        }

        // C153: Do NOT roll the baseline forward on a Changed tick.
        if (persist && tick.State != MonitorState.Changed)
            Sentinel.SaveSnapshot(current, snapshotPath);
        return tick;
    }

    /// Query sample domains against the first system resolver and record answers in the
    /// AnswerLedger. Returns any drift alerts.
    private static async Task<List<MonitorAlert>> SampleDnsAndRecord(List<string> resolvers)
    {
        var alerts = new List<MonitorAlert>();
        if (resolvers.Count == 0) return alerts;

        var resolver = resolvers[0];
        var ledger = new AnswerLedger();
        ledger.Load();

        foreach (var domain in SampleDomains)
        {
            try
            {
                var response = await DNSResolver.QueryUDPDetailedAsync(
                    new DNSQuery(name: domain, type: 1, server: resolver));
                if (response.DidFail) continue;
                foreach (var record in response.Records)
                {
                    var drift = ledger.Record(domain, "A", record.Rdata, resolver, (int)record.Ttl);
                    if (drift != null)
                    {
                        alerts.Add(new MonitorAlert(
                            Severity.Warning,
                            $"DNS answer changed for {domain}",
                            $"Previous: {drift.PreviousAnswer}, new: {drift.NewAnswer} (resolver: {drift.Resolver})"));
                    }
                }
            }
            catch { /* sampling failure — not evidence of anything */ }
        }

        try { ledger.Save(); } catch { /* persistence failure — not evidence of anything */ }
        return alerts;
    }
}
