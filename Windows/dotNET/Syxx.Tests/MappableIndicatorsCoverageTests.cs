using System.Collections.Generic;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// F-09 / B156 — the coverage denominator must be normalised over *mappable* indicator weight only.
/// Rules referencing indicators with no reachable EvidenceId mapping are permanent dead weight:
/// they can never be observed, so counting them hard-caps coverage below the emission thresholds
/// and makes entire diagnoses unreachable.
///
/// This filter existed only on the Swift side until 2026-08-05; the C# engine counted all rules.
/// The shared golden vectors do NOT discriminate on it (verified by neutralizing the filter —
/// gv-1/gv-3 pass either way, and gv-3's stored coverage 0.560976 equals the *unfiltered* ratio
/// 2.3/4.1, so that number predates B156). This test is the discriminating evidence: it fails if
/// the filter is removed.
///
/// Twin of macOS/Tests/SyxxKitTests/MappableIndicatorsCoverageTests.swift.
/// </summary>
public class MappableIndicatorsCoverageTests
{
    // Half the support weight is an unmappable indicator. Observed: only the mappable one, fully.
    //   filtered denominator   = 1.0  -> coverage 1.0  >= 0.55 -> emitted
    //   unfiltered denominator = 2.0  -> coverage 0.5  <  0.55 -> not emitted
    private static MatrixSpec SpecWith(IReadOnlySet<IndicatorId>? mappable) => new(
        diagnoses: new[]
        {
            new DiagnosisSpec(
                new DiagnosisId("coverageProbe"),
                "Coverage probe",
                new[]
                {
                    new DiagnosisIndicatorRule(new IndicatorId("mappedSignal"), IndicatorPolarity.Supports,
                        ExpectedStrength: 0.9, Weight: 1.0, EvidenceClass.Decisive, "g1"),
                    new DiagnosisIndicatorRule(new IndicatorId("ghostSignal"), IndicatorPolarity.Supports,
                        ExpectedStrength: 0.9, Weight: 1.0, EvidenceClass.Major, "g2"),
                },
                requiredSections: new[] { new SectionId("prophet") }),
        },
        thresholds: new ThresholdProfile(
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
            FailClosedOnInvalidatedRequiredSections: true),
        mappableIndicators: mappable);

    private static EvidenceBundle FullyObservedMappedSignal() => new(
        new[] { new SectionResult(new SectionId("prophet"), EvidenceUsability.Passed) },
        new[] { new ObservedIndicator(new IndicatorId("mappedSignal"), new SectionId("prophet"), 0.9) });

    [Fact]
    public void MappableFilter_ExcludesDeadWeightFromCoverage_DiagnosisEmitted()
    {
        var engine = new MatrixDeterminativeEngine(
            SpecWith(new HashSet<IndicatorId> { new("mappedSignal") }));
        var result = engine.Evaluate(FullyObservedMappedSignal());

        Assert.Equal(DeterminativeState.ThreatDetected, result.State);
        Assert.Single(result.Diagnoses);
        Assert.Equal("coverageProbe", result.Diagnoses[0].Id.Value);
    }

    /// Legacy semantics pinned: with MappableIndicators null, ALL rules count and the same
    /// evidence must NOT reach the probable tier. This also proves the two specs genuinely
    /// produce different outcomes, so the test above cannot pass by accident.
    [Fact]
    public void NullMappable_LegacyDenominator_DiagnosisNotEmitted()
    {
        var engine = new MatrixDeterminativeEngine(SpecWith(null));
        var result = engine.Evaluate(FullyObservedMappedSignal());

        Assert.Equal(DeterminativeState.Inconclusive, result.State);
        Assert.Empty(result.Diagnoses);
    }
}
