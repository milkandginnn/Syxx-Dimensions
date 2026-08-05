import Foundation

// UI-PRELUDE: the ONE shared classifier for how any UI reads Prophet's stored last-run consensus
// for a domain. Behavioural twin of Windows/Linux Syxx.Kit/ResolverContext.cs.
//
// Mirror's DNS line is a DEGRADATION FACTOR on its content conclusion, never a gate; Mirror never
// re-runs consensus. This classifier exists so the three platform UIs do not each invent their own
// staleness rule over `ResolverConsensusStore.lastRun(_:)`.
//
// HONESTY: `absent` and `stale` are Prophet-side failures to have fresh data — mild degradation,
// never asserted as a match and never collapsed into `confirmed`. Only a fresh snapshot whose
// stored peer consensus actually agrees classifies as `confirmed`.

public enum ResolverContext {

    /// Staleness horizon in milliseconds (1 hour). THE single definition on this platform; the
    /// C# twin (`ResolverContext.StalenessMs`) mirrors this value.
    public static let stalenessMs: Int64 = 3_600_000

    /// How a stored last-run consensus snapshot should degrade (or not) a content conclusion.
    public enum State: String, Sendable, Equatable, Codable {
        /// Fresh (< 1 hour) and the stored peer consensus agrees: no degradation.
        case confirmed
        /// A last run exists but is >= 1 hour old: treat like a Prophet-side failure; mild
        /// degradation, worded stale. Never assert a match from a stale snapshot.
        case stale
        /// No stored last-run for the domain: Prophet-side failure; mild degradation. Never
        /// assert a match.
        case absent
        /// Fresh but the stored peer consensus disagrees, was insufficient, or was inconclusive:
        /// degrade toward caution.
        case mismatched
    }

    /// Pure classification of a stored last-run snapshot at the injected wall clock `nowMs`
    /// (Unix epoch milliseconds). The clock is injected — this function never reads it — so
    /// every state is directly testable.
    ///
    /// BOUNDARY (pinned, tested on both platforms): a snapshot EXACTLY `stalenessMs` old is
    /// `stale` — the fresh window is `age < stalenessMs`, i.e. [0, 3_600_000) ms.
    public static func classify(_ snapshot: ResolverConsensusSnapshot?, nowMs: Int64) -> State {
        guard let snapshot else { return .absent }
        let snapshotMs = Int64((snapshot.timestamp.timeIntervalSince1970 * 1000.0).rounded())
        let ageMs = nowMs - snapshotMs
        if ageMs >= stalenessMs { return .stale }
        switch snapshot.peerConsensus {
        case .agree:
            return .confirmed
        case .disagree, .insufficient, .inconclusive:
            return .mismatched
        }
    }
}
