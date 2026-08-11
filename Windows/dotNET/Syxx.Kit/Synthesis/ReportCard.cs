using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit;

// A3 (network report card), re-derived onto the matrix determinative engine. Grades a network A–F
// with the top reasons: the shareable, glanceable summary of a run.
//
// MATRIX-MIGRATION-PLAN.md Phase 3.6 — the Windows twin of
// macOS/Sources/SyxxKit/Synthesis/ReportCard.swift.
//
// Previously graded from the removed `Conclusion.Diagnosis/Confidence/Severity`; now reads
// `Conclusion.Determination: DeterminativeResult?` (owner directive 2026-07-21).
//
// HONESTY CONTRACT — the load-bearing rule: a network we could not measure is UNGRADED, never a
// letter. An `A` on an unfinished scan is false safety; an `F` is a false accusation. `inconclusive`,
// no determination, or `clean` riding over a hard-gated section therefore returns `Ungraded` with
// the GAPS as its reasons — the honest "we don't know yet".
//
// COPY STATUS: user-visible strings PROPOSED, pending owner approval (AGENTS.md).

/// A–F, or Ungraded when the evidence does not support grading at all.
public enum Grade { A, B, C, D, F, Ungraded }

/// The A3 report card: a letter, a one-line headline, and the real top contributors.
public sealed record ReportCard(
    Grade Grade,
    string GradeLabel,
    string Headline,
    IReadOnlyList<string> TopReasons,
    Severity Severity);

public static class ReportCards
{
    public const int MaxReasons = 3;

    /// Severity of a threat diagnosis id, used to pick F/D/C and colour the card.
    private static Severity SeverityForDiagnosisId(string id) => id switch
    {
        "dnsSpoofing" or "dnsInterception" or "contentTampering" or "localCompromise" => Severity.Critical,
        "censorshipBlocking" or "transportDowngrade" or "dnsPrivacyLeak" => Severity.High,
        _ => Severity.Warning,
    };

    public static ReportCard For(Conclusion c)
    {
        bool incomplete = c.HasHardGatedSection;

        List<string> GapReasons()
        {
            var gaps = c.SectionStatuses
                .Where(s => s.Status.BlocksClean())
                .Take(MaxReasons)
                .Select(s => s.Status == SectionStatus.Untested
                    ? $"the {s.Section} check did not run"
                    : $"the {s.Section} check could not be trusted")
                .ToList();
            if (gaps.Count == 0)
                gaps.Add("the evidence did not commit to any conclusion");
            return gaps;
        }

        // Honesty gate — never grade what we did not measure.
        if (c.Determination is not { } det)
        {
            return new ReportCard(Grade.Ungraded, "N/A",
                "The determinative engine could not evaluate this run: finish the checks for a real grade.",
                GapReasons(), Severity.Unknown);
        }

        // Untested MUST be listed explicitly. It is neither Inconclusive nor Clean, so without this
        // it falls past both guards into the threat-grading path, where an empty diagnosis list
        // yields the `_ => Severity.Warning` default and grades a never-run scan **C** — a letter
        // grade for a network nobody looked at. Untested is its own state and it is ungraded.
        if (det.State == DeterminativeState.Untested)
        {
            return new ReportCard(Grade.Ungraded, "N/A",
                "Not enough evidence to grade this network yet: finish the checks for a real grade.",
                GapReasons(), Severity.Unknown);
        }

        // B310: Inconclusive with no threats detected is limited-confidence clean, not ungraded.
        // The engine may return Inconclusive because clean-support indicators are not yet wired
        // (C210), even when every section ran and found nothing wrong. Grading that as "N/A" is
        // misleading — the user sees an ungradeable scan when the evidence actually looks clean.
        // A `Clean` result with incomplete sections is also limited-confidence, not ungradeable.
        if (det.State == DeterminativeState.Inconclusive
            || (det.State == DeterminativeState.Clean && incomplete))
        {
            // When no threats were detected and the overhang is from clean-blocking (not
            // from suppressed threat signals), grade as B (limited confidence clean).
            bool hasThreatOverhang = det.Suppressed.Any(s =>
                s.Reason != SuppressedDiagnosis.SuppressionReason.HardExcluded
                && s.Id != new DiagnosisId("clean")
                && s.RawSupport >= 0.30);

            if (!hasThreatOverhang)
            {
                var reasons = c.SectionStatuses
                    .Where(s => s.Status == SectionStatus.Negative)
                    .Take(MaxReasons)
                    .Select(s => $"the {s.Section} check ran and found nothing wrong")
                    .ToList();
                if (reasons.Count == 0)
                    reasons.Add("the checks that ran found no signs of tampering or leakage");
                var headline = incomplete
                    ? "This network looks clean, but some checks could not be completed — limited confidence."
                    : "This network looks clean, but confidence is limited — some evidence was inconclusive.";
                return new ReportCard(Grade.B, "B", headline, reasons, Severity.Clean);
            }

            // Threat signals were suppressed — genuinely ungradeable.
            return new ReportCard(Grade.Ungraded, "N/A",
                "Not enough evidence to grade this network yet: possible threat signals could not be confirmed.",
                GapReasons(), Severity.Unknown);
        }

        if (det.State == DeterminativeState.Clean)
        {
            var solid = det.Diagnoses.FirstOrDefault()?.Confidence == DiagnosisConfidence.Conclusive && !incomplete;
            var grade = solid ? Grade.A : Grade.B;
            var headline = solid
                ? "This network looks clean: no tampering or leakage was found."
                : "This network looks clean, but confidence is limited: some checks were inconclusive.";
            var reasons = c.SectionStatuses
                .Where(s => s.Status == SectionStatus.Negative)
                .Take(MaxReasons)
                .Select(s => $"the {s.Section} check ran and found nothing wrong")
                .ToList();
            if (reasons.Count == 0)
                reasons.Add("the checks that ran found no signs of tampering or leakage");
            return new ReportCard(grade, grade.ToString(), headline, reasons, Severity.Clean);
        }

        // threatDetected
        var top = det.Diagnoses.FirstOrDefault();
        var sev = SeverityForDiagnosisId(top?.Id.Value ?? "");
        Grade threatGrade = sev switch
        {
            Severity.Critical => Grade.F,
            Severity.High => Grade.D,
            Severity.Warning => Grade.C,
            _ => Grade.Ungraded,
        };
        var name = top?.DisplayName ?? "Tampering";
        var threatHeadline = $"{name}: this network is not safe to trust as-is.";

        // C064: the top reasons come from the NAMED diagnosis's own supporting indicators
        var supportingIds = SynthesisEngine.EvidenceIdsSupporting(
            top?.SupportingIndicators ?? Array.Empty<IndicatorId>());
        var threatReasons = c.Evidence
            .Where(e => supportingIds.Contains(e.EvidenceId))
            .Select(e => e.Observation)
            .Take(MaxReasons)
            .ToList();
        if (threatReasons.Count == 0)
            threatReasons.Add($"the synthesis points to {name.ToLowerInvariant()}");

        return new ReportCard(threatGrade, threatGrade == Grade.Ungraded ? "N/A" : threatGrade.ToString(),
            threatHeadline, threatReasons, sev);
    }
}