using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // C058 — Cage section result must account for process-attribution completeness.
    // Parity twins of the macOS SynthesizerDatapointWiringTests C058 cases.
    public class CageAttributionCompletenessTests
    {
        private static ExfilAssessment Benign() => ExfilAnalyzer.Assess("www.example.com");

        // (a) attributionRate 0.0 + all-benign -> DEGRADED, not a bare clean, with disclosure.
        [Fact]
        public void CageZeroAttributionForcesDegradedNotClean()
        {
            var benign = Benign();
            Assert.Equal(ExfilSuspicion.Benign, benign.Suspicion);
            var se = EvidenceAdapters.Cage(new List<ExfilAssessment> { benign }, null, attributionRate: 0.0);
            Assert.Equal(SectionStatus.Degraded, se.Status);
            Assert.NotEqual(SectionStatus.Negative, se.Status);
            Assert.Contains(se.Evidence, e => e.EvidenceId == EvidenceId.AttributionIncomplete);
        }

        // (b) attributionRate 1.0 + all-benign -> clean negative, no caveat (pre-C058 behaviour).
        [Fact]
        public void CageFullAttributionStaysClean()
        {
            var se = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() }, null, attributionRate: 1.0);
            Assert.Equal(SectionStatus.Negative, se.Status);
            Assert.DoesNotContain(se.Evidence, e => e.EvidenceId == EvidenceId.AttributionIncomplete);
        }

        // Boundary: rate == threshold is adequate; just below it is degraded (shared < convention).
        [Fact]
        public void CageAttributionThresholdBoundary()
        {
            const double threshold = 0.5;   // EvidenceAdapters.CageAttributionThreshold (internal)
            var at = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() }, null, attributionRate: threshold);
            Assert.Equal(SectionStatus.Negative, at.Status);
            var below = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() }, null, attributionRate: threshold - 0.01);
            Assert.Equal(SectionStatus.Degraded, below.Status);
        }

        // null attributionRate = not a capture-sourced run: legacy contract unchanged.
        [Fact]
        public void CageNilAttributionPreservesLegacyContract()
        {
            var withNull = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() }, null, attributionRate: null);
            var legacy = EvidenceAdapters.Cage(new List<ExfilAssessment> { Benign() });
            Assert.Equal(SectionStatus.Negative, withNull.Status);
            Assert.Equal(SectionStatus.Negative, legacy.Status);
            Assert.DoesNotContain(withNull.Evidence, e => e.EvidenceId == EvidenceId.AttributionIncomplete);
        }

        // Empty-exfil capture (ran, nothing suspicious) with 0% attribution -> degraded, not clean.
        [Fact]
        public void CageEmptyExfilWithZeroAttributionIsDegraded()
        {
            var se = EvidenceAdapters.Cage(new List<ExfilAssessment>(), null, attributionRate: 0.0);
            Assert.Equal(SectionStatus.Degraded, se.Status);
        }

        // H1 — no exfil AND no response-size leg = not a capture-sourced run at all. An attributionRate
        // arriving alone must NOT manufacture a clean Negative over a run where no domain was ever
        // exfil-assessed: the section is UNTESTED (mirrors macOS Synthesizer.swift:513's early return).
        // Guards the Windows false-clean bug where Cage(null, null, 0.9) yielded subs=[Negative] -> clean
        // with completeness 1.0.
        [Fact]
        public void CageNoExfilNoResponseSizeIsUntestedRegardlessOfAttribution()
        {
            var high = EvidenceAdapters.Cage(null, null, attributionRate: 0.9);
            Assert.Equal(SectionStatus.Untested, high.Status);
            Assert.Equal(0.0, high.Completeness);
            Assert.DoesNotContain(high.Evidence, e => e.EvidenceId == EvidenceId.AttributionIncomplete);

            // Even a below-threshold rate cannot force Degraded when there was no capture leg to degrade.
            var low = EvidenceAdapters.Cage(null, null, attributionRate: 0.1);
            Assert.Equal(SectionStatus.Untested, low.Status);

            // And with no attribution either, still Untested (legacy no-input contract).
            var none = EvidenceAdapters.Cage(null, null, attributionRate: null);
            Assert.Equal(SectionStatus.Untested, none.Status);
        }
    }

    // Ph3.2 — SectionEvidence.Completeness must be a real N/M over the section's datapoint
    // catalog, never the inert 1.0 default; the count-based clean-support scaling in the
    // engine depends on it.
    public class SectionCompletenessTests
    {
        private static DNSResponse Ok() => new() { Status = DNSResponseStatus.Success };

        [Fact]
        public void ProphetCompletenessGrowsWithConclusiveDatapoints()
        {
            var two = EvidenceAdapters.Prophet(Ok(), DNSSECStatus.RecordsPresentUnverified, null, null);
            Assert.Equal(2.0 / EvidenceAdapters.ProphetDatapointCatalog, two.Completeness, 6);

            var three = EvidenceAdapters.Prophet(Ok(), DNSSECStatus.RecordsPresentUnverified, null, null,
                latency: new ResolverLatency(ResolverLatencyStatus.Fast));
            Assert.Equal(3.0 / EvidenceAdapters.ProphetDatapointCatalog, three.Completeness, 6);
            Assert.True(three.Completeness > two.Completeness,
                "an additional conclusive datapoint must raise completeness");
        }

        // The Ph3.2 acceptance bar: an N-clean + 1-timeout run must read visibly LOWER
        // completeness than the same run with all datapoints conclusive.
        [Fact]
        public void TimeoutDatapointLowersCompleteness()
        {
            var allOk = EvidenceAdapters.Prophet(Ok(), DNSSECStatus.RecordsPresentUnverified, null, null,
                latency: new ResolverLatency(ResolverLatencyStatus.Fast),
                stability: new AnswerStability(AnswerStabilityStatus.Stable),
                edns0: new EDNS0Capability(EDNS0CapabilityStatus.Supported));
            var oneTimedOut = EvidenceAdapters.Prophet(Ok(), DNSSECStatus.RecordsPresentUnverified, null, null,
                latency: new ResolverLatency(ResolverLatencyStatus.Unreachable),  // timeout → Invalidated
                stability: new AnswerStability(AnswerStabilityStatus.Stable),
                edns0: new EDNS0Capability(EDNS0CapabilityStatus.Supported));

            Assert.Equal(5.0 / EvidenceAdapters.ProphetDatapointCatalog, allOk.Completeness, 6);
            Assert.Equal(4.0 / EvidenceAdapters.ProphetDatapointCatalog, oneTimedOut.Completeness, 6);
            Assert.True(oneTimedOut.Completeness < allOk.Completeness,
                "a timed-out datapoint must not count as usable evidence");
        }

        [Fact]
        public void UntestedSectionsReadZeroCompleteness()
        {
            Assert.Equal(0.0, EvidenceAdapters.Portal(null).Completeness);
            Assert.Equal(0.0, EvidenceAdapters.Glutton(null).Completeness);
            Assert.Equal(0.0, EvidenceAdapters.Witness(null, null).Completeness);
        }

        [Fact]
        public void CompletenessSurvivesIntoSectionStatusEntries()
        {
            // End-to-end: Conclude() must carry the real per-section value into the entries the
            // engine scales clean support by — if a builder regresses to the 1.0 default for a
            // thin run, this fails.
            var inputs = new SectionInputs { Resolution = Ok() };
            var conclusion = Synthesizer.Conclude(inputs);
            var prophetEntry = conclusion.SectionStatuses.Single(s => s.Section == Section.Prophet);
            Assert.Equal(1.0 / EvidenceAdapters.ProphetDatapointCatalog, prophetEntry.Completeness, 6);
        }
    }
}
