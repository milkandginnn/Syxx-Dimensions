using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// W03 (SynthesisEngine.cs:296 + :305) — NXDOMAIN/REFUSED/SERVFAIL must never produce
/// affirmative clean support, and must never count toward the <c>MajorCount &gt;= 2</c>
/// conclusive-clean requirement.
///
/// THE DEFECT THIS EXISTS TO PREVENT
/// ---------------------------------
/// The old mapping wired <c>ResolverStatus → ResolverAgreement (0.80)</c>: the resolver's error
/// RCODE readings — the exact readings that say "the measurement itself failed" — were graded as
/// MAJOR support for the <c>clean</c> diagnosis. Two or three failed resolvers would have cleared
/// the two-majors conclusive bar by themselves: a broken network reading as a securely clean one.
///
/// STATE OF THE REPAIR (HALT finding, same class as Tasks 1.2-1.3): triage.md:13 ratifies W03 as
/// repaired; <c>IndicatorMapping</c> contains no entry for any of the four clean-support
/// indicators, and the mapping header documents it as deliberate ("inconclusive over unearned
/// secure", triage N1). The red phase is degenerate; these tests are pins.
/// Failure case: re-add <c>[EvidenceId.ResolverStatus] = (ResolverAgreement, 0.80)</c> → tests
/// 1-3 red.
/// </summary>
public class ResolverStatusCleanSupportPinTests
{
    private static readonly SyxxIndicator[] CleanSupportIndicators =
    {
        SyxxIndicator.ResolverAgreement,
        SyxxIndicator.RouteClean,
        SyxxIndicator.TrustedWitnessOK,
        SyxxIndicator.LocalEvidenceSealValid,
    };

    private static Dictionary<Section, double> FullCompleteness() => new()
    {
        [Section.Prophet] = 1.0, [Section.Mirror] = 1.0, [Section.Portal] = 1.0,
        [Section.Glutton] = 1.0, [Section.Cage] = 1.0, [Section.Witness] = 1.0,
    };

    private static List<SyxxIndicatorInput> Map(params (EvidenceId Id, string Observation)[] items)
        => SynthesisEngine.MapEvidenceToIndicators(
            items.Select(i => EvidenceItem.Create(Section.Mirror, i.Id, i.Observation, "test")).ToList(),
            FullCompleteness());

    /// <summary>The three failure readings that used to mint clean support map to nothing clean.</summary>
    [Fact]
    public void ResolverStatusFailures_NeverMapToCleanSupport()
    {
        foreach (var reading in new[] { "NXDOMAIN", "REFUSED", "SERVFAIL" })
        {
            var mapped = Map((EvidenceId.ResolverStatus, reading));
            Assert.DoesNotContain(mapped, i => CleanSupportIndicators.Contains(i.Indicator));
        }
    }

    /// <summary>
    /// The structural pin, enumerated over the WHOLE EvidenceId vocabulary: no raw evidence —
    /// resolver statuses included — can ever emit a clean-support indicator. Clean support is
    /// unwired by design (N1), so no bundle can push clean toward MajorCount >= 2.
    /// </summary>
    [Fact]
    public void NoEvidenceIdEverMapsToCleanSupport()
    {
        var emitted = new HashSet<SyxxIndicator>();
        foreach (EvidenceId id in Enum.GetValues<EvidenceId>())
        {
            foreach (var input in Map((id, "test observation")))
                emitted.Add(input.Indicator);
        }

        Assert.Empty(emitted.Intersect(CleanSupportIndicators));
    }

    /// <summary>
    /// End-to-end: a run whose ONLY observations are five resolver failures — the old defect's
    /// "five Major supports" — must not read clean, and clean must not be emitted or overhang.
    /// </summary>
    [Fact]
    public void OnlyResolverStatusFailures_NeverYieldAffirmativeClean()
    {
        var evidence = Enumerable.Range(0, 5)
            .Select(n => EvidenceItem.Create(Section.Mirror, EvidenceId.ResolverStatus, "REFUSED", $"resolver failure {n}"))
            .ToList();
        var inputs = SynthesisEngine.MapEvidenceToIndicators(evidence, FullCompleteness());
        var bundle = new EvidenceBundle(
            new[]
            {
                new SectionResult(new SectionId("prophet"), EvidenceUsability.Passed),
                new SectionResult(new SectionId("mirror"), EvidenceUsability.Passed),
                new SectionResult(new SectionId("portal"), EvidenceUsability.Passed),
                new SectionResult(new SectionId("glutton"), EvidenceUsability.Passed),
                new SectionResult(new SectionId("cage"), EvidenceUsability.Passed),
                new SectionResult(new SectionId("witness"), EvidenceUsability.Passed),
            },
            inputs.Select(i => i.ToObservedIndicator()).ToList());

        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(bundle);

        Assert.NotEqual(DeterminativeState.Clean, result.State);
        Assert.DoesNotContain(result.Diagnoses, d => d.Id.Value == "clean");
        Assert.DoesNotContain(result.Suppressed, s => s.Id.Value == "clean");
    }
}
