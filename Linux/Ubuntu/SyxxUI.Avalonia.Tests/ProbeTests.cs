using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    public class AnswerStabilityProbeTests
    {
        private static HashSet<string> S(params string[] xs) => new HashSet<string>(xs);

        [Fact]
        public void StableWhenIdenticalSetsAndTTLHonored()
        {
            var r = AnswerStabilityProbe.Classify(
                sets: [S("1.2.3.4", "5.6.7.8"), S("1.2.3.4", "5.6.7.8"), S("1.2.3.4", "5.6.7.8")],
                minTTLs: [300, 298, 295], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.Stable, r.Status);
            Assert.Equal(1, r.DistinctAnswerSets);
            Assert.True(r.TtlHonored);
        }

        [Fact]
        public void TTLAnomalyWhenSetStableButTTLRises()
        {
            var r = AnswerStabilityProbe.Classify(
                sets: [S("1.2.3.4"), S("1.2.3.4"), S("1.2.3.4")],
                minTTLs: [300, 300, 400], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.TtlAnomaly, r.Status);
            Assert.False(r.TtlHonored);
        }

        [Fact]
        public void RotatingWhenSetsShareAMember()
        {
            var r = AnswerStabilityProbe.Classify(
                sets: [S("a", "b"), S("a", "c"), S("a", "d")],
                minTTLs: [60, 60, 60], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.Rotating, r.Status);
        }

        [Fact]
        public void UnstableWhenSetsFullyChange()
        {
            var r = AnswerStabilityProbe.Classify(
                sets: [S("a"), S("b"), S("c")],
                minTTLs: [60, 60, 60], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.Unstable, r.Status);
        }

        [Fact]
        public void InconclusiveWithFewerThanTwoUsable()
        {
            var r = AnswerStabilityProbe.Classify(sets: [S("a")], minTTLs: [60], attempted: 3);
            Assert.Equal(AnswerStabilityStatus.Inconclusive, r.Status);
        }
    }

    public class DualStackProbeTests
    {
        [Fact]
        public void DualStackWhenBothResolve()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Success, ["93.184.216.34"],
                                             DNSResponseStatus.Success, ["2606:2800:220:1:248:1893:25c8:1946"]);
            Assert.Equal(DualStackStatus.DualStack, r.Status);
        }

        [Fact]
        public void Ipv4OnlyWhenAAAAIsGenuineNegative()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Success, ["1.2.3.4"],
                                             DNSResponseStatus.NoData, []);
            Assert.Equal(DualStackStatus.Ipv4Only, r.Status);
        }

        [Fact]
        public void Dns64SynthesisWhenAllAAAAInWellKnownPrefix()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Success, ["93.184.216.34"],
                                             DNSResponseStatus.Success, ["64:ff9b::5db8:d822"]);
            Assert.Equal(DualStackStatus.Dns64Synthesis, r.Status);
            Assert.Equal(1, r.Dns64Count);
        }

        [Fact]
        public void Ipv4SuppressedWhenAAAAWorksButAIsGenuineNegative()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.NXDomain, [],
                                             DNSResponseStatus.Success, ["2606:2800:220:1:248:1893:25c8:1946"]);
            Assert.Equal(DualStackStatus.Ipv4Suppressed, r.Status);
        }

        // [B038] An A-record TRANSPORT FAILURE (timeout) with a working AAAA is a missing datum, not a
        // result — inconclusive, NEVER Ipv4Suppressed (a censorship signal on a failure = §1 violation).
        [Fact]
        public void ATimeoutWithWorkingAAAAIsInconclusiveNotSuppressed()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Timeout, [],
                                             DNSResponseStatus.Success, ["2606:2800:220:1:248:1893:25c8:1946"]);
            Assert.Equal(DualStackStatus.Inconclusive, r.Status);
            Assert.NotEqual(DualStackStatus.Ipv4Suppressed, r.Status);
        }

        [Fact]
        public void InconclusiveWhenBothTransportFail()
        {
            var r = DualStackProbe.Classify(DNSResponseStatus.Timeout, [],
                                             DNSResponseStatus.Timeout, []);
            Assert.Equal(DualStackStatus.Inconclusive, r.Status);
        }

        [Fact]
        public void Dns64PrefixDetection()
        {
            Assert.True(DualStackProbe.IsDNS64("64:ff9b::5db8:d822"));
            Assert.True(DualStackProbe.IsDNS64("64:ff9b::1.2.3.4"));
            Assert.False(DualStackProbe.IsDNS64("2606:2800:220:1:248:1893:25c8:1946"));
            Assert.False(DualStackProbe.IsDNS64("64:ff9c::1"));
            Assert.False(DualStackProbe.IsDNS64("not-an-ip"));
            Assert.False(DualStackProbe.IsDNS64("1.2.3.4"));
        }
    }

    public class CNAMEChainProbeTests
    {
        private static List<DNSRecord> Rec(string name, ushort type, string rdata) =>
            [new DNSRecord { Name = name, Type = type, Ttl = 60, Rdata = rdata }];

        [Fact]
        public void ReconstructChainFollowsCNAMEs()
        {
            var recs = new List<DNSRecord>
            {
                new() { Name = "www.example.com", Type = 5, Ttl = 60, Rdata = "edge.cdn.net" },
                new() { Name = "edge.cdn.net", Type = 5, Ttl = 60, Rdata = "pop1.cdn.net" },
                new() { Name = "pop1.cdn.net", Type = 1, Ttl = 60, Rdata = "1.2.3.4" },
            };
            var chain = CNAMEChainProbe.ReconstructChain("www.example.com", recs);
            Assert.Equal(["www.example.com", "edge.cdn.net", "pop1.cdn.net"], chain);
        }

        [Fact]
        public void ReconstructChainStopsOnLoop()
        {
            var recs = new List<DNSRecord>
            {
                new() { Name = "a.com", Type = 5, Ttl = 60, Rdata = "b.com" },
                new() { Name = "b.com", Type = 5, Ttl = 60, Rdata = "a.com" },
            };
            var chain = CNAMEChainProbe.ReconstructChain("a.com", recs);
            Assert.Equal(["a.com", "b.com"], chain);
        }

        [Fact]
        public void ConsistentWhenTestMatchesReference()
        {
            var r = CNAMEChainProbe.Classify(
                testChain: ["w.example.com", "edge.cdn.net"],
                referenceChains: [["w.example.com", "edge.cdn.net"], ["w.example.com", "edge.cdn.net"]]);
            Assert.Equal(CNAMEChainStatus.Consistent, r.Status);
        }

        [Fact]
        public void DivergentWhenReferencesAgreeButTestDiffers()
        {
            var r = CNAMEChainProbe.Classify(
                testChain: ["w.example.com", "evil.cdn.net"],
                referenceChains: [["w.example.com", "edge.cdn.net"], ["w.example.com", "edge.cdn.net"]]);
            Assert.Equal(CNAMEChainStatus.Divergent, r.Status);
        }

        [Fact]
        public void InconclusiveWhenReferencesDisagree()
        {
            var r = CNAMEChainProbe.Classify(
                testChain: ["w.example.com", "us.cdn.net"],
                referenceChains: [["w.example.com", "eu.cdn.net"], ["w.example.com", "ap.cdn.net"]]);
            Assert.Equal(CNAMEChainStatus.Inconclusive, r.Status);
        }

        [Fact]
        public void NoCNAMEWhenDirectA()
        {
            var r = CNAMEChainProbe.Classify(
                testChain: ["example.com"],
                referenceChains: [["example.com"], ["example.com"]]);
            Assert.Equal(CNAMEChainStatus.NoCNAME, r.Status);
        }

        [Fact]
        public void InconclusiveWhenNoReferences()
        {
            Assert.Equal(CNAMEChainStatus.Inconclusive, CNAMEChainProbe.Classify(testChain: ["a"], referenceChains: []).Status);
        }
    }

    public class DenialOfExistenceProbeTests
    {
        private static DNSRecord Soa(string owner) => new() { Name = owner, Type = 6, Ttl = 3600, Rdata = "aabbccdd" };
        private static DNSRecord Nsec(string owner) => new() { Name = owner, Type = 47, Ttl = 3600, Rdata = "00" };
        private static DNSRecord A(string owner) => new() { Name = owner, Type = 1, Ttl = 60, Rdata = "10.0.0.1" };

        [Fact]
        public void SoaBackedNegativeIsBenign()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Soa("example.com")]);
            Assert.Equal(DenialStatus.SoaBacked, r.Status);
            Assert.Equal("example.com", r.SoaOwner);
        }

        [Fact]
        public void NsecProvenNegativeIsBenign()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Soa("example.com"), Nsec("example.com")]);
            Assert.Equal(DenialStatus.NsecProven, r.Status);
            Assert.True(r.HasNSEC);
        }

        [Fact]
        public void BareDenialIsFlagged()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: []);
            Assert.Equal(DenialStatus.BareDenial, r.Status);
        }

        [Fact]
        public void ContradictoryWhenNegativeCarriesAnswers()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [A("nope.example.com")], authority: [Soa("example.com")]);
            Assert.Equal(DenialStatus.Contradictory, r.Status);
        }

        [Fact]
        public void ContradictoryWhenSoaIsWrongZone()
        {
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Soa("blockpage.isp.net")]);
            Assert.Equal(DenialStatus.Contradictory, r.Status);
        }

        [Fact]
        public void ResolvedNameIsNotADenial()
        {
            var r = DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.Success,
                answers: [A("example.com")], authority: []);
            Assert.Equal(DenialStatus.NotADenial, r.Status);
        }

        [Fact]
        public void TransportFailureIsInconclusive()
        {
            var r = DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.Timeout, answers: [], authority: []);
            Assert.Equal(DenialStatus.Inconclusive, r.Status);
        }

        [Fact]
        public void NoDataWithSoaIsBenign()
        {
            var r = DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.NoData,
                answers: [], authority: [Soa("example.com")]);
            Assert.Equal(DenialStatus.SoaBacked, r.Status);
        }

        [Fact]
        public void AncestorMatching()
        {
            Assert.True(DenialOfExistenceProbe.IsAncestor("example.com", "a.b.example.com"));
            Assert.True(DenialOfExistenceProbe.IsAncestor("example.com.", "example.com"));
            Assert.False(DenialOfExistenceProbe.IsAncestor("example.com", "notexample.com"));
            Assert.False(DenialOfExistenceProbe.IsAncestor("evil.net", "example.com"));
        }

        // -----------------------------------------------------------------------------------------
        // D1.4 / C126 — an injected NSEC must not bypass the SOA-ancestry test.
        // Nothing here validates the NSEC interval or its RRSIG (an honest documented v1 limit), so
        // NSEC presence is cheap for an on-path injector to manufacture. It may upgrade a CONSISTENT
        // denial; it must never rescue an inconsistent one. Twin of DenialOfExistenceProbe.swift:105-116.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void InjectedNsecDoesNotRescueAWrongZoneSoa()
        {
            // The block-page shape: an ISP answers NXDOMAIN with an SOA for its OWN zone and staples
            // on an NSEC. Before C126 the hasNSEC branch returned NsecProven — "authenticated denial
            // of existence" — and the wrong-zone SOA, the only real test, was never reached.
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Soa("blockpage.isp.net"), Nsec("blockpage.isp.net")]);

            Assert.Equal(DenialStatus.Contradictory, r.Status);
            Assert.NotEqual(DenialStatus.NsecProven, r.Status);
            Assert.True(r.HasNSEC, "the NSEC observation itself is still reported honestly");
        }

        [Fact]
        public void NsecStillUpgradesAConsistentDenial()
        {
            // Guard on the fix: an ancestor-zone SOA with NSEC is a genuine authenticated denial and
            // must still grade NsecProven. C126 reorders the checks; it does not disable NSEC.
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Soa("example.com"), Nsec("example.com")]);
            Assert.Equal(DenialStatus.NsecProven, r.Status);
        }

        [Fact]
        public void NsecWithNoSoaAtAllIsStillNsecProven()
        {
            // No SOA means no ancestry test to fail, so the NSEC branch is reached as before.
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: [Nsec("example.com")]);
            Assert.Equal(DenialStatus.NsecProven, r.Status);
        }

        // -----------------------------------------------------------------------------------------
        // D1.5 / C127 — NODATA is not NXDOMAIN, and a missing SOA means different things for each.
        // RFC 2308 §5 makes the SOA a SHOULD for authoritative answers only; resolvers routinely omit
        // it on a cached NODATA. Twin of DenialOfExistenceProbe.swift:126-133.
        // -----------------------------------------------------------------------------------------

        [Fact]
        public void BareNoDataIsInconclusiveNotASuspiciousBareDenial()
        {
            // Routine traffic: the name exists, has no record of the requested type, and the cached
            // answer carries no SOA. This was graded BareDenial — "likely injected", corroborate —
            // which is a fabricated suspicion on an ordinary answer.
            var r = DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.NoData,
                answers: [], authority: []);

            Assert.Equal(DenialStatus.Inconclusive, r.Status);
            Assert.NotEqual(DenialStatus.BareDenial, r.Status);
            Assert.Equal(DenialNegativeKind.TypeHasNoData, r.NegativeKind);
        }

        [Fact]
        public void BareNxdomainIsStillABareDenial()
        {
            // The other half of C127, and proof the change above is not a blanket downgrade: a
            // genuine authoritative NXDOMAIN does include an SOA, so its absence stays a signature.
            var r = DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                answers: [], authority: []);

            Assert.Equal(DenialStatus.BareDenial, r.Status);
            Assert.Equal(DenialNegativeKind.NameDoesNotExist, r.NegativeKind);
        }

        [Fact]
        public void NegativeKindIsRecordedForEveryGradedStatus()
        {
            // NXDOMAIN and NODATA are different facts (RFC 2308 §2.1/§2.2) and both must survive to
            // the caller; a transport failure has no kind at all and must stay null rather than
            // defaulting to either one.
            Assert.Equal(DenialNegativeKind.NameDoesNotExist,
                DenialOfExistenceProbe.Classify("nope.example.com", DNSResponseStatus.NXDomain,
                    answers: [], authority: [Soa("example.com")]).NegativeKind);
            Assert.Equal(DenialNegativeKind.TypeHasNoData,
                DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.NoData,
                    answers: [], authority: [Soa("example.com")]).NegativeKind);
            Assert.Null(DenialOfExistenceProbe.Classify("example.com", DNSResponseStatus.Timeout,
                    answers: [], authority: []).NegativeKind);
        }
    }

    public class AuthoritativeVantageClassifyTests
    {
        [Fact]
        public void ClassifyMatchesWhenResolverSubsetOfAuth()
        {
            Assert.Equal(AuthComparisonStatus.Matches,
                AuthoritativeVantage.Classify(["1.1.1.1"], ["1.1.1.1", "2.2.2.2"], [], "x").Status);
        }

        [Fact]
        public void ClassifyDivergesWhenDisjoint()
        {
            Assert.Equal(AuthComparisonStatus.Diverges,
                AuthoritativeVantage.Classify(["1.1.1.1"], ["2.2.2.2"], [], "x").Status);
        }

        [Fact]
        public void ClassifyInconclusiveOnPartialOverlap()
        {
            Assert.Equal(AuthComparisonStatus.Inconclusive,
                AuthoritativeVantage.Classify(["1.1.1.1", "2.2.2.2"], ["1.1.1.1", "3.3.3.3"], [], "x").Status);
        }

        [Fact]
        public void ClassifyMatchesWhenBothEmpty()
        {
            Assert.Equal(AuthComparisonStatus.Matches,
                AuthoritativeVantage.Classify([], [], [], "x").Status);
        }
    }

    public class TTLAnomalyProbeTests
    {
        [Fact]
        public void IndependentCachingWhenTTLsVary()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 287, ["8.8.8.8"] = 254, ["9.9.9.9"] = 271 });
            Assert.Equal(TTLAnomalyStatus.IndependentCaching, r.Status);
        }

        [Fact]
        public void LockstepWhenThreeResolversAgreeExactly()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 300, ["8.8.8.8"] = 300, ["9.9.9.9"] = 300 });
            Assert.Equal(TTLAnomalyStatus.LockstepTTL, r.Status);
        }

        [Fact]
        public void TwoIdenticalIsNotLockstep()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 300, ["8.8.8.8"] = 300 });
            Assert.Equal(TTLAnomalyStatus.IndependentCaching, r.Status);
        }

        [Fact]
        public void ExtremeTTLFlagged()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 300, ["8.8.8.8"] = 700000 });
            Assert.Equal(TTLAnomalyStatus.ExtremeTTL, r.Status);
        }

        [Fact]
        public void InconclusiveWithOneAnswer()
        {
            Assert.Equal(TTLAnomalyStatus.Inconclusive, TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 300 }).Status);
            Assert.Equal(TTLAnomalyStatus.Inconclusive, TTLAnomalyProbe.Classify(new Dictionary<string, uint>()).Status);
        }

        [Fact]
        public void ZeroTTLsAreNotCountedAsLockstep()
        {
            var r = TTLAnomalyProbe.Classify(new Dictionary<string, uint> { ["1.1.1.1"] = 0, ["8.8.8.8"] = 0, ["9.9.9.9"] = 0 });
            Assert.Equal(TTLAnomalyStatus.IndependentCaching, r.Status);
        }
    }

    public class SVCBProbeTests
    {
        [Fact]
        public void ClassifyPresent()
        {
            Assert.Equal(SVCBFindingStatus.Present, SVCBProbe.Classify(present: ["a", "b"], absent: [], failed: []).Status);
        }

        [Fact]
        public void ClassifyAbsent()
        {
            Assert.Equal(SVCBFindingStatus.Absent, SVCBProbe.Classify(present: [], absent: ["a", "b"], failed: []).Status);
        }

        [Fact]
        public void ClassifyDivergent()
        {
            Assert.Equal(SVCBFindingStatus.Divergent, SVCBProbe.Classify(present: ["a"], absent: ["b"], failed: []).Status);
        }

        [Fact]
        public void ClassifyInconclusiveWhenAllFail()
        {
            Assert.Equal(SVCBFindingStatus.Inconclusive, SVCBProbe.Classify(present: [], absent: [], failed: ["a"]).Status);
        }
    }

    public class VersionBindProbeTests
    {
        private static string TxtHex(string[] strings)
        {
            var bytes = new List<byte>();
            foreach (var s in strings)
            {
                bytes.Add((byte)s.Length);
                bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(s));
            }
            return Convert.ToHexString(bytes.ToArray()).ToLower();
        }

        private static DNSResponse TxtResponse(string[] strings) => new()
        {
            Status = DNSResponseStatus.Success,
            Records = [new DNSRecord { Name = "version.bind", Type = 16, Ttl = 0, Rdata = TxtHex(strings) }]
        };

        [Fact]
        public void ForwarderBannerOnPublicResolverIsInterceptionHint()
        {
            var r = VersionBindProbe.Classify(TxtResponse(["dnsmasq-2.80"]), "8.8.8.8", expectedPublicResolver: true);
            Assert.Equal(VersionBindResultStatus.Disclosed, r.Status);
            Assert.Equal("dnsmasq-2.80", r.Banner);
            Assert.True(r.ForwarderOnPublicPath);
        }

        [Fact]
        public void ForwarderBannerOnLocalRouterIsNotEscalated()
        {
            var r = VersionBindProbe.Classify(TxtResponse(["dnsmasq-2.80"]), "192.168.1.1", expectedPublicResolver: false);
            Assert.Equal(VersionBindResultStatus.Disclosed, r.Status);
            Assert.False(r.ForwarderOnPublicPath);
        }

        [Fact]
        public void RecursiveBannerIsDisclosedButNotAForwarderHint()
        {
            var r = VersionBindProbe.Classify(TxtResponse(["unbound 1.13.1"]), "8.8.8.8", expectedPublicResolver: true);
            Assert.Equal(VersionBindResultStatus.Disclosed, r.Status);
            Assert.False(r.ForwarderOnPublicPath);
        }

        [Fact]
        public void WithheldWhenNoTXTAnswer()
        {
            var r = VersionBindProbe.Classify(new DNSResponse { Status = DNSResponseStatus.NoData }, "1.1.1.1", expectedPublicResolver: true);
            Assert.Equal(VersionBindResultStatus.Withheld, r.Status);
        }

        [Fact]
        public void UntestedWhenProbeFails()
        {
            var r = VersionBindProbe.Classify(new DNSResponse { Status = DNSResponseStatus.Timeout }, "1.1.1.1", expectedPublicResolver: true);
            Assert.Equal(VersionBindResultStatus.Untested, r.Status);
        }

        [Fact]
        public void KnownPublicResolverMembership()
        {
            Assert.True(VersionBindProbe.IsKnownPublicResolver("1.1.1.1"));
            Assert.False(VersionBindProbe.IsKnownPublicResolver("192.168.1.1"));
        }
    }

    // B051 parity twin of macOS EncryptedTransportProbeTests. A failed DoH path is Blocked ONLY when a
    // cleartext Do53 control proves the network is up; otherwise (offline / provider down) it is
    // Inconclusive, never a manufactured "blocked". Mocks keep every case deterministic and offline.
    public class EncryptedTransportProbeTests
    {
        private static Func<string, ushort, Task<DNSResponse>> ControlUp() => (name, _) =>
            Task.FromResult(new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                Records = new List<DNSRecord> { new() { Name = name, Type = 1, Rdata = "1.1.1.1" } }
            });
        private static Func<string, ushort, Task<DNSResponse>> ControlDown() => (_, _) =>
            Task.FromResult(new DNSResponse { Status = DNSResponseStatus.Timeout });

        [Fact]
        public void ClassifyWorking()
        {
            Assert.Equal(EncryptedTransportStatus.Working,
                EncryptedTransportProbe.Classify(DNSResponseStatus.Success, 2, controlSucceeded: false, "x").Status);
        }

        // B051: timeout + a working cleartext control → genuinely Blocked.
        [Fact]
        public void ClassifyBlockedWhenControlWorks()
        {
            Assert.Equal(EncryptedTransportStatus.Blocked,
                EncryptedTransportProbe.Classify(DNSResponseStatus.Timeout, 0, controlSucceeded: true, "x").Status);
        }

        // B051 mutation-proven core: timeout + failed control → OFFLINE, so Inconclusive, not Blocked.
        [Fact]
        public void ClassifyInconclusiveWhenControlAlsoDown()
        {
            Assert.Equal(EncryptedTransportStatus.Inconclusive,
                EncryptedTransportProbe.Classify(DNSResponseStatus.Timeout, 0, controlSucceeded: false, "x").Status);
            Assert.Equal(EncryptedTransportStatus.Inconclusive,
                EncryptedTransportProbe.Classify(DNSResponseStatus.TransportError, 0, controlSucceeded: false, "x").Status);
        }

        [Fact]
        public void ClassifyInconclusive()
        {
            Assert.Equal(EncryptedTransportStatus.Inconclusive,
                EncryptedTransportProbe.Classify(DNSResponseStatus.ServFail, 0, controlSucceeded: true, "x").Status);
        }

        [Fact]
        public async Task ProbeBlockedWhenDoHFailsButControlWorks()
        {
            Func<string, ushort, string, Task<DNSResponse>> doh = (_, _, _) =>
                Task.FromResult(new DNSResponse { Status = DNSResponseStatus.Timeout });
            var r = await EncryptedTransportProbe.Probe(query: doh, control: ControlUp());
            Assert.Equal(EncryptedTransportStatus.Blocked, r.Status);
        }

        [Fact]
        public async Task ProbeInconclusiveWhenDoHFailsAndControlAlsoDown()
        {
            Func<string, ushort, string, Task<DNSResponse>> doh = (_, _, _) =>
                Task.FromResult(new DNSResponse { Status = DNSResponseStatus.Timeout });
            var r = await EncryptedTransportProbe.Probe(query: doh, control: ControlDown());
            Assert.Equal(EncryptedTransportStatus.Inconclusive, r.Status);
        }
    }

    public class OpenResolverProbeTests
    {
        [Fact]
        public void AmplificationFactor()
        {
            var big = new DNSResponse { Status = DNSResponseStatus.Success, ResponseByteLength = 1200 };
            Assert.Equal(30.0, OpenResolverProbe.AmplificationFactor(40, big), precision: 3);
            Assert.Equal(0, OpenResolverProbe.AmplificationFactor(null, big));
            Assert.Equal(0, OpenResolverProbe.AmplificationFactor(40, new DNSResponse { Status = DNSResponseStatus.Success }));
        }

        [Fact]
        public void ClassifyThresholds()
        {
            Assert.Equal(OpenResolverStatus.RecursiveHighAmp, OpenResolverProbe.Classify("t", 25).Status);
            Assert.Equal(OpenResolverStatus.RecursiveLowAmp, OpenResolverProbe.Classify("t", 3).Status);
        }
    }

    public class ResolverLatencyProbeTests
    {
        [Fact]
        public void FastWhenMedianLow()
        {
            var r = ResolverLatencyProbe.Classify([8, 12, 10, 9], attempted: 4);
            Assert.Equal(ResolverLatencyStatus.Fast, r.Status);
            Assert.Equal(4, r.SuccessCount);
        }

        [Fact]
        public void ModerateBand()
        {
            var r = ResolverLatencyProbe.Classify([90, 120, 100], attempted: 3);
            Assert.Equal(ResolverLatencyStatus.Moderate, r.Status);
        }

        [Fact]
        public void SlowWhenMedianHigh()
        {
            var r = ResolverLatencyProbe.Classify([200, 210, 190], attempted: 3);
            Assert.Equal(ResolverLatencyStatus.Slow, r.Status);
        }

        [Fact]
        public void DegradedWhenSomeQueriesLost()
        {
            var r = ResolverLatencyProbe.Classify([10, 12], attempted: 4);
            Assert.Equal(ResolverLatencyStatus.Degraded, r.Status);
            Assert.Equal(2, r.SuccessCount);
        }

        [Fact]
        public void UnreachableWhenNoResponses()
        {
            var r = ResolverLatencyProbe.Classify([], attempted: 4);
            Assert.Equal(ResolverLatencyStatus.Unreachable, r.Status);
            Assert.Equal(0, r.SuccessCount);
        }
    }

    public class StubEntropyProbeTests
    {
        private static List<(ushort xid, ushort sourcePort)> Queries(int count, Func<int, ushort> xid, Func<int, ushort> port)
        {
            var q = new List<(ushort, ushort)>();
            for (int i = 0; i < count; i++) q.Add((xid(i), port(i)));
            return q;
        }

        [Fact]
        public void WellRandomized()
        {
            var q = Queries(12, i => (ushort)(i * 5000 + 123), i => (ushort)(40000 + i * 137));
            Assert.Equal(StubEntropyStatus.WellRandomized, StubEntropyProbe.Audit(q).Status);
        }

        [Fact]
        public void FixedSourcePortIsWeak()
        {
            var q = Queries(12, i => (ushort)(i * 4013 + 7), _ => (ushort)53535);
            var a = StubEntropyProbe.Audit(q);
            Assert.Equal(StubEntropyStatus.WeakRandomization, a.Status);
        }

        [Fact]
        public void SequentialTransactionIDsAreWeak()
        {
            var q = Queries(12, i => (ushort)(1000 + i), i => (ushort)(40000 + i * 200));
            Assert.Equal(StubEntropyStatus.WeakRandomization, StubEntropyProbe.Audit(q).Status);
        }

        [Fact]
        public void LowTransactionIDEntropyIsWeak()
        {
            var q = Queries(12, i => (ushort)(i % 2 == 0 ? 4242 : 4243), i => (ushort)(40000 + i * 211));
            Assert.Equal(StubEntropyStatus.WeakRandomization, StubEntropyProbe.Audit(q).Status);
        }

        [Fact]
        public void InsufficientDataIsNeverClean()
        {
            var q = Queries(4, i => (ushort)(i * 5000 + 1), i => (ushort)(40000 + i * 300));
            Assert.Equal(StubEntropyStatus.InsufficientData, StubEntropyProbe.Audit(q).Status);
        }
    }

    public class WildcardSynthesisProbeTests
    {
        private static WildcardSynthesisProbe.LabelObservation Obs(string label = "x.example.com",
            string[]? test = null, bool testHas = false, bool refHas = false, bool refDenies = false) =>
            new(label, test ?? [], testHas, refHas, refDenies);

        [Fact]
        public void NxHijackWhenReferenceDeniesButTestInvents()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: ["10.0.0.1"], testHas: true, refHas: false, refDenies: true),
            ]);
            Assert.Equal(WildcardSynthesisStatus.NxHijack, r.Status);
            Assert.Equal(["10.0.0.1"], r.SyntheticAddresses);
        }

        [Fact]
        public void WildcardZoneWhenBothResolve()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),
                Obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),
            ]);
            Assert.Equal(WildcardSynthesisStatus.WildcardZone, r.Status);
        }

        [Fact]
        public void NoSynthesisWhenBothDeny()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: [], testHas: false, refHas: false, refDenies: true),
                Obs(test: [], testHas: false, refHas: false, refDenies: true),
            ]);
            Assert.Equal(WildcardSynthesisStatus.NoSynthesis, r.Status);
        }

        [Fact]
        public void InconclusiveWhenNoUsableLabels()
        {
            Assert.Equal(WildcardSynthesisStatus.Inconclusive, WildcardSynthesisProbe.Classify([]).Status);
        }

        [Fact]
        public void InconclusiveOnMixedOutcomes()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: [], testHas: false, refHas: false, refDenies: true),
                Obs(test: ["203.0.113.5"], testHas: true, refHas: true, refDenies: false),
            ]);
            Assert.Equal(WildcardSynthesisStatus.Inconclusive, r.Status);
        }

        [Fact]
        public void AnySingleHijackWins()
        {
            var r = WildcardSynthesisProbe.Classify([
                Obs(test: [], testHas: false, refHas: false, refDenies: true),
                Obs(test: ["10.0.0.1"], testHas: true, refHas: false, refDenies: true),
            ]);
            Assert.Equal(WildcardSynthesisStatus.NxHijack, r.Status);
        }
    }

    public class ResponseSizeProbeTests
    {
        [Fact]
        public void NormalSizes()
        {
            var sizes = new List<int>(Enumerable.Repeat(120, 10)) { 300, 450 };
            Assert.Equal(ResponseSizeStatus.Normal, ResponseSizeProbe.Audit(sizes).Status);
        }

        [Fact]
        public void SustainedLargeIsOversized()
        {
            var sizes = new List<int> { 100, 120, 150, 200, 180, 90, 2100, 3000, 2500, 4096 };
            var a = ResponseSizeProbe.Audit(sizes);
            Assert.Equal(ResponseSizeStatus.Oversized, a.Status);
            Assert.Equal(4, a.OversizedCount);
            Assert.Equal(4096, a.MaxBytes);
        }

        [Fact]
        public void LoneLargeAnswerIsNotFlagged()
        {
            var sizes = new List<int>(Enumerable.Repeat(150, 11)) { 3000 };
            Assert.Equal(ResponseSizeStatus.Normal, ResponseSizeProbe.Audit(sizes).Status);
        }

        [Fact]
        public void InsufficientDataIsNeverClean()
        {
            Assert.Equal(ResponseSizeStatus.InsufficientData, ResponseSizeProbe.Audit([100, 2500, 3000]).Status);
        }
    }

    public class ProbeRunnerTests
    {
        // ── Helpers ──

        private static ProbeBudget MakeBudget()
        {
            return new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 10);
        }

        // ── Exhaustion gates ──

        [Fact]
        public async Task Run_ExhaustedBudget_ReturnsNil()
        {
            var budget = new ProbeBudget(TimeSpan.Zero, TimeSpan.FromSeconds(5), 10);
            Assert.True(budget.IsExhausted);  // elapsed >= totalTimeout

            var operationCalled = false;
            var result = await ProbeRunner.RunAsync("test", budget, () =>
            {
                operationCalled = true;
                return Task.FromResult<string?>("should-not-run");
            });
            Assert.Null(result);
            Assert.False(operationCalled);
        }

        [Fact]
        public async Task Run_MaxProbesExceeded_ReturnsNil()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 1);

            // Run one to exhaust maxProbes
            var r1 = await ProbeRunner.RunAsync("test", budget, () => Task.FromResult<string?>("first"));
            Assert.Equal("first", r1);
            Assert.True(budget.IsExhausted);  // ProbesRun(1) >= MaxProbes(1)

            var operationCalled = false;
            var r2 = await ProbeRunner.RunAsync("test", budget, () =>
            {
                operationCalled = true;
                return Task.FromResult<string?>("second");
            });
            Assert.Null(r2);
            Assert.False(operationCalled);
        }

        // ── Success path ──

        [Fact]
        public async Task Run_Success_ReturnsValue()
        {
            var budget = MakeBudget();
            var result = await ProbeRunner.RunAsync("test", budget, () => Task.FromResult<string?>("hello"));
            Assert.Equal("hello", result);
        }

        [Fact]
        public async Task Run_Success_RecordsTiming()
        {
            var budget = MakeBudget();
            Assert.Equal(0, budget.ProbesRun);
            Assert.Equal(TimeSpan.Zero, budget.Elapsed);

            _ = await ProbeRunner.RunAsync("test", budget, () => Task.FromResult<string?>("value"));

            Assert.Equal(1, budget.ProbesRun);
            Assert.True(budget.Elapsed > TimeSpan.Zero);
        }

        // ── Timeout path ──

        [Fact]
        public async Task Run_Timeout_ReturnsNil()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10), 10);

            var result = await ProbeRunner.RunAsync("test", budget, async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                return "too-late";
            });
            Assert.Null(result);
        }

        [Fact]
        public async Task Run_Timeout_AccountsCorrectly()
        {
            // F006: elapsed records real wall-clock time, not the PerProbeTimeout cap.
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10), 10);

            _ = await ProbeRunner.RunAsync("test", budget, async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10));
                return "too-late";
            });

            // elapsed >= PerProbeTimeout (timeout fired at ~10ms); well under the probe's full sleep.
            // C058: the bound is now 10ms + the 1.5s grace drain, because the deadline no longer
            // ends the probe — the runner waits out the grace window for a late answer before
            // recording an absence. The assertion still catches what it was written to catch: that
            // Elapsed is real wall-clock and never the operation's full 10s sleep.
            Assert.Equal(1, budget.ProbesRun);
            Assert.True(budget.Elapsed >= TimeSpan.FromMilliseconds(9));
            Assert.True(budget.Elapsed <= TimeSpan.FromSeconds(3),
                $"Elapsed must not be the full probe sleep duration — was {budget.Elapsed.TotalSeconds:F2}s");
        }

        [Fact]
        public async Task Run_Timeout_RealElapsedNotCapped()
        {
            // Mutation probe: if elapsed were still capped at PerProbeTimeout, TWO timed-out probes
            // would each record exactly PerProbeTimeout — sum would always equal 2×PerProbeTimeout.
            // With real elapsed, the sum must be >= 2×PerProbeTimeout.
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(10), 10);

            for (int i = 0; i < 2; i++)
            {
                _ = await ProbeRunner.RunAsync("test", budget, async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(10));
                    return "too-late";
                });
            }

            // Two timed-out probes: elapsed must be at least 2×PerProbeTimeout.
            Assert.True(budget.Elapsed >= TimeSpan.FromMilliseconds(18),
                "Two timed-out probes must accumulate real elapsed time");
            Assert.Equal(2, budget.ProbesRun);
        }

        // ── C058 / K-09(a): the grace drain ──
        //
        // The longer per-probe deadline (K-15) is only half the fix. Without a recovery window an
        // answer that arrives just after the deadline is still discarded and reported as an
        // absence — the timing changes but the evidence loss does not. Twin: the `gracePeriod`
        // branch in macOS ProbeRunner.swift.

        /// An answer arriving AFTER the deadline but INSIDE the grace window is returned, not
        /// discarded. A real (late) answer is preferred over a fabricated absence.
        /// Failure case: delete the grace drain → result is null → red.
        [Fact]
        public async Task Run_AnswerArrivingInsideGraceWindow_IsReturnedNotDiscarded()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(50), 10);

            var result = await ProbeRunner.RunAsync("late", budget, async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(400));   // past the 50ms deadline, inside the 1.5s grace
                return "arrived-late";
            });

            Assert.Equal("arrived-late", result);
        }

        /// The same recovery on the CancellationToken overload. This is the arm that matters most
        /// in production: a COOPERATIVE probe is torn down by its own token, so if cancellation
        /// still fired at the deadline the drain would have nothing left to recover and the fix
        /// would be inert exactly where it is needed.
        /// Failure case: arm the CTS at PerProbeTimeout instead of PerProbeTimeout + grace → the
        /// operation is cancelled at 50ms, returns null, and the drain recovers nothing → red.
        [Fact]
        public async Task Run_CooperativeProbe_IsNotCancelledDuringTheGraceWindow()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(50), 10);

            var result = await ProbeRunner.RunAsync<string>("coop-late", budget, async ct =>
            {
                try { await Task.Delay(TimeSpan.FromMilliseconds(400), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return null; }   // cooperative: returns, not throws
                return "arrived-late";
            });

            Assert.Equal("arrived-late", result);
        }

        /// The grace window is bounded. A probe that never answers must still be cut off — at
        /// deadline + grace, not at the deadline, and never at the operation's own duration.
        /// Failure case: drop the grace timer and await the operation → ~5s → red.
        [Fact]
        public async Task Run_AnswerArrivingAfterTheGraceWindow_IsStillAnAbsence()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromMilliseconds(50), 10);

            var sw = Stopwatch.StartNew();
            var result = await ProbeRunner.RunAsync("too-late", budget, async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
                return "never";
            });
            sw.Stop();

            Assert.Null(result);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(4),
                $"deadline + grace must bound the probe; took {sw.Elapsed.TotalSeconds:F2}s");
        }

        // ── Exhaustion between probes ──

        [Fact]
        public async Task Run_Exhaustion_BetweenProbes_SkipsSubsequent()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 1);

            var r1 = await ProbeRunner.RunAsync("test", budget, () => Task.FromResult<string?>("first"));
            Assert.Equal("first", r1);
            Assert.True(budget.IsExhausted);

            var operationCalled = false;
            var r2 = await ProbeRunner.RunAsync("test", budget, () =>
            {
                operationCalled = true;
                return Task.FromResult<string?>("second");
            });
            Assert.Null(r2);
            Assert.False(operationCalled);
        }

        // ── ProbeBudget value correctness ──

        [Fact]
        public void Budget_Add_IncrementsBoth()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 10);
            budget.Add(TimeSpan.FromSeconds(1.5));
            Assert.Equal(1, budget.ProbesRun);
            Assert.Equal(1.5, budget.Elapsed.TotalSeconds, 1);

            budget.Add(TimeSpan.FromSeconds(2.5));
            Assert.Equal(2, budget.ProbesRun);
            Assert.Equal(4.0, budget.Elapsed.TotalSeconds, 1);
        }

        [Fact]
        public void Budget_IsExhausted_OnProbesRun()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(5), 2);
            budget.Add(TimeSpan.FromMilliseconds(100));
            Assert.False(budget.IsExhausted);
            budget.Add(TimeSpan.FromMilliseconds(100));
            Assert.True(budget.IsExhausted);  // ProbesRun(2) >= MaxProbes(2)
        }

        [Fact]
        public void Budget_IsExhausted_OnElapsed()
        {
            var budget = new ProbeBudget(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), 100);
            budget.Add(TimeSpan.FromSeconds(1.5));
            Assert.False(budget.IsExhausted);
            budget.Add(TimeSpan.FromSeconds(1.0));
            Assert.True(budget.IsExhausted);  // Elapsed(2.5) >= TotalTimeout(2)
        }

        // ── Factory defaults ──
        //
        // C058. These four assertions were internally inconsistent as written: they pinned a 5s
        // per-probe timeout (correct for Prophet/Portal/Glutton) against the pre-C058 totals, and
        // 5s for Mirror where the macOS twin uses 6s because Mirror's DoH vantage comparisons have
        // a 5s request timeout of their own. Both columns are now the macOS ProbeBudget.swift
        // values. The per-probe floor is the transport worst case — UDP is MaxUDPAttempts(2) ×
        // the 2000 ms receive timeout = 4.0s on BOTH platforms — so the old flat 2.5s fired 1.5s
        // before a retried UDP query could possibly answer.

        [Fact]
        public void DefaultProphet_HasExpectedValues()
        {
            var b = ProbeBudget.DefaultProphet;
            Assert.Equal(45, b.TotalTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.PerProbeTimeout.TotalSeconds, 1);
            Assert.Equal(15, b.MaxProbes);
        }

        [Fact]
        public void DefaultMirror_HasExpectedValues()
        {
            var b = ProbeBudget.DefaultMirror;
            Assert.Equal(50, b.TotalTimeout.TotalSeconds, 1);
            Assert.Equal(6, b.PerProbeTimeout.TotalSeconds, 1);
            Assert.Equal(10, b.MaxProbes);
        }

        [Fact]
        public void DefaultPortal_HasExpectedValues()
        {
            var b = ProbeBudget.DefaultPortal;
            Assert.Equal(30, b.TotalTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.PerProbeTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.MaxProbes);
        }

        [Fact]
        public void DefaultGlutton_HasExpectedValues()
        {
            var b = ProbeBudget.DefaultGlutton;
            Assert.Equal(25, b.TotalTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.PerProbeTimeout.TotalSeconds, 1);
            Assert.Equal(5, b.MaxProbes);
        }

        /// C058 / K-15: the defect this fix exists to remove, pinned so it cannot return.
        /// A per-probe deadline below the transport worst case means a UDP probe that needs its
        /// retry can never return an answer — the deadline fires while the answer is in flight,
        /// and the section records a fabricated absence. Every section default must leave room
        /// for MaxUDPAttempts(2) × 2000 ms.
        [Fact]
        public void EverySectionDefault_PerProbeTimeout_ExceedsUDPTransportWorstCase()
        {
            var udpWorstCase = TimeSpan.FromSeconds(4.0);   // 2 attempts × 2000 ms (DNSResolver)

            foreach (var (name, b) in new (string, ProbeBudget)[]
                     {
                         ("Prophet", ProbeBudget.DefaultProphet),
                         ("Mirror",  ProbeBudget.DefaultMirror),
                         ("Portal",  ProbeBudget.DefaultPortal),
                         ("Glutton", ProbeBudget.DefaultGlutton),
                     })
            {
                Assert.True(b.PerProbeTimeout > udpWorstCase,
                    $"{name} per-probe timeout {b.PerProbeTimeout.TotalSeconds}s does not exceed the " +
                    $"{udpWorstCase.TotalSeconds}s UDP transport worst case — a retried query cannot answer in time.");
            }
        }
    }

    // B073/B071/C016/C017/C018 — DNS wire-parsing honesty cluster.
    public class WireParsingHonestyTests
    {
        // B073 — a malformed OPT/additional section is EDNS-UNKNOWN, never a confident
        // "not supported" (which would under-read a middlebox mangling the response).
        [Fact]
        public async Task MalformedOptReadsInconclusive_NotLegacyResolver()
        {
            Func<DNSQuery, Task<DNSResponse>> malformedOpt = _ => Task.FromResult(new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                Records = new List<DNSRecord> { new() { Name = "x.com", Type = 1, Ttl = 60, Rdata = "1.2.3.4" } },
                Edns = null,
                EdnsMalformed = true,
            });
            var cap = await EDNS0Probe.Probe("x.com", "", malformedOpt);
            Assert.Equal(EDNS0CapabilityStatus.Inconclusive, cap.Status);

            // Failure case: genuinely-absent OPT still reads NotSupported (the fix must not
            // swallow the legacy-resolver classification).
            Func<DNSQuery, Task<DNSResponse>> noOpt = _ => Task.FromResult(new DNSResponse
            {
                Status = DNSResponseStatus.Success,
                Records = new List<DNSRecord> { new() { Name = "x.com", Type = 1, Ttl = 60, Rdata = "1.2.3.4" } },
                Edns = null,
                EdnsMalformed = false,
            });
            var legacy = await EDNS0Probe.Probe("x.com", "", noOpt);
            Assert.Equal(EDNS0CapabilityStatus.NotSupported, legacy.Status);
        }

        // B071 — a declared-but-unparseable authority section must not grade as the BareDenial
        // POSITIVE; it is a corrupt response and reads inconclusive.
        [Fact]
        public void MalformedAuthorityIsNotABareDenial()
        {
            var malformed = DenialOfExistenceProbe.Classify("gone.example.com",
                DNSResponseStatus.NXDomain, new List<DNSRecord>(), new List<DNSRecord>(),
                authorityMalformed: true);
            Assert.Equal(DenialStatus.Inconclusive, malformed.Status);

            // Failure case: a genuinely empty authority section still reads BareDenial.
            var bare = DenialOfExistenceProbe.Classify("gone.example.com",
                DNSResponseStatus.NXDomain, new List<DNSRecord>(), new List<DNSRecord>(),
                authorityMalformed: false);
            Assert.Equal(DenialStatus.BareDenial, bare.Status);
        }

        // C016 — the wire question is punycoded (MakeQueryPacket calls ToAsciiName), so the
        // response-question guard must canonicalize before comparing or every Unicode IDN
        // query reads as spoofed.
        [Fact]
        public void UnicodeIdnQuestionMatchesItsPunycodedEcho()
        {
            var packet = DNSResolver.MakeQueryPacket("bücher.com", 1);
            Assert.True(DNSResolver.ResponseQuestionMatches(packet, "bücher.com", 1),
                "the Unicode name must match its own punycoded wire echo");
            // Failure cases: a different name or type must still be rejected.
            Assert.False(DNSResolver.ResponseQuestionMatches(packet, "buecher.com", 1));
            Assert.False(DNSResolver.ResponseQuestionMatches(packet, "bücher.com", 28));
        }

        // C017 — CNAME RDATA must decode to a real name against the FULL packet, so a
        // compression-pointer target compares equal across packets.
        [Fact]
        public void CnameRdataDecodesCompressedTargetToName()
        {
            // Hand-built response: question example.com/A, one CNAME answer whose owner AND
            // rdata are compression pointers back to the question name.
            var packet = new List<byte>
            {
                0x12, 0x34,             // ID
                0x81, 0x80,             // QR|RD|RA, RCODE 0
                0x00, 0x01,             // QD 1
                0x00, 0x01,             // AN 1
                0x00, 0x00, 0x00, 0x00, // NS 0, AR 0
            };
            packet.AddRange(new byte[] { 7 });
            packet.AddRange(System.Text.Encoding.ASCII.GetBytes("example"));
            packet.AddRange(new byte[] { 3 });
            packet.AddRange(System.Text.Encoding.ASCII.GetBytes("com"));
            packet.AddRange(new byte[] { 0, 0x00, 0x01, 0x00, 0x01 });          // root, type A, class IN
            packet.AddRange(new byte[] { 0xC0, 0x0C });                          // owner → ptr to qname
            packet.AddRange(new byte[] { 0x00, 0x05, 0x00, 0x01 });              // CNAME, IN
            packet.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x3C });              // TTL 60
            packet.AddRange(new byte[] { 0x00, 0x02, 0xC0, 0x0C });              // RDLEN 2, rdata = ptr

            var parsed = DNSResolver.ParseResponsePacketDetailed(packet.ToArray(), requestedType: 1);
            var cname = parsed.Records.Single(r => r.Type == 5);
            Assert.Equal("example.com", cname.Rdata);   // decoded name, NOT "c00c" hex

            // The chain probe must now key on it successfully.
            var chain = CNAMEChainProbe.ReconstructChain("example.com", parsed.Records);
            Assert.Equal(new List<string> { "example.com" }, chain);
        }

        // C018 — SVCB comparison covers priority/target/parameters, not just record presence.
        [Fact]
        public void SvcbParameterStrippingIsDivergent()
        {
            // prio=1, target=svc.example, alpn(key1)="h2", ech(key5)=0xAABB
            const string full = "0001" + "03737663076578616d706c6500" + "0001" + "0003" + "026832" + "0005" + "0002" + "aabb";
            // Same record with the ECH parameter stripped.
            const string stripped = "0001" + "03737663076578616d706c6500" + "0001" + "0003" + "026832";

            var canonFull = SVCBProbe.CanonicalizeSvcbRdata(full);
            var canonStripped = SVCBProbe.CanonicalizeSvcbRdata(stripped);
            Assert.NotNull(canonFull);
            Assert.NotNull(canonStripped);
            Assert.StartsWith("prio=1;target=svc.example;", canonFull);
            Assert.NotEqual(canonFull, canonStripped);
            // Compression inside SVCB RDATA is forbidden — must parse to null, not garbage.
            Assert.Null(SVCBProbe.CanonicalizeSvcbRdata("0001c00c"));

            var divergent = SVCBProbe.Classify(
                new List<string> { "1.1.1.1", "9.9.9.9" }, new List<string>(), new List<string>(),
                new Dictionary<string, List<string>>
                {
                    ["1.1.1.1"] = new() { canonFull! },
                    ["9.9.9.9"] = new() { canonStripped! },
                });
            Assert.Equal(SVCBFindingStatus.Divergent, divergent.Status);

            // Failure case: identical parameter sets must stay Present — the comparison must not
            // manufacture divergence.
            var consistent = SVCBProbe.Classify(
                new List<string> { "1.1.1.1", "9.9.9.9" }, new List<string>(), new List<string>(),
                new Dictionary<string, List<string>>
                {
                    ["1.1.1.1"] = new() { canonFull! },
                    ["9.9.9.9"] = new() { canonFull! },
                });
            Assert.Equal(SVCBFindingStatus.Present, consistent.Status);
        }
    }

    // C2 — filtering verification. Each test names its fail state; the changelog mutation matrix
    // demonstrates each one reds when the matching defect is introduced.
    public class FilteringProbeTests
    {
        private static DNSResponse Resp(DNSResponseStatus status, params string[] ips) => new()
        {
            Status = status,
            Records = ips.Select(ip => new DNSRecord { Name = "doubleclick.net", Type = 1, Ttl = 60, Rdata = ip }).ToList()
        };

        // The acceptance bar: a known-blocked domain RESOLVING publicly must read Resolved.
        // Fail state: classify public answers as sinkholed → the probe vouches for a filter that
        // let the domain through.
        [Fact]
        public void PublicAnswerMeansFilterDidNotAct()
            => Assert.Equal(FilteringProbe.DomainOutcome.Resolved, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "142.250.72.14")));

        // NXDOMAIN and null/loopback/private answers are all sinkhole styles.
        // Fail state: treat 0.0.0.0 as resolved → a working Pi-hole reads "not filtering".
        [Fact]
        public void SinkholeStylesAllReadSinkholed()
        {
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.NXDomain)));
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "0.0.0.0")));
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "127.0.0.1")));
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "192.168.1.5")));
            Assert.Equal(FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.Classify(Resp(DNSResponseStatus.NoData)));
        }

        // Any public address among sinkhole addresses means the domain ESCAPED.
        // Fail state: require all-public before Resolved → a partial escape hides.
        [Fact]
        public void MixedAnswerWithAnyPublicReadsResolved()
            => Assert.Equal(FilteringProbe.DomainOutcome.Resolved, FilteringProbe.Classify(Resp(DNSResponseStatus.Success, "0.0.0.0", "142.250.72.14")));

        // A timeout is NO READING. Fail state: map timeout to sinkholed → a dead network
        // fabricates "your filter is working" (Protocol §1 false-secure).
        [Fact]
        public void TimeoutIsUnknownNeverSinkholed()
        {
            Assert.Equal(FilteringProbe.DomainOutcome.Unknown, FilteringProbe.Classify(Resp(DNSResponseStatus.Timeout)));
            Assert.Equal(FilteringProbe.DomainOutcome.Unknown, FilteringProbe.Classify(Resp(DNSResponseStatus.TransportError)));
        }

        [Fact]
        public void AllSinkholedIsActive()
            => Assert.Equal(FilteringProbe.Enforcement.Active, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.DomainOutcome.Sinkholed }));

        [Fact]
        public void AllResolvedIsInactive()
            => Assert.Equal(FilteringProbe.Enforcement.Inactive, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Resolved, FilteringProbe.DomainOutcome.Resolved }));

        // A mix is a LEAKY blocklist, distinct from active and inactive.
        [Fact]
        public void MixIsPartial()
            => Assert.Equal(FilteringProbe.Enforcement.Partial, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Sinkholed, FilteringProbe.DomainOutcome.Resolved }));

        // All-unreadable must be inconclusive. Fail state: default the empty case to Active →
        // an offline machine reads "filter verified" with zero evidence (§6 zero-data).
        [Fact]
        public void AllUnknownIsInconclusiveNeverActive()
        {
            Assert.Equal(FilteringProbe.Enforcement.Inconclusive, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Unknown, FilteringProbe.DomainOutcome.Unknown }));
            Assert.Equal(FilteringProbe.Enforcement.Inconclusive, FilteringProbe.Aggregate(System.Array.Empty<FilteringProbe.DomainOutcome>()));
        }

        // Unreadable probes drop out of the denominator; the readable remainder still decides.
        [Fact]
        public void UnknownsDropOutOfDenominator()
        {
            Assert.Equal(FilteringProbe.Enforcement.Active, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Unknown, FilteringProbe.DomainOutcome.Sinkholed }));
            Assert.Equal(FilteringProbe.Enforcement.Inactive, FilteringProbe.Aggregate(new[] { FilteringProbe.DomainOutcome.Unknown, FilteringProbe.DomainOutcome.Resolved }));
        }
    }

    // K-19 / K-20 — domain name validation and canonicalization parity with the macOS twins.
    public class DomainNameParityTests
    {
        /// K-19: a leading dot was accepted by macOS `validateDomainName` and rejected by the C#
        /// twin, so identical input succeeded on one platform and failed on the other. C#'s own
        /// encoder already trimmed both ends, so the validator was stricter than the code it
        /// guards — and the wire bytes it refused to produce were correct.
        /// Failure case: revert `Trim('.')` to `TrimEnd('.')` → the leading dot leaves an empty
        /// first label → FormatException → red.
        [Fact]
        public void LeadingDot_IsAccepted_AndProducesTheSameWireBytesAsWithoutIt()
        {
            var withDot = DNSResolver.MakeQueryPacket(".example.com", 1, out _);
            var without = DNSResolver.MakeQueryPacket("example.com", 1, out _);

            // Skip the first two bytes: the transaction ID is random per call.
            Assert.Equal(without.Skip(2).ToArray(), withDot.Skip(2).ToArray());
        }

        /// The leading-dot trim must not weaken the empty-label check it shares code with: an
        /// INTERIOR empty label still writes a premature root terminator and must stay rejected.
        [Fact]
        public void InteriorEmptyLabel_IsStillRejected()
        {
            Assert.Throws<FormatException>(() => DNSResolver.MakeQueryPacket("example..com", 1, out _));
        }

        /// K-20: a rejected domain must say WHY. A bare null told the caller only that a domain
        /// vanished — an unexplained silent drop. macOS throws a typed `DomainCanonical.Error`
        /// with descriptions; C# now reports the same three cases.
        /// Failure case: collapse the error to null → red.
        [Fact]
        public void RejectedDomains_ReportTheReason_NotABareNull()
        {
            Assert.Null(DomainCanonical.TryCanonicalize("   ", out var emptyError));
            Assert.IsType<DomainCanonical.DomainCanonicalError.Empty>(emptyError);
            Assert.Equal("domain is empty", emptyError!.ToString());

            var overlong = string.Join(".", Enumerable.Repeat("abcdefghijklmnopqrstuvwxyz012345678901234567890123456789012", 5));
            Assert.Null(DomainCanonical.TryCanonicalize(overlong, out var longError));
            var tooLong = Assert.IsType<DomainCanonical.DomainCanonicalError.TooLong>(longError);
            Assert.True(tooLong.Length > 253);
            Assert.Contains("exceeds 253", tooLong.ToString(), StringComparison.Ordinal);
        }

        /// A successful canonicalization reports no error — the reason channel must not fire on
        /// the happy path, or callers learn to ignore it.
        [Fact]
        public void AcceptedDomain_ReportsNoReason_AndStillCanonicalizes()
        {
            var result = DomainCanonical.TryCanonicalize("EXAMPLE.COM.", out var error);
            Assert.Equal("example.com", result);
            Assert.Null(error);
        }

        /// The convenience wrapper must keep behaving exactly as before for existing callers.
        [Fact]
        public void CanonicalizeOrNull_IsUnchangedByTheTypedErrorAddition()
        {
            Assert.Equal("example.com", DomainCanonical.CanonicalizeOrNull("Example.com."));
            Assert.Null(DomainCanonical.CanonicalizeOrNull("   "));
        }
    }
}
