using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// Stream A: offline coverage for the unified <see cref="ResolverConsensusEngine"/> — the single
    /// canonical roster, the symmetric peer-consensus derivation, and the shared last-run store. The
    /// live fan-out needs a network and is exercised end-to-end elsewhere; these tests pin the pure
    /// wiring that makes Prophet and Mirror share ONE roster + ONE store. Twin of the macOS
    /// ResolverConsensusEngineTests.
    /// </summary>
    public class ResolverConsensusEngineTests
    {
        private static ProphetResult.ResolverResponse R(string name, string ip) => new(name, ip);

        // -- One canonical roster: the façades are projections, never separate lists ---------------

        [Fact]
        public void ProphetRosterIsProjectionOfEngineRoster()
        {
            // The 11 public oracles Prophet renders are the engine roster, display-named "Name (ip)".
            Assert.Equal(ResolverConsensusEngine.PublicResolvers.Count, ProphetOracleRoster.PublicOracles.Count);
            foreach (var engine in ResolverConsensusEngine.PublicResolvers)
            {
                var expected = engine.Name + " (" + engine.Ip + ")";
                Assert.Contains(ProphetOracleRoster.PublicOracles, o => o.Name == expected && o.Ip == engine.Ip);
            }
        }

        [Fact]
        public void MirrorResolversAreNamedSliceOfEngineRoster()
        {
            // Mirror's primary slice must resolve to the same IPs the engine holds — no hand-maintained
            // copy. The witness wire is pinned to exactly these three keys.
            Assert.Equal(new[] { "Cloudflare", "Google", "Quad9" }, VantageComparer.PublicResolvers.Keys.OrderBy(k => k).ToArray());
            foreach (var kvp in VantageComparer.PublicResolvers)
            {
                var engineIp = ResolverConsensusEngine.PublicResolvers.First(r => r.Name == kvp.Key).Ip;
                Assert.Equal(engineIp, kvp.Value);
            }
            // DoH slice is the same three keys, projected from the engine's canonical DoH map.
            Assert.Equal(new[] { "Cloudflare", "Google", "Quad9" }, VantageComparer.DoHEndpoints.Keys.OrderBy(k => k).ToArray());
            foreach (var kvp in VantageComparer.DoHEndpoints)
                Assert.Equal(ResolverConsensusEngine.DoHEndpoints[kvp.Key], kvp.Value);
        }

        [Fact]
        public void RosterIsLocalFirstAndTwelveEntries()
        {
            var roster = ResolverConsensusEngine.Roster("192.168.1.1");
            Assert.Equal(ResolverConsensusEngine.PublicResolvers.Count + 1, roster.Count);
            Assert.Equal("Local (192.168.1.1)", roster[0].Name);
            Assert.Equal("192.168.1.1", roster[0].Ip);
        }

        // -- Symmetric peer-consensus: agree / disagree / insufficient / inconclusive --------------

        [Fact]
        public void ClassifyPeerAllSameIpIsAgree()
        {
            var responses = new List<ProphetResult.ResolverResponse>
            {
                R("Local (192.168.1.1)", "93.184.216.34"),
                R("Cloudflare (1.1.1.1)", "93.184.216.34"),
                R("Google (8.8.8.8)", "93.184.216.34"),
            };
            Assert.Equal(ResolverConsensusEngine.PeerConsensusState.Agree, ResolverConsensusEngine.ClassifyPeer(responses));
        }

        [Fact]
        public void ClassifyPeerDifferentIpsIsDisagree()
        {
            var responses = new List<ProphetResult.ResolverResponse>
            {
                R("Local (192.168.1.1)", "93.184.216.34"),
                R("Cloudflare (1.1.1.1)", "10.0.0.5"),
            };
            Assert.Equal(ResolverConsensusEngine.PeerConsensusState.Disagree, ResolverConsensusEngine.ClassifyPeer(responses));
        }

        [Fact]
        public void ClassifyPeerSingleReachableIsInsufficient()
        {
            var responses = new List<ProphetResult.ResolverResponse>
            {
                R("Local (192.168.1.1)", "93.184.216.34"),
                R("Cloudflare (1.1.1.1)", "[unreachable]"),
            };
            Assert.Equal(ResolverConsensusEngine.PeerConsensusState.Insufficient, ResolverConsensusEngine.ClassifyPeer(responses));
        }

        [Fact]
        public void ClassifyPeerNoneReachableIsInconclusiveNeverAgree()
        {
            // HONESTY: an all-unreachable run is inconclusive, never silently promoted to agreement.
            var responses = new List<ProphetResult.ResolverResponse>
            {
                R("Local (192.168.1.1)", "[unreachable]"),
                R("Cloudflare (1.1.1.1)", "[unreachable]"),
            };
            Assert.Equal(ResolverConsensusEngine.PeerConsensusState.Inconclusive, ResolverConsensusEngine.ClassifyPeer(responses));
        }

        // -- Shared last-run store -----------------------------------------------------------------

        [Fact]
        public void StoreRecordsAndReadsBackKeyedByDomainCaseInsensitively()
        {
            ResolverConsensusStore.Clear();
            Assert.Null(ResolverConsensusStore.LastRun("example.com"));

            var snap = new ResolverConsensusSnapshot(
                "example.com",
                System.DateTimeOffset.UtcNow,
                ResolverConsensusEngine.PeerConsensusState.Agree,
                ReachableCount: 12,
                LocalDelta: null);
            ResolverConsensusStore.Record(snap);

            // DNS names are case-insensitive; the store must read back either casing.
            Assert.Equal(snap, ResolverConsensusStore.LastRun("EXAMPLE.com"));
            Assert.Equal(ResolverConsensusEngine.PeerConsensusState.Agree, ResolverConsensusStore.LastRun("example.com")!.PeerConsensus);
        }
    }
}
