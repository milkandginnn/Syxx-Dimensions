using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Windows half of the ALL-NEGATIVE cross-platform convergence guard (UNIFIED_IMPLEMENTATION_PLAN.md
/// Phase 7.1 QA-1). Twin of macOS/Tests/SyxxKitTests/MatrixAllNegativeParityTests.swift — same two
/// fixtures, same asserted states, so whichever platform drifts is the one that goes red.
///
/// THE DEFECT THIS EXISTS TO PREVENT
/// ---------------------------------
/// The retired Dempster-Shafer suite asserted that all-negative section statuses with NO observed
/// evidence committed to a reassuring clean result:
///     archive/dst-tests-windows/SynthesisEngineTests.cs:22-25
///         AllClean_isCleanResult_highConfidence_noReferral()
///         Assert.True(c.ReadsClean(), "an all-clean complete run must commit to clean");
/// The current matrix engine returns Inconclusive for that same input. For a period both suites
/// were green while asserting OPPOSITE answers to identical evidence, because each platform was
/// exercising a different engine. Retiring the DST engine removed the wrong answer but ALSO removed
/// the only test that pinned this scenario at all: after the archive move, NO test on EITHER
/// platform asserted what all-negative evidence produces. This file restores that assertion against
/// the engine that actually ships, rather than leaving the question unpinned.
///
/// WHY Inconclusive IS THE CORRECT ANSWER, NOT clean
/// -------------------------------------------------
/// Engine doc-comment, CRITICAL DISTINCTION (Syxx.Kit/Synthesis/MatrixDeterminativeEngine.cs:44-51):
/// Inconclusive means the check RAN and produced VALID data that did not resolve; it is USABLE
/// evidence and does NOT hard-gate. It is a decision state, not a synonym for "no result".
/// The clean branch (MatrixDeterminativeEngine.cs:1108-1116, macOS twin :894-901) emits Clean ONLY
/// when the `clean` diagnosis was itself AFFIRMATIVELY emitted — i.e. its supporting indicators were
/// actually observed. Sections that merely "ran and found nothing" observe no indicators at all, so
/// nothing supports clean and the engine abstains. Under the honesty spine (rule 2, missing data is
/// not clean data) that is the required behaviour: absence of observed threat evidence is not
/// evidence of absence, and the reassuring answer is the one that must earn its place.
/// </summary>
public class MatrixAllNegativeParityTests
{
    private static readonly string[] AllSections =
        { "prophet", "mirror", "portal", "glutton", "cage", "witness" };

    private static IReadOnlyList<SectionResult> AllSectionsRanAndPassed()
        => AllSections.Select(s => new SectionResult(new SectionId(s), EvidenceUsability.Passed)).ToList();

    /// FIXTURE A — the exact input the retired DST test called "an all-clean complete run":
    /// every section RAN and produced valid data, and not one indicator was observed.
    ///
    /// Failure case: if the clean branch ever stops requiring an affirmatively emitted `clean`
    /// diagnosis (e.g. someone makes clean the fall-through default), this returns Clean → red.
    [Fact]
    public void AllSectionsPassedWithNoObservedIndicators_IsInconclusiveNeverClean()
    {
        var bundle = new EvidenceBundle(AllSectionsRanAndPassed(), new List<ObservedIndicator>());

        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(bundle);

        // Assert the POSITIVE state. `!= Clean` alone would also pass on Untested/ThreatDetected.
        Assert.Equal(DeterminativeState.Inconclusive, result.State);
        Assert.NotEqual(DeterminativeState.Clean, result.State);
        // Sections RAN, so this is NOT the never-looked case either.
        Assert.NotEqual(DeterminativeState.Untested, result.State);
        // HARD INVARIANT: non-scored states are never emitted as a ranked diagnosis.
        Assert.Empty(result.Diagnoses);
    }

    /// FIXTURE B — what an EARNED clean actually requires: the same all-passed sections, plus the
    /// four indicators the `clean` DiagnosisSpec lists as Supports
    /// (SyxxMatrixIntegration.cs:217-220), actually observed at their expected strengths.
    ///
    /// This is the positive half of the guard and it is deliberate: before this file, every
    /// clean-related assertion in BOTH suites was a negative (`NotEqual(Clean)`), so the entire
    /// clean-emission path could have been dead code and both suites would still have been green.
    /// A suite that can only prove clean is NOT produced cannot detect that clean can never be
    /// produced — which would make the product unable to ever tell a user their DNS is fine.
    ///
    /// Failure case: break the clean branch or the clean spec's rules → returns Inconclusive → red.
    [Fact]
    public void AllSectionsPassedWithCleanSupportingIndicators_IsClean()
    {
        var bundle = new EvidenceBundle(
            AllSectionsRanAndPassed(),
            new List<ObservedIndicator>
            {
                new(new IndicatorId("resolverAgreement"), new SectionId("prophet"), 0.95),
                new(new IndicatorId("routeClean"), new SectionId("portal"), 0.90),
                new(new IndicatorId("trustedWitnessOK"), new SectionId("witness"), 0.95),
                new(new IndicatorId("localEvidenceSealValid"), new SectionId("cage"), 0.95),
            });

        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(bundle);

        Assert.Equal(DeterminativeState.Clean, result.State);
        Assert.Single(result.Diagnoses);
        Assert.Equal("clean", result.Diagnoses[0].Id.Value);
    }

    /// The wire value is the cross-platform contract that the CI parity gate
    /// (scripts/cross-platform-parity-gate.py) also asserts from source. Pinning it from a LIVE
    /// engine result too means a rename that changes what the engine actually emits — as opposed to
    /// what the enum declares — cannot pass unnoticed.
    [Fact]
    public void InconclusiveFromAllNegativeSerialisesToTheSharedWireValue()
    {
        var bundle = new EvidenceBundle(AllSectionsRanAndPassed(), new List<ObservedIndicator>());

        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(bundle);

        Assert.Equal("inconclusive", result.State.RawValue());
    }
}
