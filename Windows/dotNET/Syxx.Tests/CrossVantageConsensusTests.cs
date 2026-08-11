using System.Collections.Generic;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// B281 — Windows twin of macOS <c>CrossVantageConsensusTests.swift</c>. Direct coverage of the
    /// shared policy helpers (the adversarial review found ZERO), plus the Mirror <c>Evaluate</c>
    /// disjoint behaviour. Decision-table parity itself is enforced by the shared golden vector.
    /// </summary>
    public class CrossVantageConsensusTests
    {
        private const string PoolA = "140.82.113.3";
        private const string PoolB = "140.82.113.4";     // adjacent, same /16
        private const string PoolFar = "140.82.121.9";   // same /16, different /24
        private const string Unrelated = "45.33.32.156"; // different /16
        private const string Blocked = "127.0.0.1";

        private static DNSRecord Rec(string ip, ushort type = 1)
            => new DNSRecord { Name = "example.com", Type = type, Ttl = 60, Rdata = ip };
        private static ResolverQueryOutcome Out(string[] ips, DNSResponseStatus status = DNSResponseStatus.Success)
        {
            var recs = new List<DNSRecord>();
            foreach (var ip in ips) recs.Add(Rec(ip));
            return new ResolverQueryOutcome { Records = recs, Status = status };
        }
        private static HashSet<string> Set(params string[] xs) => new HashSet<string>(xs);

        // MARK: classifyPeerConsensus

        [Fact]
        public void PeerConsensus_Identical_IsMatch()
            => Assert.Equal(CrossVantageOutcome.Match,
                CrossVantageConsensus.ClassifyPeerConsensus(new List<HashSet<string>> { Set(PoolB), Set(PoolB) }));

        [Fact]
        public void PeerConsensus_OwnerSameNetworkSplit_IsInconclusiveNotMismatch()
            => Assert.Equal(CrossVantageOutcome.Inconclusive,
                CrossVantageConsensus.ClassifyPeerConsensus(new List<HashSet<string>> { Set(PoolA), Set(PoolB) }));

        [Fact]
        public void PeerConsensus_P0c_DifferentMultiAddressSharingFirst_IsNotMatch()
        {
            var got = CrossVantageConsensus.ClassifyPeerConsensus(
                new List<HashSet<string>> { Set(PoolA, PoolB), Set(PoolA, "9.9.9.9") });
            Assert.NotEqual(CrossVantageOutcome.Match, got);   // records.first would collapse to a false clean
        }

        [Fact]
        public void PeerConsensus_BlockingBesideRoutable_IsMismatch()
            => Assert.Equal(CrossVantageOutcome.Mismatch,
                CrossVantageConsensus.ClassifyPeerConsensus(new List<HashSet<string>> { Set(Blocked), Set(PoolB) }));

        [Fact]
        public void PeerConsensus_OneUsable_IsInsufficient()
            => Assert.Equal(CrossVantageOutcome.Insufficient,
                CrossVantageConsensus.ClassifyPeerConsensus(new List<HashSet<string>> { Set(PoolB), Set() }));

        // MARK: classifyDivergence / strength

        [Fact]
        public void Divergence_NoReference_IsInconclusive()
            => Assert.Equal(CrossVantageOutcome.Inconclusive,
                CrossVantageConsensus.ClassifyDivergence(Set(Unrelated), Set()));

        [Fact]
        public void Divergence_SameNetworkAsReference_IsInconclusive()
            => Assert.Equal(CrossVantageOutcome.Inconclusive,
                CrossVantageConsensus.ClassifyDivergence(Set(PoolA), Set(PoolB)));

        [Fact]
        public void Divergence_DifferentNetworkFromReference_IsMismatch()
            => Assert.Equal(CrossVantageOutcome.Mismatch,
                CrossVantageConsensus.ClassifyDivergence(Set(Unrelated), Set(PoolB)));

        [Fact]
        public void Strong_SameSlash16DifferentSlash24_IsWeak()
            => Assert.False(CrossVantageConsensus.IsStrongDisagreement(Set(PoolFar), Set(PoolB)));

        [Fact]
        public void Strong_MixedFamily_IsNotStrong()
            => Assert.False(CrossVantageConsensus.IsStrongDisagreement(Set("2606:50c0:8000::153"), Set(PoolB)));

        [Fact]
        public void SharesNetwork_SameSlash16_True()
            => Assert.True(CrossVantageConsensus.SharesResponsibleNetwork(PoolA, PoolFar));
        [Fact]
        public void SharesNetwork_DifferentSlash16_False()
            => Assert.False(CrossVantageConsensus.SharesResponsibleNetwork(PoolA, Unrelated));

        // MARK: dohCorroboratedPool (UDP never counted; failures never counted)

        [Fact]
        public void DohPool_TwoEndpointsAgree_Included()
        {
            var doh = new Dictionary<string, ResolverQueryOutcome> {
                { "Cloudflare", Out(new[]{ PoolB }) }, { "Google", Out(new[]{ PoolB }) }, { "Quad9", Out(new[]{ PoolA }) }
            };
            var pool = CrossVantageConsensus.DohCorroboratedPool(doh);
            Assert.Contains(PoolB, pool);
            Assert.DoesNotContain(PoolA, pool);
        }

        [Fact]
        public void DohPool_FailedEndpoints_NotCounted()
        {
            var doh = new Dictionary<string, ResolverQueryOutcome> {
                { "Cloudflare", Out(new[]{ PoolB }, DNSResponseStatus.Timeout) },
                { "Google", Out(new[]{ PoolB }, DNSResponseStatus.Timeout) }
            };
            Assert.Empty(CrossVantageConsensus.DohCorroboratedPool(doh));
        }

        // MARK: Mirror Evaluate B281 behaviour

        [Fact]
        public void Evaluate_DisjointSameNetworkAsDoHConsensus_IsInconclusiveNotRedirection()
        {
            var local = Out(new[] { PoolA });
            var publics = new Dictionary<string, ResolverQueryOutcome> {
                { "Cloudflare", Out(new[]{ PoolB }) }, { "Google", Out(new[]{ PoolB }) }, { "Quad9", Out(new[]{ PoolB }) }
            };
            var doh = new Dictionary<string, ResolverQueryOutcome> {
                { "Cloudflare", Out(new[]{ PoolB }) }, { "Google", Out(new[]{ PoolB }) }, { "Quad9", Out(new[]{ PoolB }) }
            };
            var r = VantageComparer.Evaluate(local, publics, doh);
            Assert.False(r.Redirection);
            Assert.True(r.Inconclusive);
        }

        [Fact]
        public void Evaluate_DisjointDifferentNetworkFromDoHConsensus_IsRedirection()
        {
            var local = Out(new[] { Unrelated });
            var publics = new Dictionary<string, ResolverQueryOutcome> {
                { "Cloudflare", Out(new[]{ PoolB }) }, { "Google", Out(new[]{ PoolB }) }, { "Quad9", Out(new[]{ PoolB }) }
            };
            var doh = new Dictionary<string, ResolverQueryOutcome> {
                { "Cloudflare", Out(new[]{ PoolB }) }, { "Google", Out(new[]{ PoolB }) }, { "Quad9", Out(new[]{ PoolB }) }
            };
            var r = VantageComparer.Evaluate(local, publics, doh);
            Assert.True(r.Redirection);
            Assert.False(r.Inconclusive);
        }
    }
}
