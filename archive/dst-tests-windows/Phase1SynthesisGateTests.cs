using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

public sealed class Phase1SynthesisGateTests
{
    private static SectionStatusEntry St(Section section, SectionStatus status) => new(section, status);

    private static List<SectionStatusEntry> AllNegative =>
        Enum.GetValues<Section>().Select(section => St(section, SectionStatus.Negative)).ToList();

    [Fact]
    public void CombineInvalidatedDominatesNegativeAndDegradedButNotPositive()
    {
        Assert.Equal(SectionStatus.Invalidated, EvidenceAdapters.Combine(new[] { SectionStatus.Negative, SectionStatus.Invalidated }).Status);
        Assert.Equal(SectionStatus.Invalidated, EvidenceAdapters.Combine(new[] { SectionStatus.Degraded, SectionStatus.Invalidated }).Status);
        Assert.Equal(SectionStatus.Positive, EvidenceAdapters.Combine(new[] { SectionStatus.Positive, SectionStatus.Invalidated }).Status);
    }

    [Fact]
    public void PositiveSectionBlocksClean()
    {
        var statuses = AllNegative;
        statuses[0] = St(Section.Prophet, SectionStatus.Positive);
        var evidence = new[]
        {
            EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped, "stripped", "confirmed")
        };
        var c = SynthesisEngine.Synthesize(evidence, statuses);
        Assert.False(c.ReadsClean(), "must never read committed-clean over a positive threat section");
        Assert.True(c.ReferralTriggered);
    }

    [Fact]
    public void CommittedThreatMassFloorBlocksCleanWhenConfirmedThreatIsWashed()
    {
        // P1.6: a confirmed (.Positive) strong strip that the clean sections wash below the fused floor
        // must STILL not read clean — the committed-mass backstop reads the per-support focal mass (~0.95),
        // not the washed Bel, and never edits the belief number (R3). Redundant with P1.5 in Phase 1.
        var statuses = AllNegative;
        statuses[0] = St(Section.Prophet, SectionStatus.Positive);
        var evidence = new[]
        {
            EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped, "stripped", "confirmed", "canaries")
        };
        var c = SynthesisEngine.Synthesize(evidence, statuses);
        Assert.False(c.ReadsClean(), "must never read committed-clean over a positive threat section");
        Assert.True(c.ReferralTriggered);
    }

    [Fact]
    public void CommittedMassGateScopedToConfirmedSectionsNoFalseTampered()
    {
        // Honesty-symmetric guard: a WEAK positive-weighted overlay (dnsUnencrypted, 0.6 -> committed ~0.45)
        // on an otherwise all-clean run must NOT block clean. The committed-mass gate is scoped to .Positive
        // (confirmed) sections so a benign Do53 tilt can never manufacture a false-tampered verdict.
        var c = SynthesisEngine.Synthesize(new[]
        {
            EvidenceItem.Create(Section.Portal, EvidenceId.DnsUnencrypted, "cleartext", "benign")
        }, AllNegative);
        Assert.True(c.ReadsClean(), "a weak benign tilt must not manufacture a false-tampered/inconclusive read");
    }

    [Fact]
    public void CriticalInconclusiveBlocksClean()
    {
        var statuses = AllNegative;
        statuses[0] = St(Section.Prophet, SectionStatus.Inconclusive);
        var c = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), statuses);
        Assert.True(c.IsInconclusive());
        Assert.True(c.ReferralTriggered);
    }

    [Fact]
    public void CriticalDegradedSectionForbidsClean()
    {
        // P1.2: a degraded CRITICAL section (Glutton) forbids a clean verdict even with everything else
        // clean and no threat evidence — the run is incomplete, so it reads inconclusive+referral.
        var clean = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), AllNegative);
        var degradedStatuses = AllNegative;
        degradedStatuses[3] = St(Section.Glutton, SectionStatus.Degraded);
        var degraded = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), degradedStatuses);
        Assert.False(degraded.ReadsClean(), "a degraded critical section forbids a committed clean");
        Assert.True(degraded.Confidence < clean.Confidence);
    }

    [Fact]
    public void DegradedSourceDiscountLowersNamedThreatConfidenceNeverZeroes()
    {
        // P1.3 (Shafer alpha): the SAME confirmed threat evidence from a .Degraded source earns strictly
        // LOWER confidence than from a .Positive source, but is NEVER zeroed (I4). Cage is non-critical and
        // uniquely covers LocalCompromise, so the threat is named in both cases and alpha is isolated.
        var evidence = new[] { EvidenceItem.Create(Section.Cage, EvidenceId.ExfilLikely, "exfil", "confirmed") };
        var pos = AllNegative; pos[4] = St(Section.Cage, SectionStatus.Positive);
        var deg = AllNegative; deg[4] = St(Section.Cage, SectionStatus.Degraded);
        var full = SynthesisEngine.Synthesize(evidence, pos);
        var discounted = SynthesisEngine.Synthesize(evidence, deg);
        Assert.True(full.Names(Hypothesis.LocalCompromise));
        Assert.True(discounted.Names(Hypothesis.LocalCompromise));
        Assert.True(discounted.Confidence < full.Confidence);
        Assert.True(discounted.Confidence > 0);
    }

    [Fact]
    public void SingleSourceThreatForcesReferralWhileTwoIndependentSourcesCanStandAlone()
    {
        var statuses = AllNegative;
        statuses[3] = St(Section.Glutton, SectionStatus.Positive);

        var oneSource = SynthesisEngine.Synthesize(new[]
        {
            EvidenceItem.Create(Section.Glutton, EvidenceId.DnsLeakConfirmed, "collector confirmed", "confirmed")
        }, statuses);
        Assert.True(oneSource.Names(Hypothesis.PrivacyLeak));
        Assert.True(oneSource.ReferralTriggered);

        var twoSources = SynthesisEngine.Synthesize(new[]
        {
            EvidenceItem.Create(Section.Glutton, EvidenceId.DnsLeakConfirmed, "collector confirmed", "confirmed"),
            EvidenceItem.Create(Section.Glutton, EvidenceId.EcsSubnetLeak, "ecs leaked", "independent corroboration")
        }, statuses);
        Assert.True(twoSources.Names(Hypothesis.PrivacyLeak));
        Assert.False(twoSources.ReferralTriggered);
    }

    [Fact]
    public void LatencyAndTTLQualityCaveatsStayDegradedWithoutThreatEvidence()
    {
        var latency = EvidenceAdapters.Prophet(null, null, null, null,
            new ResolverLatency(ResolverLatencyStatus.Degraded, SampleCount: 4, SuccessCount: 2, MedianMs: 200));
        Assert.Equal(SectionStatus.Degraded, latency.Status);
        Assert.Empty(latency.Evidence);

        var stability = EvidenceAdapters.Prophet(null, null, null, null,
            stability: new AnswerStability(AnswerStabilityStatus.TtlAnomaly, SampleCount: 3, UsableCount: 3, TtlHonored: false));
        Assert.Equal(SectionStatus.Degraded, stability.Status);
        Assert.Empty(stability.Evidence);
    }
}
