using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests;

/// P0.1 — the cross-platform integer-percentage gate primitive (design §3 M4 D2; NE7 Δ5/Δ6). Asserts
/// the IDENTICAL expected integers the Swift twin (`RoundingParityTests.swift`) asserts, so both
/// suites green proves the platforms round a `.5` boundary the same way: away-from-zero, NOT C#'s
/// default banker's/ToEven, which straddled the 75% gate.
public class RoundingParityTests
{
    /// 0.125·100 == 12.5 and 0.625·100 == 62.5 are EXACT `.5` boundaries where away-from-zero (odd
    /// result) and banker's-to-even DIVERGE (the design's illustrative 74.5 is not dyadic-exact).
    [Fact]
    public void RoundPercent_AwayFromZero_AtExactHalfBoundary()
    {
        Assert.Equal(13, SynthesisEngine.RoundPercent(0.125)); // 12.5 -> 13 (away); banker's gives 12
        Assert.Equal(63, SynthesisEngine.RoundPercent(0.625)); // 62.5 -> 63 (away); banker's gives 62
        // The bug this fix closes: the default C# mode WOULD give the banker's answer.
        Assert.Equal(12, (int)Math.Round(0.125 * 100));
        Assert.Equal(62, (int)Math.Round(0.625 * 100));
    }

    [Fact]
    public void RoundPercent_OrdinaryValues()
    {
        Assert.Equal(0, SynthesisEngine.RoundPercent(0.0));
        Assert.Equal(100, SynthesisEngine.RoundPercent(1.0));
        Assert.Equal(74, SynthesisEngine.RoundPercent(0.744));
        Assert.Equal(75, SynthesisEngine.RoundPercent(0.746));
        Assert.True(SynthesisEngine.RoundPercent(0.7449) < 75);
    }

    /// The fold is bit-stable: synthesizing the same multi-support input repeatedly yields the same
    /// confidence and belief vector (the canonicalized ascending-key Dempster accumulation, P0.1).
    [Fact]
    public void SynthesisIsDeterministicAcrossRepeatedRuns()
    {
        var ev = new List<EvidenceItem>
        {
            EvidenceItem.Create(Section.Mirror, EvidenceId.Redirection, "o", "r"),
            EvidenceItem.Create(Section.Prophet, EvidenceId.DnssecCanaryStripped, "o", "r", "canaries"),
        };
        var statuses = Enum.GetValues<Section>()
            .Select(s => new SectionStatusEntry(s, s == Section.Glutton ? SectionStatus.Untested : SectionStatus.Negative))
            .ToList();
        var a = SynthesisEngine.Synthesize(ev, statuses);
        for (int i = 0; i < 20; i++)
        {
            var b = SynthesisEngine.Synthesize(ev, statuses);
            Assert.Equal(a.Confidence, b.Confidence);
            Assert.Equal(a.Diagnosis, b.Diagnosis);
            Assert.Equal(a.Conclusivity, b.Conclusivity);
            Assert.Equal(a.Belief.Select(p => p.Probability), b.Belief.Select(p => p.Probability));
            Assert.Equal(a.Uncertainty, b.Uncertainty);
            Assert.Equal(a.Conflict, b.Conflict);
        }
    }
}
