using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;

namespace Syxx.Kit;

// Windows-parity twin of macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift.

public sealed record SectionEvidence(SectionStatus Status, IReadOnlyList<EvidenceItem> Evidence, IReadOnlyList<Advisory> Advisories, double Completeness = 1.0,
    int? DatapointsObserved = null, int? DatapointsExpected = null)
{
    // Clamp completeness to [0,1] at construction time, matching macOS Synthesizer.swift:12-17
    public double Completeness { get; init; } = Math.Max(0.0, Math.Min(1.0, Completeness));
}

/// Optional per-section results; a null field means the section was not run (-> Untested).
public sealed class SectionInputs
{
    public DNSResponse? Resolution { get; set; }
    public DNSSECStatus? Dnssec { get; set; }
    public DNSCanaries.CanaryResult? NxCanary { get; set; }
    public DNSCanaries.CanaryResult? DnssecCanary { get; set; }
    public ComparisonResult? Comparison { get; set; }
    /// The authoritative query context for the Stage-5 answer matrix — the domain/type Mirror
    /// actually queried; when null, Conclude() falls back to deriving from the first observed record.
    public string? TargetDomain { get; set; }
    public ushort? TargetQueryType { get; set; }
    /// LEGACY warp result (retired proxy path) — kept as a compile shim for the Mirror View surface
    /// (owned by the UI stream). No longer populated; read <see cref="WarpManifest"/>.
    public WarpResult? Warp { get; set; }
    /// WARP_MANIFEST_V1 signed-manifest content-integrity result (the live warp result).
    public WarpManifestResult? WarpManifest { get; set; }
    public NetworkStateReport? Network { get; set; }
    public LeakTestResult? Leak { get; set; }
    public IReadOnlyList<ExfilAssessment>? Exfil { get; set; }
    /// C058 — fraction (0..1) of Cage capture records that carried a real process identity, or null
    /// when this was not a capture-sourced run. Threaded to EvidenceAdapters.Cage(attributionRate:)
    /// by Conclude so a mostly-unattributed capture is Degraded, never a silent clean.
    public double? CageAttributionRate { get; set; }
    public IReadOnlyList<AnswerDrift>? Drifts { get; set; }
    public IReadOnlyList<ConfigChange>? ConfigChanges { get; set; }
    // Workstream H datapoints
    public ResolverLatency? Latency { get; set; }
    public AnswerStability? Stability { get; set; }
    public WildcardSynthesis? WildcardSynthesis { get; set; }
    public DenialOfExistence? Denial { get; set; }
    public DualStackAudit? DualStack { get; set; }
    public EDNS0Capability? Edns0 { get; set; }
    public VersionBindResult? VersionBind { get; set; }
    public StubEntropyAudit? StubEntropy { get; set; }
    public AuthoritativeComparison? Authoritative { get; set; }
    public SVCBFinding? Svcb { get; set; }
    public TTLAnomaly? TtlAnomaly { get; set; }
    public CNAMEChainAudit? CnameChain { get; set; }
    public EncryptedTransport? EncryptedTransport { get; set; }
    public OpenResolverFinding? OpenResolver { get; set; }
    public ECSLeak? Ecs { get; set; }
    public ResponseSizeAudit? ResponseSize { get; set; }
    /// Mirror · A2 server corroboration (Pro, additive).
    public GroundTruth? ServerGroundTruth { get; set; }
    /// B174: true when the system resolver was undetectable and a public substitute (e.g. 8.8.8.8)
    /// was used instead. Under substitution Mirror's local-versus-public comparison degenerates —
    /// local IS public — so `Conclude` caps the Mirror section at Degraded and the outcome can
    /// never be clean on that evidence.
    ///
    /// F-01 (2026-08-05): this field and its cap existed only on the Swift side. Windows and Linux
    /// had no substituted-resolver input and therefore no cap, so the same run could return a clean
    /// Mirror section from evidence macOS treats as too degenerate to conclude from. Twin of
    /// `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:82`.
    public bool ResolverSubstituted { get; set; }
    /// The OBSERVED DNS-transport state during this section's own query window — the third
    /// transport question, distinct from the configured one (`Network.EncryptedDNS`) and the
    /// reachability one (`EncryptedTransport`). The three can legitimately disagree and a
    /// disagreement is itself a finding. `null` means not observed for this run, which is
    /// undetermined — never "cleartext".
    ///
    /// F-02 (2026-08-05): added for structural parity with
    /// `macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:67`. **Neither platform reads it yet** —
    /// it is carried, not consumed, on macOS too, so no conclusion depends on it today. The field
    /// is mirrored now precisely because the first code that starts reading it on one side would
    /// otherwise create a silent behavioral divergence.
    public DNSTransportWindow? TransportObservation { get; set; }
}

public static class EvidenceAdapters
{
    private static bool DidFail(DNSResponse r) =>
        r.Status is DNSResponseStatus.Timeout or DNSResponseStatus.TransportError
                 or DNSResponseStatus.MalformedResponse or DNSResponseStatus.ResponseIdMismatch;

    /// <summary>
    /// Ph3.2: real per-section datapoint completeness N/M. M is the section's full datapoint
    /// catalog (the builder's probe slots); N is the slots that produced a CONCLUSIVE reading
    /// (Negative / Positive / Degraded). A probe that never ran, returned null, or read
    /// Inconclusive/Invalidated counts toward M only — so a 4-clean + 1-timeout run reads 0.8,
    /// never the inert default 1.0, and the count-based clean-support scaling in
    /// SynthesisEngine (clean mass × completeness) actually engages.
    /// </summary>
    internal static double DatapointCompleteness(IReadOnlyList<SectionStatus> subs, int totalDatapoints)
    {
        if (totalDatapoints <= 0) return 0.0;
        int usable = subs.Count(s => s is SectionStatus.Negative or SectionStatus.Positive or SectionStatus.Degraded);
        return Math.Min(1.0, (double)usable / totalDatapoints);
    }

    /// <summary>
    /// B003: common probe-evaluation pattern — null-guard + status classification → subs/evidence.
    /// Eliminates the duplicated switch(probe.Status) boilerplate that appeared across every
    /// section adapter (Prophet, Mirror, Portal, Glutton, Cage). The <paramref name="classify"/>
    /// function maps a non-null probe to its (SectionStatus, optional EvidenceItem) outcome.
    /// </summary>
    private static void Evaluate<T>(T? probe,
        Func<T, (SectionStatus Status, EvidenceItem? Evidence)> classify,
        List<EvidenceItem> evidence, List<SectionStatus> subs) where T : class
    {
        if (probe is null) return;
        var (status, ev) = classify(probe);
        if (ev is not null) evidence.Add(ev);
        subs.Add(status);
    }

    // Datapoint-catalog sizes (M) per section — MUST track the corresponding builder's probe
    // slots below and the macOS Synthesizer.swift twins. Portal's openResolver is advisory-only
    // (never a result datapoint) and Mirror's serverGroundTruth corroborates `authoritative`
    // rather than being its own slot, so neither counts.
    /// C1 — true only when both strings are IPv4 dotted-quads sharing their first three octets.
    /// Non-IPv4 input is never "same subnet" (the caller skips IPv6 separately).
    internal static bool Ipv4SameSlash24(string a, string b)
    {
        var pa = a.Split('.'); var pb = b.Split('.');
        if (pa.Length != 4 || pb.Length != 4) return false;
        return pa[0] == pb[0] && pa[1] == pb[1] && pa[2] == pb[2];
    }

    // B173: exclude versionBind (tilt-only — emits EvidenceItem but never appends a subs entry)
    // and stubEntropy (capture-sourced — absent from every non-capture run). Counting them in M
    // structurally capped Prophet completeness at 10/12 = 0.833 for the CLI/GUI path,
    // systematically under-stating every Prophet-derived indicator's weight.
    internal const int ProphetDatapointCatalog = 10; // resolution, dnssec, nxCanary, dnssecCanary, latency, stability, wildcard, denial, dualStack, edns0
    internal const int MirrorDatapointCatalog = 6;   // comparison, warp, authoritative, svcb, ttlAnomaly, cnameChain
    internal const int PortalDatapointCatalog = 4;   // captivePortal, encryptedDNS, encryptedTransport, gateway (C1)
    internal const int GluttonDatapointCatalog = 2;  // leak, ecs

    // B309: delegate to CombineWithEvidence to eliminate duplicated precedence logic.
    // A single source of truth prevents silent divergence between the two methods.
    internal static SectionEvidence Combine(IReadOnlyList<SectionStatus> subs, IReadOnlyList<Advisory>? advisories = null, double completeness = 1.0, int? datapointsExpected = null)
        => CombineWithEvidence(new List<EvidenceItem>(), subs.ToList(), advisories?.ToList(), completeness, datapointsExpected);

    internal static SectionEvidence CombineWithEvidence(List<EvidenceItem> ev, List<SectionStatus> subs, List<Advisory>? advisories = null, double completeness = 1.0, int? datapointsExpected = null)
    {
        advisories ??= new List<Advisory>();
        int? dpObserved = datapointsExpected is null ? null
            : subs.Count(s => s is SectionStatus.Negative or SectionStatus.Positive or SectionStatus.Degraded);
        // C198: check Invalidated BEFORE Positive — a forged/invalidated packet must not launder
        // clean through a coincident Positive (parity with macOS Synthesizer.swift:78-79).
        var ran = subs.Where(s => s != SectionStatus.Untested).ToList();
        if (ran.Count == 0) return new SectionEvidence(SectionStatus.Untested, ev, advisories, 0.0, dpObserved, datapointsExpected);
        if (ran.Contains(SectionStatus.Invalidated)) return new SectionEvidence(SectionStatus.Invalidated, ev, advisories, completeness, dpObserved, datapointsExpected);
        if (subs.Contains(SectionStatus.Positive)) return new SectionEvidence(SectionStatus.Positive, ev, advisories, completeness, dpObserved, datapointsExpected);
        if (ran.Contains(SectionStatus.Inconclusive)) return new SectionEvidence(SectionStatus.Inconclusive, ev, advisories, completeness, dpObserved, datapointsExpected);
        if (ran.Contains(SectionStatus.Degraded)) return new SectionEvidence(SectionStatus.Degraded, ev, advisories, completeness, dpObserved, datapointsExpected);
        return new SectionEvidence(SectionStatus.Negative, ev, advisories, completeness, dpObserved, datapointsExpected);
    }

    public static SectionEvidence Prophet(DNSResponse? resolution, DNSSECStatus? dnssec,
        DNSCanaries.CanaryResult? nxCanary, DNSCanaries.CanaryResult? dnssecCanary,
        ResolverLatency? latency = null, AnswerStability? stability = null,
        WildcardSynthesis? wildcardSynthesis = null, DenialOfExistence? denial = null,
        DualStackAudit? dualStack = null, EDNS0Capability? edns0 = null,
        VersionBindResult? versionBind = null, StubEntropyAudit? stubEntropy = null)
    {
        var ev = new List<EvidenceItem>();
        var subs = new List<SectionStatus>();

        // Original fields
        if (resolution is not null)
        {
            if (DidFail(resolution)) subs.Add(SectionStatus.Invalidated);
            else if (resolution.Status is DNSResponseStatus.NXDomain or DNSResponseStatus.Refused or DNSResponseStatus.ServFail)
            {
                    ev.Add(EvidenceItem.Create(Section.Prophet,
                    EvidenceId.ResolverStatus,
                    $"Resolver returned {resolution.Status} for the target domain",
                    "A refusal / failure / NXDOMAIN can be a block — ambiguous alone; corroborate with the NXDOMAIN canary."));
                subs.Add(SectionStatus.Inconclusive);
            }
            else subs.Add(SectionStatus.Negative);
        }
        if (dnssec is DNSSECStatus d)
        {
            switch (d)
            {
                case DNSSECStatus.SignatureExpiredOrNotYetValid:
                    // B050: an expired / not-yet-valid RRSIG on the TARGET is ambiguous — replay OR a
                    // benign broken/misconfigured signer — so it is a run-quality caveat (Degraded),
                    // never a standalone tampering finding (Positive). It lowers confidence and blocks an
                    // unearned clean, but cannot NAME a threat alone. The B047 control-canary escalation
                    // is untouched: a known-signed CONTROL returning unsigned (DnssecCanary) is on-path
                    // DNSSEC stripping and stays Positive.
                    ev.Add(EvidenceItem.Create(Section.Prophet,
                        EvidenceId.DnssecExpired,
                        "DNSSEC signatures for the target are expired / not-yet-valid",
                        "Stale RRSIGs can indicate replay or a broken / interfering signer."));
                    subs.Add(SectionStatus.Degraded);
                    break;
                case DNSSECStatus.RecordsPresentUnverified: subs.Add(SectionStatus.Negative); break;
                default: subs.Add(SectionStatus.Inconclusive); break;
            }
        }
        foreach (var c in new[] { nxCanary, dnssecCanary }.Where(x => x is not null).Cast<DNSCanaries.CanaryResult>())
        {
            if (c.Evidence is not null) ev.Add(c.Evidence);
            subs.Add(c.Status);
        }

        // Workstream H: latency — B193: adopt macOS mapping. Slow/Degraded/Unreachable all
        // contribute Degraded (a quality knock, not a hard gate; Unreachable means the probe
        // timed out — usable but degraded evidence, matching macOS behavior).
        // Fast/Moderate are Negative (clean).
        // No default: arm so a new enum case is a compile error.
        Evaluate(latency, l => l.Status switch
        {
            ResolverLatencyStatus.Slow or ResolverLatencyStatus.Degraded or ResolverLatencyStatus.Unreachable => (SectionStatus.Degraded, null),
            ResolverLatencyStatus.Fast or ResolverLatencyStatus.Moderate => (SectionStatus.Negative, null),
            ResolverLatencyStatus.Untested => (SectionStatus.Untested, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        // Workstream H: stability
        if (stability is AnswerStability stab)
        {
            switch (stab.Status)
            {
                case AnswerStabilityStatus.Unstable:
                    ev.Add(EvidenceItem.Create(Section.Prophet, EvidenceId.AnswerUnstable,
                        $"Resolver's answer set changed with no shared address across {stab.UsableCount} samples",
                        "Answers that flap with nothing in common can be intermittent poisoning or on-path interference — corroborate against the authoritative and consensus checks."));
                    subs.Add(SectionStatus.Positive);
                    break;
                case AnswerStabilityStatus.TtlAnomaly:
                    // E017: emit an EvidenceItem for TTL anomaly so the degraded knock is explained
                    // (parity with macOS).
                    ev.Add(EvidenceItem.Create(Section.Prophet, EvidenceId.AnswerUnstable,
                        $"Resolver's TTL values are anomalous across {stab.UsableCount} samples",
                        "Unexpected TTL patterns can indicate caching interference or on-path manipulation — corroborate against the authoritative and consensus checks."));
                    subs.Add(SectionStatus.Degraded);
                    break;
                // B195: Inconclusive is a real "ran-but-unresolved" sub-status (macOS maps it
                // to .inconclusive); Untested means "never ran" and contributes nothing.
                case AnswerStabilityStatus.Inconclusive:
                    subs.Add(SectionStatus.Inconclusive);
                    break;
                case AnswerStabilityStatus.Untested:
                    break;
                case AnswerStabilityStatus.Stable:
                case AnswerStabilityStatus.Rotating:
                    // Swift parity (B038): .stable and .rotating both map to .negative — rotating
                    // load-balancer answers without suspicion are clean.
                    subs.Add(SectionStatus.Negative);
                    break;
                default:
                    // Unhandled AnswerStabilityStatus — breadcrumb rather than silently drop.
                    SyxxDiagnostics.Breadcrumb(
                        category: "synthesizer.stability.unhandled",
                        message: $"Unhandled AnswerStabilityStatus: {stab.Status}",
                        level: DiagnosticSeverity.Warning);
                    break;
            }
        }

        // Workstream H: wildcard synthesis
        Evaluate(wildcardSynthesis, wc => wc.Status switch
        {
            WildcardSynthesisStatus.NxHijack => (SectionStatus.Positive, EvidenceItem.Create(Section.Prophet, EvidenceId.NxSubdomainHijack,
                "Resolver invented address(es) for nonexistent subdomains of the target that a trusted resolver denies"
                    + (wc.SyntheticAddresses is { Count: > 0 } ? $" (→ {string.Join(", ", wc.SyntheticAddresses.Take(2))})" : ""),
                "A resolver fabricating answers for names that provably do not exist is hijacking NXDOMAIN to a block/ad page, aimed at this domain's namespace.")),
            WildcardSynthesisStatus.WildcardZone or WildcardSynthesisStatus.NoSynthesis => (SectionStatus.Negative, null),
            WildcardSynthesisStatus.Untested => (SectionStatus.Untested, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        // Workstream H: denial of existence
        // B036: NotADenial means the name resolved — the denial check does not apply.
        // Skip this datapoint entirely (no sub-status appended), matching Swift's `break`.
        if (denial is not { Status: DenialStatus.NotADenial })
        {
            Evaluate(denial, de => de.Status switch
            {
                DenialStatus.Contradictory => (SectionStatus.Positive, EvidenceItem.Create(Section.Prophet, EvidenceId.DenialInjected,
                    $"Negative answer for {de.QueriedName} is inconsistent — {de.Detail}",
                    "A negative that carries answers, or is signed by an SOA outside the name's zone, is a fabricated denial — a censor/middlebox pretending the name does not exist.")),
                DenialStatus.BareDenial => (SectionStatus.Degraded, EvidenceItem.Create(Section.Prophet, EvidenceId.DenialBare,
                    $"Negative answer for {de.QueriedName} carries no SOA or NSEC — bare denial",
                    "A genuine authoritative negative includes an SOA (and NSEC in a signed zone); a bare denial can be a synthesised block — corroborate.")),
                DenialStatus.NsecProven or DenialStatus.SoaBacked => (SectionStatus.Negative, null),
                DenialStatus.Inconclusive => (SectionStatus.Inconclusive, null),
                DenialStatus.Untested => (SectionStatus.Untested, null),
                _ => (SectionStatus.Inconclusive, null),
            }, ev, subs);
        }

        // Workstream H: dual-stack
        Evaluate(dualStack, ds => ds.Status switch
        {
            DualStackStatus.Ipv4Suppressed => (SectionStatus.Positive, EvidenceItem.Create(Section.Prophet, EvidenceId.Ipv4Suppressed,
                "AAAA resolves but the A record does not for the target",
                "A missing IPv4 record while IPv6 resolves can be selective IPv4 blocking — weak; a domain may also legitimately publish only AAAA, so corroborate.")),
            // B195: Inconclusive is a real "ran-but-unresolved" sub-status (macOS maps it
            // to .inconclusive); Untested means "never ran" and contributes nothing.
            DualStackStatus.Inconclusive => (SectionStatus.Inconclusive, null),
            DualStackStatus.Untested => (SectionStatus.Untested, null),
            DualStackStatus.DualStack or DualStackStatus.Ipv4Only or DualStackStatus.Dns64Synthesis => (SectionStatus.Negative, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        // Workstream H: EDNS0
        Evaluate(edns0, edns => edns.Status switch
        {
            EDNS0CapabilityStatus.Broken => (SectionStatus.Positive, EvidenceItem.Create(Section.Prophet, EvidenceId.EdnsBlocked,
                "EDNS0 (OPT) queries are dropped on this path while plain queries work",
                "A network device stripping EDNS0 is interfering with DNS at the protocol level (it also breaks DNSSEC and large answers) — corroborate.")),
            EDNS0CapabilityStatus.NotSupported => (SectionStatus.Degraded, null),
            EDNS0CapabilityStatus.Supported => (SectionStatus.Negative, null),
            EDNS0CapabilityStatus.Untested => (SectionStatus.Untested, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        // C010: version.bind is tilt-only — emit EvidenceItem, never section-gate.
        if (versionBind is VersionBindResult vb)
        {
            // Guard: require a non-null/non-empty banner, matching macOS Synthesizer.swift:225
            // (`if let banner = vb.banner`). A null banner must not produce evidence asserting one was observed.
            if (vb.ForwarderOnPublicPath && !string.IsNullOrEmpty(vb.Banner))
            {
                ev.Add(EvidenceItem.Create(Section.Prophet, EvidenceId.ResolverBannerAnomaly,
                    $"{vb.Server} is expected to be a public recursive but answered version.bind with a local-forwarder banner ({vb.Banner})",
                    "A big upstream resolver does not identify as dnsmasq/pi-hole/router software — a local device is answering on the path. Contextual: it aids attribution/triage and corroborates other interception signals, but never decides on its own."));
            }
        }

        // Workstream H: stub entropy (capture-sourced).
        // B043: handle InsufficientData explicitly (→ inconclusive, never clean).
        // No default arm: unhandled cases are compile errors.
        if (stubEntropy is not null)
        {
            switch (stubEntropy.Status)
            {
                case StubEntropyStatus.WeakRandomization:
                    ev.Add(EvidenceItem.Create(Section.Prophet, EvidenceId.WeakStubEntropy,
                        $"Local stub/OS DNS randomization is weak — {stubEntropy.Detail}",
                        "Low source-port / transaction-ID entropy on the LOCAL resolver path makes this host easier to spoof — a spoofability posture, not proof of upstream cache poisoning. Weak and contextual: corroborate with the spoofing/interception checks."));
                    subs.Add(SectionStatus.Degraded);
                    break;
                case StubEntropyStatus.WellRandomized:
                    subs.Add(SectionStatus.Negative);
                    break;
                case StubEntropyStatus.InsufficientData:
                    // B043: too few captured queries to judge — inconclusive, never clean.
                    subs.Add(SectionStatus.Inconclusive);
                    break;
                case StubEntropyStatus.Untested:
                    break;
            }
        }

        return CombineWithEvidence(ev, subs, completeness: DatapointCompleteness(subs, ProphetDatapointCatalog),
            datapointsExpected: ProphetDatapointCatalog);
    }

    public static SectionEvidence Mirror(ComparisonResult? comparison, WarpManifestResult? warpManifest,
        AuthoritativeComparison? authoritative = null, SVCBFinding? svcb = null,
        TTLAnomaly? ttlAnomaly = null, CNAMEChainAudit? cnameChain = null,
        GroundTruth? serverGroundTruth = null)
    {
        var ev = new List<EvidenceItem>();
        var subs = new List<SectionStatus>();

        // Original fields
        if (comparison is not null)
        {
            if (comparison.IsInconclusive) subs.Add(SectionStatus.Inconclusive);
            else if (comparison.IsRedirectionDetected)
            {
                ev.Add(EvidenceItem.Create(Section.Mirror,
                    EvidenceId.Redirection,
                    "Local resolver's answer diverges from the public-resolver consensus",
                    "You received a different answer than the rest of the internet — redirection / poisoning, unless a captive portal explains it."));
                subs.Add(SectionStatus.Positive);
            }
            else if (comparison.IsDegraded) subs.Add(SectionStatus.Degraded);
            else if ((comparison.LocalRecords is null || comparison.LocalRecords.Count == 0) &&
                     (comparison.PublicResolverResults is null || comparison.PublicResolverResults.Count == 0))
            {
                // B136: a comparison with NO local records and NO public results compared NOTHING.
                // All flags are false because nothing was observed, not because the answers agreed.
                // Falling through to Negative makes zero cross-vantage data supply CLEAN SUPPORT —
                // the engine treating "Mirror never ran" as "Mirror found nothing wrong". Port of the
                // macOS fix (Synthesizer.swift:262-273, landed 2026-07-16, ROADMAP TOP-TEN #1).
                subs.Add(SectionStatus.Inconclusive);
            }
            else subs.Add(SectionStatus.Negative);
        }
        // WARP_MANIFEST_V1: drive the sub-status from the signed-manifest CONCLUSION lattice (§5).
        // TAMPERING_DETECTED is positive interference evidence; INCONCLUSIVE never renders clean;
        // VERIFIED_MATCH is clean support. Missing/unverifiable data is never a silent pass.
        if (warpManifest is not null)
        {
            switch (warpManifest.Conclusion)
            {
                case WarpConclusion.TAMPERING_DETECTED:
                    string detail;
                    var mismatched = warpManifest.Assets.FirstOrDefault(a => a.State == WarpContentState.Mismatch);
                    if (warpManifest.HtmlState == WarpContentState.Mismatch)
                        detail = "Page HTML bytes differ from the signed clean manifest";
                    else if (warpManifest.SignatureState == WarpSignatureState.InvalidSignature)
                        detail = "The manifest signature is present but does not verify";
                    else if (mismatched is not null)
                        detail = $"Asset bytes differ from the signed manifest: {mismatched.Url}";
                    else
                        detail = "Content bytes differ from the signed manifest";
                    ev.Add(EvidenceItem.Create(Section.Mirror, EvidenceId.ScriptMismatch, detail,
                        "A verified, fresh signed manifest disagrees byte-for-byte with what your vantage fetched (or the manifest signature failed) — evidence of on-path content interference. Benign per-request/dynamic content is possible; see the diff before concluding."));
                    subs.Add(SectionStatus.Positive);
                    break;
                case WarpConclusion.INCONCLUSIVE:
                    // B034: an inconclusive warp is NOT a confirmed script mismatch — it is a
                    // distinct evidence type that says the check could not determine whether
                    // content was modified. Using ScriptMismatch here would overclaim.
                    ev.Add(EvidenceItem.Create(Section.Mirror, EvidenceId.ScriptMismatchInconclusive,
                        "Content warp check was inconclusive"
                            + (string.IsNullOrEmpty(warpManifest.InconclusiveReason) ? "" : $": {warpManifest.InconclusiveReason}"),
                        "An inconclusive warp (untrusted/unpinned signer, stale/forged freshness, or unfetchable content) could not determine whether content was modified — not a clean result; requires corroboration."));
                    subs.Add(SectionStatus.Inconclusive);
                    break;
                case WarpConclusion.VERIFIED_MATCH:
                    subs.Add(SectionStatus.Negative);
                    break;
            }
        }

        // Workstream H: authoritative comparison
        if (authoritative is AuthoritativeComparison ac)
        {
            switch (ac.Status)
            {
                case AuthComparisonStatus.Diverges:
                    // Additive Pro corroboration: when the Dimension server's untampered ground truth ALSO
                    // shows the resolver's answers are not served by the authority, upgrade to DECISIVE.
                    bool serverConfirmed = serverGroundTruth?.Authoritative?.Answers is { Count: > 0 }
                        && serverGroundTruth?.Result == "authoritative"
                        && ac.ResolverAnswers?.Count > 0
                        && !ac.ResolverAnswers.Select(a => a.ToLowerInvariant()).Any(addr =>
                            serverGroundTruth.Authoritative.Answers.Any(gt => gt.ToLowerInvariant() == addr));
                    // B041 (B092): the Dimension response arrives over ordinary TLS with no response
                    // signature or pinned key. "Near-proof" / "independent, untampered" overclaims
                    // what the transport actually proved — a compromised CA, TLS interceptor, or
                    // Dimension service could forge this. Report the corroboration honestly as a
                    // second source, not as cryptographic proof.
                    if (serverConfirmed)
                    {
                        ev.Add(EvidenceItem.Create(Section.Mirror, EvidenceId.AuthoritativeDivergesConfirmed,
                            "Recursive answer diverges from the domain's authoritative nameservers — corroborated by Dimension server"
                                + (ac.Nameservers is { Count: > 0 } ? $" ({string.Join(", ", ac.Nameservers.Take(2))})" : ""),
                            "The Dimension server also reports the authority does not serve the address your resolver returned — corroborates an upstream rewrite over TLS, but the response is unsigned and should be independently verified."));
                    }
                    else
                    {
                        // K-16 COUPLING — the rationale below is load-bearing on the classifier.
                        // It used to end "(benign CDN/geo routing is already excluded for a full
                        // divergence)". That was only true because AuthoritativeVantage.Classify
                        // softened a same-provider disjoint result to Inconclusive. K-16 removed that
                        // softening (an attacker may sit inside the provider's own ASN), so a
                        // `Diverges` can now legitimately carry a shared provider and the old claim
                        // would be false. The wording states what is actually established.
                        ev.Add(EvidenceItem.Create(Section.Mirror, EvidenceId.AuthoritativeDiverges,
                            "Recursive answer diverges from the domain's authoritative nameservers"
                                + (ac.Nameservers is { Count: > 0 } ? $" ({string.Join(", ", ac.Nameservers.Take(2))})" : ""),
                            "The domain's own authoritative servers do not serve the address your resolver returned — evidence of an upstream rewrite. CDN/geo routing can also produce this shape, including when both answers sit in one provider's ranges, so corroborate before concluding."));
                    }
                    subs.Add(SectionStatus.Positive);
                    break;
                case AuthComparisonStatus.Matches:
                    subs.Add(SectionStatus.Negative);
                    break;
                case AuthComparisonStatus.Untested:
                    break;
                default:
                    subs.Add(SectionStatus.Inconclusive);
                    break;
            }
        }

        // Workstream H: SVCB
        Evaluate(svcb, sv => sv.Status switch
        {
            SVCBFindingStatus.Divergent => (SectionStatus.Positive, EvidenceItem.Create(Section.Mirror, EvidenceId.SvcbStripped,
                $"HTTPS/SVCB record served by {sv.PresentAt?.Count ?? 0} resolver(s) but returned by none at {sv.AbsentAt?.Count ?? 0}",
                "Inconsistent HTTPS (type 65) records across modern resolvers can indicate SVCB/ECH stripping that silently downgrades transport — corroborate.")),
            SVCBFindingStatus.Present or SVCBFindingStatus.Absent => (SectionStatus.Negative, null),
            SVCBFindingStatus.Untested => (SectionStatus.Untested, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        // Workstream H: TTL anomaly
        Evaluate(ttlAnomaly, ta => ta.Status switch
        {
            TTLAnomalyStatus.LockstepTTL => (SectionStatus.Positive, EvidenceItem.Create(Section.Mirror, EvidenceId.TtlLockstep,
                $"{ta.TtlsByResolver?.Count ?? 0} independent resolvers report the identical TTL",
                "Lockstep TTLs across independent resolvers indicate a shared upstream or on-path injection, not independent caching.")),
            TTLAnomalyStatus.ExtremeTTL => (SectionStatus.Positive, EvidenceItem.Create(Section.Mirror, EvidenceId.TtlExtreme,
                "A resolver reports an implausibly large TTL (> 7 days)",
                "An extreme TTL suggests a pinned/stale answer, possible replay attack.")),
            TTLAnomalyStatus.IndependentCaching => (SectionStatus.Negative, null),
            TTLAnomalyStatus.Untested => (SectionStatus.Untested, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        // Workstream H: CNAME chain
        Evaluate(cnameChain, cc => cc.Status switch
        {
            CNAMEChainStatus.Divergent => (SectionStatus.Positive, EvidenceItem.Create(Section.Mirror, EvidenceId.CnameChainDivergent,
                "The resolver's CNAME chain diverges from the unanimous reference consensus",
                "A resolver that follows a different CNAME path than all trusted references may be redirected.")),
            CNAMEChainStatus.Consistent or CNAMEChainStatus.NoCNAME => (SectionStatus.Negative, null),
            CNAMEChainStatus.Untested => (SectionStatus.Untested, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        return CombineWithEvidence(ev, subs, completeness: DatapointCompleteness(subs, MirrorDatapointCatalog),
            datapointsExpected: MirrorDatapointCatalog);
    }

    public static SectionEvidence Portal(NetworkStateReport? report,
        EncryptedTransport? encryptedTransport = null, OpenResolverFinding? openResolver = null)
    {
        var ev = new List<EvidenceItem>();
        var subs = new List<SectionStatus>();
        var advisories = new List<Advisory>();

        if (report is null && encryptedTransport is null && openResolver is null)
            return new SectionEvidence(SectionStatus.Untested, Array.Empty<EvidenceItem>(), Array.Empty<Advisory>(), 0.0,
                0, PortalDatapointCatalog);

        // Original fields
        if (report is not null)
        {
            switch (report.CaptivePortal.State)
            {
                case ConditionState.Yes:
                    ev.Add(EvidenceItem.Create(Section.Portal,
                        EvidenceId.CaptivePortalActive,
                        "A captive portal is active on this network",
                        "Captive portals legitimately rewrite DNS before login — a benign explanation for apparent redirection."));
                    subs.Add(SectionStatus.Degraded);
                    break;
                case ConditionState.Unknown: subs.Add(SectionStatus.Inconclusive); break;
                default: subs.Add(SectionStatus.Negative); break;
            }
            switch (report.EncryptedDNS.State)
            {
                case ConditionState.No:
                    ev.Add(EvidenceItem.Create(Section.Portal,
                        EvidenceId.DnsUnencrypted,
                        "System DNS is unencrypted (Do53)",
                        "Cleartext DNS on port 53 can be observed and altered by your network or ISP."));
                    subs.Add(SectionStatus.Negative);
                    break;
                case ConditionState.Yes:
                    ev.Add(EvidenceItem.Create(Section.Portal,
                        EvidenceId.DnsEncrypted,
                        "System DNS is encrypted (DoH/DoT)",
                        "Encrypted DNS (DoH/DoT) is active, which prevents cleartext snooping or transparent redirection on port 53."));
                    subs.Add(SectionStatus.Negative);
                    break;
                case ConditionState.Unknown: subs.Add(SectionStatus.Inconclusive); break;
                default: subs.Add(SectionStatus.Negative); break;
            }
        }

        // C1 — router/gateway DNS audit. The decisive rogue shape is DHCP pushing a PRIVATE IPv4
        // resolver that is neither the gateway nor on the gateway's own /24 — the classic
        // rogue-DHCP / hijacked-router redirect. ISP-pushed PUBLIC resolvers are normal and never
        // flagged, and IPv6 pushes are skipped (a dual-stack router pushing its own ULA is benign
        // and off-subnet judgment needs prefix knowledge we do not have). A failed detection is
        // inconclusive, never silently clean; an audited-clean gateway is a real negative datapoint.
        if (report?.Gateway is GatewayInfo gw)
        {
            if (string.IsNullOrEmpty(gw.Ip))
            {
                subs.Add(SectionStatus.Inconclusive);
            }
            else
            {
                var offLink = gw.DhcpPushedDNS.Where(pushed =>
                    pushed != gw.Ip
                    && pushed.Contains('.', StringComparison.Ordinal)
                    && IPClassifier.Classify(pushed) == IPAddressClass.PrivateUse
                    && !Ipv4SameSlash24(pushed, gw.Ip)).ToList();
                if (offLink.Count > 0)
                {
                    ev.Add(EvidenceItem.Create(Section.Portal,
                        EvidenceId.GatewayDhcpOffLinkDNS,
                        $"DHCP pushed a private DNS server off your gateway's subnet: {string.Join(", ", offLink)}",
                        "Your router (or something impersonating it) directed DNS to a private address that is not the gateway and not on its subnet — the shape of a rogue-DHCP or hijacked-router redirect."));
                    subs.Add(SectionStatus.Positive);
                }
                else
                {
                    subs.Add(SectionStatus.Negative);
                }
            }
        }

        // Workstream H: encrypted transport
        Evaluate(encryptedTransport, et => et.Status switch
        {
            EncryptedTransportStatus.Blocked => (SectionStatus.Positive, EvidenceItem.Create(Section.Portal, EvidenceId.EncryptedDnsBlocked,
                $"DoH endpoint {et.Endpoint} is unreachable — encrypted DNS is blocked/filtered",
                "A network that blocks encrypted DNS forces cleartext resolution and makes tampering easier.")),
            EncryptedTransportStatus.Working => (SectionStatus.Negative, null),
            EncryptedTransportStatus.Untested => (SectionStatus.Untested, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        // Workstream H: open resolver — produces advisory, not evidence
        if (openResolver is OpenResolverFinding orf)
        {
            switch (orf.Status)
            {
                case OpenResolverStatus.RecursiveHighAmp:
                    advisories.Add(new Advisory(Section.Portal, AdvisoryId.OpenResolverExposure,
                        $"Possible open-resolver / amplification exposure — {orf.Target}",
                        orf.Detail,
                        Severity.High));
                    break;
                case OpenResolverStatus.RecursiveLowAmp:
                    advisories.Add(new Advisory(Section.Portal, AdvisoryId.OpenResolverExposure,
                        $"Recursive resolver on your network — {orf.Target}",
                        orf.Detail,
                        Severity.Info));
                    break;
                case OpenResolverStatus.Untested:
                    break;
                default:
                    break;
            }
        }

        // B176(c): `ev` is provably empty when `subs` is empty — every evidence-emitting branch
        // above also appends to `subs`. Assert the invariant so a future code change that breaks
        // this coupling is caught in debug builds rather than silently discarding evidence.
        // Twin of macOS Synthesizer.swift:557-559.
        if (subs.Count == 0 && advisories.Count == 0)
            Debug.Assert(ev.Count == 0, "B176(c): ev non-empty with empty subs — evidence would be silently discarded");

        return CombineWithEvidence(ev, subs, advisories,
            completeness: DatapointCompleteness(subs, PortalDatapointCatalog),
            datapointsExpected: PortalDatapointCatalog);
    }

    public static SectionEvidence Glutton(LeakTestResult? leak, ECSLeak? ecs = null)
    {
        var ev = new List<EvidenceItem>();
        var subs = new List<SectionStatus>();

        if (leak is null && ecs is null)
            return new SectionEvidence(SectionStatus.Untested, Array.Empty<EvidenceItem>(), Array.Empty<Advisory>(), 0.0,
                0, GluttonDatapointCatalog);

        // B043/B192: accumulate leak results into subs/ev (do not return early) so the ECS
        // block below always runs when ecs is present. B191: enumerate all five LeakResult
        // cases explicitly — Invalid and Error are unconditionally Invalidated (structurally
        // unusable), only Inconclusive applies the signature test. No default: arm so a new
        // enum case is a compile error rather than a silent downgrade.
        if (leak is not null)
        {
            switch (leak.Result)
            {
                case LeakResult.Confirmed:
                    var who = leak.ObservedResolvers.Count == 0 ? "" : $" ({string.Join(", ", leak.ObservedResolvers)})";
                    ev.Add(EvidenceItem.Create(Section.Glutton,
                        EvidenceId.DnsLeakConfirmed,
                        $"Collector confirmed DNS reached an unexpected resolver{who}",
                        "Your DNS is leaking outside the tunnel, or to a resolver you didn't choose."));
                    subs.Add(SectionStatus.Positive);
                    break;
                case LeakResult.NotDetected:
                    subs.Add(SectionStatus.Negative);
                    break;
                case LeakResult.Inconclusive:
                    // C008: a null signature means the leak test never had a collector to talk to
                    // (ProbePlan.RunGluttonAsync synthesises this for every collector-less run) — the
                    // probe never executed, so it is Untested, not Invalidated ("ran but corrupt").
                    // Parity with macOS Synthesizer.swift:579 (`l.signature == nil ? .untested : .inconclusive`).
                    subs.Add(leak.Signature is null ? SectionStatus.Untested : SectionStatus.Inconclusive);
                    break;
                case LeakResult.Invalid:
                case LeakResult.Error:
                    subs.Add(SectionStatus.Invalidated);
                    break;
            }
        }

        // B040 (B138): the ECS probe sends a synthetic test subnet (203.0.113.0), so a non-zero
        // echoed scope proves ECS CAPABILITY, not that the user's real subnet was forwarded.
        // A confirmed privacy leak requires paired plain/ECS comparison or collector-grounded
        // evidence. Report capability honestly; do not promote to a privacy-leak claim.
        // Workstream H: ECS leak
        Evaluate(ecs, ecsL => ecsL.Status switch
        {
            ECSLeakStatus.Forwarded => (SectionStatus.Positive, EvidenceItem.Create(Section.Glutton, EvidenceId.EcsSubnetLeak,
                "Resolver honours EDNS Client Subnet and echoed a scope"
                    + (ecsL.ScopePrefixLength is byte spl ? $" (/{spl})" : "")
                    + " for the synthetic test subnet",
                "The resolver processes ECS options — a capability that could expose your subnet to authoritative servers if your real prefix is forwarded. Confirming an actual leak requires a paired plain/ECS query or a collector observation.")),
            ECSLeakStatus.NotForwarded => (SectionStatus.Negative, null),
            ECSLeakStatus.Untested => (SectionStatus.Untested, null),
            _ => (SectionStatus.Inconclusive, null),
        }, ev, subs);

        return CombineWithEvidence(ev, subs, completeness: DatapointCompleteness(subs, GluttonDatapointCatalog),
            datapointsExpected: GluttonDatapointCatalog);
    }

    // C058 — expert-selected attribution-completeness floor. Below this fraction of capture records
    // carrying a real process identity, the process-initiator signal (a GroundTruth Medium tampering
    // signal) is too sparse for the section to read a silent clean: it is forced to Degraded with a
    // disclosing evidence item. 0.5 = "at least half of the events could be attributed"; chosen
    // conservatively and applied identically on both platforms (Synthesizer.swift mirror).
    internal const double CageAttributionThreshold = 0.5;

    /// <param name="attributionRate">Fraction (0..1) of capture records that carried a real process
    /// identity, or <c>null</c> when not applicable (not a capture-sourced run). Optional/defaulted so
    /// existing call sites compile unchanged until the app surfaces are wired (Phase 2).</param>
    public static SectionEvidence Cage(IReadOnlyList<ExfilAssessment>? exfil, ResponseSizeAudit? responseSize = null,
                                       double? attributionRate = null)
    {
        // H1 — mirror macOS Synthesizer.swift:513 exactly: when neither the exfil nor the response-size
        // leg was supplied, this is not a capture-sourced run at all. Return UNTESTED BEFORE the
        // attribution leg so an attributionRate arriving without any exfil/response-size evidence cannot
        // manufacture a clean Negative over a run where no domain was ever exfil-assessed.
        if (exfil is null && responseSize is null)
            return new SectionEvidence(SectionStatus.Untested, Array.Empty<EvidenceItem>(), Array.Empty<Advisory>(), 0.0, 0, 0);

        var ev = new List<EvidenceItem>();
        var subs = new List<SectionStatus>();

        // Original fields
        if (exfil is not null)
        {
            if (exfil.Count == 0)
            {
                // A capture that ran and flagged nothing is a conclusive negative for the exfil leg.
                subs.Add(SectionStatus.Negative);
            }
            else
            {
                foreach (var x in exfil)
                {
                    switch (x.Suspicion)
                    {
                        case ExfilSuspicion.LikelyExfil:
                        case ExfilSuspicion.Suspicious:
                            ev.Add(EvidenceItem.Create(Section.Cage,
                                x.Suspicion == ExfilSuspicion.LikelyExfil ? EvidenceId.ExfilLikely : EvidenceId.ExfilSuspicious,
                                $"{x.Domain}: {x.Suspicion.Label()} (entropy {x.EntropyBitsPerChar.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} b/char)",
                                x.Rationale.Count == 0 ? "High-entropy / beaconing DNS traffic." : string.Join("; ", x.Rationale)));
                            subs.Add(SectionStatus.Positive);
                            break;
                        case ExfilSuspicion.Benign: subs.Add(SectionStatus.Negative); break;
                        default: subs.Add(SectionStatus.Inconclusive); break;
                    }
                }
            }
        }

        // Workstream H: response size.
        // B043/B044: handle InsufficientData explicitly (→ inconclusive, never clean); add
        // Inconclusive member parity with Swift. No default arm: unhandled cases are compile errors.
        if (responseSize is not null)
        {
            switch (responseSize.Status)
            {
                case ResponseSizeStatus.Oversized:
                    ev.Add(EvidenceItem.Create(Section.Cage, EvidenceId.ResponseSizeAnomaly,
                        $"Sustained large DNS responses observed — {responseSize.Detail}",
                        "A run of unusually large responses can be amplification or covert DNS tunnelling (large TXT/NULL answers). Weak and contextual — big DNSSEC/TXT answers are also legitimately large — so it corroborates the exfil check, never decides alone."));
                    subs.Add(SectionStatus.Degraded);
                    break;
                case ResponseSizeStatus.Normal:
                    subs.Add(SectionStatus.Negative);
                    break;
                case ResponseSizeStatus.InsufficientData:
                    // B043: too few samples to judge — inconclusive, never rendered clean.
                    subs.Add(SectionStatus.Inconclusive);
                    break;
                case ResponseSizeStatus.Untested:
                    break;
            }
        }

        // C058 — process-attribution completeness. When the capture could attribute a process identity
        // to too few records, the process-initiator signal (a Medium GroundTruth tampering signal) was
        // effectively not evaluated across the capture; a section that combined to a silent clean here
        // would be claiming tampering was ruled out on a dimension it could not see. A below-threshold
        // rate forces Degraded (never a bare clean) with a disclosing evidence item; an adequate rate is
        // recorded as an evaluated negative datapoint. null = not a capture-sourced run -> no datapoint.
        if (attributionRate is double rate)
        {
            if (rate < CageAttributionThreshold)
            {
                ev.Add(EvidenceItem.Create(Section.Cage, EvidenceId.AttributionIncomplete,
                    $"Process attribution incomplete — {(int)Math.Round(rate * 100, MidpointRounding.AwayFromZero)}% of capture records carried a process identity",
                    "Without a process initiator for most events, on-host tampering whose only distinguishing signal is its originating process cannot be ruled out — the Cage section is degraded, not clean."));
                subs.Add(SectionStatus.Degraded);
            }
            else
            {
                subs.Add(SectionStatus.Negative);
            }
        }

        if (subs.Count == 0)
            return new SectionEvidence(SectionStatus.Untested, ev, Array.Empty<Advisory>(), 0.0, 0, 0);

        // Cage's datapoint set is data-driven (one per assessed domain + the size audit + the
        // attribution-completeness slot when a capture supplied one), so M is the observed datapoint
        // count rather than a fixed catalog. When exfil is non-null (even if empty, meaning the
        // assessment ran) it is at least 1 datapoint.
        int cageDatapoints = (exfil is not null ? Math.Max(exfil.Count, 1) : 0)
            + (responseSize is not null ? 1 : 0)
            + (attributionRate is not null ? 1 : 0);
        return CombineWithEvidence(ev, subs, completeness: DatapointCompleteness(subs, cageDatapoints),
            datapointsExpected: cageDatapoints);
    }

    public static SectionEvidence Witness(IReadOnlyList<AnswerDrift>? drifts, IReadOnlyList<ConfigChange>? configChanges)
    {
        if (drifts is null && configChanges is null) return new SectionEvidence(SectionStatus.Untested, Array.Empty<EvidenceItem>(), Array.Empty<Advisory>(), 0.0, 0, 1);
        var ev = new List<EvidenceItem>();
        var subs = new List<SectionStatus>();
        foreach (var d in drifts ?? Array.Empty<AnswerDrift>())
        {
            ev.Add(EvidenceItem.Create(Section.Witness,
                EvidenceId.AnswerDrift,
                $"{d.Domain} answer changed: {d.PreviousAnswer} -> {d.NewAnswer}",
                "An answer that drifted from your trusted baseline may be new tampering (or a legitimate change — corroborate)."));
            subs.Add(SectionStatus.Positive);
        }
        foreach (var c in configChanges ?? Array.Empty<ConfigChange>())
        {
            ev.Add(EvidenceItem.Create(Section.Witness,
                EvidenceId.ConfigChange,
                $"Network config changed: {c.Kind.Label()} — {c.Detail}",
                "An unexplained change to your resolver / VPN / encryption state warrants a re-audit."));
            subs.Add(SectionStatus.Inconclusive);
        }
        // Baseline ran and observed no drift/config change: complete, conclusive negative.
        if (subs.Count == 0) return new SectionEvidence(SectionStatus.Negative, Array.Empty<EvidenceItem>(), Array.Empty<Advisory>(), 1.0, 1, 1);
        // Witness datapoints are data-driven (each drift/change is one observation).
        return CombineWithEvidence(ev, subs, completeness: DatapointCompleteness(subs, subs.Count),
            datapointsExpected: subs.Count);
    }
}

/// Runs every section adapter and hands the aggregate to SynthesisEngine — the entry point the
/// CLI/GUI call after running whatever sections they could.
public static class Synthesizer
{
    public static Conclusion Conclude(SectionInputs inputs, SynthesisEngine.Config? config = null, DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var prophet = EvidenceAdapters.Prophet(inputs.Resolution, inputs.Dnssec, inputs.NxCanary, inputs.DnssecCanary,
            inputs.Latency, inputs.Stability, inputs.WildcardSynthesis, inputs.Denial, inputs.DualStack,
            inputs.Edns0, inputs.VersionBind, inputs.StubEntropy);
        var mirror = EvidenceAdapters.Mirror(inputs.Comparison, inputs.WarpManifest,
            inputs.Authoritative, inputs.Svcb, inputs.TtlAnomaly, inputs.CnameChain,
            inputs.ServerGroundTruth);
        var portal = EvidenceAdapters.Portal(inputs.Network, inputs.EncryptedTransport, inputs.OpenResolver);
        var glutton = EvidenceAdapters.Glutton(inputs.Leak, inputs.Ecs);
        var cage = EvidenceAdapters.Cage(inputs.Exfil, inputs.ResponseSize, inputs.CageAttributionRate);
        var witness = EvidenceAdapters.Witness(inputs.Drifts, inputs.ConfigChanges);

        var sections = new (Section, SectionEvidence)[]
        {
            (Section.Prophet, prophet),
            (Section.Mirror,  mirror),
            (Section.Portal,  portal),
            (Section.Glutton, glutton),
            (Section.Cage,    cage),
            (Section.Witness, witness),
        };
        var statuses = new List<SectionStatusEntry>();
        var evidence = new List<EvidenceItem>();
        var advisories = new List<Advisory>();
        foreach (var (section, se) in sections)
        {
            statuses.Add(new SectionStatusEntry(section, se.Status, se.Completeness,
                se.DatapointsObserved, se.DatapointsExpected));
            evidence.AddRange(se.Evidence);
            advisories.AddRange(se.Advisories);
        }
        // B174: when the system resolver was undetectable and a public substitute was used,
        // the Mirror section's local-vs-public comparison degenerates (local IS public).
        // Cap the Mirror section at Degraded so the result is never clean on substituted evidence.
        // Twin of macOS/Sources/SyxxKit/Synthesis/Synthesizer.swift:772.
        if (inputs.ResolverSubstituted)
        {
            // B299: Swift twin uses SyxxDiagnostics.breadcrumb, not an Advisory — there is no
            // AdvisoryId for resolver-substituted. Match the Swift pattern.
            SyxxDiagnostics.Breadcrumb(
                category: "synthesis.resolver",
                message: "Resolver was substituted — Mirror section capped at degraded",
                level: DiagnosticSeverity.Warning);

            var idx = statuses.FindIndex(s => s.Section == Section.Mirror);
            if (idx >= 0 && statuses[idx].Status == SectionStatus.Negative)
            {
                statuses[idx] = statuses[idx] with { Status = SectionStatus.Degraded };
            }
        }
        // The matrix determinative engine consumes evidence + section statuses directly. The old
        // DST-era extra inputs to `synthesize` (the answer-matrix, the decisive-act catalog, and the
        // resolver-provenance confidence scalar) were removed together with that engine
        // (owner directive 2026-07-21); the raw structured comparison still reaches the engine via
        // the per-section evidence built above.
        var conclusion = SynthesisEngine.Synthesize(evidence, statuses, advisories, config, now, ct: ct);
        ReportResultProvenance(conclusion);
        return conclusion;
    }

    /// <summary>
    /// Emit the run's result AND the attribution for every degraded section, once, from the
    /// finished <see cref="Conclusion"/> — never before it exists. Twin of macOS
    /// <c>Synthesizer.reportResultProvenance</c>.
    ///
    /// <para>WHY: a fleet-wide rise in inconclusive results is otherwise uninvestigable — you
    /// cannot tell whether a resolver started dropping queries, an OS release broke a platform
    /// API, or the matrix spec regressed. This links errors to the result they produced.</para>
    ///
    /// <para>It also enforces the standing invariant: a section that is untested/invalidated/
    /// inconclusive with NO diagnostic recorded against it means some path degraded a measurement
    /// silently — the defect class this project exists to prevent, made mechanically detectable.</para>
    /// </summary>
    internal static void ReportResultProvenance(Conclusion conclusion)
    {
        // FIVE states, never four: a null determination is "the engine could not run", which is
        // categorically different from Inconclusive ("ran, valid data, did not resolve").
        var state = conclusion.Determination is { } d ? d.State.RawValue() : "no_determination";

        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["syxx.result.state"] = state,
        };
        if (conclusion.Determination is { } det)
        {
            data["syxx.result.diagnoses"] = det.Diagnoses.Count.ToString(CultureInfo.InvariantCulture);
            data["syxx.result.suppressed"] = det.Suppressed.Count.ToString(CultureInfo.InvariantCulture);
        }

        var blocking = new List<string>();
        var unattributed = new List<string>();
        foreach (var entry in conclusion.SectionStatuses)
        {
            var name = entry.Section.ToString().ToLowerInvariant();
            data[$"syxx.section.{name}.status"] = entry.Status.ToString().ToLowerInvariant();
            if (!entry.Status.BlocksClean() && entry.Status != SectionStatus.Inconclusive) { continue; }
            blocking.Add(name);
            if (SyxxDiagnostics.Run?.ErrorClassesFor(entry.Section).Count is null or 0)
            {
                unattributed.Add(name);
            }
        }
        blocking.Sort(StringComparer.Ordinal);
        data["syxx.blocking_sections"] = string.Join(",", blocking);
        // Use lowercase "true"/"false" to match macOS String(bool) output (Synthesizer.swift:707)
        data["syxx.attribution_complete"] = unattributed.Count == 0 ? "true" : "false";

        // An abstention or a non-result is the interesting case; a decided run is routine.
        var level = state is "clean" or "threatDetected"
            ? DiagnosticSeverity.Warning
            : DiagnosticSeverity.Error;
        SyxxDiagnostics.Breadcrumb("syxx.result", $"result={state}", level, data);

        // A degraded section nobody explained is a bug in OUR code, not in the network.
        foreach (var section in unattributed)
        {
            SyxxDiagnostics.Capture(new DiagnosticEvent(
                ErrorClass: "result.unattributed_degradation",
                Component: "Synthesizer",
                Section: null,
                Impact: MeasurementImpact.None,
                Severity: DiagnosticSeverity.Warning,
                Underlying: null,
                Fingerprint: new[] { "result.unattributed_degradation", section },
                Tags: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["syxx.section"] = section,
                    ["syxx.result.state"] = state,
                }));
        }
    }
}
