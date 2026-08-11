using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    // B1 — SignedIncidentReport renderer tests. Every test builds a real signed WitnessSnapshot
    // through the production WitnessSigner path, so output is tested against real signed data.
    // Failure cases (Protocol §3): every assertion fails when the renderer produces wrong result
    // text, omits required sections, or fabricates data — never passes vacuously.
    public class SignedIncidentReportTests
    {
        private static (string Priv, string Pub) TestKeyPair => WitnessSigner.GenerateKeyPair();

        private static ComparisonResult MakeComparison(
            string domain = "example.com",
            bool redirected = false,
            bool inconclusive = false,
            bool degraded = false)
        {
            var rec = new DNSRecord { Name = domain, Type = 1, Ttl = 60, Rdata = "93.184.216.34" };
            var okOutcome = new ResolverQueryOutcome
            {
                Records = new List<DNSRecord> { rec },
                Status = DNSResponseStatus.Success
            };
            return new ComparisonResult
            {
                LocalServer = "1.1.1.1",
                LocalRecords = new List<DNSRecord> { rec },
                PublicResolverResults = new Dictionary<string, List<DNSRecord>>
                {
                    { "Cloudflare", new List<DNSRecord> { rec } }
                },
                Result = redirected ? VantageComparisonResult.RedirectionDetected
                    : degraded ? VantageComparisonResult.Degraded
                    : inconclusive ? VantageComparisonResult.Inconclusive
                    : VantageComparisonResult.Consistent,
                LocalOutcome = okOutcome,
                PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
                {
                    { "Cloudflare", okOutcome }
                }
            };
        }

        private static string CaptureReport(ComparisonResult comparison, bool includeExplanation = false)
        {
            var keypair = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", comparison, keypair.Priv);
            PlainExplanation? explanation = null;
            if (includeExplanation)
            {
                var conclusion = Synthesizer.Conclude(new SectionInputs());
                explanation = PlainExplanations.For(conclusion);
            }
            return SignedIncidentReport.Render(snapshot, explanation);
        }

        private static string CaptureReportFromSnapshot(WitnessSnapshot snapshot)
            => SignedIncidentReport.Render(snapshot);
        [Fact]
        public void CleanComparison_RendersMatchesResult()
        {
            var report = CaptureReport(MakeComparison(redirected: false, inconclusive: false, degraded: false));
            Assert.Contains("MATCHES", report);
            Assert.DoesNotContain("DIVERGES", report);
            Assert.DoesNotContain("INCONCLUSIVE", report);
            Assert.DoesNotContain("DEGRADED", report);
        }

        [Fact]
        public void RedirectedComparison_RendersDivergesResult()
        {
            var report = CaptureReport(MakeComparison(redirected: true));
            Assert.Contains("DIVERGES", report);
            Assert.DoesNotContain("MATCHES", report);
        }

        [Fact]
        public void InconclusiveComparison_RendersInconclusiveResult()
        {
            var report = CaptureReport(MakeComparison(inconclusive: true));
            Assert.Contains("INCONCLUSIVE", report);
            Assert.DoesNotContain("MATCHES", report);
            Assert.DoesNotContain("DIVERGES", report);
        }

        [Fact]
        public void DegradedComparison_RendersDegradedResult()
        {
            var report = CaptureReport(MakeComparison(degraded: true));
            Assert.Contains("DEGRADED", report);
            Assert.DoesNotContain("MATCHES", report);
        }

        [Fact]
        public void DegradedAndInconclusive_RendersDegraded()
        {
            var report = CaptureReport(MakeComparison(degraded: true, inconclusive: true));
            Assert.Contains("DEGRADED", report);
        }

        [Fact]
        public void NoConclusionProvided_OmitsDecisionSection()
        {
            var report = CaptureReport(MakeComparison(), includeExplanation: false);
            Assert.DoesNotContain("DECISION", report);
        }

        [Fact]
        public void ConclusionProvided_IncludesDecisionSection()
        {
            var report = CaptureReport(MakeComparison(), includeExplanation: true);
            Assert.Contains("DECISION", report);
        }

        [Fact]
        public void ReportContainsAccurateMetadata()
        {
            var keypair = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), keypair.Priv);
            var report = CaptureReportFromSnapshot(snapshot);
            Assert.Contains("example.com", report);
            Assert.Contains("SYXX SIGNED INCIDENT REPORT", report);
            Assert.Contains($"Key ID:     {snapshot.KeyId}", report);
        }

        [Fact]
        public void ReportContainsCryptographicSeal()
        {
            var keypair = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("example.com", MakeComparison(), keypair.Priv);
            var report = CaptureReportFromSnapshot(snapshot);
            Assert.Contains("CRYPTOGRAPHIC SEAL", report);
            Assert.Contains(snapshot.KeyId, report);
            var pubPrefix = snapshot.PublicKey[..Math.Min(48, snapshot.PublicKey.Length)];
            Assert.Contains(pubPrefix, report);
            Assert.Contains("syxx witness verify", report);
        }

        [Fact]
        public void ReportContainsEvidenceSummary()
        {
            var report = CaptureReport(MakeComparison());
            Assert.Contains("EVIDENCE SUMMARY", report);
            Assert.Contains("1.1.1.1", report);
            Assert.Contains("93.184.216.34", report);
            Assert.Contains("Cloudflare", report);
        }

        [Fact]
        public void EmptyEvidence_DoesNotFabricateIps()
        {
            var empty = MakeComparison(domain: "empty.test");
            empty.LocalServer = "";
            empty.LocalRecords.Clear();
            empty.LocalOutcome = new ResolverQueryOutcome
            {
                Records = new List<DNSRecord>(),
                Status = DNSResponseStatus.NoData
            };
            empty.PublicResolverResults.Clear();
            empty.PublicResolverOutcomes.Clear();
            empty.Result = VantageComparisonResult.Degraded;
            var keypair = TestKeyPair;
            var snapshot = WitnessSigner.CreateSignedSnapshot("empty.test", empty, keypair.Priv);
            var report = CaptureReportFromSnapshot(snapshot);
            Assert.DoesNotContain("93.184.216.34", report);
            Assert.Contains("no reliable reference", report);
        }
    }
}
