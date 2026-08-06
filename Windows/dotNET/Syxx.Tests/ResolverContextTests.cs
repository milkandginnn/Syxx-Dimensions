using System;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    /// <summary>
    /// UI-PRELUDE — Windows twin of macOS <c>ResolverContextTests.swift</c>. Coverage for the
    /// shared <see cref="ResolverContext"/> classifier that every platform UI uses to read
    /// Prophet's stored last-run consensus. All four states plus the pinned staleness boundary:
    /// a snapshot EXACTLY <see cref="ResolverContext.StalenessMs"/> (3_600_000 ms) old is STALE;
    /// the fresh window is age &lt; StalenessMs.
    ///
    /// The clock is injected as <c>nowMs</c> — no test here sleeps or reads the wall clock.
    /// </summary>
    public class ResolverContextTests
    {
        /// Fixed "now" so every age below is exact.
        private const long NowMs = 1_700_000_000_000;

        private static ResolverConsensusSnapshot Snapshot(
            long ageMs,
            ResolverConsensusEngine.PeerConsensusState consensus,
            int reachable = 3)
            => new(
                Domain: "example.com",
                Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(NowMs - ageMs),
                PeerConsensus: consensus,
                ReachableCount: reachable,
                LocalDelta: null);

        // -- absent --------------------------------------------------------------------------------

        [Fact]
        public void NullSnapshot_IsAbsent()
            => Assert.Equal(ResolverContext.State.Absent, ResolverContext.Classify(null, NowMs));

        // -- confirmed -----------------------------------------------------------------------------

        [Fact]
        public void FreshAgree_IsConfirmed()
        {
            var s = Snapshot(60_000, ResolverConsensusEngine.PeerConsensusState.Agree);
            Assert.Equal(ResolverContext.State.Confirmed, ResolverContext.Classify(s, NowMs));
        }

        [Fact]
        public void ZeroAgeAgree_IsConfirmed()
        {
            var s = Snapshot(0, ResolverConsensusEngine.PeerConsensusState.Agree);
            Assert.Equal(ResolverContext.State.Confirmed, ResolverContext.Classify(s, NowMs));
        }

        // -- mismatched (fresh but consensus is not agreement) -------------------------------------

        [Fact]
        public void FreshDisagree_IsMismatched()
        {
            var s = Snapshot(60_000, ResolverConsensusEngine.PeerConsensusState.Disagree);
            Assert.Equal(ResolverContext.State.Mismatched, ResolverContext.Classify(s, NowMs));
        }

        [Fact]
        public void FreshInsufficient_IsMismatched()
        {
            var s = Snapshot(60_000, ResolverConsensusEngine.PeerConsensusState.Insufficient, reachable: 1);
            Assert.Equal(ResolverContext.State.Mismatched, ResolverContext.Classify(s, NowMs));
        }

        [Fact]
        public void FreshInconclusive_IsMismatched()
        {
            var s = Snapshot(60_000, ResolverConsensusEngine.PeerConsensusState.Inconclusive, reachable: 0);
            Assert.Equal(ResolverContext.State.Mismatched, ResolverContext.Classify(s, NowMs));
        }

        // -- stale ---------------------------------------------------------------------------------

        [Fact]
        public void OldAgree_IsStale()
        {
            var s = Snapshot(ResolverContext.StalenessMs + 1, ResolverConsensusEngine.PeerConsensusState.Agree);
            Assert.Equal(ResolverContext.State.Stale, ResolverContext.Classify(s, NowMs));
        }

        [Fact]
        public void OldDisagree_IsStale_NeverMismatched()
        {
            // Staleness dominates: an hour-old disagreement is worded stale, not mismatched.
            var s = Snapshot(ResolverContext.StalenessMs + 60_000, ResolverConsensusEngine.PeerConsensusState.Disagree);
            Assert.Equal(ResolverContext.State.Stale, ResolverContext.Classify(s, NowMs));
        }

        // -- the pinned boundary at exactly 3_600_000 ms -------------------------------------------

        [Fact]
        public void ExactlyStalenessMsOld_IsStale()
        {
            // PINNED: age >= StalenessMs is stale, so exactly 3_600_000 ms old is STALE.
            var s = Snapshot(ResolverContext.StalenessMs, ResolverConsensusEngine.PeerConsensusState.Agree);
            Assert.Equal(ResolverContext.State.Stale, ResolverContext.Classify(s, NowMs));
        }

        [Fact]
        public void OneMsInsideTheWindow_IsNotStale()
        {
            // age == StalenessMs - 1 is the last fresh millisecond.
            var s = Snapshot(ResolverContext.StalenessMs - 1, ResolverConsensusEngine.PeerConsensusState.Agree);
            Assert.Equal(ResolverContext.State.Confirmed, ResolverContext.Classify(s, NowMs));
        }

        [Fact]
        public void StalenessConstant_IsOneHour()
            => Assert.Equal(3_600_000, ResolverContext.StalenessMs);
    }
}
