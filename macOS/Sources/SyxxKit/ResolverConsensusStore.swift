import Foundation

// Stream A (Warp/Mirror reimagining §3): the SHARED last-run store, keyed by domain, that both
// Prophet and Mirror read. Behavioural twin of Windows Syxx.Kit/ResolverConsensusStore.cs.
//
// Prophet's cross-resolver consensus and Mirror's local-vs-consensus comparison are now driven by
// the single ResolverConsensusEngine, which records its result + timestamp here after every run.
// Mirror can therefore read Prophet's LAST peer-consensus result (and how long ago it ran) instead
// of re-running consensus itself — the §3 "supplementary degradation factor, never a gate" model.
//
// HONESTY: the store never fabricates a result. A domain with no recorded run returns nil; "not
// measured" and "stale" are the caller's to interpret and are never collapsed into a clean match.
// The stored peerConsensus preserves the full tri-state (agree/disagree/insufficient/inconclusive)
// so a not-reachable or single-vantage run can never be read back as agreement.

/// One recorded consensus run for a domain. Carries the symmetric peer-consensus result (what
/// Prophet computes and Mirror reads), the reachable-resolver count, and — when the run also
/// evaluated the local-vs-consensus delta (the unified Mirror run) — the asymmetric result.
public struct ResolverConsensusSnapshot: Sendable, Equatable {
    public let domain: String
    public let timestamp: Date
    public let peerConsensus: ResolverConsensusEngine.PeerConsensusState
    public let reachableCount: Int
    public let localDelta: ResolverConsensusEngine.LocalDeltaState?

    public init(domain: String,
                timestamp: Date,
                peerConsensus: ResolverConsensusEngine.PeerConsensusState,
                reachableCount: Int,
                localDelta: ResolverConsensusEngine.LocalDeltaState? = nil) {
        self.domain = domain
        self.timestamp = timestamp
        self.peerConsensus = peerConsensus
        self.reachableCount = reachableCount
        self.localDelta = localDelta
    }
}

/// Process-wide, thread-safe store of the most recent `ResolverConsensusSnapshot` per domain. Keyed
/// case-insensitively (DNS names are case-insensitive). Deliberately an in-memory cache of the
/// current session's runs — NOT persisted to disk and NOT part of the signed evidence wire.
public enum ResolverConsensusStore {
    private static let lock = NSLock()
    nonisolated(unsafe) private static var byDomain: [String: ResolverConsensusSnapshot] = [:]

    /// Record (overwrite) the latest consensus snapshot for its domain.
    public static func record(_ snapshot: ResolverConsensusSnapshot) {
        lock.lock(); defer { lock.unlock() }
        byDomain[snapshot.domain.lowercased()] = snapshot
    }

    /// The most recent snapshot for `domain`, or nil if none has been recorded this session. Nil
    /// means "not measured" — never silently treat it as a clean match.
    public static func lastRun(_ domain: String) -> ResolverConsensusSnapshot? {
        lock.lock(); defer { lock.unlock() }
        return byDomain[domain.lowercased()]
    }

    /// Drop all recorded snapshots. Test seam only — keeps cases independent of run order.
    public static func clear() {
        lock.lock(); defer { lock.unlock() }
        byDomain.removeAll()
    }
}
