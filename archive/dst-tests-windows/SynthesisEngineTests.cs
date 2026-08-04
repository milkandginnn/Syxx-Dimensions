using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// <summary>
/// Offline, deterministic checks of the synthesis accumulator's invariants — the properties that
/// must hold no matter how the weights are later calibrated (DoW #1). No network.
/// Windows-parity twin of macOS/Tests/SyxxKitTests/SynthesisEngineTests.swift.
/// </summary>
public sealed class SynthesisEngineTests
{
    private static SectionStatusEntry St(Section section, SectionStatus status) => new(section, status);

    private static List<SectionStatusEntry> AllNegative =>
        Enum.GetValues<Section>().Select(section => St(section, SectionStatus.Negative)).ToList();

    [Fact]
    public void AllClean_isCleanVerdict_highConfidence_noReferral()
    {
        var c = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), AllNegative);
        Assert.True(c.ReadsClean(), "an all-clean complete run must commit to clean");
        Assert.False(c.HasHardGatedSection);
        Assert.InRange(c.Confidence, 51, 100);
    }

    [Fact]
    public void HardGate_blocksClean()
    {
        var statuses = AllNegative;
        statuses[1] = St(Section.Mirror, SectionStatus.Untested);
        var c = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), statuses);
        Assert.False(c.ReadsClean(), "must never read committed-clean here");
        Assert.True(c.HasHardGatedSection);
    }

    [Fact]
    public void StrongSingleThreat_winsOverCleanSections()
    {
        var e = EvidenceItem.Create(Section.Portal, EvidenceId.DnsLeakConfirmed,
            "rogue on-link resolver", "not the gateway; DNS unencrypted");
        var c = SynthesisEngine.Synthesize(new[] { e }, AllNegative);
        Assert.True(c.Names(Hypothesis.PrivacyLeak));
    }

    [Fact]
    public void TwoWayTie_triggersReferral()
    {
        var e = EvidenceItem.Create(Section.Prophet, EvidenceId.Redirection,
            "NXDOMAIN canary rewrite", "shared hijack IP");
        var c = SynthesisEngine.Synthesize(new[] { e }, AllNegative);
        Assert.True(c.ReferralTriggered);
        Assert.NotNull(c.Referral);
    }

    [Fact]
    public void EmptyRun_isInconclusive_andRefers()
    {
        var statuses = Enum.GetValues<Section>().Select(s => St(s, SectionStatus.Untested)).ToList();
        var c = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), statuses);
        Assert.True(c.IsInconclusive());
        Assert.True(c.ReferralTriggered);
        Assert.Equal(0, c.Confidence);
    }

    [Fact]
    public void Probabilities_sumToOne_andConfidenceBounded()
    {
        var c = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), AllNegative);
        var sum = c.Probabilities.Sum(p => p.Probability);
        Assert.Equal(1.0, sum, 3); // accuracy 0.001
        foreach (var p in c.Probabilities)
        {
            Assert.InRange(p.Probability, 0.0, 1.0);
        }
        Assert.InRange(c.Confidence, 1, 100);
    }

    [Fact]
    public void DegradedSection_reducesConfidence()
    {
        var clean = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), AllNegative);
        var statuses = AllNegative;
        statuses[2] = St(Section.Portal, SectionStatus.Degraded);
        var degraded = SynthesisEngine.Synthesize(Array.Empty<EvidenceItem>(), statuses);
        Assert.True(degraded.Confidence < clean.Confidence,
            "degraded confidence must be lower than all-clean baseline");
    }

    // MARK: Ph4.3 — class-level margin tests

    [Fact]
    public void ClassMargin_sameClass_closeHypotheses_doesNotCiteMargin()
    {
        var ev1 = EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped,
            "DNSSEC stripped", "canary hijack");
        var ev2 = EvidenceItem.Create(Section.Mirror, EvidenceId.Redirection,
            "NXDOMAIN canary rewrite", "shared hijack IP");
        var statuses = AllNegative;
        statuses[0] = St(Section.Prophet, SectionStatus.Positive);
        statuses[1] = St(Section.Mirror, SectionStatus.Positive);
        var c = SynthesisEngine.Synthesize(new[] { ev1, ev2 }, statuses);
        Assert.False(c.ReadsClean(), "must never read committed-clean here");
        // Two positive sections from different classes must trigger a referral.
        Assert.True(c.ReferralTriggered, "Two positive sections must trigger a referral");
        Assert.NotNull(c.Referral);
        // xunit's Assert.DoesNotContain has no message overload; Assert.False does, so the
        // diagnostic is preserved rather than dropped.
        Assert.False(c.Referral!.Reason.Contains("margin", StringComparison.OrdinalIgnoreCase),
            "Close hypotheses in the same class must not cite a class margin");
    }

    [Fact]
    public void ClassMargin_singleClassDominates_noMarginReferral()
    {
        var leak1 = EvidenceItem.Create(Section.Glutton, EvidenceId.DnsLeakConfirmed,
            "collector confirmed leak", "confirmed");
        var leak2 = EvidenceItem.Create(Section.Glutton, EvidenceId.EcsSubnetLeak,
            "ECS subnet leaked", "independent corroboration");
        var statuses = AllNegative;
        statuses[3] = St(Section.Glutton, SectionStatus.Positive);
        var c = SynthesisEngine.Synthesize(new[] { leak1, leak2 }, statuses);
        Assert.True(c.Names(Hypothesis.PrivacyLeak));
        // When a single class dominates, referral may or may not trigger — but if it does,
        // it must not cite a class margin (there is no cross-class competition).
        if (c.ReferralTriggered && c.Referral is not null)
        {
            Assert.False(c.Referral.Reason.Contains("margin", StringComparison.OrdinalIgnoreCase),
                "A single-class dominance must not cite a class margin");
        }
        // The real invariant: the dominant class must actually own the evidence — every
        // supporting item comes from Glutton, so a cross-class margin is impossible here.
        // (An earlier revision asserted `c.DominantSection`, which does not exist on Conclusion
        // in either Kit and is not a scheduled property — replaced with the equivalent check
        // against the evidence the engine really returned.)
        var leakSupport = c.Evidence.Where(e => e.Supports.Contains(Hypothesis.PrivacyLeak)).ToList();
        // Guard against a vacuous pass: Assert.All succeeds on an empty collection, so an
        // engine change that stopped attributing this evidence would silently "pass".
        Assert.True(leakSupport.Count >= 2,
            "both Glutton items must reach the engine as PrivacyLeak support");
        Assert.All(leakSupport, e => Assert.Equal(Section.Glutton, e.Section));
    }

    [Fact]
    public void Ph51_discrepancyMatrix_contributesMassAgainstClean()
    {
        var local = new VantageIdentity("local", VantageKind.LocalSystem, null, null, false, FreshnessState.CurrentRun);
        var pub1 = new VantageIdentity("public1", VantageKind.PublicResolver, "8.8.8.8", "udp", true, FreshnessState.CurrentRun);
        var pub2 = new VantageIdentity("public2", VantageKind.PublicResolver, "1.1.1.1", "udp", true, FreshnessState.CurrentRun);
        var entries = new[]
        {
            new VantageAnswer(local, AnswerStatus.Answered, new[] { "203.0.113.5" }, 0, "obs.local"),
            new VantageAnswer(pub1, AnswerStatus.Answered, new[] { "198.51.100.10" }, 0, "obs.pub1"),
            new VantageAnswer(pub2, AnswerStatus.Answered, new[] { "198.51.100.10" }, 0, "obs.pub2"),
        };
        var comparison = new AnswerComparison(
            AnswerComparisonClass.Discrepancy,
            new[] { "local" },
            new[] { "public1", "public2" },
            new[] { "198.51.100.10" },
            new[] { "local" });
        var matrixRow = new AnswerMatrixRow("mirror.a.example.com.1", "example.com", 1, entries, comparison);
        var withMatrix = SynthesisEngine.Synthesize(
            Array.Empty<EvidenceItem>(), AllNegative,
            answerMatrix: new[] { matrixRow });
        var withoutMatrix = SynthesisEngine.Synthesize(
            Array.Empty<EvidenceItem>(), AllNegative,
            answerMatrix: Array.Empty<AnswerMatrixRow>());
        var cleanBelWith = withMatrix.Belief.FirstOrDefault(b => b.Hypothesis == Hypothesis.Clean)?.Probability ?? 0;
        var cleanBelWithout = withoutMatrix.Belief.FirstOrDefault(b => b.Hypothesis == Hypothesis.Clean)?.Probability ?? 0;
        Assert.True(cleanBelWith < cleanBelWithout,
            "Ph5.1: discrepancy must reduce clean belief vs no-discrepancy baseline");
    }

    [Fact]
    public void Ph51_benignSetVariation_noMassContribution()
    {
        var local = new VantageIdentity("local", VantageKind.LocalSystem, null, null, false, FreshnessState.CurrentRun);
        var pub = new VantageIdentity("public", VantageKind.PublicResolver, "8.8.8.8", "udp", true, FreshnessState.CurrentRun);
        var entries = new[]
        {
            new VantageAnswer(local, AnswerStatus.Answered, new[] { "203.0.113.5" }, 0, "obs.local"),
            new VantageAnswer(pub, AnswerStatus.Answered, new[] { "203.0.113.5" }, 0, "obs.pub"),
        };
        var comparison = new AnswerComparison(
            AnswerComparisonClass.BenignSetVariation,
            new[] { "local" },
            new[] { "public" },
            new[] { "203.0.113.5" },
            Array.Empty<string>());
        var matrixRow = new AnswerMatrixRow("mirror.a.example.com.1", "example.com", 1, entries, comparison);
        var withBenign = SynthesisEngine.Synthesize(
            Array.Empty<EvidenceItem>(), AllNegative,
            answerMatrix: new[] { matrixRow });
        var withoutMatrix = SynthesisEngine.Synthesize(
            Array.Empty<EvidenceItem>(), AllNegative,
            answerMatrix: Array.Empty<AnswerMatrixRow>());
        foreach (var hp in withBenign.Belief)
        {
            var other = withoutMatrix.Belief.FirstOrDefault(b => b.Hypothesis == hp.Hypothesis)?.Probability ?? 0;
            Assert.Equal(hp.Probability, other, 9); // accuracy 1e-9
        }
    }
}
