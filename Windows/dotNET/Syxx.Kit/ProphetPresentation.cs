using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

// Parity twin of the macOS ProphetView consensus evaluation + presentation helpers
// (ProphetView.swift:127-211, 326-371). Extracted to the Kit so the tri-state mapping
// is testable offline from Syxx.Tests without a WinUI host (the SyxxUI project cannot
// compile on a non-Windows host because the XAML compiler is a Windows binary).
//
// HONESTY:
// - The strongest DNSSEC posture emitted is RecordsPresentUnverified, coloured
//   IoProphetBlueBrush and titled "DNSSEC is rightly set for this domain."
//   NEVER "validated", "secure", or "protected" (CF-08 / A2: full chain validation is
//   not performed by this client).
// - A failed DNSSEC query renders as Inconclusive with IoTextSecondaryBrush, NEVER red.
//   Red reads as an accusation against a domain Syxx simply could not reach (E144).
// - Consensus is computed from the actual returned IPs, not just the resolver count.
//   Need >= 2 reachable AND all same IP before an agreeing conclusion.
// - Agree and disagree carry the owner's two-run lore headline (MEMORY.md:1827-1833).
//   Inconclusive and insufficient keep their own honest wording and never borrow either
//   voice, so an unmeasured run cannot read as a success or as an accusation.
// - B231: an unmeasured median latency reads "not measured", never "0ms" (a fabricated
//   measurement) and never an em-dash (banned in user-facing text per AGENTS.md).

/// <summary>
/// Consensus evaluation + presentation for the Prophet 12-oracle result card.
/// Mirrors macOS ProphetView.evaluateConsensus + prophetConsensusLabel.
/// </summary>
public static class ProphetPresentation
{
    /// Tri-state consensus derived from the actual returned IPs.
    public enum ResolverConsensus
    {
        /// 0 reachable resolvers: no data to judge.
        Inconclusive,
        /// 1 reachable resolver: need >= 2 for consensus.
        Insufficient,
        /// All reachable resolvers returned the same IP.
        Agree,
        /// Reachable resolvers returned different IPs.
        Disagree,
    }

    /// The evaluated consensus plus the data needed to build the presentation label.
    public sealed record ConsensusResult(
        ResolverConsensus State,
        int ActiveCount,
        IReadOnlyList<string> UniqueIPs);

    /// User-visible headline, detail, colour key, and agreement fraction for a consensus card.
    /// <param name="HeadlinePrimary">Headline run 1. For agree/disagree this is the Spectral-Light
    /// 20pt lore line in the module highlight colour (MEMORY.md:1827/1829). For inconclusive and
    /// insufficient it is the single honest status title in the semantic colour.</param>
    /// <param name="HeadlineSecondary">Headline run 2 (Spectral-Light 17pt), present ONLY for the
    /// lore agree/disagree states. Null for inconclusive/insufficient, which never borrow the
    /// success or failure voice.</param>
    public sealed record ConsensusPresentation(
        string HeadlinePrimary,
        string? HeadlineSecondary,
        string Detail,
        string ColorKey,
        string AgreementFraction);

    /// User-visible label, detail, colour key, and short status tag for a DNSSEC card.
    public sealed record DnssecPresentation(
        string Title,
        string Detail,
        string ColorKey,
        string StatusLabel);

    /// <summary>
    /// Evaluate resolver consensus by comparing actual returned IPs, not just resolver count.
    /// Mirrors macOS ProphetView.evaluateConsensus (ProphetView.swift:146-159).
    /// </summary>
    public static ConsensusResult EvaluateConsensus(IReadOnlyList<ProphetResult.ResolverResponse> responses)
    {
        var active = responses
            .Where(r => !r.Ip.Contains("[unreachable]", StringComparison.Ordinal))
            .ToList();
        if (active.Count == 0)
            return new ConsensusResult(ResolverConsensus.Inconclusive, 0, Array.Empty<string>());
        // Parity macOS `.insufficient(active:)` (ProphetView.swift:178), which carries no IPs:
        // one vantage is not a set of answers to compare, so none are surfaced.
        if (active.Count < 2)
            return new ConsensusResult(ResolverConsensus.Insufficient, active.Count, Array.Empty<string>());
        var uniqueIPs = active
            .Select(r => r.Ip)
            .Distinct()
            .OrderBy(ip => ip, StringComparer.Ordinal)
            .ToList();
        if (uniqueIPs.Count == 1)
            return new ConsensusResult(ResolverConsensus.Agree, active.Count, uniqueIPs);
        return new ConsensusResult(ResolverConsensus.Disagree, active.Count, uniqueIPs);
    }

    /// <summary>
    /// Build the user-visible headline, explanatory detail, semantic colour key, and agreement
    /// fraction for a consensus state. Called once per result card to avoid triple-evaluating
    /// the consensus switch. Mirrors macOS prophetConsensusLabel (ProphetView.swift:208-257):
    /// agree/disagree carry the two-run lore headline, inconclusive and insufficient keep their
    /// own honest wording.
    /// </summary>
    /// <param name="consensus">The evaluated consensus from <see cref="EvaluateConsensus"/>.</param>
    /// <param name="domain">The domain that was queried (for the detail text).</param>
    /// <param name="medianLatencyMs">Median latency in ms, or null when the latency probe was
    /// skipped or timed out (B231: null renders as "not measured", never "0ms").</param>
    public static ConsensusPresentation BuildConsensusLabel(
        ConsensusResult consensus,
        string domain,
        int? medianLatencyMs)
    {
        // B231: an unmeasured median reads "not measured". "0ms" would fabricate a perfect
        // measurement of something never measured, and the prior em-dash is banned in
        // user-facing text (AGENTS.md). Parity macOS ProphetView.swift:215.
        string latency = medianLatencyMs.HasValue ? $"{medianLatencyMs}ms median" : "not measured";
        return consensus.State switch
        {
            // Honest inconclusive, distinct from both success and failure. No lore run 2.
            ResolverConsensus.Inconclusive => new ConsensusPresentation(
                HeadlinePrimary: "Inconclusive: no reachable resolver",
                HeadlineSecondary: null,
                Detail: $"No resolver could be reached for {domain}. Check your network connection.",
                ColorKey: "IoTextSecondaryBrush",
                AgreementFraction: $"0 / 0 reachable · {latency}"),
            // Honest under-measured state: one vantage cannot establish consensus either way.
            ResolverConsensus.Insufficient => new ConsensusPresentation(
                HeadlinePrimary: $"Insufficient: only {consensus.ActiveCount} resolver reachable",
                HeadlineSecondary: null,
                Detail: $"Only one resolver responded for {domain}. Add public oracles to test consensus.",
                ColorKey: "IoTextSecondaryBrush",
                AgreementFraction: $"{consensus.ActiveCount} / {consensus.ActiveCount} (single vantage) · {latency}"),
            // Success lore (MEMORY.md:1827 headline, :1831 detail). Owner directive 2026-08-02:
            // the agreeing container's chrome and conclusion lines use the Prophet blue #00b4ff,
            // not the prior green.
            ResolverConsensus.Agree => new ConsensusPresentation(
                HeadlinePrimary: "No tampering doth abide;",
                HeadlineSecondary: "the 12 resolvers are of one accord.",
                Detail: "Then came the vision of twelve oracles, each bearing witness, and all in steadfast accord, as though one truth moved through many mouths.",
                ColorKey: "IoProphetBlueBrush",
                AgreementFraction: $"{consensus.ActiveCount} / {consensus.ActiveCount} agree · {latency}"),
            // Failure lore (MEMORY.md:1829 headline, :1833 detail).
            ResolverConsensus.Disagree => new ConsensusPresentation(
                HeadlinePrimary: "Tampering doth yet abide;",
                HeadlineSecondary: "the twelve resolvers are not of one accord.",
                Detail: "Then came the vision of twelve oracles, each bearing witness, yet their voices fell apart, and no single truth held them in accord.",
                ColorKey: "IoWarningAmberBrush",
                AgreementFraction: $"0 / {consensus.ActiveCount} agree · {latency}"),
            _ => throw new ArgumentOutOfRangeException(nameof(consensus)),
        };
    }

    /// <summary>
    /// Build the user-visible label, explanatory detail, semantic colour key, and short status
    /// tag for a DNSSECStatus. The strongest positive state emitted is
    /// <see cref="DNSSECStatus.RecordsPresentUnverified"/> (DNSSEC configured, signatures within
    /// validity window) and never "validated" or "secure", because this client does not perform
    /// full cryptographic chain validation (CF-08 / A2). Mirrors macOS
    /// dnssecPresentation (ProphetView.swift:339-371).
    /// </summary>
    /// <param name="status">The DNSSEC posture from <see cref="DNSSECVerifier.CheckStatusAsync"/>.</param>
    /// <param name="domain">The domain that was queried (empty string renders as "this domain").</param>
    public static DnssecPresentation BuildDnssecPresentation(DNSSECStatus status, string domain)
    {
        string display = string.IsNullOrEmpty(domain) ? "this domain" : domain;
        return status switch
        {
            DNSSECStatus.Unsigned => new DnssecPresentation(
                Title: "No DNSSEC: zone unsigned",
                Detail: $"{display} returned no DNSKEY or RRSIG records. The zone appears unsigned, so resolver answers cannot be cryptographically verified by DNSSEC.",
                ColorKey: "IoWarningAmberBrush",
                StatusLabel: "unsigned"),
            // Success (MEMORY.md:1835). Owner directive 2026-08-02: the "rightly set" container
            // uses the Prophet blue, in parity with the agreeing consensus container.
            DNSSECStatus.RecordsPresentUnverified => new DnssecPresentation(
                Title: "DNSSEC is rightly set for this domain.",
                Detail: $"{display} returned DNSKEY and RRSIG records with at least one signature inside its validity window. Full cryptographic validation is not performed by this client, so this is the strongest positive state emitted.",
                ColorKey: "IoProphetBlueBrush",
                StatusLabel: "configured · unverified"),
            // Failure (MEMORY.md:1837). A genuinely broken signer: signatures present but expired
            // or not yet valid. The alarming wording is warranted here, unlike the unsigned case.
            DNSSECStatus.SignatureExpiredOrNotYetValid => new DnssecPresentation(
                Title: "DNSSEC is not rightly set for this domain; resolvers may not speak true, and their witness cannot be held in trust.",
                Detail: $"{display} returned RRSIG records, but every one observed is expired or not yet valid. This is a stale-signature or broken-signer signal, not a trustworthy configuration.",
                ColorKey: "IoCriticalRedBrush",
                StatusLabel: "broken"),
            DNSSECStatus.Inconclusive => new DnssecPresentation(
                Title: "DNSSEC status inconclusive",
                Detail: $"DNSSEC records for {display} could not be retrieved (timeout, transport error, or blocked). No determination can be made.",
                ColorKey: "IoTextSecondaryBrush",
                StatusLabel: "inconclusive"),
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
    }
}