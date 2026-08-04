using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

/// <summary>
/// B005 sub-scorers for the synthesis engine — per DESIGN.md "Score-design invariants" section.
/// Each scorer follows the no-data / partial-data / full-data contracts, never defaulting to
/// confident on absent or degraded input.
/// Windows-parity twin of macOS/Sources/SyxxKit/Synthesis/Scorers.swift.
/// </summary>
public static class Scorers
{
    /// <summary>
    /// 3. Evidence Confidence Score (DESIGN.md §3) — flagship number shown to the end user.
    /// No evidence → nil. Partial → baseScore * (K/6), halved if critical section inconclusive.
    /// Full → baseScore as-is.
    /// </summary>
    public static int? EvidenceConfidence(
        int baseConfidence,
        IReadOnlyList<SectionStatusEntry> sectionStatuses,
        HashSet<Section>? criticalSections = null)
    {
        criticalSections ??= new HashSet<Section> { Section.Prophet, Section.Mirror, Section.Glutton };
        int conclusive = sectionStatuses.Count(s =>
            s.Status is SectionStatus.Positive or SectionStatus.Negative or SectionStatus.Degraded);
        int total = sectionStatuses.Count;
        if (total == 0) return null;

        // No evidence regime
        if (conclusive == 0) return null;

        // Full evidence regime
        if (conclusive == total)
            return Math.Max(0, Math.Min(100, baseConfidence));

        // Partial evidence regime
        double fraction = (double)conclusive / total;
        double score = baseConfidence * fraction;

        // Halve if any critical section is inconclusive
        bool criticalInconclusive = sectionStatuses.Any(s =>
            criticalSections.Contains(s.Section) && s.Status == SectionStatus.Inconclusive);
        if (criticalInconclusive)
            score *= 0.5;

        // Parity (B-corr): round AWAY-FROM-ZERO to match Swift's `Double.rounded()` default and the
        // SynthesisEngine.RoundPercent primitive. C# `Math.Round` defaults to banker's/ToEven, which at a
        // `.5` boundary (e.g. 99/6 = 16.5) rounds to 16 while macOS rounds to 17 — that single-point
        // divergence made two independent supports (99) tie the correlated group (95→16) on Windows only.
        return Math.Max(0, Math.Min(100, (int)Math.Round(score, MidpointRounding.AwayFromZero)));
    }

    /// <summary>
    /// 1. Resolver Provenance Confidence (DESIGN.md §1) — how sure we are the response came
    /// from the intended resolver. No data → nil. Partial → attenuated, capped at 0.40 if k &lt; N/2.
    /// </summary>
    public static double? ResolverProvenanceConfidence(
        bool? hasEDNS0,
        double? portEntropy,
        double? txidEntropy,
        bool? transportIsEncrypted,
        bool? resolverMatchesExpected)
    {
        // Ph5.2 contract fix: every signal is nullable. The original non-nullable bools made the
        // no-data regime UNREACHABLE (two signals were always "available", so the scorer could
        // never return null) — a probe that did not run must drop out of the denominator, not
        // inject a fabricated 0/0.1 reading.
        var signals = new List<double>();
        const int signalCount = 5;  // EDNS0, port, TXID, transport, resolver-match

        if (hasEDNS0 is not null) signals.Add(hasEDNS0.Value ? 0.2 : 0);
        if (portEntropy is not null) signals.Add(portEntropy.Value * 0.25);
        if (txidEntropy is not null) signals.Add(txidEntropy.Value * 0.2);
        if (transportIsEncrypted is not null) signals.Add(transportIsEncrypted.Value ? 0.2 : 0.1);
        if (resolverMatchesExpected is not null) signals.Add(resolverMatchesExpected.Value ? 0.15 : 0);

        int available = signals.Count;
        if (available == 0) return null;

        double rawScore = signals.Sum();

        // Full data regime
        if (available == signalCount)
            return Math.Max(0.0, Math.Min(1.0, rawScore));

        // Partial data regime
        double attenuated = rawScore * ((double)available / signalCount);
        if (available < signalCount / 2.0)
            attenuated = Math.Min(0.40, attenuated);
        return Math.Max(0.0, Math.Min(1.0, attenuated));
    }

    /// <summary>
    /// 2. Tamper Mechanism Fingerprint (DESIGN.md §2) — what kind of interference is occurring.
    /// No tamper signals → nil. Partial/conflicting → tentative fingerprint ≤ 0.35.
    /// Full consistent (≥3 signals converge on same class) → 0.35…1.0.
    /// </summary>
    public static (MechanismClass? MechanismClass, double? Confidence)? TamperMechanismFingerprint(
        IReadOnlyList<EvidenceItem> evidence,
        IReadOnlyList<SectionStatusEntry> sectionStatuses)
    {
        var threatHypos = new[]
        {
            Hypothesis.Spoofing, Hypothesis.Censorship, Hypothesis.InterceptionTransport,
            Hypothesis.InterceptionOnPath, Hypothesis.ContentTampering,
            Hypothesis.PrivacyLeak, Hypothesis.LocalCompromise,
        };

        // Filter to evidence with positive threat weight
        var positiveEvidence = evidence.Where(e =>
            threatHypos.Any(h => WeightRubric.GetWeight(e.EvidenceId, h) > 0)).ToList();
        if (positiveEvidence.Count == 0) return null;

        // Count signals per mechanism class
        var signalsPerClass = new Dictionary<MechanismClass, int>();
        foreach (var e in positiveEvidence)
        {
            var seenClasses = new HashSet<MechanismClass>();
            foreach (var h in threatHypos)
            {
                if (WeightRubric.GetWeight(e.EvidenceId, h) > 0)
                {
                    var mc = h.ToMechanismClass();
                    if (mc is not null && seenClasses.Add(mc.Value))
                        signalsPerClass[mc.Value] = signalsPerClass.GetValueOrDefault(mc.Value, 0) + 1;
                }
            }
        }

        if (signalsPerClass.Count == 0) return null;
        var topClass = signalsPerClass.MaxBy(kvp => kvp.Value);

        // Minimum corroboration (B005 §2): ≥2 signals from INDEPENDENT sources. Counted as
        // distinct evidence IDs — a single act echoing into multiple hypothesis classes is
        // still ONE signal, and two copies of the same reading are not independent. (The old
        // per-class sum let one lone evidence item fingerprint by itself.)
        int independentSignals = positiveEvidence.Select(e => e.EvidenceId).Distinct().Count();
        if (independentSignals < 2) return null;

        // Full consistent: ≥3 signals converge on same class
        if (topClass.Value >= 3)
        {
            double confidence = Math.Min(1.0, 0.35 + (topClass.Value - 3) * 0.15);
            return (topClass.Key, confidence);
        }

        // Partial/conflicting: tentative fingerprint ≤ 0.35
        double tentConfidence = Math.Min(0.35, 0.15 + (topClass.Value - 1) * 0.10);
        return (topClass.Key, tentConfidence);
    }
}
