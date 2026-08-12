using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// W20 — an observed indicator whose PRODUCING SECTION is not usable must contribute NOTHING.
///
/// THE DEFECT THIS EXISTS TO PREVENT
/// ---------------------------------
/// <c>MatrixDeterminativeEngine.ComputeRawScore</c> guarded its support loop only on "was this
/// indicator observed at all":
/// <code>
///     if (!indicatorById.TryGetValue(rule.IndicatorId, out var obs) || obs.ObservedStrength is not { } s) { continue; }
///     observedSupportWeight += rule.Weight;
/// </code>
/// There was no usability test. The required-section hard gate only inspects the sections a
/// diagnosis LISTS in <c>RequiredSections</c>, so an indicator produced by some OTHER section —
/// one that was never run (<c>Untested</c>) or whose premise failed (<c>Invalidated</c>) — scored
/// at full face value for every diagnosis that does not list that section. It reached:
///   * <c>observedSupportWeight</c>  → inflated the coverage numerator,
///   * the <c>Contradiction</c> denominator → deflated measured contradiction,
///   * <c>alignedSupport</c> / <c>ObservedSupport</c> / <c>SupportingIndicators</c>,
///   * <c>HasDecisive</c> / <c>MajorCount</c> → cleared the decisive-or-two-majors tier gate.
///
/// The last one is the sharp edge: evidence from a section that produced NO USABLE OUTPUT could
/// promote a diagnosis to <c>Conclusive</c>. The macOS twin has guarded this since B162
/// (MatrixDeterminativeEngine.swift:782-785) by skipping such an indicator before it contributes
/// anything; Windows did not, so the same evidence produced two different results.
///
/// WHY EACH FIXTURE IS SHAPED THE WAY IT IS
/// ----------------------------------------
/// A coverage-only assertion would pass over the live bug: coverage falling is a side effect of the
/// fix, not proof that the tier gate stopped seeing the ghost observation. So the fixtures below
/// hold coverage ABOVE <c>MinimumCoverageForConclusive</c> after the fix and let the tier turn
/// solely on <c>HasDecisive</c> (fixture B) and <c>MajorCount</c> (fixture C). Each asserts the
/// specific field, not just the final state.
/// </summary>
public class MatrixSectionUsabilityGuardTests
{
    private static readonly SectionId Prophet = new("prophet");
    private static readonly SectionId Mirror = new("mirror");
    private static readonly SectionId Portal = new("portal");
    private static readonly SectionId Glutton = new("glutton");
    private static readonly SectionId Cage = new("cage");
    private static readonly SectionId Witness = new("witness");

    /// The shipping threshold profile, transcribed from
    /// <c>SyxxMatrixIntegration.BuildDefaultSpec()</c>. Using the real constants means the fixtures
    /// below are calibrated against the gates the product actually applies.
    private static ThresholdProfile ShippingThresholds() => new(
        MinimumCoverageForProbable: 0.55,
        MinimumCoverageForConclusive: 0.80,
        MinimumFitForProbable: 0.62,
        MinimumFitForConclusive: 0.84,
        MinimumMarginForProbable: 0.08,
        MinimumMarginForConclusive: 0.18,
        MaxContradictionForConclusive: 0.10,
        MaxContradictionForProbable: 0.22,
        MinimumIndependentSupportGroupsForProbable: 1,
        MinimumIndependentSupportGroupsForConclusive: 2,
        RequireDecisiveOrTwoMajorsForConclusive: true,
        HardGateOnMissingRequiredSections: true,
        FailClosedOnInvalidatedRequiredSections: true);

    private static DiagnosisIndicatorRule Support(string indicator, double weight, EvidenceClass cls, string group)
        => new(new IndicatorId(indicator), IndicatorPolarity.Supports,
               ExpectedStrength: 0.90, Weight: weight, cls, IndependenceGroup: group);

    private static DiagnosisIndicatorRule Exclude(string indicator, double weight, string group)
        => new(new IndicatorId(indicator), IndicatorPolarity.Excludes,
               ExpectedStrength: 1.00, Weight: weight, EvidenceClass.Major, IndependenceGroup: group);

    /// One diagnosis, no required sections, no exclusionary indicators — so the ONLY thing under
    /// test is <c>ComputeRawScore</c>'s treatment of the support/exclude observations themselves.
    /// The required-section gate is deliberately out of the picture: it is exactly the gate that
    /// does NOT catch this case, which is why the defect existed.
    private static MatrixDeterminativeEngine EngineFor(params DiagnosisIndicatorRule[] rules)
        => new(new MatrixSpec(
            diagnoses: new[]
            {
                new DiagnosisSpec(
                    id: new DiagnosisId("probe"),
                    displayName: "Probe diagnosis",
                    rules: rules),
            },
            thresholds: ShippingThresholds()));

    /// Four sections ran and produced usable output; <paramref name="ghostSection"/> did not.
    private static IReadOnlyList<SectionResult> SectionsWith(SectionId ghostSection, EvidenceUsability ghostStatus)
        => new[] { Prophet, Mirror, Portal, Glutton, Cage, Witness }
            .Select(s => new SectionResult(s, s == ghostSection ? ghostStatus : EvidenceUsability.Passed))
            .ToList();

    private static ObservedIndicator Observed(string indicator, SectionId section)
        => new(new IndicatorId(indicator), section, ObservedStrength: 0.90, Quality: 1.0);

    /// The four usable minors that carry coverage in fixtures B and C. Weight 1.00 each, four
    /// distinct independence groups, all from sections that ran and produced usable output.
    private static DiagnosisIndicatorRule[] FourUsableMinors() => new[]
    {
        Support("usableMinorA", 1.00, EvidenceClass.Minor, "g1"),
        Support("usableMinorB", 1.00, EvidenceClass.Minor, "g2"),
        Support("usableMinorC", 1.00, EvidenceClass.Minor, "g3"),
        Support("usableMinorD", 1.00, EvidenceClass.Minor, "g4"),
    };

    private static List<ObservedIndicator> FourUsableMinorObservations() => new()
    {
        Observed("usableMinorA", Prophet),
        Observed("usableMinorB", Mirror),
        Observed("usableMinorC", Portal),
        Observed("usableMinorD", Cage),
    };

    // MARK: - Fixture B — a DECISIVE observation from an Invalidated section

    /// The ghost rule carries weight 0.10 so that after the guard lands coverage is
    /// 4.00 / 4.10 = 0.975610 — comfortably ABOVE <c>MinimumCoverageForConclusive</c> (0.80). Fit,
    /// contradiction, independent groups and margin all clear the conclusive bar too. The ONLY
    /// remaining conclusive gate is <c>RequireDecisiveOrTwoMajorsForConclusive</c>, and the only
    /// decisive-class rule in the spec is the one produced by the Invalidated section.
    ///
    /// So: <c>Probable</c> is achievable ONLY if the ghost stopped reaching <c>HasDecisive</c>.
    /// Before the fix this returned <c>Conclusive</c> with coverage 1.0 and the ghost listed among
    /// the supporting indicators.
    ///
    /// Failure case: remove the usability guard from the support loop → Conclusive → red.
    [Fact]
    public void DecisiveObservationFromInvalidatedSection_ReachesNeitherCoverageNorTheTierGate()
    {
        var engine = EngineFor(FourUsableMinors()
            .Append(Support("ghostDecisive", 0.10, EvidenceClass.Decisive, "g5"))
            .ToArray());

        var observations = FourUsableMinorObservations();
        observations.Add(Observed("ghostDecisive", Glutton));

        var result = engine.Evaluate(new EvidenceBundle(
            SectionsWith(Glutton, EvidenceUsability.Invalidated), observations));

        var score = Assert.Single(result.Diagnoses);

        // TIER GATE FIRST — this is the sharp edge, and asserting it first means the red output
        // names it rather than stopping at the coverage assertion below.
        // Everything except decisive-or-two-majors clears the conclusive bar (asserted explicitly
        // so a future threshold change cannot turn this into a coverage test by stealth)…
        Assert.True(score.Coverage >= 0.80, "fixture must hold coverage above the conclusive bar");
        Assert.Equal(1.0, score.Fit, 6);
        Assert.Equal(0.0, score.Contradiction, 6);
        Assert.True(score.Margin >= 0.18, "fixture must hold margin above the conclusive bar");
        // …therefore the tier can only be Probable if HasDecisive never saw the ghost.
        Assert.Equal(DiagnosisConfidence.Probable, score.Confidence);
        Assert.Equal(4, score.IndependentSupportGroups);

        // COVERAGE NUMERATOR: the ghost's 0.10 is out of the numerator but still in the
        // denominator, so coverage is 4.00/4.10, not 1.0.
        Assert.Equal(0.975610, score.Coverage, 6);

        // SUPPORTING INDICATORS: the ghost is not evidence for anything.
        Assert.DoesNotContain(new IndicatorId("ghostDecisive"), score.SupportingIndicators);
        Assert.Equal(4, score.SupportingIndicators.Count);
    }

    // MARK: - Fixture C — two MAJOR observations from an Untested section

    /// The <c>MajorCount &gt;= 2</c> half of the same tier gate, and the second non-usable status
    /// (<c>Untested</c> — the section never ran at all). Same weights, so coverage after the fix is
    /// again 4.00/4.10 = 0.975610, above the conclusive bar; the two ghost majors are the only
    /// thing that could satisfy decisive-or-two-majors.
    ///
    /// Failure case: guard only <c>Invalidated</c> rather than <c>!IsUsable()</c> → Conclusive → red.
    [Fact]
    public void TwoMajorObservationsFromUntestedSection_DoNotSatisfyTheTwoMajorsTierGate()
    {
        var engine = EngineFor(FourUsableMinors()
            .Concat(new[]
            {
                Support("ghostMajorX", 0.05, EvidenceClass.Major, "g5"),
                Support("ghostMajorY", 0.05, EvidenceClass.Major, "g6"),
            })
            .ToArray());

        var observations = FourUsableMinorObservations();
        observations.Add(Observed("ghostMajorX", Glutton));
        observations.Add(Observed("ghostMajorY", Glutton));

        var result = engine.Evaluate(new EvidenceBundle(
            SectionsWith(Glutton, EvidenceUsability.Untested), observations));

        var score = Assert.Single(result.Diagnoses);

        // Tier gate first, for the same reason as fixture B.
        Assert.True(score.Coverage >= 0.80, "fixture must hold coverage above the conclusive bar");
        Assert.Equal(1.0, score.Fit, 6);
        Assert.Equal(DiagnosisConfidence.Probable, score.Confidence);
        Assert.Equal(4, score.IndependentSupportGroups);
        Assert.Equal(0.975610, score.Coverage, 6);

        Assert.DoesNotContain(new IndicatorId("ghostMajorX"), score.SupportingIndicators);
        Assert.DoesNotContain(new IndicatorId("ghostMajorY"), score.SupportingIndicators);
        Assert.Equal(4, score.SupportingIndicators.Count);
    }

    // MARK: - Fixture D — the ghost was also DEFLATING contradiction

    /// <c>Contradiction = contradictionMass / (contradictionMass + observedSupportWeight)</c>.
    /// A ghost support observation inflates <c>observedSupportWeight</c>, which SHRINKS measured
    /// contradiction — real contradicting evidence reads weaker than it is because unusable
    /// evidence is padding the denominator. That is a false-clean direction and must be pinned
    /// separately from the coverage/tier assertions above.
    ///
    /// Weights are chosen so the ghost's 0.20 is exactly what carried contradiction under the
    /// conclusive cap: 0.30/(0.30+2.80) = 0.096774 &lt;= 0.10 with the bug, versus the honest
    /// 0.30/(0.30+2.60) = 0.103448 &gt; 0.10 after the fix.
    ///
    /// Failure case: remove the guard → Contradiction reads 0.096774 and the tier is Conclusive → red.
    [Fact]
    public void SupportObservationFromInvalidatedSection_DoesNotDeflateContradiction()
    {
        var engine = EngineFor(
            Support("usableMajorA", 1.30, EvidenceClass.Major, "g1"),
            Support("usableMajorB", 1.30, EvidenceClass.Major, "g2"),
            Support("ghostSupport", 0.20, EvidenceClass.Minor, "g3"),
            Exclude("realContradiction", 0.30, "g4"));

        var observations = new List<ObservedIndicator>
        {
            Observed("usableMajorA", Prophet),
            Observed("usableMajorB", Mirror),
            Observed("ghostSupport", Glutton),
            new(new IndicatorId("realContradiction"), Portal, ObservedStrength: 1.00, Quality: 1.0),
        };

        var result = engine.Evaluate(new EvidenceBundle(
            SectionsWith(Glutton, EvidenceUsability.Invalidated), observations));

        var score = Assert.Single(result.Diagnoses);

        // The honest contradiction, measured against usable support only.
        Assert.Equal(0.103448, score.Contradiction, 6);
        Assert.Equal(0.928571, score.Coverage, 6);
        // Two usable majors satisfy decisive-or-two-majors, coverage/fit/margin/groups all clear
        // the conclusive bar — so the tier turns purely on the contradiction cap (0.10).
        Assert.Equal(DiagnosisConfidence.Probable, score.Confidence);
        Assert.DoesNotContain(new IndicatorId("ghostSupport"), score.SupportingIndicators);
    }

    // MARK: - Fixture E — the guard removes SCORING, never the honesty overhang

    /// The carve-out, and the one place this deliberately diverges from the Swift twin.
    ///
    /// Swift's B162 guard <c>continue</c>s before <c>observedSupport</c> accumulates, which also
    /// deletes the observation from the overhang-ADMISSION measure. That measure is
    /// quality-independent by design (C062/C065): it answers "did a real observation FIRE," so a
    /// thin or incomplete section can attenuate evidential weight but must never HIDE a seen threat
    /// signal. Dropping it reopens the false-clean hole the overhang exists to close.
    ///
    /// Here the ONLY support the diagnosis has comes from an Invalidated section, and the
    /// diagnosis also REQUIRES that section — so it is hard-gated and can never be emitted. What
    /// must survive is the admission to <c>Suppressed</c>: the user is told a threat signal was
    /// seen but could not be verified, rather than being told nothing at all.
    ///
    /// Failure case: make the guard skip before <c>ObservedSupport</c> accumulates (i.e. copy the
    /// Swift twin verbatim) → <c>Suppressed</c> is empty → red. This is the same invariant
    /// <c>MatrixUntestedStateTests.UntestedSectionsWithObservedIndicators_IsNotUntested</c> pins
    /// against the shipping spec; pinned here too, against the mechanism rather than the spec.
    [Fact]
    public void ObservationFromNonUsableSectionStillReachesTheHonestyOverhang()
    {
        var engine = new MatrixDeterminativeEngine(new MatrixSpec(
            diagnoses: new[]
            {
                new DiagnosisSpec(
                    id: new DiagnosisId("probe"),
                    displayName: "Probe diagnosis",
                    rules: new[] { Support("ghostDecisive", 1.20, EvidenceClass.Decisive, "g1") },
                    requiredSections: new[] { Glutton }),
            },
            thresholds: ShippingThresholds()));

        var result = engine.Evaluate(new EvidenceBundle(
            SectionsWith(Glutton, EvidenceUsability.Invalidated),
            new List<ObservedIndicator> { Observed("ghostDecisive", Glutton) }));

        // It scored nothing, so it was never emitted…
        Assert.Empty(result.Diagnoses);
        Assert.NotEqual(DeterminativeState.Clean, result.State);
        Assert.Equal(DeterminativeState.Inconclusive, result.State);

        // …but the firing was NOT hidden.
        var overhang = Assert.Single(result.Suppressed);
        Assert.Equal("probe", overhang.Id.Value);
        Assert.Equal(SuppressedDiagnosis.SuppressionReason.InvalidatedRequiredSection, overhang.Reason);
        // Weight 1.20 × alignment 1.0 — un-attenuated, quality-independent, above the 0.30 floor.
        Assert.Equal(1.20, overhang.RawSupport, 6);
        // And the un-emittable diagnosis is what forbids a bare Clean.
        Assert.Contains("could not be verified", result.Explanation);
    }

    // MARK: - The guard must not over-reach

    /// An indicator whose producing section is <c>Inconclusive</c> — which IS usable output under
    /// <c>EvidenceUsabilityExtensions.IsUsable</c> (the section RAN and produced valid data that
    /// did not resolve) — must still contribute in full. Likewise the macOS guard is
    /// <c>if let producing = sectionByID[obs.section]</c>: an indicator whose section is ABSENT
    /// from the bundle is NOT skipped. Both halves are pinned so the fix cannot be over-applied
    /// into silently dropping working evidence.
    [Fact]
    public void ObservationsFromUsableOrUnlistedSectionsStillContributeInFull()
    {
        var engine = EngineFor(FourUsableMinors()
            .Append(Support("ghostDecisive", 0.10, EvidenceClass.Decisive, "g5"))
            .ToArray());

        var observations = FourUsableMinorObservations();
        observations.Add(Observed("ghostDecisive", Glutton));

        // Glutton RAN and produced valid-but-unresolved data ⇒ usable ⇒ full contribution.
        var usable = engine.Evaluate(new EvidenceBundle(
            SectionsWith(Glutton, EvidenceUsability.Inconclusive), observations));
        var usableScore = Assert.Single(usable.Diagnoses);
        Assert.Equal(1.0, usableScore.Coverage, 6);
        Assert.Contains(new IndicatorId("ghostDecisive"), usableScore.SupportingIndicators);
        Assert.Equal(DiagnosisConfidence.Conclusive, usableScore.Confidence);

        // Glutton absent from the bundle entirely — no status to consult, so no skip (Swift parity).
        var sectionsWithoutGlutton = new[] { Prophet, Mirror, Portal, Cage, Witness }
            .Select(s => new SectionResult(s, EvidenceUsability.Passed))
            .ToList();
        var unlisted = engine.Evaluate(new EvidenceBundle(sectionsWithoutGlutton, observations));
        var unlistedScore = Assert.Single(unlisted.Diagnoses);
        Assert.Equal(1.0, unlistedScore.Coverage, 6);
        Assert.Contains(new IndicatorId("ghostDecisive"), unlistedScore.SupportingIndicators);
        Assert.Equal(DiagnosisConfidence.Conclusive, unlistedScore.Confidence);
    }
}
