using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    /// <summary>
    /// D100 — regression coverage for the TTL/type "representative record" tie-break that used to
    /// depend on Dictionary/LINQ enumeration order. When multiple public resolvers agree on the
    /// same rdata but report different TTLs (the ordinary case on live DNS — TTLs count down
    /// independently per resolver cache), <see cref="SignedEvidencePayload.Build"/> must always
    /// pick the SAME representative TTL and type for a given input set, regardless of what order
    /// the candidates are encountered in. Before the fix this depended on
    /// <c>comparison.PublicResolverOutcomes.Values</c> enumeration order (.NET does not
    /// contractually guarantee Dictionary iteration order), so re-signing/re-verifying the
    /// identical input could pick a different TTL and make an honest, unmodified snapshot flip
    /// between VALID and DISPLAY MISMATCH. Twin of the Swift
    /// <c>D100TtlTieBreakDeterminismTests</c>.
    /// </summary>
    public class D100TtlTieBreakDeterminismTests
    {
        private static ResolverQueryOutcome Outcome(params DNSRecord[] records) => new()
        {
            Status = DNSResponseStatus.Success,
            Records = records.ToList()
        };

        /// <summary>
        /// Directly reproduces the defect shape: several DNSRecords sharing one rdata but
        /// different TTLs, arriving in the SAME resolver's record list but in different array
        /// orders — the cleanest reproducible probe for the underlying "pick a representative"
        /// logic, since it does not depend on Dictionary iteration order at all.
        /// </summary>
        [Fact]
        public void RepresentativeTtlIsOrderIndependent_PicksMinimum()
        {
            const string rdata = "93.184.216.34";
            var high = new DNSRecord { Name = "example.com", Type = 1, Ttl = 300, Rdata = rdata };
            var mid = new DNSRecord { Name = "example.com", Type = 1, Ttl = 120, Rdata = rdata };
            var low = new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = rdata };

            var orderings = new List<DNSRecord[]>
            {
                new[] { high, mid, low }, new[] { low, mid, high }, new[] { mid, low, high },
                new[] { high, low, mid }, new[] { mid, high, low }, new[] { low, high, mid },
            };

            var seenTtls = new HashSet<uint>();
            foreach (var ordering in orderings)
            {
                var comparison = new ComparisonResult
                {
                    LocalServer = "1.1.1.1",
                    LocalRecords = new List<DNSRecord> { low },
                    LocalOutcome = Outcome(low),
                    PublicResolverOutcomes = new Dictionary<string, ResolverQueryOutcome>
                    {
                        { "Cloudflare", Outcome(ordering) }
                    },
                    Result = VantageComparisonResult.Consistent
                };

                var payload = SignedEvidencePayload.Build(
                    "example.com", comparison, 1_700_000_000_000UL, null);

                Assert.Equal(new List<uint> { 60 }, payload.ComparisonResult.AuthoritativeTtls);
                seenTtls.Add(payload.ComparisonResult.AuthoritativeTtls!.First());
            }

            Assert.Single(seenTtls);
            Assert.Equal(60u, seenTtls.First());
        }

        /// <summary>
        /// Multi-resolver shape: three resolvers agree on the same rdata but each reports a
        /// different TTL. Swaps which resolver key carries which TTL and asserts the BUILT BYTES
        /// are byte-identical across every permutation — the property the verifier relies on when
        /// it rebuilds the payload from the same comparison shape.
        /// </summary>
        [Fact]
        public void RepresentativeTtlIsIdenticalAcrossResolverKeyPermutations()
        {
            const string rdata = "93.184.216.34";
            uint[] ttls = { 300, 60, 120 };
            string[] keys = { "Cloudflare", "Google", "Quad9" };

            var canonicalBytesSeen = new HashSet<string>();
            foreach (var perm in Permutations(ttls))
            {
                var publicOutcomes = new Dictionary<string, ResolverQueryOutcome>();
                foreach (var (key, ttl) in keys.Zip(perm, (k, t) => (k, t)))
                {
                    var record = new DNSRecord { Name = "example.com", Type = 1, Ttl = ttl, Rdata = rdata };
                    publicOutcomes[key] = Outcome(record);
                }

                var localRecord = new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = rdata };
                var comparison = new ComparisonResult
                {
                    LocalServer = "1.1.1.1",
                    LocalRecords = new List<DNSRecord> { localRecord },
                    LocalOutcome = Outcome(localRecord),
                    PublicResolverOutcomes = publicOutcomes,
                    Result = VantageComparisonResult.Consistent
                };

                var payload = SignedEvidencePayload.Build(
                    "example.com", comparison, 1_700_000_000_000UL, null);

                Assert.Equal(new List<uint> { 60 }, payload.ComparisonResult.AuthoritativeTtls);
                canonicalBytesSeen.Add(System.Text.Encoding.UTF8.GetString(payload.CanonicalBytes()));
            }

            Assert.Single(canonicalBytesSeen);
        }

        private static IEnumerable<uint[]> Permutations(uint[] array)
        {
            if (array.Length <= 1)
            {
                yield return array;
                yield break;
            }

            for (int i = 0; i < array.Length; i++)
            {
                var rest = array.Where((_, idx) => idx != i).ToArray();
                foreach (var perm in Permutations(rest))
                    yield return new[] { array[i] }.Concat(perm).ToArray();
            }
        }
    }
}
