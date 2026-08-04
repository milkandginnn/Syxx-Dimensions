using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // A3 — network report card. The load-bearing tests here are the UNGRADED ones: a network we
    // could not measure must never receive a letter (an A is false safety, an F a false accusation).
    public class ReportCardTests
    {
        private static EvidenceItem Ev(string obs, double weight, params Hypothesis[] supports) =>
            new EvidenceItem(Section.Prophet, EvidenceId.AuthoritativeDiverges, obs, "rationale",
                             supports.ToList(), weight, Source: null);

        private static Conclusion Make(
            Hypothesis verdict,
            Conclusivity conclusivity = Conclusivity.Conclusive,
            int confidence = 90,
            bool referral = false,
            IEnumerable<EvidenceItem>? evidence = null,
            IEnumerable<SectionStatusEntry>? statuses = null)
        {
            statuses ??= new[] { new SectionStatusEntry(Section.Prophet, SectionStatus.Negative) };
            return new Conclusion(
                verdict, conclusivity, confidence,
                Probabilities: Array.Empty<HypothesisProbability>(),
                Evidence: (evidence ?? Array.Empty<EvidenceItem>()).ToList(),
                SectionStatuses: statuses.ToList(),
                NextSteps: Array.Empty<string>(),
                ReferralTriggered: referral,
                Referral: referral ? new Referral("low", new[] { "r/AskNetSec" }, "") : null,
                GeneratedAt: DateTimeOffset.UnixEpoch);
        }

        [Fact]
        public void Inconclusive_IsUngraded_NeverALetter()
        {
            var card = ReportCards.For(Make(Hypothesis.Clean, Conclusivity.Inconclusive, confidence: 0));
            Assert.Equal(Grade.Ungraded, card.Grade);
            Assert.Equal("—", card.GradeLabel);
            Assert.Equal(Severity.Unknown, card.Severity);
            Assert.Contains("Not enough evidence", card.Headline);
        }

        [Fact]
        public void CleanButHardGated_IsUngraded_NotAnA()
        {
            var gated = new[]
            {
                new SectionStatusEntry(Section.Prophet, SectionStatus.Negative),
                new SectionStatusEntry(Section.Mirror,  SectionStatus.Untested),
            };
            var card = ReportCards.For(Make(Hypothesis.Clean, statuses: gated));

            Assert.Equal(Grade.Ungraded, card.Grade);           // NOT Grade.A — the honesty gate
            Assert.Contains(card.TopReasons, r => r.Contains("Mirror") && r.Contains("did not run"));

            // Mutation guard: the gate must actually fire — a complete clean run grades A.
            Assert.Equal(Grade.A, ReportCards.For(Make(Hypothesis.Clean)).Grade);
        }

        [Fact]
        public void CleanAndComplete_AndConfident_GradesA()
        {
            var card = ReportCards.For(Make(Hypothesis.Clean, confidence: 95));
            Assert.Equal(Grade.A, card.Grade);
            Assert.Equal(Severity.Clean, card.Severity);
            Assert.NotEmpty(card.TopReasons);
        }

        [Fact]
        public void Clean_ButLowConfidence_GradesB_NotA()
        {
            var card = ReportCards.For(Make(Hypothesis.Clean, confidence: 50, referral: true));
            Assert.Equal(Grade.B, card.Grade);
            Assert.Contains("confidence is limited", card.Headline);
        }

        [Theory]
        [InlineData(Hypothesis.Spoofing, Grade.F)]         // Critical
        [InlineData(Hypothesis.LocalCompromise, Grade.F)]  // Critical
        [InlineData(Hypothesis.Censorship, Grade.D)]       // High
        [InlineData(Hypothesis.ContentTampering, Grade.D)] // High
        [InlineData(Hypothesis.PrivacyLeak, Grade.C)]      // Warning
        [InlineData(Hypothesis.InterceptionOnPath, Grade.C)]
        public void Threats_GradeBySeverity(Hypothesis h, Grade expected)
        {
            var card = ReportCards.For(Make(h, evidence: new[] { Ev("a real observation", 0.9, h) }));
            Assert.Equal(expected, card.Grade);
            Assert.Contains(card.TopReasons, r => r.Contains("a real observation"));
        }

        [Fact]
        public void Grade_IsDeterministic_SameRunSameGrade()
        {
            var c = Make(Hypothesis.Spoofing, evidence: new[] { Ev("obs", 0.9, Hypothesis.Spoofing) });
            Assert.Equal(ReportCards.For(c).Grade, ReportCards.For(c).Grade);
            Assert.Equal(ReportCards.For(c).Headline, ReportCards.For(c).Headline);
        }

        [Fact]
        public void TopReasons_NeverExceedsThree()
        {
            var many = Enumerable.Range(0, 8).Select(i => Ev($"obs {i}", 0.9 - i * 0.05, Hypothesis.Spoofing));
            var card = ReportCards.For(Make(Hypothesis.Spoofing, evidence: many));
            Assert.True(card.TopReasons.Count <= ReportCards.MaxReasons);
        }
    }
}
