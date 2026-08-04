using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

// A2 (plain-language explanation layer), re-derived onto the matrix determinative engine.
//
// MATRIX-MIGRATION-PLAN.md Phase 3.6 — the Windows twin of
// macOS/Sources/SyxxKit/Synthesis/PlainExplanation.swift.
//
// Previously this rendered a Dempster-Shafer synthesis ("we believe X because A and B, despite C")
// off `Conclusion.Diagnosis/Confidence/Probabilities/evidence[].Supports`. That engine was removed
// (owner directive 2026-07-21) and replaced by `MatrixDeterminativeEngine`, whose output lives on
// `Conclusion.Determination: DeterminativeResult?`. This file now composes the same because/despite
// shape from that result.
//
// HONESTY CONTRACT: an inconclusive / hard-gated / no-determination result is explained as
// UNFINISHED, never "clean"; the "despite" list (section gaps + unverified threat overhang) is never
// suppressed to make a result look cleaner.
//
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md).

/// The A2 plain-language explanation of a Conclusion.
public sealed record PlainExplanation(string Summary, IReadOnlyList<string> Because, IReadOnlyList<string> Despite)
{
    /// One-paragraph prose: "&lt;summary&gt; because &lt;a&gt; and &lt;b&gt;. That said, &lt;c&gt; and &lt;d&gt;."
    public string Narrative
    {
        get
        {
            var s = Summary;
            if (Because.Count > 0) s += " because " + JoinClauses(Because);
            s += ".";
            if (Despite.Count > 0) s += " That said, " + JoinClauses(Despite) + ".";
            return s;
        }
    }

    private static string JoinClauses(IReadOnlyList<string> parts)
    {
        if (parts.Count == 1) return parts[0];
        return string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
    }
}

public static class PlainExplanations
{
    private const int MaxBecause = 3;
    private const int MaxDespite = 3;

    public static PlainExplanation For(Conclusion c)
    {
        List<string> SectionGaps()
        {
            var gaps = new List<string>();
            foreach (var s in c.SectionStatuses)
            {
                if (s.Status is SectionStatus.Positive or SectionStatus.Negative) continue;
                var name = s.Section.ToString();
                gaps.Add(s.Status switch
                {
                    SectionStatus.Untested => $"the {name} check did not run",
                    SectionStatus.Invalidated => $"the {name} check could not be trusted",
                    SectionStatus.Degraded => $"the {name} check was degraded",
                    SectionStatus.Inconclusive => $"the {name} check was inconclusive",
                    _ => $"the {name} check was incomplete",
                });
            }
            return gaps;
        }

        // No determination at all: the engine could not run. Honest "unfinished".
        if (c.Determination is not { } det)
        {
            return new PlainExplanation(
                "We could not determine whether your DNS is trustworthy — this result is unfinished, not clean",
                Array.Empty<string>(),
                SectionGaps().Take(MaxDespite).ToList());
        }

        switch (det.State)
        {
            // Untested is its own state: nothing ran, so there is nothing to explain — not an
            // ambiguous finding (Inconclusive) and emphatically not a threat. Without this case
            // an unmeasured run falls through to the ThreatDetected branch below. Mirrors macOS
            // PlainExplanation.swift:77-81.
            case DeterminativeState.Untested:
                return new PlainExplanation(
                    "No sections have been evaluated yet — run a scan to see results",
                    Array.Empty<string>(),
                    Array.Empty<string>());

            case DeterminativeState.Inconclusive:
                var despite = det.Suppressed
                    .Select(s => $"a possible {s.DisplayName.ToLowerInvariant()} signal could not be verified")
                    .ToList();
                despite.AddRange(SectionGaps());
                return new PlainExplanation(
                    "We could not confirm whether your DNS is trustworthy — this result is unfinished, not clean",
                    Array.Empty<string>(),
                    despite.Take(MaxDespite).ToList());

            case DeterminativeState.Clean:
                var because = c.SectionStatuses
                    .Where(s => s.Status == SectionStatus.Negative)
                    .Take(MaxBecause)
                    .Select(s => $"the {s.Section} check ran and found nothing wrong")
                    .ToList();
                if (because.Count == 0)
                    because.Add("the checks that ran found no signs of tampering or leakage");
                return new PlainExplanation(
                    "We believe your DNS is clean",
                    because,
                    SectionGaps().Take(MaxDespite).ToList());

            case DeterminativeState.ThreatDetected:
                var top = det.Diagnoses.FirstOrDefault();
                var summary = top is not null
                    ? $"We believe this is {top.DisplayName.ToLowerInvariant()}"
                    : "We believe your DNS is being tampered with";

                // BECAUSE (C064) — ONLY observations that actually support the NAMED diagnosis
                var supportingIds = SynthesisEngine.EvidenceIdsSupporting(
                    top?.SupportingIndicators ?? Array.Empty<IndicatorId>());
                var becauses = c.Evidence
                    .Where(e => supportingIds.Contains(e.EvidenceId))
                    .Select(e => e.Observation)
                    .Take(MaxBecause)
                    .ToList();
                if (becauses.Count == 0)
                {
                    // C064: an emitted diagnosis whose supporting indicators match no ledger evidence
                    // falls back to a generic clause. This should not happen silently — report it.
                    SyxxDiagnostics.Capture(new DiagnosticEvent(
                        ErrorClass: "surface.unsupported_diagnosis",
                        Component: "PlainExplanation",
                        Section: null,
                        Impact: MeasurementImpact.None,
                        Severity: DiagnosticSeverity.Warning,
                        Underlying: $"C064: diagnosis {top?.DisplayName ?? "(unknown)"} has no matching evidence observations for its supporting indicators",
                        Fingerprint: new[] { "surface.unsupported_diagnosis", top?.DisplayName ?? "(unknown)" },
                        Tags: null));
                    becauses.Add("the determinative engine recorded supporting evidence for this diagnosis (details in the full evidence list)");
                }

                // DESPITE — a runner-up diagnosis, section gaps, unverified overhang.
                var despites = new List<string>();
                if (det.Diagnoses.Count > 1)
                {
                    despites.Add($"there are also signs of {det.Diagnoses[1].DisplayName.ToLowerInvariant()}");
                }
                despites.AddRange(det.Suppressed
                    .Select(s => $"a possible {s.DisplayName.ToLowerInvariant()} signal could not be verified"));
                despites.AddRange(SectionGaps());

                return new PlainExplanation(
                    summary,
                    becauses,
                    despites.Take(MaxDespite).ToList());

            default:
                return new PlainExplanation(
                    "We could not determine whether your DNS is trustworthy — this result is unfinished, not clean",
                    Array.Empty<string>(),
                    SectionGaps().Take(MaxDespite).ToList());
        }
    }
}