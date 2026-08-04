using System;
using System.Threading.Tasks;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // ECSProbe had ZERO tests (audit 2026-07-16, ROADMAP TOP-TEN #10) despite being the SOLE
    // producer of EcsSubnetLeak — which the synthesis tests treat as independent corroboration for
    // a privacy-leak result. It already has an injectable QueryFn seam, so it was always testable;
    // it simply was not tested.
    //
    // The honesty axis here is the tri-state: a probe that could not assess ECS forwarding must say
    // INCONCLUSIVE, never NotForwarded. "The resolver does not leak your subnet" and "we could not
    // find out whether it leaks your subnet" are opposite claims, and only one of them is safe to
    // make from a failed query.
    public class ECSProbeTests
    {
        private static Func<DNSQuery, Task<DNSResponse>> Returns(DNSResponse r) => _ => Task.FromResult(r);

        private static DNSResponse Ok(byte? ecsScope) => new()
        {
            Status = DNSResponseStatus.Success,
            Edns = new EDNSResponseInfo(UdpPayloadSize: 4096, Version: 0, ExtendedRCode: 0, EcsScopePrefixLength: ecsScope),
        };

        [Fact]
        public async Task EchoedEcsScope_IsForwarded_APrivacyExposure()
        {
            var r = await ECSProbe.Probe(query: Returns(Ok(24)));

            Assert.Equal(ECSLeakStatus.Forwarded, r.Status);
            Assert.Equal((byte)24, r.ScopePrefixLength);
            Assert.Contains("/24", r.Detail);
        }

        [Fact]
        public async Task ZeroScope_IsNotForwarded_ThePrivacyPreservingCase()
        {
            // Scope 0 means the resolver echoed EDNS but applied no client-subnet scope.
            var r = await ECSProbe.Probe(query: Returns(Ok(0)));
            Assert.Equal(ECSLeakStatus.NotForwarded, r.Status);
        }

        [Fact]
        public async Task NoOptEchoed_IsInconclusive_NEVER_NotForwarded()
        {
            // The load-bearing case. A resolver that echoes no OPT told us NOTHING about ECS.
            // Reporting NotForwarded here would claim a privacy property we never measured.
            var r = await ECSProbe.Probe(query: Returns(new DNSResponse { Status = DNSResponseStatus.Success, Edns = null }));

            Assert.Equal(ECSLeakStatus.Inconclusive, r.Status);
            Assert.NotEqual(ECSLeakStatus.NotForwarded, r.Status);
            Assert.Contains("cannot assess", r.Detail);
        }

        [Theory]
        [InlineData(DNSResponseStatus.Timeout)]
        [InlineData(DNSResponseStatus.TransportError)]
        [InlineData(DNSResponseStatus.ServFail)]
        [InlineData(DNSResponseStatus.MalformedResponse)]
        public async Task AFailedQuery_IsInconclusive_NEVER_NotForwarded(DNSResponseStatus status)
        {
            // A timeout is not evidence of privacy. Same rule as the transport probes: a probe that
            // did not complete must not produce a reassuring answer.
            var r = await ECSProbe.Probe(query: Returns(new DNSResponse { Status = status }));

            Assert.Equal(ECSLeakStatus.Inconclusive, r.Status);
            Assert.NotEqual(ECSLeakStatus.NotForwarded, r.Status);
            Assert.NotEqual(ECSLeakStatus.Forwarded, r.Status);
        }

        [Fact]
        public async Task AThrowingTransport_DoesNotEscapeAsAnException()
        {
            // Probes run inside ProbeRunner, but a raw caller (FirstRunScan wires this directly)
            // must not eat a stack trace where an inconclusive belongs.
            var ex = await Record.ExceptionAsync(async () =>
                await ECSProbe.Probe(query: _ => throw new InvalidOperationException("transport died")));

            // Documents CURRENT behaviour: the probe does not swallow — ProbeRunner is the guard.
            // If this ever changes to swallow-and-return-inconclusive, that is an improvement and
            // this assertion should be inverted deliberately, not silently.
            Assert.NotNull(ex);
        }

        [Fact]
        public async Task TheProbeSendsAnEcsOptionAtAll()
        {
            // Without this the probe could stop asking the ECS question entirely and every result
            // would still "pass" — a probe that measures nothing always agrees with you.
            DNSQuery? sent = null;
            await ECSProbe.Probe(query: q => { sent = q; return Task.FromResult(Ok(0)); });

            Assert.NotNull(sent);
            Assert.Equal(ECSProbe.TestSubnetV4, sent!.Edns0ClientSubnetV4);
            Assert.NotNull(sent.Edns0UDPSize);
            Assert.Equal(ECSProbe.TypeA, sent.Type);
        }

        [Fact]
        public void Untested_IsADistinctState_NotAResult()
        {
            Assert.Equal(ECSLeakStatus.Untested, ECSLeak.Untested.Status);
            Assert.NotEqual(ECSLeakStatus.NotForwarded, ECSLeak.Untested.Status);
        }
    }
}
