using System.Collections.Generic;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
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
                ["Google"]     = Success(TamperIP),
                ["Quad9"]      = Success(TamperIP)
            };
            // All DoH endpoints blocked — no records returned from any.
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Failure(),
                ["Google"]     = Failure(),
                ["Quad9"]      = Failure()
            };
            var result = VantageComparer.Evaluate(local, publics, doh);

            Assert.True(result.Inconclusive,
                "UDP unanimous + all DoH blocked must be Inconclusive, not false-secure (B010 residual)");
            Assert.False(result.Redirection,
                "DoH-blocked run must not fabricate a tampered result — no evidence for it");
            Assert.False(result.Degraded,
                "Inconclusive and Degraded are mutually exclusive in Evaluate()");
        }

        // W04 residual: DoH provided, but only ONE endpoint answered. DetectUnanimousTampering
        // abandons the cross-check at its `dohSets.Count < 2` gate, so the run carries NO
        // cross-transport corroboration at all — yet the lone DoH answer is enough to make an
        // "all DoH failed" test false. Before the fix, Evaluate emitted a fully clean result
        // (no redirection, no inconclusive, no degraded) from a run that never corroborated
        // anything. Under-2 answering endpoints must read exactly like zero: not corroborated.
        //
        // This is the benign-looking shape — the lone DoH endpoint AGREES with the unanimous UDP
        // answer and with local. One agreeing witness is still below the ≥2 corroboration floor
        // this comparer applies everywhere else, so it cannot license a clean result.
        //
        // Mutation proof: revert the guard in Evaluate() to `allDohFailed` and this goes RED —
        // Inconclusive becomes false on a run with exactly one answering DoH endpoint.
        [Fact]
        public void UDPUnanimousWithSingleAgreeingDoH_IsInconclusive()
        {
            var local = Success(GoodIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"]     = Success(GoodIP),
                ["Quad9"]      = Success(GoodIP)
            };
            // Exactly one DoH endpoint answered; the other two failed.
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"]     = Failure(),
                ["Quad9"]      = Failure()
            };
            var result = VantageComparer.Evaluate(local, publics, doh);

            Assert.True(result.Inconclusive,
                "one answering DoH endpoint is below the ≥2 cross-transport corroboration floor; " +
                "the run must not be reported clean (W04)");
            Assert.False(result.Redirection,
                "an uncorroborated run must not fabricate a tampered result either");
        }

        // W04, the accusatory-looking shape: the lone DoH endpoint DISAGREES with the unanimous UDP
        // answer that local echoes. One witness is below the tamper floor, so no redirection — but
        // it is equally not a clean run, because the cross-check never reached quorum.
        [Fact]
        public void PartialDoHFailureWithOneSuccess_IsInconclusive()
        {
            var local = Success(TamperIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(TamperIP),
                ["Google"]     = Success(TamperIP),
                ["Quad9"]      = Success(TamperIP)
            };
            // One DoH endpoint succeeds and returns the real IP — partial success, not total blockage.
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),   // succeeds with real answer
                ["Google"]     = Failure(),
                ["Quad9"]      = Failure()
            };
            var result = VantageComparer.Evaluate(local, publics, doh);
            Assert.True(result.Inconclusive,
                "one answering DoH endpoint leaves the cross-transport check below quorum (W04)");
            Assert.False(result.Redirection,
                "a lone DoH answer does not meet the ≥2 corroboration floor");
        }

        // W04 boundary, the other side of the gate: TWO answering DoH endpoints DO reach the
        // corroboration floor, so a third failing endpoint must not force Inconclusive. This is the
        // test that keeps the fix from degenerating into "any DoH failure ⇒ inconclusive".
        [Fact]
        public void TwoAgreeingDoHWithOneFailure_IsNotInconclusive()
        {
            var local = Success(GoodIP);
            var publics = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"]     = Success(GoodIP),
                ["Quad9"]      = Success(GoodIP)
            };
            var doh = new System.Collections.Generic.Dictionary<string, ResolverQueryOutcome>
            {
                ["Cloudflare"] = Success(GoodIP),
                ["Google"]     = Success(GoodIP),
                ["Quad9"]      = Failure()
            };
            var result = VantageComparer.Evaluate(local, publics, doh);
            Assert.False(result.Inconclusive,
                "two answering DoH endpoints meet the corroboration floor — this run IS corroborated");
            Assert.False(result.Redirection);
        }
    }
}
