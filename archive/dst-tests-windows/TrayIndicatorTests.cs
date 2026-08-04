using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // A6 — tray/menu-bar trust indicator. The plan's bar: "indicator colour always equals the last
    // real verdict; never green on inconclusive". Both halves are tested here, the second as an
    // anti-divergence property across EVERY hypothesis rather than a handful of examples.
    public class TrayIndicatorTests
    {
        private static Conclusion Make(
            Hypothesis diagnosis,
            Conclusivity conclusivity = Conclusivity.Conclusive,
            int confidence = 90,
            IEnumerable<SectionStatusEntry>? statuses = null) => new Conclusion(
                diagnosis, conclusivity, confidence,
                Probabilities: Array.Empty<HypothesisProbability>(),
                Evidence: Array.Empty<EvidenceItem>(),
                SectionStatuses: (statuses ?? new[] { new SectionStatusEntry(Section.Prophet, SectionStatus.Negative) }).ToList(),
                NextSteps: Array.Empty<string>(),
                ReferralTriggered: false, Referral: null,
                GeneratedAt: DateTimeOffset.UnixEpoch);

        [Fact]
        public void NeverScanned_IsUnknown_NeverGreen()
        {
            // The tray is always visible, so a green dot on a network nobody has looked at would be
            // this product's most-seen lie.
            var t = TrayIndicators.For(null);
            Assert.Equal(Severity.Unknown, t.Severity);
            Assert.NotEqual(Severity.Clean, t.Severity);
            Assert.False(t.Attention);
            Assert.Contains("has not checked", t.Tooltip);
            // The glyph must not read as a tick.
            Assert.DoesNotContain("check", TrayIndicators.GlyphFor(Severity.Unknown));
        }

        [Fact]
        public void Inconclusive_IsNeverGreen()
        {
            var t = TrayIndicators.For(Make(Hypothesis.Clean, Conclusivity.Inconclusive, confidence: 0));
            Assert.NotEqual(Severity.Clean, t.Severity);
            Assert.Equal(Severity.Unknown, t.Severity);
        }

        [Fact]
        public void CleanButHardGated_IsNeverGreen()
        {
            // A Clean verdict that rode over a skipped required check must not light the tray green.
            var gated = new[]
            {
                new SectionStatusEntry(Section.Prophet, SectionStatus.Negative),
                new SectionStatusEntry(Section.Mirror,  SectionStatus.Untested),
            };
            var t = TrayIndicators.For(Make(Hypothesis.Clean, statuses: gated));

            Assert.NotEqual(Severity.Clean, t.Severity);
            Assert.Equal(Severity.Unknown, t.Severity);

            // Mutation guard: a complete clean run DOES go green, so the gate is what changed it.
            Assert.Equal(Severity.Clean, TrayIndicators.For(Make(Hypothesis.Clean)).Severity);
        }

        [Fact]
        public void CleanAndComplete_IsGreen_AndDoesNotBidForAttention()
        {
            var t = TrayIndicators.For(Make(Hypothesis.Clean));
            Assert.Equal(Severity.Clean, t.Severity);
            Assert.False(t.Attention);   // good news must never bounce the icon
        }

        [Theory]
        [InlineData(Hypothesis.Spoofing)]
        [InlineData(Hypothesis.LocalCompromise)]
        [InlineData(Hypothesis.Censorship)]
        [InlineData(Hypothesis.ContentTampering)]
        public void SevereThreats_DemandAttention(Hypothesis h)
        {
            var t = TrayIndicators.For(Make(h));
            Assert.True(t.Attention, $"{h} must draw the user's eye");
            Assert.NotEqual(Severity.Clean, t.Severity);
        }

        [Fact]
        public void UnknownDoesNotCryWolf()
        {
            // Attention on grey would train the user to ignore the indicator that must work when
            // it turns red.
            Assert.False(TrayIndicators.For(null).Attention);
            Assert.False(TrayIndicators.For(Make(Hypothesis.Clean, Conclusivity.Inconclusive)).Attention);
        }

        [Theory]
        [InlineData(Hypothesis.Clean)]
        [InlineData(Hypothesis.Spoofing)]
        [InlineData(Hypothesis.Censorship)]
        [InlineData(Hypothesis.InterceptionTransport)]
        [InlineData(Hypothesis.InterceptionOnPath)]
        [InlineData(Hypothesis.ContentTampering)]
        [InlineData(Hypothesis.PrivacyLeak)]
        [InlineData(Hypothesis.LocalCompromise)]
        public void TrayNeverDisagreesWithTheReportCard(Hypothesis h)
        {
            // The anti-divergence invariant: the tray is DERIVED from the card, so a future edit
            // that gives the tray its own severity mapping — the first step toward two surfaces
            // contradicting each other (cf. D069) — must turn this red.
            //
            // NOTE: this Theory alone CANNOT catch that rewiring. For every COMPLETE fixture the
            // card's severity and Verdict.ToSeverity() happen to agree, so the assertion holds
            // either way. The case with teeth is TrayNeverDisagreesWithTheCard_WhenTheGateFires
            // below — do not delete it thinking this Theory covers it. (Proven on the macOS twin:
            // rewiring the tray left this loop green and only the hard-gated fixture went red.)
            var c = Make(h);
            Assert.Equal(ReportCards.For(c).Severity, TrayIndicators.For(c).Severity);
        }

        [Fact]
        public void TrayNeverDisagreesWithTheCard_WhenTheGateFires()
        {
            // The ONLY case where reading Verdict.ToSeverity() directly diverges from the card:
            // the verdict says Clean, the Ungraded gate says Unknown. This is what actually
            // catches a tray that grows its own mapping.
            var gated = new[]
            {
                new SectionStatusEntry(Section.Prophet, SectionStatus.Negative),
                new SectionStatusEntry(Section.Mirror,  SectionStatus.Untested),
            };
            var c = Make(Hypothesis.Clean, statuses: gated);

            // The verdict alone would say Clean...
            Assert.Equal(Severity.Clean, c.Diagnosis.ToSeverity());
            // ...but the card's Ungraded gate says Unknown, and the tray must follow the CARD.
            Assert.Equal(Severity.Unknown, ReportCards.For(c).Severity);
            Assert.Equal(Severity.Unknown, TrayIndicators.For(c).Severity);
            Assert.NotEqual(c.Diagnosis.ToSeverity(), TrayIndicators.For(c).Severity);
        }

        [Fact]
        public void EverySeverity_HasADistinctGlyphForItsClass_AndUnknownIsNotATick()
        {
            Assert.NotEqual(TrayIndicators.GlyphFor(Severity.Clean), TrayIndicators.GlyphFor(Severity.Unknown));
            Assert.NotEqual(TrayIndicators.GlyphFor(Severity.Clean), TrayIndicators.GlyphFor(Severity.Critical));
            Assert.All(Enum.GetValues<Severity>(),
                s => Assert.False(string.IsNullOrWhiteSpace(TrayIndicators.GlyphFor(s))));
        }
    }
}
