using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    // Two guards on the ground-truth comparison path that the 2026-07-16 audit found had ZERO
    // tests. Both could be deleted with a green suite, and both change a RESULT:
    //
    //   * B036 resolverDidFail — the one branch stopping a FAILED resolver query from reading
    //     "consistent with authoritative". Without it, a timeout looks like agreement.
    //   * NetworkCohort.SharedProvider — ANNOTATES a disjoint result with the shared CDN provider
    //     when both answer sets sit in one CDN's ranges. It USED TO downgrade Diverges ->
    //     Inconclusive; K-16 removed that softening on both platforms, because an attacker can rent
    //     space inside the provider's own ASN, and because Inconclusive fell through Synthesizer's
    //     `default:` and emitted NO evidence item at all — the finding did not soften, it
    //     DISAPPEARED. The provider name must still reach the detail string, so an overbroad range
    //     or a wrong mask stays detectable here.
    public class AuthoritativeGuardTests
    {
        private static HashSet<string> S(params string[] ips) => new(ips);
        private static readonly List<string> NoNs = new();

        // MARK: - B036

        [Fact]
        public void BothEmpty_AndResolverSucceeded_IsMatches()
        {
            // The baseline the guard modifies — kept so "always inconclusive" cannot pass.
            var r = AuthoritativeVantage.Classify(S(), S(), NoNs, "x");
            Assert.Equal(AuthComparisonStatus.Matches, r.Status);
        }

        [Fact]
        public void BothEmpty_ButResolverFAILED_IsInconclusive_NotMatches()
        {
            // B036. An empty set from a failed query is "no response", not "no address".
            // Deleting this branch makes a timeout read as consistency with ground truth.
            var r = AuthoritativeVantage.Classify(S(), S(), NoNs, "x", resolverDidFail: true);

            Assert.Equal(AuthComparisonStatus.Inconclusive, r.Status);
            Assert.NotEqual(AuthComparisonStatus.Matches, r.Status);
            Assert.Contains("resolver query failed", r.Detail);
        }

        // MARK: - SharedProvider (the dampener)

        [Fact]
        public void DisjointAnswers_InOneCdn_RetainDivergence_ProviderNoted()
        {
            // AMENDED DELIBERATELY (K-16), not softened to go green. Both sets are Fastly
            // (151.101.0.0/16). This test used to assert Inconclusive and was therefore pinning the
            // exact behaviour K-16 corrected: same-provider is NOT evidence of benign geo-routing,
            // and the softened result emitted no evidence item at all. The corrected contract is
            // that the divergence is RETAINED and the shared provider is surfaced as a
            // corroboration note. Equivalent, richer coverage also lives in
            // ForensicsAnalyzerTests.AuthoritativeSameProviderTests (which additionally asserts
            // "divergence retained"), so this amendment loses no coverage.
            var r = AuthoritativeVantage.Classify(S("151.101.1.1"), S("151.101.2.2"), NoNs, "x");
            Assert.Equal(AuthComparisonStatus.Diverges, r.Status);
            Assert.Contains("Fastly", r.Detail);
        }

        [Fact]
        public void DisjointAnswers_AcrossDifferentProviders_StayDiverges()
        {
            // Fastly vs Google — NOT one provider, so the dampener must NOT fire. This is the
            // assertion that stops the dampener from swallowing real rewrites: if SharedProvider
            // wrongly returned a name here, a genuine divergence would read as inconclusive.
            var r = AuthoritativeVantage.Classify(S("151.101.1.1"), S("142.250.1.1"), NoNs, "x");
            Assert.Equal(AuthComparisonStatus.Diverges, r.Status);
        }

        [Fact]
        public void DisjointAnswers_WithAnUnknownAddress_StayDiverges()
        {
            // 203.0.113.9 (TEST-NET-3) is in no provider range. An attacker's address is unlikely
            // to be in a published CDN block, so this is the realistic rewrite case — it must NOT
            // be softened.
            var r = AuthoritativeVantage.Classify(S("203.0.113.9"), S("151.101.2.2"), NoNs, "x");
            Assert.Equal(AuthComparisonStatus.Diverges, r.Status);
        }

        [Fact]
        public void SharedProvider_RequiresEveryAddressInOneProvider()
        {
            Assert.Equal("Fastly", NetworkCohort.SharedProvider(
                new List<string> { "151.101.1.1" }, new List<string> { "151.101.2.2" }));

            // Mixed providers -> null (no dampening).
            Assert.Null(NetworkCohort.SharedProvider(
                new List<string> { "151.101.1.1" }, new List<string> { "142.250.1.1" }));

            // One unknown address -> null. A single unrecognised IP must veto the whole group,
            // otherwise an attacker address rides along with a CDN answer and gets softened.
            Assert.Null(NetworkCohort.SharedProvider(
                new List<string> { "151.101.1.1" }, new List<string> { "203.0.113.9" }));

            // Empty -> null, never a provider name.
            Assert.Null(NetworkCohort.SharedProvider(new List<string>(), new List<string>()));
        }

        [Fact]
        public void ProviderForIPv4_MapsKnownRangesAndRejectsGarbage()
        {
            Assert.Equal("Fastly", NetworkCohort.ProviderForIPv4("151.101.1.1"));
            Assert.Equal("Google", NetworkCohort.ProviderForIPv4("142.250.1.1"));
            Assert.Null(NetworkCohort.ProviderForIPv4("203.0.113.9"));   // TEST-NET-3
            Assert.Null(NetworkCohort.ProviderForIPv4("not-an-ip"));
            Assert.Null(NetworkCohort.ProviderForIPv4(""));
        }
    }
}
