using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// W02 (ReportCard.cs:88 + :83) — an INCONCLUSIVE or UNTESTED run must never yield a letter
/// grade, a clean severity, or a "looks clean" headline.
///
/// THE DEFECT THIS EXISTS TO PREVENT
/// ---------------------------------
/// The original card graded <c>DeterminativeState.Inconclusive</c> as B / <c>Severity.Clean</c>
/// with the "This network looks clean" headline, and its <c>hasThreatOverhang</c> helper skipped
/// <c>HardExcluded</c> suppressions — both contradicting the file's own honesty-contract header:
/// "a network we could not measure is UNGRADED, never a letter."
///
/// STATE OF THE REPAIR (recorded as a HALT finding for Task 1.2): triage.md:13 ratifies W02 as
/// already repaired, and the fix is in-tree — the Untested/Inconclusive honesty gate at
/// ReportCard.cs:76-83, and the overhang concept itself now lives in the ENGINE (Assemble,
/// MatrixDeterminativeEngine.cs:1146, C065), where it decides Clean-vs-Inconclusive; the card no
/// longer re-derives overhang from suppressions. The red phase of this task therefore degenerates
/// into a PIN: the tests below pass against the current tree and exist so the repair can never be
/// silently reverted. Failure case: remove the gate at ReportCard.cs:76-83 → tests 1-4 red.
/// </summary>
public class ReportCardInconclusiveHonestyTests
{
    private static Conclusion Run(DeterminativeState state,
                                  IReadOnlyList<SuppressedDiagnosis> suppressed,
                                  IReadOnlyList<SectionStatus> statuses,
                                  DiagnosisScore? emitted = null)
    {
        var determination = new DeterminativeResult(
            state,
            emitted is null ? Array.Empty<DiagnosisScore>() : new[] { emitted },
            suppressed,
            "test determination");

        var entries = Enum.GetValues<Section>()
            .Zip(statuses, (section, status) => new SectionStatusEntry(section, status))
            .ToList();

        return new Conclusion(
            DateTimeOffset.UnixEpoch,
            entries,
            Array.Empty<EvidenceItem>(),
            Array.Empty<Advisory>(),
            determination);
    }

    private static readonly SectionStatus[] AllPassed =
        { SectionStatus.Negative, SectionStatus.Negative, SectionStatus.Negative,
          SectionStatus.Negative, SectionStatus.Negative, SectionStatus.Negative };

    private static readonly SectionStatus[] OneUntested =
        { SectionStatus.Untested, SectionStatus.Negative, SectionStatus.Negative,
          SectionStatus.Negative, SectionStatus.Negative, SectionStatus.Negative };

    private static readonly SuppressedDiagnosis HardExcludedThreat = new(
        new DiagnosisId("dnsSpoofing"), "DNS spoofing / poisoning",
        SuppressedDiagnosis.SuppressionReason.HardExcluded,
        RawStrength: 0.292683, RawSupport: 1.2,
        "Hard-excluded by a firing exclusionary indicator.");

    /// <summary>
    /// The sharp edge: a run whose threat was HARD-EXCLUDED (a real threat signal existed, an
    /// exclusionary indicator fired, the run abstains). Inconclusive must be ungraded — the
    /// "looks clean" headline on an unverified threat signal is the exact false safety the
    /// honesty contract forbids.
    /// </summary>
    [Fact]
    public void InconclusiveWithHardExcludedThreatOverhang_IsUngradedAndNeverLooksClean()
    {
        var card = ReportCards.For(Run(DeterminativeState.Inconclusive,
            new[] { HardExcludedThreat }, AllPassed));

        Assert.Equal(Grade.Ungraded, card.Grade);
        Assert.Equal("N/A", card.GradeLabel);
        Assert.Equal(Severity.Unknown, card.Severity);
        Assert.DoesNotContain("looks clean", card.Headline, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Plain inconclusive (no suppression at all) is ungraded for the same reason.</summary>
    [Fact]
    public void InconclusiveWithoutSuppression_IsUngraded()
    {
        var card = ReportCards.For(Run(DeterminativeState.Inconclusive,
            Array.Empty<SuppressedDiagnosis>(), AllPassed));

        Assert.Equal(Grade.Ungraded, card.Grade);
        Assert.Equal(Severity.Unknown, card.Severity);
        Assert.DoesNotContain("looks clean", card.Headline, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Untested is its own state, not Inconclusive: without the explicit arm at ReportCard.cs:76
    /// it falls into the threat path and a never-run scan grades C.
    /// </summary>
    [Fact]
    public void UntestedDetermination_IsUngraded()
    {
        var card = ReportCards.For(Run(DeterminativeState.Untested,
            Array.Empty<SuppressedDiagnosis>(), OneUntested));

        Assert.Equal(Grade.Ungraded, card.Grade);
        Assert.Equal("N/A", card.GradeLabel);
        Assert.Equal(Severity.Unknown, card.Severity);
    }

    /// <summary>
    /// The :78 arm: a Clean determination riding over a hard-gated (untested/invalidated) section
    /// is incomplete evidence, not a B. Clean may only be graded when every required check ran.
    /// </summary>
    [Fact]
    public void CleanRidingOverAHardGatedSection_IsUngradedNotB()
    {
        var cleanDiagnosis = new DiagnosisScore(
            new DiagnosisId("clean"), "Clean", DiagnosisConfidence.Conclusive,
            Strength: 0.9, Fit: 0.95, Coverage: 0.9, Margin: 0.8, Contradiction: 0.0,
            IndependentSupportGroups: 3, Array.Empty<IndicatorId>(), "test");

        var card = ReportCards.For(Run(DeterminativeState.Clean,
            Array.Empty<SuppressedDiagnosis>(), OneUntested, cleanDiagnosis));

        Assert.Equal(Grade.Ungraded, card.Grade);
        Assert.Equal(Severity.Unknown, card.Severity);
        Assert.DoesNotContain("looks clean", card.Headline, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Negative control — the gate must not over-reach: a fully measured clean run still earns
    /// its letter and its "looks clean" headline, and a detected threat still grades as a threat.
    /// Without these, tests 1-4 could be satisfied by a card that is ungraded for EVERYTHING.
    /// </summary>
    [Fact]
    public void FullyMeasuredCleanAndDetectedThreat_StillGrade()
    {
        var cleanDiagnosis = new DiagnosisScore(
            new DiagnosisId("clean"), "Clean", DiagnosisConfidence.Conclusive,
            Strength: 0.9, Fit: 0.95, Coverage: 0.9, Margin: 0.8, Contradiction: 0.0,
            IndependentSupportGroups: 3, Array.Empty<IndicatorId>(), "test");
        var cleanCard = ReportCards.For(Run(DeterminativeState.Clean,
            Array.Empty<SuppressedDiagnosis>(), AllPassed, cleanDiagnosis));
        Assert.Equal(Grade.A, cleanCard.Grade);
        Assert.Equal(Severity.Clean, cleanCard.Severity);
        Assert.Contains("looks clean", cleanCard.Headline, StringComparison.OrdinalIgnoreCase);

        var threatDiagnosis = new DiagnosisScore(
            new DiagnosisId("dnsSpoofing"), "DNS spoofing / poisoning", DiagnosisConfidence.Conclusive,
            Strength: 0.7, Fit: 0.95, Coverage: 0.74, Margin: 0.6, Contradiction: 0.0,
            IndependentSupportGroups: 2, Array.Empty<IndicatorId>(), "test");
        var threatCard = ReportCards.For(Run(DeterminativeState.ThreatDetected,
            Array.Empty<SuppressedDiagnosis>(), AllPassed, threatDiagnosis));
        Assert.Equal(Grade.F, threatCard.Grade);
        Assert.Equal(Severity.Critical, threatCard.Severity);
        Assert.DoesNotContain("looks clean", threatCard.Headline, StringComparison.OrdinalIgnoreCase);
    }
}
