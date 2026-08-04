using System;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // A4 — first-run scan. RunAsync dials real resolvers and is untestable by construction, so the
    // decision logic lives in the pure Summarize() seam and is tested here against REAL engine
    // output (production Synthesizer.Conclude), not hand-built Conclusions.
    //
    // The honesty contract under test: a probe that produced nothing is reported as UNMEASURED and
    // the scan is graded Ungraded — never a letter, and never silently treated as clean.
    public class FirstRunScanTests
    {
        private const string AllDisciplines = "Prophet,Mirror,Portal,Glutton";

        [Fact]
        public void EveryDiscipline_IsAccountedFor_RanPlusSkippedIsComplete()
        {
            // Guards the drop I actually made while refactoring: a Track() call silently removed
            // still compiles, and the discipline just vanishes from the report rather than being
            // reported as unmeasured. Ran + Skipped must always name all four.
            var r = FirstRunScan.Summarize(new SectionInputs());
            var accounted = r.Ran.Concat(r.Skipped).OrderBy(x => x).ToList();
            Assert.Equal(4, accounted.Count);
            Assert.Equal(AllDisciplines.Split(',').OrderBy(x => x), accounted);
        }

        [Fact]
        public void NothingMeasured_IsUngraded_AndEveryDisciplineReportedSkipped()
        {
            var r = FirstRunScan.Summarize(new SectionInputs());

            Assert.Equal(Grade.Ungraded, r.Card.Grade);      // never a letter
            Assert.Equal("N/A", r.Card.GradeLabel);
            Assert.Empty(r.Ran);
            Assert.Equal(4, r.Skipped.Count);
            // The engine's own gate must be what drives this, not our bookkeeping.
            Assert.True(r.Conclusion.HasHardGatedSection);
        }

        [Fact]
        public void AProbeThatProducedNothing_IsSkipped_NotCountedAsRun()
        {
            // Portal produced a report; Mirror's vantage comparison failed (null) — the honest
            // outcome is Mirror reported unmeasured, NOT quietly omitted or treated as clean.
            var inputs = new SectionInputs
            {
                Resolution = new DNSResponse { Status = DNSResponseStatus.Success },
                Comparison = null,
            };
            var r = FirstRunScan.Summarize(inputs);

            Assert.Contains("Prophet", r.Ran);
            Assert.Contains("Mirror", r.Skipped);
            Assert.DoesNotContain("Mirror", r.Ran);
        }

        [Fact]
        public void Prophet_CountsAsRun_OnACanaryAlone_EvenWithoutAResolution()
        {
            // A hijacking resolver can fail the A query yet still trip the canary. That is evidence,
            // so Prophet must count as having run — otherwise a detected hijack reads as unmeasured.
            var hijack = new DNSCanaries.CanaryResult(
                Section.Prophet, SectionStatus.Positive,
                EvidenceItem.Create(Section.Prophet, EvidenceId.NxCanaryHijack,
                    "Two unrelated random names both resolved to 203.0.113.7",
                    "Synthesized answers for names that cannot exist.", source: "canaries"),
                "hijack");
            var r = FirstRunScan.Summarize(new SectionInputs { NxCanary = hijack });

            Assert.Contains("Prophet", r.Ran);
            // And a real hijack must not be graded A/B by the card.
            Assert.NotEqual(Grade.A, r.Card.Grade);
            Assert.NotEqual(Grade.B, r.Card.Grade);
            Assert.False(r.Conclusion.ReadsClean(), "a real hijack must never commit to clean");
        }

        [Fact]
        public void Summarize_IsDeterministic_SameEvidenceSameGrade()
        {
            var inputs = new SectionInputs { Resolution = new DNSResponse { Status = DNSResponseStatus.Success } };
            Assert.Equal(FirstRunScan.Summarize(inputs).Card.Grade, FirstRunScan.Summarize(inputs).Card.Grade);
        }

        [Fact]
        public void DefaultDomain_IsSetSoAFirstRunNeedsNoInput()
        {
            Assert.False(string.IsNullOrWhiteSpace(FirstRunScan.DefaultDomain));
        }
    }
}
