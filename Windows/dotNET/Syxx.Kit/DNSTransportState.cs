using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Syxx.Kit;

// MARK: - DNS transport state (the third question) — Windows/Linux twin of
// macOS/Sources/SyxxKit/DNSTransportState.swift. The determination rules are identical on all
// three platforms; only the observers that feed them differ.
//
// Syxx asks three DIFFERENT questions about DNS transport. They can legitimately disagree, and a
// disagreement is itself a finding. Collapsing them is how the app came to display a transport it
// had never observed.
//
//   Q1 CONFIGURED  — NetworkKeyState.EncryptedDNSStatus() — inference from OS resolver config.
//   Q2 REACHABLE   — EncryptedTransportProbe.Probe() — a live DoH query to a public endpoint.
//   Q3 OBSERVED    — THIS FILE. Are MY DNS queries actually leaving this machine encrypted?
//
// HONESTY SPINE, enforced structurally:
//   • Two independent tri-states, never a bool and never one collapsed value. A tunnel being up is
//     NOT evidence that DNS is encrypted.
//   • Silence is not evidence: a window with no observed DNS activity is Undetermined.
//   • No observer, no privilege, no sample => Undetermined.
//   • A sample is readable ONLY through Reading(...), which requires a clock, so a stale value
//     cannot be rendered as current.
//   • Confirming cleartext needs one observation. Confirming encrypted needs full window coverage
//     plus affirmative encrypted-DNS activity plus zero cleartext.

/// A three-valued signal. Never a bool: every observation path here fails routinely, and a bool has
/// nowhere to put those but false, which would read as "not encrypted".
public enum TransportSignal
{
    ConfirmedTrue,
    ConfirmedFalse,
    Undetermined
}

/// Why a signal holds the value it holds. Machine-readable codes, not display copy.
public enum TransportReason
{
    CleartextQueryObserved,
    EncryptedTransportObserved,
    TunnelInterfaceActive,
    NoTunnelInterfaceActive,
    NotYetObserved,
    ObserverUnavailable,
    ObservationFailed,
    NoDnsActivityObserved,
    DnsActivityTransportUnobservable,
    SampleExpired,
    NetworkChanged,
    IncompleteWindowCoverage,
    StateChangedDuringWindow
}

public enum TransportObservationMethod
{
    /// Definitive tier: DNS packets observed on the wire (requires privilege).
    PacketObservation,
    /// Corroborating tier: process-attributed socket/flow deltas (no privilege). Cannot see the
    /// peer of an unconnected UDP socket, so it cannot confirm or exclude cleartext UDP/53.
    FlowObservation,
    /// Interface enumeration — the VPN signal only.
    InterfaceEnumeration,
    None
}

/// What was observed, in counts. Carries no domain names and no packet payload.
public sealed record TransportEvidence(
    TransportObservationMethod Method,
    int CleartextOffHost = 0,
    int EncryptedDns = 0,
    int CleartextLoopback = 0,
    int TransportUnobservable = 0,
    IReadOnlyList<string>? Interfaces = null,
    string Detail = "")
{
    public static readonly TransportEvidence None =
        new(TransportObservationMethod.None, Detail: "no observation");

    /// True when this evidence records no observation at all. A state with no evidence is
    /// undetermined by definition.
    public bool IsEmpty =>
        Method == TransportObservationMethod.None
        || (CleartextOffHost == 0 && EncryptedDns == 0
            && CleartextLoopback == 0 && TransportUnobservable == 0);
}

/// Monotonic timestamp source for staleness checks.
///
/// Wall-clock time is manipulable: moving the clock backwards would make an arbitrarily old sample
/// look fresh. Stopwatch.GetTimestamp() is the platform's monotonic counter and cannot be set.
public static class MonotonicClock
{
    public static ulong NowNanos()
    {
        double ticks = Stopwatch.GetTimestamp();
        return (ulong)(ticks / Stopwatch.Frequency * 1_000_000_000d);
    }
}

/// One observation tick. Two independent signals, each with its own evidence.
///
/// Deliberately NOT modelled as a single enum of (dns / vpn / both): that shape cannot express
/// "undetermined" per signal and collapses the distinction the moment one cannot be observed.
public sealed record DNSTransportSample(
    TransportSignal DnsEncryption,
    TransportReason DnsReason,
    TransportEvidence DnsEvidence,
    TransportSignal Vpn,
    TransportReason VpnReason,
    TransportEvidence VpnEvidence,
    DateTime ObservedAt,
    ulong ObservedAtMonotonicNanos,
    double ValidForSeconds,
    string NetworkFingerprint)
{
    /// Age in seconds, or null when `now` precedes the observation — impossible for a monotonic
    /// clock, and therefore a reading that is not trustworthy.
    public double? Age(ulong nowMonotonicNanos)
        => nowMonotonicNanos >= ObservedAtMonotonicNanos
            ? (nowMonotonicNanos - ObservedAtMonotonicNanos) / 1_000_000_000d
            : (double?)null;

    /// Resolve this sample against the current clock and network identity. A stale sample NEVER
    /// returns its last value.
    public DNSTransportReading Reading(ulong nowMonotonicNanos, string currentFingerprint)
    {
        var age = Age(nowMonotonicNanos);
        if (age is null) return DNSTransportReading.Undetermined(TransportReason.SampleExpired);
        if (age > ValidForSeconds) return DNSTransportReading.Undetermined(TransportReason.SampleExpired);
        if (currentFingerprint != NetworkFingerprint)
            return DNSTransportReading.Undetermined(TransportReason.NetworkChanged);
        return new DNSTransportReading(DnsEncryption, DnsReason, DnsEvidence,
                                       Vpn, VpnReason, VpnEvidence, ObservedAt, age);
    }
}

/// The ONLY way to read a sample's signals. Requires a clock and the current network fingerprint.
public sealed record DNSTransportReading(
    TransportSignal DnsEncryption,
    TransportReason DnsReason,
    TransportEvidence DnsEvidence,
    TransportSignal Vpn,
    TransportReason VpnReason,
    TransportEvidence VpnEvidence,
    DateTime? ObservedAt,
    double? Age)
{
    public static DNSTransportReading Undetermined(TransportReason reason)
        => new(TransportSignal.Undetermined, reason, TransportEvidence.None,
               TransportSignal.Undetermined, reason, TransportEvidence.None, null, null);

    /// Menubar/tray glyph convenience: animates when EITHER signal is confirmed true, so a
    /// third-party VPN doing the encrypting also drives it. This either-or is correct for the glyph
    /// and ONLY for the glyph — Prophet, Mirror and the dashboard read the two signals separately.
    /// Undetermined stops the animation; it never continues the last state.
    public bool GlyphShouldAnimate =>
        DnsEncryption == TransportSignal.ConfirmedTrue || Vpn == TransportSignal.ConfirmedTrue;
}

/// The transport state AS IT WAS during a module's scan window. Prophet and Mirror read this, not
/// the live value at redraw: a pane showing the current transport next to results gathered under a
/// different one is a fabricated correlation.
///
/// A SHARED INPUT, not a shared decision. Prophet asks a symmetric peer-consensus question; Mirror
/// asks an asymmetric local-versus-baseline question. Each draws its own conclusion from these
/// facts; nothing routes them through one decision function.
public sealed record DNSTransportWindow(
    TransportSignal DnsEncryption,
    TransportReason DnsReason,
    TransportSignal Vpn,
    TransportReason VpnReason,
    double Coverage,
    int SampleCount,
    int ExpectedSampleCount,
    DateTime Start,
    DateTime End,
    int CleartextOffHost = 0,
    int EncryptedDns = 0,
    int TransportUnobservable = 0)
{
    public static DNSTransportWindow Unobserved(DateTime start, DateTime end, int expected,
                                                TransportReason reason = TransportReason.NotYetObserved)
        => new(TransportSignal.Undetermined, reason, TransportSignal.Undetermined, reason,
               0, 0, expected, start, end);
}

/// One slot of the dashboard time series. A slot with no sample is Undetermined and MUST render
/// visibly distinct from cleartext — a gap is not a measurement. Gaps are emitted as values in the
/// array, not as missing elements, so a renderer cannot interpolate across one without noticing.
public sealed record DNSTransportSlot(
    int Index,
    TransportSignal DnsEncryption,
    TransportReason DnsReason,
    TransportSignal Vpn,
    TransportReason VpnReason,
    DateTime? ObservedAt)
{
    public bool IsGap => ObservedAt is null;
}

/// The pure determination: evidence in, tri-states out. Free of I/O so every branch — including
/// every undetermined branch — is exhaustively testable, and so the same rules govern the
/// privileged and unprivileged observers.
public static class DNSTransportDetermination
{
    public static (TransportSignal, TransportReason) ClassifyDns(
        TransportEvidence? evidence, bool observerAvailable, bool observationSucceeded)
    {
        if (!observerAvailable)
            return (TransportSignal.Undetermined, TransportReason.ObserverUnavailable);
        if (!observationSucceeded || evidence is null)
            return (TransportSignal.Undetermined, TransportReason.ObservationFailed);

        // Cleartext dominates: one observed cleartext query answers the user's question with a "no"
        // for at least some of their traffic — the same all-must-be-encrypted rule D056 applies to
        // the configured question.
        if (evidence.CleartextOffHost > 0)
            return (TransportSignal.ConfirmedFalse, TransportReason.CleartextQueryObserved);

        // Encrypted requires AFFIRMATIVE evidence. "We saw no cleartext" is not evidence of
        // encryption; it is equally consistent with no DNS having happened.
        if (evidence.EncryptedDns > 0)
            return (TransportSignal.ConfirmedTrue, TransportReason.EncryptedTransportObserved);

        if (evidence.TransportUnobservable > 0 || evidence.CleartextLoopback > 0)
            return (TransportSignal.Undetermined, TransportReason.DnsActivityTransportUnobservable);

        return (TransportSignal.Undetermined, TransportReason.NoDnsActivityObserved);
    }

    /// Map the existing VPN tri-state onto the published signal without flattening it.
    public static (TransportSignal, TransportReason) ClassifyVpn(ConditionState state) => state switch
    {
        ConditionState.Yes => (TransportSignal.ConfirmedTrue, TransportReason.TunnelInterfaceActive),
        ConditionState.No => (TransportSignal.ConfirmedFalse, TransportReason.NoTunnelInterfaceActive),
        _ => (TransportSignal.Undetermined, TransportReason.ObservationFailed)
    };

    /// Aggregate samples into the window a module reads.
    ///
    /// Asymmetric on purpose:
    ///   • ConfirmedFalse needs ONE sample that saw cleartext — a gap cannot un-observe a query.
    ///   • ConfirmedTrue needs FULL coverage, at least one sample with affirmative encrypted-DNS
    ///     activity, and zero cleartext. A gap could hide a cleartext query.
    ///   • Samples that disagree mean the state changed mid-window; reporting either value beside
    ///     whole-window results would be a fabricated correlation.
    public static DNSTransportWindow Summarize(IReadOnlyList<DNSTransportSample> samples,
                                               int expectedSampleCount, DateTime start, DateTime end)
    {
        if (samples.Count == 0)
            return DNSTransportWindow.Unobserved(start, end, expectedSampleCount);

        double coverage = Math.Min(1.0, samples.Count / (double)Math.Max(1, expectedSampleCount));
        int cleartext = samples.Sum(s => s.DnsEvidence.CleartextOffHost);
        int encrypted = samples.Sum(s => s.DnsEvidence.EncryptedDns);
        int unobservable = samples.Sum(s => s.DnsEvidence.TransportUnobservable);

        TransportSignal dnsSignal;
        TransportReason dnsReason;
        if (samples.Any(s => s.DnsEncryption == TransportSignal.ConfirmedFalse))
        {
            dnsSignal = TransportSignal.ConfirmedFalse;
            dnsReason = TransportReason.CleartextQueryObserved;
        }
        else if (coverage < 1.0)
        {
            dnsSignal = TransportSignal.Undetermined;
            dnsReason = TransportReason.IncompleteWindowCoverage;
        }
        else if (samples.All(s => s.DnsEncryption == TransportSignal.ConfirmedTrue))
        {
            dnsSignal = TransportSignal.ConfirmedTrue;
            dnsReason = TransportReason.EncryptedTransportObserved;
        }
        else if (samples.Any(s => s.DnsEncryption == TransportSignal.ConfirmedTrue))
        {
            // Some ticks confirmed encrypted, others were undetermined: the undetermined ticks may
            // have carried cleartext we did not see. Not "mostly encrypted" — undetermined.
            dnsSignal = TransportSignal.Undetermined;
            dnsReason = TransportReason.StateChangedDuringWindow;
        }
        else
        {
            dnsSignal = TransportSignal.Undetermined;
            dnsReason = samples[0].DnsReason;
        }

        // VPN: unanimity or nothing. A tunnel that came up or dropped mid-scan must not be reported
        // as a stable condition of the scan.
        var vpnStates = samples.Select(s => s.Vpn).Distinct().ToList();
        TransportSignal vpnSignal;
        TransportReason vpnReason;
        if (vpnStates.Count > 1)
        {
            vpnSignal = TransportSignal.Undetermined;
            vpnReason = TransportReason.StateChangedDuringWindow;
        }
        else
        {
            vpnSignal = vpnStates[0];
            vpnReason = samples[0].VpnReason;
        }

        return new DNSTransportWindow(dnsSignal, dnsReason, vpnSignal, vpnReason,
                                      coverage, samples.Count, expectedSampleCount, start, end,
                                      cleartext, encrypted, unobservable);
    }
}

/// Bounded, thread-safe store of observation samples. Feeds all four consumers from one observation
/// so no module re-derives its own transport state. Nothing is written to disk by this type.
public sealed class DNSTransportStateStore
{
    /// Observation cadence, per the owner's spec.
    public const double SampleIntervalSeconds = 5;
    /// A sample may be treated as current for three intervals; past that it is SampleExpired, not
    /// its last value.
    public const double ValidityWindowSeconds = 15;
    /// Graph retention: 5 minutes at a 5 s cadence = 60 slots.
    public const double RetentionSeconds = 300;
    public const int MaxSamples = 120;

    private readonly object _lock = new();
    private readonly List<DNSTransportSample> _samples = new();
    private ulong _changeToken;

    public void Record(DNSTransportSample sample)
    {
        lock (_lock)
        {
            _samples.Add(sample);
            if (_samples.Count > MaxSamples) _samples.RemoveRange(0, _samples.Count - MaxSamples);
            ulong retentionNanos = (ulong)(RetentionSeconds * 1_000_000_000);
            if (sample.ObservedAtMonotonicNanos > retentionNanos)
            {
                ulong cutoff = sample.ObservedAtMonotonicNanos - retentionNanos;
                _samples.RemoveAll(s => s.ObservedAtMonotonicNanos < cutoff);
            }
            _changeToken++;
        }
    }

    /// Increments on every recorded sample, so a consumer that caches a reading can tell whether it
    /// is still the one the store holds.
    public ulong Token { get { lock (_lock) return _changeToken; } }

    public DNSTransportReading Current(ulong nowMonotonicNanos, string networkFingerprint)
    {
        DNSTransportSample? latest;
        lock (_lock) latest = _samples.Count > 0 ? _samples[^1] : null;
        return latest is null
            ? DNSTransportReading.Undetermined(TransportReason.NotYetObserved)
            : latest.Reading(nowMonotonicNanos, networkFingerprint);
    }

    /// The dashboard series: one slot per interval ending at `now`, gaps included as explicit
    /// undetermined slots.
    public IReadOnlyList<DNSTransportSlot> Timeline(ulong nowMonotonicNanos,
                                                    string networkFingerprint, int slots = 60)
    {
        List<DNSTransportSample> snapshot;
        lock (_lock) snapshot = new List<DNSTransportSample>(_samples);

        ulong intervalNanos = (ulong)(SampleIntervalSeconds * 1_000_000_000);
        var output = new List<DNSTransportSlot>(slots);
        for (int i = 0; i < slots; i++)
        {
            ulong slotsBack = (ulong)(slots - i);
            if (nowMonotonicNanos < slotsBack * intervalNanos)
            {
                output.Add(Gap(i, TransportReason.NotYetObserved));
                continue;
            }
            ulong lower = nowMonotonicNanos - slotsBack * intervalNanos;
            ulong upper = lower + intervalNanos;
            var inSlot = snapshot.LastOrDefault(s => s.ObservedAtMonotonicNanos >= lower
                                                     && s.ObservedAtMonotonicNanos < upper);
            if (inSlot is null)
            {
                output.Add(Gap(i, TransportReason.NotYetObserved));
                continue;
            }
            if (inSlot.NetworkFingerprint != networkFingerprint)
            {
                output.Add(Gap(i, TransportReason.NetworkChanged));
                continue;
            }
            output.Add(new DNSTransportSlot(i, inSlot.DnsEncryption, inSlot.DnsReason,
                                            inSlot.Vpn, inSlot.VpnReason, inSlot.ObservedAt));
        }
        return output;

        static DNSTransportSlot Gap(int index, TransportReason reason)
            => new(index, TransportSignal.Undetermined, reason,
                   TransportSignal.Undetermined, reason, null);
    }

    /// The transport state as it was across a module's scan window, for Prophet and Mirror.
    public DNSTransportWindow Window(ulong startMonotonicNanos, ulong endMonotonicNanos,
                                     DateTime startWall, DateTime endWall, string networkFingerprint)
    {
        List<DNSTransportSample> snapshot;
        lock (_lock) snapshot = new List<DNSTransportSample>(_samples);

        var covering = snapshot
            .Where(s => s.ObservedAtMonotonicNanos >= startMonotonicNanos
                        && s.ObservedAtMonotonicNanos <= endMonotonicNanos
                        && s.NetworkFingerprint == networkFingerprint)
            .ToList();
        double span = endMonotonicNanos >= startMonotonicNanos
            ? (endMonotonicNanos - startMonotonicNanos) / 1_000_000_000d
            : 0;
        int expected = Math.Max(1, (int)Math.Ceiling(span / SampleIntervalSeconds));
        return DNSTransportDetermination.Summarize(covering, expected, startWall, endWall);
    }
}
