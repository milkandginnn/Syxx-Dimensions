using System;
using System.Collections.Concurrent;

namespace Syxx.Kit;

// Stream A (Warp/Mirror reimagining §3): the SHARED last-run store, keyed by domain, that both
// Prophet and Mirror read. Behavioural twin of macOS SyxxKit/ResolverConsensusStore.swift.
//
// Prophet's cross-resolver consensus and Mirror's local-vs-consensus comparison used to be two
// independent fan-outs over two independent rosters. They are now driven by the single
// ResolverConsensusEngine, which records its result + timestamp here after every run. Mirror can
// therefore read Prophet's LAST peer-consensus result (and how long ago it ran) instead of
// re-running consensus itself — the §3 "supplementary degradation factor, never a gate" model.
//
// HONESTY: the store never fabricates a result. A domain with no recorded run returns null (C#) /
// nil (Swift); "not measured" and "stale" are the caller's to interpret, and are never collapsed
// into a clean match. The stored PeerConsensus preserves the full tri-state
// (agree/disagree/insufficient/inconclusive) so a not-reachable or single-vantage run can never be
// read back as agreement.

/// One recorded consensus run for a domain. Carries the symmetric peer-consensus result (what
/// Prophet computes and Mirror reads), the reachable-resolver count, and — when the run also
/// evaluated the local-vs-consensus delta (the unified Mirror run) — the asymmetric result.
public sealed record ResolverConsensusSnapshot(
    string Domain,
    DateTimeOffset Timestamp,
    ResolverConsensusEngine.PeerConsensusState PeerConsensus,
    int ReachableCount,
    ResolverConsensusEngine.LocalDeltaState? LocalDelta);

/// <summary>
/// Process-wide, thread-safe store of the most recent <see cref="ResolverConsensusSnapshot"/> per
/// domain. Keyed case-insensitively (DNS names are case-insensitive). This is deliberately an
/// in-memory cache of the current session's runs — it is NOT persisted to disk and NOT part of the
/// signed evidence wire.
/// </summary>
public static class ResolverConsensusStore
{
    private static readonly ConcurrentDictionary<string, ResolverConsensusSnapshot> ByDomain =
        new(StringComparer.OrdinalIgnoreCase);

    /// Record (overwrite) the latest consensus snapshot for its domain.
    public static void Record(ResolverConsensusSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        ByDomain[snapshot.Domain] = snapshot;
    }

    /// The most recent snapshot for <paramref name="domain"/>, or null if none has been recorded
    /// this session. Null means "not measured" — never silently treat it as a clean match.
    public static ResolverConsensusSnapshot? LastRun(string domain)
        => domain is not null && ByDomain.TryGetValue(domain, out var s) ? s : null;

    /// Drop all recorded snapshots. Test seam only — keeps cases independent of run order.
    public static void Clear() => ByDomain.Clear();
}
