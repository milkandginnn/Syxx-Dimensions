using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

/// Stage-5 builder (STAGE5-CONTRACT.md §4–§5) — the Windows-parity twin of
/// macOS/Sources/SyxxKit/Synthesis/Stage5Builder.swift. Turns the engine's STRUCTURED cross-vantage
/// evidence into the typed presentation DTOs instead of collapsing it to prose. Slice 1 covers CX-1's
/// canonical answer matrix and the direct observations that back it, sourced from a ComparisonResult
/// (VantageComparer) which already carries per-vantage typed outcomes and raw answer sets.
///
/// Determinism: no GUIDs, no wall-clock — IDs derive from stable inputs and the observation instant is
/// passed in, so two runs on identical inputs produce byte-identical DTOs matching the Swift twin (§11).
public static class Stage5Builder
{
    // ===== CX-1 answer matrix (§5) =====

    /// Build one answer-matrix row (local + each public resolver) plus the direct observations that
    /// back every answered cell, from a single-query ComparisonResult. rowIndex disambiguates IDs when a
    /// run compares several queries; observedAt is the RFC-3339 UTC instant the comparison was taken.
    public static (AnswerMatrixRow Row, IReadOnlyList<DirectObservation> Observations) AnswerRow(
        ComparisonResult c, string domain, ushort queryType, int rowIndex, string observedAt)
    {
        var qname = CanonicalName(domain);
        var datapointId = $"mirror.answer.{qname}.{queryType}";
        var entries = new List<VantageAnswer>();
        var observations = new List<DirectObservation>();

        void AppendVantage(VantageIdentity vantage, ResolverQueryOutcome outcome, string obsSuffix)
        {
            var observationId = $"obs:mirror:{rowIndex}:{obsSuffix}";
            var answers = CanonicalAnswerSet(outcome.Records);
            var status = AnswerStatusOf(outcome.Status);
            // Rule §4.3 + invariant §13.3: only a real, non-empty answer SET becomes a dnsAnswerSet direct
            // observation (rawValues must be non-empty) and earns an observationId the matrix cell links
            // to. A timeout / NXDOMAIN / NODATA carries its meaning in status/rcode and links to nothing.
            var linkable = status == AnswerStatus.Answered && answers.Count > 0;
            entries.Add(new VantageAnswer(vantage, status, answers, RcodeOf(outcome.Status),
                linkable ? observationId : null));
            if (linkable)
                observations.Add(new DirectObservation(observationId, datapointId, Section.Mirror, observedAt,
                    vantage, answers, ObservationValueKind.DnsAnswerSet, Array.Empty<EvidenceId>()));
        }

        AppendVantage(new VantageIdentity("local", VantageKind.LocalSystem, c.LocalServer, "do53", false,
            FreshnessState.CurrentRun), c.LocalOutcome, "local");
        foreach (var name in c.PublicResolverOutcomes.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var vid = CanonicalVantageId(name);
            VantageComparer.PublicResolvers.TryGetValue(name, out var endpoint);
            AppendVantage(new VantageIdentity(vid, VantageKind.PublicResolver, endpoint, "do53", false,
                FreshnessState.CurrentRun), c.PublicResolverOutcomes[name], vid);
        }

        var row = new AnswerMatrixRow($"row:mirror:{rowIndex}", qname, queryType, entries, Classify(entries));
        return (row, observations);
    }

    // ===== Comparison classification (§5 / §8 invariants) =====

    /// Classify a row by the same conservative disjoint/overlap/baseline logic VantageComparer uses for
    /// its redirection verdict, expressed in the Stage-5 taxonomy. A local blocking-IP answer or a set
    /// disjoint from consensus is a discrepancy (local outlier); partial overlap is benign CDN/geo
    /// variation; fewer than two valid references is insufficientReferences (never a fabricated
    /// consensus, §13.7); a failed or empty local answer is incomparable (a non-answer is not a
    /// disagreement, B017).
    internal static AnswerComparison Classify(IReadOnlyList<VantageAnswer> entries)
    {
        var local = entries.FirstOrDefault(e => e.Vantage.Kind == VantageKind.LocalSystem);
        var validRefs = entries.Where(e => e.Vantage.Kind == VantageKind.PublicResolver
            && e.Status == AnswerStatus.Answered && e.CanonicalAnswers.Count > 0).ToList();
        var referenceVantageIds = validRefs.Select(e => e.Vantage.VantageId)
            .OrderBy(v => v, StringComparer.Ordinal).ToList();
        var localIds = local is null ? new List<string>() : new List<string> { local.Vantage.VantageId };

        AnswerComparison Result(AnswerComparisonClass cls, IReadOnlyList<string>? consensus = null,
                                IReadOnlyList<string>? outliers = null) =>
            new(cls, localIds, referenceVantageIds, consensus ?? Array.Empty<string>(), outliers ?? Array.Empty<string>());

        if (validRefs.Count < 2) return Result(AnswerComparisonClass.InsufficientReferences);
        var consensus = CanonicalMerge(validRefs.SelectMany(e => e.CanonicalAnswers));

        if (local is null || local.Status != AnswerStatus.Answered || local.CanonicalAnswers.Count == 0)
            return Result(AnswerComparisonClass.Incomparable, consensus);

        var localSet = local.CanonicalAnswers.ToHashSet();
        var consensusSet = consensus.ToHashSet();
        var localBlocked = local.CanonicalAnswers.Any(IPClassifier.IsBlockingAnswer);
        if (localBlocked || !localSet.Overlaps(consensusSet))
            return Result(AnswerComparisonClass.Discrepancy, consensus, new List<string> { local.Vantage.VantageId });
        if (localSet.SetEquals(consensusSet)) return Result(AnswerComparisonClass.Agreement, consensus);
        return Result(AnswerComparisonClass.BenignSetVariation, consensus);     // partial overlap → CDN/geo
    }

    // ===== Canonicalization (§5) — must be byte-identical to the Swift twin =====

    /// Lower-case absolute domain form: trim, lower-case, collapse to exactly one trailing dot.
    internal static string CanonicalName(string s) => s.Trim().ToLowerInvariant().Trim('.') + ".";

    /// Vantage id: lower-case, restricted to the id charset [A-Za-z0-9._:-] (§13.1); others → '-'.
    internal static string CanonicalVantageId(string s) =>
        new(s.ToLowerInvariant().Select(ch => (char.IsLetterOrDigit(ch) || ch is '.' or '_' or ':' or '-') ? ch : '-').ToArray());

    /// Canonical answer SET: trim + lower-case each value (lower-cases IPv6 hex/domain rdata; IPv4
    /// unaffected), de-duplicate, sort ordinally. Full RFC 5952 IPv6 compression is a follow-up.
    internal static IReadOnlyList<string> CanonicalAnswerSet(IEnumerable<DNSRecord> records) =>
        CanonicalMerge(records.Select(r => r.Rdata));

    internal static IReadOnlyList<string> CanonicalMerge(IEnumerable<string> values) =>
        values.Select(v => v.Trim().ToLowerInvariant()).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToList();

    // ===== Status / rcode mapping =====

    internal static AnswerStatus AnswerStatusOf(DNSResponseStatus s) => s switch
    {
        DNSResponseStatus.Success => AnswerStatus.Answered,
        DNSResponseStatus.NoData => AnswerStatus.NoData,
        DNSResponseStatus.NXDomain => AnswerStatus.Nxdomain,
        DNSResponseStatus.ServFail => AnswerStatus.Servfail,
        DNSResponseStatus.Refused => AnswerStatus.Refused,
        DNSResponseStatus.Timeout => AnswerStatus.TimedOut,
        DNSResponseStatus.Truncated or DNSResponseStatus.ResponseIdMismatch or DNSResponseStatus.MalformedResponse => AnswerStatus.Invalid,
        DNSResponseStatus.TransportError => AnswerStatus.Unavailable,
        _ => AnswerStatus.Invalid,
    };

    /// The DNS RCODE the status implies, when one exists. Transport-level failures have no rcode.
    internal static int? RcodeOf(DNSResponseStatus s) => s switch
    {
        DNSResponseStatus.Success or DNSResponseStatus.NoData => 0,
        DNSResponseStatus.ServFail => 2,
        DNSResponseStatus.NXDomain => 3,
        DNSResponseStatus.Refused => 5,
        _ => null,
    };
}
