using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// N1 — is the <c>clean</c> diagnosis reachable from a REAL run?
///
/// THE QUESTION
/// ------------
/// The <c>clean</c> DiagnosisSpec declares four Supports rules
/// (<c>SyxxMatrixIntegration.BuildDefaultSpec</c>): <c>resolverAgreement</c> (w 1.00),
/// <c>routeClean</c> (0.80), <c>trustedWitnessOK</c> (1.20), <c>localEvidenceSealValid</c> (0.70).
/// <c>SynthesisEngine.IndicatorMapping</c> — the ONLY writer of observed indicators on the real
/// pipeline — holds 14 entries, every one of them threat-side. If nothing populates those four,
/// then <c>clean</c>'s coverage is structurally 0 on every real run and the engine can never tell
/// a user their DNS is fine.
///
/// WHAT THESE TESTS ESTABLISH
/// --------------------------
/// The three tests below are a chain, deliberately split so a future change tells you WHICH link
/// moved rather than just "something changed":
///   1. the shipping <c>clean</c> spec still rests on exactly those four indicators;
///   2. no <c>EvidenceId</c> in the entire catalog produces any of them;
///   3. therefore a threat-free end-to-end run lands on <c>Inconclusive</c>, never <c>Clean</c>.
///
/// Test 2 is the mechanical proof of unreachability. If someone wires an affirmative-clean signal
/// into the evidence model it goes RED — which is the intended signal: N1 has changed, come back
/// and re-derive test 3.
///
/// WHAT THESE TESTS DO NOT CLAIM
/// -----------------------------
/// Unreachable-from-the-pipeline is NOT unreachable-from-the-engine. A caller that supplies the
/// four indicators directly does get <c>Clean</c> — that path is already pinned by
/// <c>MatrixAllNegativeParityTests.AllSectionsPassedWithCleanSupportingIndicators_IsClean</c>.
/// The gap is in the evidence model, not the matrix engine, and it is documented as deliberate at
/// <c>SynthesisEngine.cs</c> (<c>MapEvidenceToIndicators</c> doc-comment): "Clean-support
/// indicators … are intentionally NOT wired here … Consequently a benign run reaches
/// <c>.inconclusive</c> rather than <c>.clean</c> — the honest posture (inconclusive over unearned
/// secure) until those affirmative-clean signals are wired."
/// </summary>
public class CleanDiagnosisReachabilityTests
{
    private static readonly SyxxIndicator[] CleanSupportIndicators =
    {
        SyxxIndicator.ResolverAgreement,
        SyxxIndicator.RouteClean,
        SyxxIndicator.TrustedWitnessOK,
        SyxxIndicator.LocalEvidenceSealValid,
    };

    /// LINK 1 — the shipping spec's clean diagnosis rests on exactly these four Supports rules.
    /// Pinned from the live spec so a rule added or renamed there invalidates this analysis loudly
    /// rather than leaving tests 2 and 3 quietly reasoning about a spec that no longer exists.
    [Fact]
    public void CleanDiagnosisRestsOnExactlyTheFourAffirmativeCleanIndicators()
    {
        var spec = new SyxxMatrixBuilder().BuildDefaultSpec();
        var clean = Assert.Single(spec.Diagnoses.Where(d => d.Id.Value == "clean"));

        var supports = clean.Rules
            .Where(r => r.Polarity == IndicatorPolarity.Supports)
            .Select(r => r.IndicatorId.Value)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            CleanSupportIndicators.Select(i => i.Id().Value).OrderBy(v => v, StringComparer.Ordinal).ToList(),
            supports);
    }

    /// LINK 2 — the mechanical proof. Every <c>EvidenceId</c> the product can emit is pushed
    /// through the ONLY writer of observed indicators; not one of them yields a clean-support
    /// indicator, so those four are permanently absent from a real bundle.
    ///
    /// Failure case (intended): wire an affirmative-clean signal into <c>IndicatorMapping</c> →
    /// red → N1 has changed, re-derive the reachability conclusion below.
    [Fact]
    public void NoEvidenceIdInTheCatalogProducesAnyCleanSupportIndicator()
    {
        // One evidence item per EvidenceId, all attributed to a fully-complete Prophet section so
        // quality never zeroes an indicator out and hides it from this sweep.
        var evidence = Enum.GetValues<EvidenceId>()
            .Select(id => EvidenceItem.Create(Section.Prophet, id, observation: "sweep", rationale: "sweep"))
            .ToList();
        var completeness = new Dictionary<Section, double> { [Section.Prophet] = 1.0 };

        var produced = SynthesisEngine.MapEvidenceToIndicators(evidence, completeness)
            .Select(i => i.Indicator)
            .ToHashSet();

        // Sanity: the sweep really did exercise the mapper (otherwise an empty result would pass
        // this test for the wrong reason).
        Assert.NotEmpty(produced);

        foreach (var cleanIndicator in CleanSupportIndicators)
        {
            Assert.DoesNotContain(cleanIndicator, produced);
        }
    }

    /// LINK 3 — the end-to-end consequence. Every section ran and produced usable output at full
    /// completeness, and nothing threat-side was observed: the most favourable threat-free run the
    /// product can produce. Because nothing supports <c>clean</c>, its coverage and fit are 0 and
    /// the engine abstains.
    ///
    /// Failure case: if <c>clean</c> ever becomes reachable end-to-end this goes red, and that is
    /// correct — the honesty posture would have changed and must be re-argued, not silently drifted
    /// into. (An engine that returned Clean here WITHOUT affirmative support would be the
    /// false-clean defect the whole matrix design exists to prevent.)
    [Fact]
    public void ThreatFreeEndToEndRunReachesInconclusiveNotClean()
    {
        var statuses = Enum.GetValues<Section>()
            .Select(s => new SectionStatusEntry(s, SectionStatus.Negative, Completeness: 1.0))
            .ToList();

        var conclusion = SynthesisEngine.Synthesize(
            evidence: Array.Empty<EvidenceItem>(),
            sectionStatuses: statuses);

        var determination = Assert.IsType<DeterminativeResult>(conclusion.Determination);

        // Assert the POSITIVE state: `!= Clean` alone would also pass on Untested/ThreatDetected.
        Assert.Equal(DeterminativeState.Inconclusive, determination.State);
        // Sections RAN, so this is not the never-looked case either.
        Assert.NotEqual(DeterminativeState.Untested, determination.State);
        // Nothing was emitted at all — clean did not merely lose a ranking, it never scored.
        Assert.Empty(determination.Diagnoses);
        Assert.DoesNotContain(determination.Suppressed, s => s.Id.Value == "clean");
    }
}
