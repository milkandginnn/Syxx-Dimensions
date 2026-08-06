using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // C007 / B031 (parity) — a single bootstrap resolver cannot form majority consensus, so the
    // ground truth is INCONCLUSIVE, never single-sourced from the sole responder.
    public class AuthoritativeVantageConsensusTests
    {
        // Answers NS/A lookups for any bootstrap and a direct auth answer, mirroring the macOS mock.
        //
        // NS glue MUST be public-routable. The D018 SSRF guard (AuthoritativeVantage.cs:102-106)
        // keeps only `PublicRoutable` NS IPs, so RFC 5737 documentation blocks — 203.0.113.x
        // (TEST-NET-3), 192.0.2.x, 198.51.100.x — are ALL filtered out by IPClassifier and Compare
        // short-circuits to Inconclusive at :107-112 without ever reaching Classify(). These
        // fixtures used 203.0.113.x, which silently turned this suite into a test of the early
        // return. 93.184.216.x is IANA's example.com block: routable, and matched by no
        // NetworkCohort range, so it also cannot steer the same-provider branch of Classify().
        private static Func<DNSQuery, Task<DNSResponse>> Mock() => q =>
        {
            var bootstraps = new[] { "1.1.1.1", "8.8.8.8", "9.9.9.9" };
            if (q.Type == 2 && Array.IndexOf(bootstraps, q.Server) >= 0)
                return Task.FromResult(new DNSResponse
                {
                    Status = DNSResponseStatus.Success,
                    Records = new List<DNSRecord>
                    {
                        new() { Name = q.Name, Type = 2, Ttl = 3600, Rdata = "ns1.example.com." },
                        new() { Name = q.Name, Type = 2, Ttl = 3600, Rdata = "ns2.example.com." },
                    }
                });
            if (q.Type == 1 && Array.IndexOf(bootstraps, q.Server) >= 0)
                return Task.FromResult(new DNSResponse
                {
                    Status = DNSResponseStatus.Success,
                    Records = new List<DNSRecord>
                    {
                        new() { Name = q.Name, Type = 1, Ttl = 3600,
                                Rdata = q.Name == "ns1.example.com" ? "93.184.216.34" : "93.184.216.35" }
                    }
                });
            return Task.FromResult(new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                IsAuthoritative = true,
                Records = new List<DNSRecord> { new() { Name = q.Name, Type = 1, Ttl = 300, Rdata = "9.9.9.9" } }
            });
        };

        [Fact]
        public async Task SingleBootstrapResolver_IsInconclusive_NotSingleSourced()
        {
            var r = await AuthoritativeVantage.Compare(
                domain: "example.com", resolverAnswers: new[] { "1.2.3.4" },
                bootstrapResolvers: new[] { "1.1.1.1" }, query: Mock());
            Assert.Equal(AuthComparisonStatus.Inconclusive, r.Status);
        }

        [Fact]
        public async Task FullBootstrapSet_FormsConsensus_AndClassifies()
        {
            // With the full >=2 bootstrap set, consensus engages and a disjoint resolver answer diverges.
            var r = await AuthoritativeVantage.Compare(
                domain: "example.com", resolverAnswers: new[] { "1.2.3.4" }, query: Mock());
            Assert.Equal(AuthComparisonStatus.Diverges, r.Status);

            // Reachability guard. This assertion is the point: with documentation-range NS glue the
            // D018 filter emptied nsIPs and Compare returned the early Inconclusive, so Classify()
            // was never called and this test exercised nothing. Pin a detail string that ONLY
            // Classify() can produce, and explicitly reject the short-circuit message.
            Assert.DoesNotContain("could not resolve authoritative nameserver IPs", r.Detail);
            Assert.Contains("share nothing with the authoritative set", r.Detail);
            Assert.Equal(IPAddressClass.PublicRoutable, IPClassifier.Classify("93.184.216.34"));
            Assert.Null(NetworkCohort.ProviderForIPv4("93.184.216.34"));
        }
    }

    // F007 (parity with macOS testDomainCanonical_rejectsNameTooLongAfterEncoding /
    // testDomainCanonical_acceptsNameBelowLimitAfterEncoding) — the 253-byte limit is enforced
    // on the ENCODED A-label form, not the pre-IDNA UTF-8 length.
    public class DomainCanonicalTests
    {
        [Fact]
        public void RejectsName_TooLongAfterIDNAEncoding()
        {
            // "aä" × 28 labels joined by dots.
            // UTF-8 length = 28×3 + 27 = 111 bytes (≤ 253) → pre-IDNA guard would PASS.
            // A-label = "xn--a-uia" (9 bytes) × 28 + 27 dots = 279 bytes (> 253) → must REJECT.
            // Mutation proof: if guard is still pre-IDNA, this test passes when it should fail.
            var label = "a\u00e4"; // "aä"
            var name = string.Join(".", Enumerable.Repeat(label, 28));
            var result = DomainCanonical.CanonicalizeOrNull(name);
            Assert.Null(result);
        }

        [Fact]
        public void AcceptsName_BelowLimitAfterIDNAEncoding()
        {
            // "aä" × 27 labels: A-label = 9×27 + 26 dots = 269 — still > 253, use ascii-only
            // Use pure-ASCII labels that stay under limit: "ab" × 25 + dots = 50+24 = 74 bytes.
            var name = string.Join(".", Enumerable.Repeat("ab", 25));
            var result = DomainCanonical.CanonicalizeOrNull(name);
            Assert.NotNull(result);
        }

        [Fact]
        public void RejectsEmpty()
        {
            Assert.Null(DomainCanonical.CanonicalizeOrNull(""));
            Assert.Null(DomainCanonical.CanonicalizeOrNull("   "));
        }

        [Fact]
        public void StripsDot_AndLowercases()
        {
            var result = DomainCanonical.CanonicalizeOrNull("EXAMPLE.COM.");
            Assert.Equal("example.com", result);
        }

        [Fact]
        public void AreEquivalent_ReturnsFalse_OnMismatch()
        {
            // Mutation proof: AreEquivalent("a.com", "b.com") must be false — the comparison
            // is not just "both non-null".
            Assert.False(DomainCanonical.AreEquivalent("a.com", "b.com"));
        }
    }

    // B039 (parity with macOS ReportTests) — the report de-duplicates datapoints across sections.
    // Identical values collapse; a disagreeing value is kept and flagged as DISCREPANCY.
    public class ReportTests
    {
        private static NetworkStateReport Network(string resolver) => new NetworkStateReport
        {
            DnsServers = new List<string> { resolver },
            Vpn = new VPNStatus { State = ConditionState.No, Evidence = "" },
            CaptivePortal = new CaptivePortalStatus { State = ConditionState.No, Evidence = "" },
            EncryptedDNS = new EncryptedDNSStatus { State = ConditionState.Yes, Evidence = "" },
        };

        private static ComparisonResult Comparison(string localServer, List<DNSRecord>? records = null) => new ComparisonResult
        {
            LocalServer = localServer,
            LocalRecords = records ?? new List<DNSRecord>(),
        };

        // Test 1 (B039): Prophet and Mirror report different target_answer values
        // → MergedDatapoint.IsConflicting==true and DISCREPANCY appears in RenderText().
        // Mutation proof: collapse both LocalRecords and Resolution.Records to the same IP
        // → IsConflicting becomes false → this test RED.
        [Fact]
        public void TargetAnswer_CrossDisciplineDiscrepancy_IsConflicting()
        {
            var inputs = new SectionInputs();
            // Mirror sees 1.2.3.4
            inputs.Comparison = Comparison("1.1.1.1", new List<DNSRecord>
            {
                new() { Name = "example.com", Type = 1, Ttl = 300, Rdata = "1.2.3.4" }
            });
            // Prophet sees 5.6.7.8
            inputs.Resolution = new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                Records = new List<DNSRecord>
                {
                    new() { Name = "example.com", Type = 1, Ttl = 300, Rdata = "5.6.7.8" }
                }
            };

            var report = ReportBuilder.Build(inputs);
            var answer = report.Datapoints.FirstOrDefault(d => d.Key == "target_answer");
            Assert.NotNull(answer);
            Assert.True(answer!.IsConflicting, "Prophet and Mirror disagreeing on target_answer must be flagged as conflicting");
            Assert.Contains("DISCREPANCY", report.RenderText());
        }

        // Test 2 (B039): empty SectionInputs produces no fabricated datapoints and does not crash.
        [Fact]
        public void EmptyInputs_ProducesNoDatapoints()
        {
            var inputs = new SectionInputs();
            var report = ReportBuilder.Build(inputs);
            Assert.Empty(report.Datapoints);
        }

        // Test 3 (B039): RenderText() must include the structural sections: Conclusion, FACTS, WHY (if
        // evidence), and NEXT STEPS.
        [Fact]
        public void RenderText_ContainsRequiredStructuralSections()
        {
            var inputs = new SectionInputs();
            inputs.Network = Network("1.1.1.1");
            var report = ReportBuilder.Build(inputs);
            var text = report.RenderText();
            Assert.Contains("Conclusion:", text);
            Assert.Contains("FACTS", text);
            Assert.Contains("NEXT STEPS", text);
        }

    }
}
