using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Windows half of the Untested-state parity guard for the matrix determinative engine.
///
/// THE DEFECT THIS EXISTS TO PREVENT: <c>DeterminativeState</c> shipped on Windows with only
/// three cases (Clean / ThreatDetected / Inconclusive) while the macOS twin
/// (MatrixDeterminativeEngine.swift:447-456) had four. A run in which nothing was measured
/// therefore reported <c>Inconclusive</c> on Windows and <c>untested</c> on macOS — the same
/// evidence, two different results, and an unmeasured network described to the user as an
/// ambiguous finding rather than as "we did not look".
///
/// WHY IT MATTERS (engine doc-comment, CRITICAL DISTINCTION, MatrixDeterminativeEngine.cs:45-50):
/// Inconclusive means the check RAN and produced VALID data that did not resolve — it is USABLE
/// evidence and does NOT hard-gate. Untested (never ran) and Invalidated (ran, data corrupt) are
/// NOT usable and DO hard-gate. Collapsing the three is a structural misclassification of working
/// evidence, and it is the reason Inconclusive is a decision state in its own right rather than a
/// synonym for "no result".
///
/// FAILURE CASES (each test is written to go RED against a specific regression — see per-test doc):
/// deleting the Untested enum case, deleting the all-untested guard in Evaluate, or letting any
/// renderer fall Untested through to a default/UNKNOWN/threat branch.
/// </summary>
public class MatrixUntestedStateTests
{
    private static SectionResult Untested(string id)
        => new(new SectionId(id), EvidenceUsability.Untested);

    private static SectionResult Inconclusive(string id)
        => new(new SectionId(id), EvidenceUsability.Inconclusive);

    private static SectionResult Passed(string id)
        => new(new SectionId(id), EvidenceUsability.Passed);

    private static readonly string[] AllSections =
        { "prophet", "mirror", "portal", "glutton", "cage", "witness" };

    private static EvidenceBundle AllUntestedBundle()
        => new(AllSections.Select(Untested).ToList(), new List<ObservedIndicator>());

    // MARK: - The state exists and is distinct

    /// Failure case: delete <c>DeterminativeState.Untested</c> → does not compile → red.
    /// Also asserts the four cases are mutually distinct, so Untested cannot be aliased to
    /// Inconclusive to "make it compile".
    [Fact]
    public void UntestedIsADistinctResultCase()
    {
        var all = new[]
        {
            DeterminativeState.Clean,
            DeterminativeState.ThreatDetected,
            DeterminativeState.Inconclusive,
            DeterminativeState.Untested,
        };
        Assert.Equal(4, all.Distinct().Count());
        Assert.NotEqual(DeterminativeState.Inconclusive, DeterminativeState.Untested);
    }

    /// The wire value must be "untested", byte-identical to the macOS
    /// `DeterminativeState.untested` raw value, or a signed cross-platform payload diverges.
    /// Failure case: map Untested to "inconclusive" in RawValue → red.
    [Fact]
    public void UntestedRawValueMatchesMacOSWireFormat()
    {
        Assert.Equal("untested", DeterminativeState.Untested.RawValue());
    }

    /// Round-trips through the JSON converter. Failure case: omit the "untested" arm of the
    /// converter's Read switch → throws JsonException → red.
    [Fact]
    public void UntestedRoundTripsThroughJson()
    {
        var opts = new JsonSerializerOptions();
        opts.Converters.Add(new DeterminativeStateJsonConverter());

        var json = JsonSerializer.Serialize(DeterminativeState.Untested, opts);
        Assert.Equal("\"untested\"", json);
        Assert.Equal(DeterminativeState.Untested,
                     JsonSerializer.Deserialize<DeterminativeState>(json, opts));
    }

    // MARK: - The engine emits it

    /// Every section untested AND no indicators observed ⇒ Untested, never Inconclusive.
    /// This is the exact conflation the guard prevents.
    /// Failure case: delete the all-untested guard in Evaluate → falls through to the abstention
    /// path → returns Inconclusive → red.
    [Fact]
    public void AllUntestedSectionsWithNoIndicators_IsUntestedNotInconclusive()
    {
        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(AllUntestedBundle());

        Assert.Equal(DeterminativeState.Untested, result.State);
        Assert.NotEqual(DeterminativeState.Inconclusive, result.State);
        Assert.Empty(result.Diagnoses);
        Assert.Equal("No sections have been evaluated yet.", result.Explanation);
    }

    /// The converse, and the reason the guard is two-part: sections that RAN and produced valid
    /// but ambiguous data are Inconclusive, NOT Untested. Failure case: widen the guard to fire on
    /// any non-passed status → returns Untested for real ambiguous evidence → red.
    [Fact]
    public void AllInconclusiveSections_IsInconclusiveNotUntested()
    {
        var bundle = new EvidenceBundle(
            AllSections.Select(Inconclusive).ToList(), new List<ObservedIndicator>());

        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(bundle);

        // Assert the POSITIVE state. `!= Untested` alone would also pass on Clean, so the
        // test named "IsInconclusiveNotUntested" could survive a false-clean regression.
        Assert.Equal(DeterminativeState.Inconclusive, result.State);
        Assert.Empty(result.Diagnoses);
    }

    /// Indicators are real measured data even when the sections did not complete, so their
    /// presence must defeat the Untested shortcut and force real evaluation.
    /// Failure case: drop the `noIndicators` half of the guard → observed evidence is discarded
    /// and the run reports Untested → red.
    [Fact]
    public void UntestedSectionsWithObservedIndicators_IsNotUntested()
    {
        var bundle = new EvidenceBundle(
            AllSections.Select(Untested).ToList(),
            new List<ObservedIndicator>
            {
                new(new IndicatorId("authoritativeMismatch"), new SectionId("prophet"), 0.95),
            });

        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(bundle);

        // THE FALSE-CLEAN HOLE this test exists to guard: a strong (0.95) threat indicator
        // sits behind untested required sections. Asserting only `!= Untested` would pass if
        // the gate regressed and the engine returned Clean — the exact scenario. Assert the
        // positive state AND that the diagnosis was gated for the right reason.
        Assert.Equal(DeterminativeState.Inconclusive, result.State);
        Assert.NotEqual(DeterminativeState.Clean, result.State);
        Assert.Empty(result.Diagnoses);
        Assert.Contains(result.Suppressed, s => s.Id.Value == "dnsSpoofing");
    }

    /// Untested must never read as a clean or reassuring outcome — the honesty spine's rule 2
    /// (missing data is not clean data).
    /// Failure case: map Untested to Clean anywhere in the engine → red.
    [Fact]
    public void UntestedIsNeverCleanAndNeverAThreat()
    {
        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(AllUntestedBundle());

        Assert.NotEqual(DeterminativeState.Clean, result.State);
        Assert.NotEqual(DeterminativeState.ThreatDetected, result.State);
        Assert.Empty(result.Diagnoses);
    }

    // MARK: - Renderers keep it distinct

    /// PlainExplanation must have its own Untested arm. Without it, Untested falls through the
    /// switch to the ThreatDetected branch and an unmeasured run is narrated as a threat.
    /// Failure case: delete the Untested case in PlainExplanation.cs → red on both asserts.
    [Fact]
    public void PlainExplanation_UntestedSaysNothingRanAndNamesNoThreat()
    {
        var conclusion = ConclusionFrom(AllUntestedBundle());
        var explanation = PlainExplanations.For(conclusion);

        Assert.Equal("No sections have been evaluated yet — run a scan to see results",
                     explanation.Summary);
        Assert.Empty(explanation.Because);
        // The Inconclusive copy must not be reused: it asserts a completed-but-unresolved run.
        Assert.DoesNotContain("could not confirm", explanation.Summary);
    }

    /// The report's result line must distinguish Untested from Inconclusive and from the bare
    /// "UNKNOWN" default. Failure case: remove the Untested arm in Report.cs → renders "UNKNOWN" → red.
    [Fact]
    public void Report_UntestedRendersItsOwnLabel()
    {
        var report = new SynthesisReport(ConclusionFrom(AllUntestedBundle()),
                                         new List<MergedDatapoint>());
        var text = report.RenderText();

        Assert.Contains("Conclusion: NOT YET TESTED", text);
        Assert.DoesNotContain("Conclusion: INCONCLUSIVE", text);
        Assert.DoesNotContain("Conclusion: UNKNOWN", text);
    }

    /// A grade must not be invented for a run that never happened.
    /// Failure case: let Untested reach the Clean grading branch → red.
    [Fact]
    public void ReportCard_UntestedIsUngraded()
    {
        var card = ReportCards.For(ConclusionFrom(AllUntestedBundle()));

        Assert.Equal(Grade.Ungraded, card.Grade);
        Assert.NotEqual(Severity.Clean, card.Severity);
    }

    /// THE WORST CONFLATION: ResultActions.For uses if-guards, not an exhaustive switch, so
    /// before the fix an Untested result fell to the threat arm and rendered
    /// "Your DNS shows signs of tampering." at Severity.High — a fabricated threat result for a
    /// scan that never ran. Failure case: delete the Untested arm in ResultAction.cs → red.
    [Fact]
    public void ResultAction_UntestedIsNeverReportedAsTampering()
    {
        var action = ResultActions.For(ConclusionFrom(AllUntestedBundle()));

        Assert.DoesNotContain("tampering", action.Meaning, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(Severity.High, action.Severity);
        Assert.NotEqual(Severity.Critical, action.Severity);
        Assert.Equal(Severity.Unknown, action.Severity);
        Assert.True(action.Qualified, "an unmeasured run must be marked qualified");
        // ...and equally must not read as clean.
        Assert.DoesNotContain("no tampering", action.Meaning, StringComparison.OrdinalIgnoreCase);
    }

    private static Conclusion ConclusionFrom(EvidenceBundle bundle)
    {
        var det = SyxxMatrixIntegration.MakeEngine().Evaluate(bundle);
        return new Conclusion(
            DateTimeOffset.UnixEpoch.UtcDateTime,
            new List<SectionStatusEntry>(),
            new List<EvidenceItem>(),
            new List<Advisory>(),
            det);
    }
}
