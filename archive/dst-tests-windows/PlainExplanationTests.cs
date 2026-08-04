using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // A2 — plain-language "we believe X because A and B, despite C" layer. Locks that the
    // explanation is built from real evidence Supports sets and that the honesty contract holds:
    // an unfinished result is never explained as clean, and contradicting factors are surfaced.
    public class PlainExplanationTests
    {
        private static EvidenceItem Ev(string obs, double weight, params Hypothesis[] supports) =>
            new EvidenceItem(Section.Prophet, EvidenceId.AuthoritativeDiverges, obs, "rationale",
                             supports.ToList(), weight, Source: null);

        private static Conclusion Make(
            Hypothesis verdict,
            Conclusivity conclusivity = Conclusivity.Conclusive,
            int confidence = 90,
            IEnumerable<EvidenceItem>? evidence = null,
            IEnumerable<SectionStatusEntry>? statuses = null,
            IEnumerable<HypothesisProbability>? probs = null,
            double conflict = 0.0)
        {
            // `Negative` = ran and found nothing. `Positive` = a THREAT was found and gates Clean out
            // (SynthesisEngine.cs:110) — it must not be the default for a "clean" fixture.
            statuses ??= new[] { new SectionStatusEntry(Section.Prophet, SectionStatus.Negative) };
            return new Conclusion(
                verdict, conclusivity, confidence,
                Probabilities: (probs ?? Array.Empty<HypothesisProbability>()).ToList(),
                Evidence: (evidence ?? Array.Empty<EvidenceItem>()).ToList(),
                SectionStatuses: statuses.ToList(),
                NextSteps: Array.Empty<string>(),
                ReferralTriggered: false, Referral: null,
                GeneratedAt: DateTimeOffset.UnixEpoch)
            { Conflict = conflict };
        }

        [Fact]
        public void Threat_Because_DrawsFromEvidenceSupportingTheVerdict()
        {
            var c = Make(Hypothesis.Spoofing, evidence: new[]
            {
                Ev("resolver returned an address no trusted vantage point agrees with", 0.9, Hypothesis.Spoofing),
                Ev("a background detail", 0.1, Hypothesis.Clean),
            });
            var e = PlainExplanations.For(c);
            Assert.Contains("we believe this is", e.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(e.Because, b => b.Contains("no trusted vantage point"));
            // Clean-supporting evidence is not offered as a reason for a spoofing verdict.
            Assert.DoesNotContain(e.Because, b => b.Contains("background detail"));
        }

        [Fact]
        public void Despite_SurfacesCompetingSignalsAndGaps_NeverSuppressed()
        {
            var c = Make(Hypothesis.Spoofing,
                evidence: new[]
                {
                    Ev("answers diverge from authoritative", 0.9, Hypothesis.Spoofing),
                    Ev("transport looks downgraded too", 0.6, Hypothesis.InterceptionTransport),
                },
                statuses: new[]
                {
                    new SectionStatusEntry(Section.Prophet, SectionStatus.Positive),
                    new SectionStatusEntry(Section.Witness, SectionStatus.Untested),
                },
                conflict: 0.30);

            var e = PlainExplanations.For(c);
            Assert.Contains(e.Despite, d => d.Contains("transport looks downgraded"));      // competing signal
            Assert.Contains(e.Despite, d => d.Contains("Witness") && d.Contains("did not run")); // gap
            Assert.Contains(e.Despite, d => d.Contains("disagree"));                         // conflict
            Assert.Contains("That said,", e.Narrative);
        }

        [Fact]
        public void CleanAndComplete_StatesHonestBasis_NoDespiteInvented()
        {
            var c = Make(Hypothesis.Clean);
            var e = PlainExplanations.For(c);
            Assert.Contains("clean", e.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.Single(e.Because);
            Assert.Contains("no signs of tampering", e.Because[0]);
            Assert.Empty(e.Despite);
        }

        [Fact]
        public void Inconclusive_ExplainedAsUnfinished_NotClean()
        {
            var c = Make(Hypothesis.Clean, Conclusivity.Inconclusive, confidence: 0);
            var e = PlainExplanations.For(c);
            Assert.Contains("unfinished", e.Summary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("clean", e.Summary.Replace("not clean", ""), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void CleanButHardGated_ExplainedAsUnfinished_NotClean()
        {
            var c = Make(Hypothesis.Clean, statuses: new[]
            {
                new SectionStatusEntry(Section.Prophet, SectionStatus.Positive),
                new SectionStatusEntry(Section.Mirror,  SectionStatus.Untested),
            });
            var e = PlainExplanations.For(c);
            Assert.Contains("unfinished", e.Summary, StringComparison.OrdinalIgnoreCase);
            // The skipped section is surfaced as a caveat.
            Assert.Contains(e.Despite, d => d.Contains("Mirror") && d.Contains("did not run"));
        }
    }
}
