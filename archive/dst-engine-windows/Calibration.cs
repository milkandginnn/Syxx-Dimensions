using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

/// <summary>
/// Ph7: Calibration harness + LLR-tier defaults for the WeightRubric.
/// Windows-parity twin of macOS/Sources/SyxxKit/Synthesis/Calibration.swift.
/// Corpus-dependent measurement (P7.1) deferred until O1 (labeled corpus) is resolved.
/// Defaults are expert-elicited and documented as such.
/// </summary>
public enum CalibrationTier
{
    VeryWeak,    // LLR < 0.6   (Bel < 0.45)
    Weak,        // LLR 0.6–1.2 (Bel 0.45–0.70)
    Moderate,    // LLR 1.2–2.0 (Bel 0.70–0.86)
    Strong,      // LLR 2.0–3.0 (Bel 0.86–0.95)
    VeryStrong,  // LLR > 3.0   (Bel > 0.95)
}

public static class CalibrationHarness
{
    public static CalibrationTier TierFor(double weight) => weight switch
    {
        < 0.6   => CalibrationTier.VeryWeak,
        < 1.2   => CalibrationTier.Weak,
        < 2.0   => CalibrationTier.Moderate,
        < 3.0   => CalibrationTier.Strong,
        _       => CalibrationTier.VeryStrong,
    };

    /// <summary>
    /// Ph7.1: run <paramref name="body"/> with <paramref name="overrides"/> applied over the
    /// default weight table, restoring the prior table afterwards even on throw. Scoped via
    /// AsyncLocal, so concurrent production syntheses never observe a calibration table.
    /// This is the Windows twin of macOS runWithWeights — which was a NO-OP parity break until
    /// this phase (loading a different table must actually change verdicts).
    /// </summary>
    public static T RunWithWeights<T>(
        IReadOnlyDictionary<(EvidenceId, Hypothesis), double> overrides, Func<T> body)
    {
        var prior = WeightRubric.ActiveOverrides.Value;
        WeightRubric.ActiveOverrides.Value = overrides;
        try { return body(); }
        finally { WeightRubric.ActiveOverrides.Value = prior; }
    }

    /// <summary>
    /// Ph7.1: parse a weight-table file (JSON object of "EvidenceId:Hypothesis" → weight, e.g.
    /// <c>{"NxCanaryHijack:Censorship": 2.5}</c>). Unknown evidence or hypothesis names throw —
    /// a typo silently ignored would calibrate the wrong table and report it as a real result.
    /// </summary>
    public static Dictionary<(EvidenceId, Hypothesis), double> ParseWeightTable(string json)
    {
        var raw = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, double>>(json)
            ?? throw new FormatException("Weight table must be a JSON object of \"EvidenceId:Hypothesis\" to number.");
        var table = new Dictionary<(EvidenceId, Hypothesis), double>();
        foreach (var (key, weight) in raw)
        {
            var parts = key.Split(':');
            if (parts.Length != 2
                || !Enum.TryParse<EvidenceId>(parts[0], ignoreCase: false, out var eid)
                || !Enum.TryParse<Hypothesis>(parts[1], ignoreCase: false, out var hypo))
            {
                throw new FormatException($"Unrecognized weight-table key '{key}' (expected \"EvidenceId:Hypothesis\").");
            }
            table[(eid, hypo)] = weight;
        }
        return table;
    }

    /// <summary>
    /// Returns the current expert-elicited weights as a tier-annotated report.
    /// </summary>
    public static List<(EvidenceId, Hypothesis, double, CalibrationTier)> ReportCurrentTiers()
    {
        var threatHypos = new[]
        {
            Hypothesis.Spoofing, Hypothesis.Censorship, Hypothesis.InterceptionTransport,
            Hypothesis.InterceptionOnPath, Hypothesis.ContentTampering,
            Hypothesis.PrivacyLeak, Hypothesis.LocalCompromise,
        };
        var result = new List<(EvidenceId, Hypothesis, double, CalibrationTier)>();
        foreach (var h in threatHypos)
        {
            foreach (EvidenceId eid in Enum.GetValues<EvidenceId>())
            {
                double w = WeightRubric.GetWeight(eid, h);
                if (w != 0)
                    result.Add((eid, h, w, TierFor(w)));
            }
        }
        return result;
    }
}
