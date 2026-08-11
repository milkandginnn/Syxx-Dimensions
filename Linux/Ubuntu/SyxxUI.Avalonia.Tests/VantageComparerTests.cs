using System.Collections.Generic;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    public class VantageComparerTests
    {
        private static ResolverQueryOutcome Success(params string[] ips)
        {
            var records = new System.Collections.Generic.List<DNSRecord>();
            foreach (var ip in ips)
                records.Add(new DNSRecord { Name = "example.com", Type = 1, Ttl = 60, Rdata = ip });
            return new ResolverQueryOutcome { Records = records, Status = DNSResponseStatus.Success };
        }

        private static ResolverQueryOutcome Failure(DNSResponseStatus status = DNSResponseStatus.Timeout)
            => new ResolverQueryOutcome { Records = new System.Collections.Generic.List<DNSRecord>(), Status = status };

        private static readonly string GoodIP = "93.184.216.34";

        [Fact]
        public void AllPublicResolversFailIsInconclusive()
        {
            var local = Success(GoodIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Failure(DNSResponseStatus.Timeout),
                ["B"] = Failure(DNSResponseStatus.TransportError),
                ["C"] = Failure(DNSResponseStatus.Timeout)
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.True(result.Inconclusive);
            Assert.False(result.Redirection);
        }

        [Fact]
        public void LocalQueryFailIsInconclusive()
        {
            var local = Failure();
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.True(result.Inconclusive);
        }

        [Fact]
        public void EmptyButSuccessfulLocalIsInconclusiveNotRedirection()
        {
            // B017 regression (parity with macOS): a local resolver that SUCCEEDS but returns no
            // records (NODATA/NXDOMAIN) while public resolvers return records is NOT tampering —
            // split-horizon/geo/no-such-record all produce it. Must read as inconclusive, never a
            // fabricated redirection. (Distinct from a transport failure, already inconclusive.)
            var local = Success();   // empty records, Status.Success
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP),
                ["B"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.False(result.Redirection);
            Assert.True(result.Inconclusive);
        }

        [Fact]
        public void CDNDivergenceNotFlaggedAsRedirection()
        {
            // Local matches one of the two public answers — normal geo/CDN variation.
            var local = Success("1.2.3.4");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success("1.2.3.4"),
                ["B"] = Success("5.6.7.8")
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.False(result.Inconclusive);
            Assert.False(result.Redirection);
        }

        [Fact]
        public void LocalDisjointFromConsensusIsRedirection()
        {
            var local = Success("10.10.10.10");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP),
                ["B"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.False(result.Inconclusive);
            Assert.True(result.Redirection);
        }

        [Fact]
        public void BlockingAnswerFlaggedAsRedirection()
        {
            var local = Success("127.0.0.1");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP),
                ["B"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.True(result.Redirection);
        }

        [Fact]
        public void SingleBaselineIsDegradedNotRedirection()
        {
            // Min-2-vantage guard (parity with macOS): with only one usable public baseline there
            // is no consensus to compare against, so even a fully disjoint local answer must read
            // as degraded — not a redirection result built on a single witness.
            var local = Success("10.10.10.10");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.False(result.Redirection);
            Assert.False(result.Inconclusive);
            Assert.True(result.Degraded);
        }

        [Fact]
        public void OnePublicFailureIsDegradedNotInconclusive()
        {
            var local = Success(GoodIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["A"] = Success(GoodIP),
                ["B"] = Failure()
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.False(result.Inconclusive);
            Assert.False(result.Redirection);
            Assert.True(result.Degraded);
        }

        // A routable, non-blocking IP: keeps these cases exercising the DoH cross-check and the union
        // upgrade rather than short-circuiting through IPClassifier.IsBlockingAnswer (private/loopback).
        private static readonly string TamperIP = "45.33.32.156";

        // B010: unanimous-UDP tamper (every UDP vantage, local included, agrees on the tampered IP)
        // is invisible to the union test but caught by the HTTPS-routed DoH consensus.
        [Fact]
        public void UnanimousUDPTamperCaughtByCleanDoH()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"] = Success(TamperIP),
                ["Quad9"] = Success(TamperIP)
            };
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"] = Success(GoodIP),
                ["Quad9"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics, doh);
            Assert.True(result.Redirection);
            Assert.False(result.Inconclusive);
        }

        [Fact]
        public void SingleDoHDisagreementIsNotTamper()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"] = Success(TamperIP),
                ["Quad9"] = Success(TamperIP)
            };
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"] = Failure(),
                ["Quad9"] = Failure()
            };
            var result = VantageComparer.Evaluate(local, publics, doh);
            Assert.False(result.Redirection);
        }

        // WS2-B.2 / B058-B059: a single poisoned baseline echoing the local tampered IP must not
        // downgrade a real redirection to DEGRADED when ≥2 other resolvers corroborate a different
        // destination.
        [Fact]
        public void PoisonedBaselineEchoUpgradedToConfirmedRedirection()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),  // poisoned echo
                ["Google"] = Success(GoodIP),
                ["Quad9"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.True(result.Redirection);
            Assert.False(result.Degraded);
            Assert.False(result.Inconclusive);
        }

        [Fact]
        public void CDNSingleOverlapWithDisagreeingResolversStaysDegraded()
        {
            var local = Success("1.2.3.4");
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success("1.2.3.4"),  // shares local edge
                ["Google"] = Success("5.6.7.8"),
                ["Quad9"] = Success("9.10.11.12")
            };
            var result = VantageComparer.Evaluate(local, publics);
            Assert.False(result.Redirection);
            Assert.True(result.Degraded);
            Assert.False(result.Inconclusive);
        }

        // B010 residual: UDP unanimous + all DoH blocked must be Inconclusive, never false-secure.
        // The false-secure path: every UDP vantage returns the same IP set (possibly tampered),
        // and every DoH endpoint is blocked or fails. Without the DoH cross-check there is no way to
        // distinguish "tamper + attacker also blocks DoH" from "clean + transient DoH outage".
        // Result MUST be Inconclusive.
        //
        // Mutation proof: remove the dohBlockedWhileUdpUnanimous guard in VantageComparer.Evaluate()
        // and this test goes RED — Inconclusive becomes false on an all-DoH-failed run.
        [Fact]
        public void UDPUnanimousAllDoHBlocked_IsInconclusive()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"] = Success(TamperIP),
                ["Quad9"] = Success(TamperIP)
            };
            // All DoH endpoints blocked — no records returned from any.
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Failure(),
                ["Google"] = Failure(),
                ["Quad9"] = Failure()
            };
            var result = VantageComparer.Evaluate(local, publics, doh);

            Assert.True(result.Inconclusive,
                "UDP unanimous + all DoH blocked must be Inconclusive, not false-secure (B010 residual)");
            Assert.False(result.Redirection,
                "DoH-blocked run must not fabricate a tampered result — no evidence for it");
            Assert.False(result.Degraded,
                "Inconclusive and Degraded are mutually exclusive in Evaluate()");
        }

        // Boundary guard: DoH provided but ONLY a subset fails (not all) — must not falsely
        // force Inconclusive when a partial DoH result is still usable.
        [Fact]
        public void PartialDoHFailureWithOneSuccess_IsNotInconclusive()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"] = Success(TamperIP),
                ["Quad9"] = Success(TamperIP)
            };
            // One DoH endpoint succeeds and returns the real IP — partial success, not total blockage.
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),   // succeeds with real answer
                ["Google"] = Failure(),
                ["Quad9"] = Failure()
            };
            var result = VantageComparer.Evaluate(local, publics, doh);
            // One DoH success is below the ≥2 corroboration floor for a tamper result, but also
            // does NOT trigger the all-DoH-blocked Inconclusive guard.
            Assert.False(result.Inconclusive,
                "partial DoH failure (one success) must not trigger the all-DoH-blocked Inconclusive guard");
            Assert.False(result.Redirection,
                "a lone DoH answer does not meet the ≥2 corroboration floor");
        }

        // ---------------------------------------------------------------------------------------
        // D1.1 / C055 / C117 — CNAME and RRSIG rdata must never enter a vantage comparison.
        // DNSResolver deliberately surfaces type 5 (CNAME) and type 46 (RRSIG) alongside the
        // requested type, and IPClassifier maps anything IPAddress.TryParse rejects to
        // IPAddressClass.Invalid, which IsBlockingAnswer reports as blocking. Both directions of
        // the resulting defect are pinned below. Twin of the type filter in macOS
        // VantageComparer.swift:181-200.
        // ---------------------------------------------------------------------------------------

        /// Builds an outcome carrying a CNAME (type 5) ahead of its A records, as a CDN-fronted
        /// name genuinely resolves on the wire.
        private static ResolverQueryOutcome CnameFronted(string cnameTarget, params string[] ips)
        {
            var records = new List<DNSRecord>
            {
                new DNSRecord { Name = "example.com", Type = 5, Ttl = 60, Rdata = cnameTarget }
            };
            foreach (var ip in ips)
                records.Add(new DNSRecord { Name = cnameTarget, Type = 1, Ttl = 60, Rdata = ip });
            return new ResolverQueryOutcome { Records = records, Status = DNSResponseStatus.Success };
        }

        [Fact]
        public void CnameRdataDoesNotFabricateABlockedAnswer()
        {
            // The fabricated-positive direction. Local agrees with both public baselines on the
            // address, and merely carries the CNAME that fronts it. Before the C055 filter, the
            // CNAME target failed IPAddress.TryParse ⇒ Invalid ⇒ IsBlockingAnswer ⇒ Redirection.
            // That accused every CDN-hosted site of tampering on ordinary traffic.
            var local = CnameFronted("e1.akamaiedge.net", GoodIP);
            var publics = new Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = CnameFronted("e1.akamaiedge.net", GoodIP),
                ["Google"] = CnameFronted("e1.akamaiedge.net", GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);

            Assert.False(result.Redirection,
                "a CNAME target is a domain name, not a blocking IP — it must never fabricate a redirection");
            Assert.False(result.Inconclusive);
            Assert.False(result.Degraded);
        }

        [Fact]
        public void RrsigRdataDoesNotFabricateABlockedAnswer()
        {
            // Same defect, second carrier: RRSIG (type 46) rdata is surfaced as a hex string, which
            // also fails IPAddress.TryParse. A DO-enabled answer must not read as blocked.
            var local = Success(GoodIP);
            local.Records.Add(new DNSRecord
            {
                Name = "example.com",
                Type = 46,
                Ttl = 60,
                Rdata = "00010802000151800000000000000000"
            });
            var publics = new Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);

            Assert.False(result.Redirection,
                "RRSIG hex rdata is not an address — it must never fabricate a redirection");
            Assert.False(result.Inconclusive);
        }

        [Fact]
        public void CnameInTheUdpBaselinesDoesNotSuppressUnanimousTamperDetection()
        {
            // The false-negative direction (C117), exercised where it is actually reachable.
            //
            // In the disjointness branch the false negative is masked: any CNAME in the LOCAL answer
            // trips the C055 fabricated-positive first, so it never gets that far. The reachable case
            // is the B010 unanimous-UDP comparison, where the local answer carries only an address
            // and the rewritten UDP baselines carry the CNAME as well. Before the filter, the extra
            // CNAME string broke `localRdatas.SetEquals(firstUdp)` and the whole tamper detection —
            // corroborated by two independent DoH endpoints — returned a silent clean.
            var local = Success(TamperIP);                                   // address only, no CNAME
            var publics = new Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = CnameFronted("cdn.example.net", TamperIP),
                ["Google"] = CnameFronted("cdn.example.net", TamperIP),
                ["Quad9"] = CnameFronted("cdn.example.net", TamperIP)
            };
            // Two attacker-independent DoH endpoints agree on the real destination, which is what
            // licenses the accusation at all (B010 needs the cross-transport disagreement).
            var doh = new Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Quad9"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics, doh);

            Assert.True(result.Redirection,
                "a CNAME present only in the UDP baselines must not break unanimity and suppress a DoH-corroborated tamper detection");
        }

        [Fact]
        public void CnameOnlyLocalAnswerIsInconclusiveNotRedirection()
        {
            // Honesty guard on the fix itself: once CNAME rdata is excluded, a local answer that
            // carries ONLY a CNAME has no address to compare. That is "cannot determine", not
            // "tampering" — the empty candidate must fall through ClassifyDivergence to
            // Inconclusive, never to an accusation.
            var local = CnameFronted("cdn.example.net");   // CNAME, no A record
            var publics = new Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"] = Success(GoodIP)
            };
            var result = VantageComparer.Evaluate(local, publics);

            Assert.False(result.Redirection,
                "no address in the local answer is no evidence of redirection");
            Assert.True(result.Inconclusive,
                "an address-free local answer must read as inconclusive, never as a clean or a positive");
        }
    }
}
