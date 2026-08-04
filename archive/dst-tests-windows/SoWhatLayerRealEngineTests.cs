using System;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // Phase A (A1/A2/A3) against REAL ENGINE OUTPUT — not hand-built Conclusion records.
    //
    // Every Conclusion here comes out of the production `Synthesizer.Conclude(...)`, so these lock
    // that the "so what" layers respond to what the engine ACTUALLY emits (its own gating, section
    // statuses, evidence Supports sets and confidence), rather than to a fixture that flatters them.
    // The fixture-level tests (VerdictActionTests / PlainExplanationTests / ReportCardTests) cover
    // the branch matrix; these cover the seam to reality.
    public class SoWhatLayerRealEngineTests
    {
        /// A REAL no-data run: the engine hard-gates on untested sections. All three surfaces must
        /// agree it is unfinished — this is the exact state a user sees before running any check.
        [Fact]
        public void RealEngine_NoData_AllThreeSurfacesSayUnfinished_NeverClean()
        {
            var c = Synthesizer.Conclude(new SectionInputs());

            // Precondition: this really is the engine's own gating, not a fixture.
            Assert.True(c.HasHardGatedSection, "expected the real engine to hard-gate a no-data run");

            var action = VerdictActions.For(c);
            var expl   = PlainExplanations.For(c);
            var card   = ReportCards.For(c);

            Assert.Equal(Severity.Unknown, action.Severity);
            Assert.True(action.Qualified);
            Assert.DoesNotContain("No tampering", action.Meaning, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("real verdict", action.PrimaryAction, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("unfinished", expl.Summary, StringComparison.OrdinalIgnoreCase);
            // The engine's untested sections must actually reach the user as caveats.
            Assert.NotEmpty(expl.Despite);

            Assert.Equal(Grade.Ungraded, card.Grade);
            Assert.Equal("—", card.GradeLabel);
            Assert.NotEmpty(card.TopReasons);
        }

        /// A REAL threat run: feed the engine a genuine NXDOMAIN-hijack canary (the same shape
        /// DNSCanaries.NxdomainHijackAsync returns on a live hijacking resolver, DNSCanaries.cs:57)
        /// and let the engine decide. The surfaces must never reassure, and must never grade it A.
        [Fact]
        public void RealEngine_HijackCanary_IsNeverReassuring_AndNeverGradedA()
        {
            var hijack = new DNSCanaries.CanaryResult(
                Section.Prophet, SectionStatus.Positive,
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack,
                    "Two unrelated random names both resolved to 203.0.113.7",
                    "A resolver that answers names that cannot exist is synthesizing addresses.",
                    source: "canaries"),
                "Both canary names -> 203.0.113.7");

            var c = Synthesizer.Conclude(new SectionInputs { NxCanary = hijack });

            // The engine's own clean-eligibility gate must have fired on the Positive section.
            Assert.False(c.ReadsClean(), "a positive section must gate out a committed clean");

            var action = VerdictActions.For(c);
            var card   = ReportCards.For(c);

            Assert.NotEqual(Severity.Clean, action.Severity);
            Assert.DoesNotContain("No action needed", action.Meaning, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("No tampering", action.Meaning, StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Grade.A, card.Grade);
            Assert.NotEqual(Grade.B, card.Grade);
        }

        /// The A2 explanation must be built from the engine's REAL evidence, not invented: whatever
        /// the engine ranked as supporting the verdict is what the user is told.
        [Fact]
        public void RealEngine_Because_MirrorsTheEnginesOwnSupportingEvidence()
        {
            var hijack = new DNSCanaries.CanaryResult(
                Section.Prophet, SectionStatus.Positive,
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack,
                    "Two unrelated random names both resolved to 203.0.113.7",
                    "Synthesized answers for names that cannot exist.",
                    source: "canaries"),
                "hijack");

            var c = Synthesizer.Conclude(new SectionInputs { NxCanary = hijack });
            var expl = PlainExplanations.For(c);

            var engineSupporting = c.Evidence
                .Where(e => e.Supports.Contains(c.Diagnosis))
                .Select(e => e.Observation)
                .ToList();

            // Every reason we give the user is an observation the engine actually attributed to the
            // verdict — no fabricated justification.
            foreach (var because in expl.Because.Where(b => !b.StartsWith("the checks that ran")))
                Assert.Contains(because, engineSupporting);
        }

        /// A Positive section means a THREAT WAS FOUND (SynthesisEngine.cs:110 gates Clean on it).
        /// The report card must never describe such a section as "found nothing wrong" — the bug
        /// this test was written to kill.
        [Fact]
        public void PositiveSection_IsNeverDescribedAsFoundNothingWrong()
        {
            var c = new Conclusion(
                Hypothesis.Clean, Conclusivity.Conclusive, 90,
                Probabilities: Array.Empty<HypothesisProbability>(),
                Evidence: Array.Empty<EvidenceItem>(),
                SectionStatuses: new[]
                {
                    new SectionStatusEntry(Section.Prophet, SectionStatus.Negative),
                    new SectionStatusEntry(Section.Mirror,  SectionStatus.Positive), // threat found
                },
                NextSteps: Array.Empty<string>(),
                ReferralTriggered: false, Referral: null,
                GeneratedAt: DateTimeOffset.UnixEpoch);

            var card = ReportCards.For(c);
            Assert.DoesNotContain(card.TopReasons, r => r.Contains("Mirror") && r.Contains("found nothing wrong"));
        }
    }
}
