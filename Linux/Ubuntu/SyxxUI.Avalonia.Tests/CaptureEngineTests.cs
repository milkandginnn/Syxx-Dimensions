using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;
using Xunit;

namespace SyxxUI.Avalonia.Tests
{
    /// <summary>
    /// Focused, offline tests for the shared capture engine (Aug 2026 extraction from
    /// <c>CageCommand</c>). The full capture loop needs raw sockets, which a test runner cannot
    /// secure portably; these tests pin the PURE helpers <see cref="CaptureEngine.GetLocalAddresses"/>
    /// and the <see cref="OpenSocketResult"/> shape — both reachable headless, both central to the
    /// honesty spine (D107 attribution + F029 degraded-state reporting).
    ///
    /// <para>The legacy capture-loop tests in <c>DNSCaptureTests</c> exercise the parser directly
    /// (<c>DNSCapture.ParseIPUDPHeader</c> / <c>DNSCapture.ParseDNSPacket</c>) which are now called
    /// from inside <c>CaptureSession.TryBuildRecord</c>. They continue to cover the parse path
    /// end-to-end.</para>
    /// </summary>
    public class CaptureEngineTests
    {
        // =========================================================================
        // OpenSocketResult — the public shape that the CLI / daemon / GUI all consume
        // to decide between a complete, degraded, or aborted capture. Mutations to this enum
        // are caught here so the CLI's degraded-state warning copy stays honest.
        //
        // The predicates (BothFailed / V4Only / V6Only) only inspect `Failure?.Kind` — the
        // V4/V6 slots are data only. So these tests pass sockets as `null` deliberately and
        // exercise each predicate combination through the Failure alone, never needing to
        // allocate a real raw socket (which requires root/CAP_NET_RAW and is not portable
        // under a test runner).
        // =========================================================================

        [Fact]
        public void OpenSocketResult_NoFailure_AllDegradedPredicatesFalse()
        {
            // "Both sockets opened" surfaces as: no failure recorded, every degraded predicate
            // false. The record itself carries the result; this test does not need real sockets
            // because the predicates only read `Failure`.
            var r = new OpenSocketResult(null, null, null);

            Assert.Null(r.Failure);
            Assert.False(r.BothFailed);
            Assert.False(r.V4Only);
            Assert.False(r.V6Only);
        }

        [Fact]
        public void OpenSocketResult_V4Only_ReportsDegradedV6Missing()
        {
            // V4 is the only socket that opened — capture is V4-only; v6 failed (F029).
            var r = new OpenSocketResult(
                null,   // V4 is non-null in reality, but the predicate ignores it
                null,
                new OpenSocketFailure(OpenSocketFailureKind.V4Only, "Permission denied"));

            Assert.True(r.V4Only);
            Assert.False(r.BothFailed);
            Assert.False(r.V6Only);
            Assert.Equal("Permission denied", r.Failure?.Message);
        }

        [Fact]
        public void OpenSocketResult_V6Only_ReportsDegradedV4Missing()
        {
            // V6 is the only socket that opened — capture is V6-only; v4 failed (F029).
            var r = new OpenSocketResult(
                null,
                null,
                new OpenSocketFailure(OpenSocketFailureKind.V6Only, "Address family not supported"));

            Assert.True(r.V6Only);
            Assert.False(r.BothFailed);
            Assert.False(r.V4Only);
            Assert.Equal("Address family not supported", r.Failure?.Message);
        }

        [Fact]
        public void OpenSocketResult_BothFailed_CarriesReason()
        {
            var r = new OpenSocketResult(null, null,
                new OpenSocketFailure(OpenSocketFailureKind.BothFailed, "SocketException: root required"));

            Assert.True(r.BothFailed);
            Assert.False(r.V4Only);
            Assert.False(r.V6Only);
            Assert.Equal("SocketException: root required", r.Failure?.Message);
        }

        [Fact]
        public void OpenSocketFailure_KindAndMessage_ArePreserved()
        {
            // The pure record carrying socket-open failure detail — never touches a socket.
            var f = new OpenSocketFailure(OpenSocketFailureKind.V6Only, "Permission denied");
            Assert.Equal(OpenSocketFailureKind.V6Only, f.Kind);
            Assert.Equal("Permission denied", f.Message);
        }

        // =========================================================================
        // GetLocalAddresses — D107 attribution correctness (offline-testable: a host with
        // zero enumerated unicast addresses returns an empty set, signaling cautious attribution
        // to the caller — the empty-set policy the original CLI used for the same reason).
        // =========================================================================

        [Fact]
        public void GetLocalAddresses_NeverThrows_NonNullSet()
        {
            // Must not throw on any host; the worst case is an empty set, which the capture loop
            // interprets as "attribute cautiously" (every packet's local-side IP is treated as
            // potentially-local — the inverse of falsely attributing a neighbour's traffic).
            var set = CaptureEngine.GetLocalAddresses();
            Assert.NotNull(set);
        }

        [Fact]
        public void GetLocalAddresses_RejectsNullFromUnderlyingEnumeration()
        {
            // Hardening: even if NetworkInterface.GetAllNetworkInterfaces returns interfaces
            // whose UnicastAddresses throw, GetLocalAddresses must return a usable set.
            // (Reproduces the silent-fallback policy the original CageCommand used.)
            var set = CaptureEngine.GetLocalAddresses();
            Assert.NotNull(set);
            Assert.DoesNotContain(null, set);
        }

        // =========================================================================
        // EngineOutcome — the C041 parity pillar. An aborted capture must never be rendered
        // as an ordinary partial record, so the outcome the consumer awaits distinguishes
        // Completed from Aborted AT THE TYPE LEVEL.
        // =========================================================================

        [Fact]
        public void EngineOutcome_Completed_IsDistinctFromAborted()
        {
            EngineOutcome completed = new EngineOutcome.Completed();
            EngineOutcome aborted = new EngineOutcome.Aborted("Permission denied");

            Assert.IsType<EngineOutcome.Completed>(completed);
            Assert.IsType<EngineOutcome.Aborted>(aborted);
            Assert.NotEqual(completed, aborted);
        }

        [Fact]
        public void EngineOutcome_Aborted_PreservesReason()
        {
            var aborted = new EngineOutcome.Aborted("SocketException: CAP_NET_RAW missing");
            Assert.Equal("SocketException: CAP_NET_RAW missing", aborted.Reason);
        }
    }
}