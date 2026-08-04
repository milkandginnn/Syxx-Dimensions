using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // A1 — Verdict→Action "so what" layer. These lock the HONESTY CONTRACT: an inconclusive or
    // unfinished (hard-gated) result must never read as safe, and a Clean verdict that skipped a
    // required check must be presented as UNFINISHED. Twin owed on macOS VerdictActionTests.swift.
    public class VerdictActionTests
    {
        private static Conclusion Make(
            Hypothesis verdict,
            Conclusivity conclusivity = Conclusivity.Conclusive,
            int confidence = 90,
            bool referral = false,
            IEnumerable<SectionStatusEntry>? statuses = null)
        {
            // `Negative` = the check RAN and found nothing (clean-support, SynthesisEngine.cs:227).
            // `Positive` means a THREAT was found and gates Clean out entirely (:110) — using it as
            // the default here would encode an engine-impossible "clean" state.
            statuses ??= new[] { new SectionStatusEntry(Section.Prophet, SectionStatus.Negative) };
            return new Conclusion(
                Diagnosis: verdict,
                Conclusivity: conclusivity,
                Confidence: confidence,
                Probabilities: Array.Empty<HypothesisProbability>(),
                Evidence: Array.Empty<EvidenceItem>(),
                SectionStatuses: statuses.ToList(),
                NextSteps: Array.Empty<string>(),
                ReferralTriggered: referral,
                Referral: referral ? new Referral("low confidence", new[] { "r/AskNetSec" }, "") : null,
                GeneratedAt: DateTimeOffset.UnixEpoch);
        }

        [Theory]
        [InlineData(Hypothesis.Spoofing)]
        [InlineData(Hypothesis.Censorship)]
        [InlineData(Hypothesis.InterceptionTransport)]
        [InlineData(Hypothesis.InterceptionOnPath)]
        [InlineData(Hypothesis.ContentTampering)]
        [InlineData(Hypothesis.PrivacyLeak)]
        [InlineData(Hypothesis.LocalCompromise)]
        public void EveryThreat_ExplainsAndDirects_NeverReassures(Hypothesis h)
        {
            var va = VerdictActions.For(Make(h));

            // Substantive prose, not a placeholder: a real sentence with an actionable imperative.
            Assert.True(va.Meaning.Length > 40, $"{h} meaning is too thin to explain anything");
            Assert.True(va.PrimaryAction.Length > 20, $"{h} action is too thin to act on");
            Assert.EndsWith(".", va.Meaning);

            // A threat must never be described in reassuring terms.
            Assert.DoesNotContain("no action needed", va.Meaning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("no tampering", va.Meaning, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Severity.Clean, va.Severity);
        }

        [Fact]
        public void EachVerdict_GetsItsOwnDistinctGuidance_NotABoilerplateFallback()
        {
            // Responsive to intent: a per-hypothesis map, not one generic string reused. If a future
            // hypothesis falls through to the default branch, this fails.
            var threats = new[]
            {
                Hypothesis.Spoofing, Hypothesis.Censorship, Hypothesis.InterceptionTransport,
                Hypothesis.InterceptionOnPath, Hypothesis.ContentTampering,
                Hypothesis.PrivacyLeak, Hypothesis.LocalCompromise,
            };
            var actions = threats.Select(h => VerdictActions.For(Make(h)).PrimaryAction).ToList();
            Assert.Equal(actions.Count, actions.Distinct().Count());

            // And none of them is the inconclusive fallback text.
            var fallback = VerdictActions.For(Make(Hypothesis.Clean, Conclusivity.Inconclusive)).PrimaryAction;
            Assert.DoesNotContain(fallback, actions);
        }

        [Fact]
        public void CleanAndComplete_IsAffirmative_NotQualified()
        {
            var va = VerdictActions.For(Make(Hypothesis.Clean));
            Assert.Equal(Severity.Clean, va.Severity);
            Assert.False(va.Qualified);
            Assert.Contains("No tampering", va.Meaning);
        }

        [Fact]
        public void Inconclusive_NeverReadsAsSafe()
        {
            var va = VerdictActions.For(Make(Hypothesis.Clean, Conclusivity.Inconclusive, confidence: 0));
            Assert.Equal(Severity.Unknown, va.Severity);
            Assert.True(va.Qualified);
            // Must not reassure.
            Assert.DoesNotContain("No action needed", va.Meaning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("No tampering", va.Meaning, StringComparison.OrdinalIgnoreCase);
            // Must direct the user to get a real result.
            Assert.Contains("real verdict", va.PrimaryAction, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CleanButHardGated_IsPresentedAsUnfinished_NotSafe()
        {
            // A Clean verdict that rode over a required check that never ran (DoW #3 hard gate).
            var gated = new[]
            {
                new SectionStatusEntry(Section.Prophet, SectionStatus.Positive),
                new SectionStatusEntry(Section.Mirror,  SectionStatus.Untested),
            };
            var va = VerdictActions.For(Make(Hypothesis.Clean, statuses: gated));

            Assert.Equal(Severity.Unknown, va.Severity);   // NOT Severity.Clean
            Assert.True(va.Qualified);
            Assert.DoesNotContain("No tampering", va.Meaning, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("NOT a clean result", va.Meaning, StringComparison.OrdinalIgnoreCase);

            // Mutation guard: the honesty gate must actually change the output vs a complete clean run.
            var complete = VerdictActions.For(Make(Hypothesis.Clean));
            Assert.NotEqual(complete.Meaning, va.Meaning);
        }

        [Fact]
        public void ThreatUnderReferral_IsQualified_ButActionNotSoftened()
        {
            var confident = VerdictActions.For(Make(Hypothesis.Spoofing));
            var referred = VerdictActions.For(Make(Hypothesis.Spoofing, confidence: 40, referral: true));

            Assert.False(confident.Qualified);
            Assert.True(referred.Qualified);
            Assert.Contains(VerdictActions.ConfidenceCaveat, referred.Meaning);
            // The recommended action stays identical — a limited-confidence threat is still acted on.
            Assert.Equal(confident.PrimaryAction, referred.PrimaryAction);
        }
    }
}
