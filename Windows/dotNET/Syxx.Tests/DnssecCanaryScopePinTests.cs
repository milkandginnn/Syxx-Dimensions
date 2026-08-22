using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// W01 (SynthesisEngine.cs:326) — a DNSSEC canary success is measured on the CONTROL domain,
/// not the target domain, and must therefore be able to hard-exclude NOTHING for the target.
///
/// THE DEFECT THIS EXISTS TO PREVENT
/// ---------------------------------
/// The old mapping wired <c>DnssecCanarySecure → TrustedWitnessOK (0.75)</c>. Because
/// <c>trustedWitnessOK</c> is an exclusionary indicator of <c>dnsSpoofing</c> and
/// <c>dnsInterception</c> (firing at/above the 0.5 exclusion threshold hard-excludes both), a
/// healthy canary on a control domain suppressed spoofing diagnoses for a target domain that the
/// canary never touched — a control-plane reading cancelling a data-plane threat.
///
/// STATE OF THE REPAIR (HALT finding, same class as Task 1.2): triage.md:13 ratifies W01 as
/// repaired; <c>DnssecCanarySecure</c> has no entry in <c>IndicatorMapping</c>. The red phase is
/// degenerate; these tests are pins. Failure case: re-add
/// <c>[EvidenceId.DnssecCanarySecure] = (TrustedWitnessOK, 0.75)</c> → tests 1-2 red.
/// </summary>
public class DnssecCanaryScopePinTests
{
    private static Dictionary<Section, double> FullCompleteness() => new()
    {
        [Section.Prophet] = 1.0, [Section.Mirror] = 1.0, [Section.Portal] = 1.0,
        [Section.Glutton] = 1.0, [Section.Cage] = 1.0, [Section.Witness] = 1.0,
    };

    private static List<SyxxIndicatorInput> Map(params (EvidenceId Id, string Observation)[] items)
        => SynthesisEngine.MapEvidenceToIndicators(
            items.Select(i => EvidenceItem.Create(Section.Prophet, i.Id, i.Observation, "test")).ToList(),
            FullCompleteness());

    private static IReadOnlyList<SectionResult> AllSectionsPassed() => new[]
        {
            new SectionResult(new SectionId("prophet"), EvidenceUsability.Passed),
            new SectionResult(new SectionId("mirror"), EvidenceUsability.Passed),
            new SectionResult(new SectionId("portal"), EvidenceUsability.Passed),
            new SectionResult(new SectionId("glutton"), EvidenceUsability.Passed),
            new SectionResult(new SectionId("cage"), EvidenceUsability.Passed),
            new SectionResult(new SectionId("witness"), EvidenceUsability.Passed),
        };

    private static IReadOnlyList<ObservedIndicator> AsObserved(params (EvidenceId Id, string Observation)[] items)
        => Map(items).Select(i => i.ToObservedIndicator()).ToList();

    /// <summary>
    /// The control-domain canary success must map to NO indicator — in particular not
    /// <c>TrustedWitnessOK</c>, the exclusionary trigger for both spoofing diagnoses.
    /// </summary>
    [Fact]
    public void ControlDomainCanarySuccess_MapsToNothing()
    {
        var mapped = Map((EvidenceId.DnssecCanarySecure, "canary verified on control domain"));

        Assert.DoesNotContain(mapped, i => i.Indicator == SyxxIndicator.TrustedWitnessOK);
        Assert.Empty(mapped);
    }

    /// <summary>
    /// End-to-end: real target-domain spoofing support plus a healthy control-domain canary.
    /// The canary must not hard-exclude the threat — dnsSpoofing is suppressed only for being
    /// below the reporting tier, and the run must not read clean.
    /// </summary>
    [Fact]
    public void CanarySuccessDoesNotHardExcludeTargetDomainThreat()
    {
        var bundle = new EvidenceBundle(
            AllSectionsPassed(),
            AsObserved(
                (EvidenceId.AuthoritativeDivergesConfirmed, "authoritative answer diverges on target"),
                (EvidenceId.DnssecCanarySecure, "canary verified on control domain")));

        var result = SyxxMatrixIntegration.MakeEngine().Evaluate(bundle);

        Assert.NotEqual(DeterminativeState.Clean, result.State);
        var suppression = Assert.Single(result.Suppressed, s => s.Id.Value == "dnsSpoofing");
        Assert.NotEqual(SuppressedDiagnosis.SuppressionReason.HardExcluded, suppression.Reason);
        Assert.Equal(SuppressedDiagnosis.SuppressionReason.BelowTier, suppression.Reason);
    }

    /// <summary>
    /// Negative control — the fix must not over-reach: the TAMPERING-side canary signal
    /// (<c>DnssecCanaryStripped → SignedAnswerMismatch, 0.80</c>) is target-relevant evidence and
    /// must still map.
    /// </summary>
    [Fact]
    public void TamperingSideCanarySignal_StillMaps()
    {
        var mapped = Map((EvidenceId.DnssecCanaryStripped, "canary signature stripped"));

        var input = Assert.Single(mapped);
        Assert.Equal(SyxxIndicator.SignedAnswerMismatch, input.Indicator);
        Assert.Equal(0.80, input.ObservedStrength);
    }
}
