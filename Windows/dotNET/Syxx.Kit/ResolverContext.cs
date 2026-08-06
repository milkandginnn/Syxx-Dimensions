using System;

namespace Syxx.Kit;

// UI-PRELUDE: the ONE shared classifier for how any UI reads Prophet's stored last-run consensus
// for a domain. Behavioural twin of macOS SyxxKit/ResolverContext.swift. Used by the Windows AND
// Linux UIs.
//
// Mirror's DNS line is a DEGRADATION FACTOR on its content conclusion, never a gate; Mirror never
// re-runs consensus. This classifier exists so the three platform UIs do not each invent their own
// staleness rule over <see cref="ResolverConsensusStore.LastRun"/>.
//
// HONESTY: Absent and Stale are Prophet-side failures to have fresh data — mild degradation, never
// asserted as a match and never collapsed into Confirmed. Only a fresh snapshot whose stored peer
// consensus actually agrees classifies as Confirmed.

public static class ResolverContext
{
    /// Staleness horizon in milliseconds (1 hour). THE single definition on this platform; the
    /// Swift twin (<c>ResolverContext.stalenessMs</c>) mirrors this value.
    public const long StalenessMs = 3_600_000;

    /// How a stored last-run consensus snapshot should degrade (or not) a content conclusion.
    public enum State
    {
        /// Fresh (&lt; 1 hour) and the stored peer consensus agrees: no degradation.
        Confirmed,
        /// A last run exists but is &gt;= 1 hour old: treat like a Prophet-side failure; mild
        /// degradation, worded stale. Never assert a match from a stale snapshot.
        Stale,
        /// No stored last-run for the domain: Prophet-side failure; mild degradation. Never
        /// assert a match.
        Absent,
        /// Fresh but the stored peer consensus disagrees, was insufficient, or was inconclusive:
        /// degrade toward caution.
        Mismatched,
    }

    /// <summary>
    /// Pure classification of a stored last-run snapshot at the injected wall clock
    /// <paramref name="nowMs"/> (Unix epoch milliseconds). The clock is injected — this method
    /// never reads it — so every state is directly testable.
    ///
    /// BOUNDARY (pinned, tested on both platforms): a snapshot EXACTLY <see cref="StalenessMs"/>
    /// old is Stale — the fresh window is age &lt; StalenessMs, i.e. [0, 3_600_000) ms.
    /// </summary>
    public static State Classify(ResolverConsensusSnapshot? snapshot, long nowMs)
    {
        if (snapshot is null) return State.Absent;
        long snapshotMs = snapshot.Timestamp.ToUnixTimeMilliseconds();
        long ageMs = nowMs - snapshotMs;
        if (ageMs >= StalenessMs) return State.Stale;
        return snapshot.PeerConsensus == ResolverConsensusEngine.PeerConsensusState.Agree
            ? State.Confirmed
            : State.Mismatched;
    }
}
