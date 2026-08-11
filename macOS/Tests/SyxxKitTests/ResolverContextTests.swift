import XCTest
@testable import SyxxKit

/// UI-PRELUDE — coverage for the shared `ResolverContext` classifier that every platform UI uses
/// to read Prophet's stored last-run consensus. All four states plus the pinned staleness
/// boundary: a snapshot EXACTLY `stalenessMs` (3_600_000 ms) old is STALE; the fresh window is
/// `age < stalenessMs`. Byte-parity twin: Windows `Syxx.Tests/ResolverContextTests.cs`.
///
/// The clock is injected as `nowMs` — no test here sleeps or reads the wall clock.
final class ResolverContextTests: XCTestCase {

    /// Fixed "now" so every age below is exact.
    private let nowMs: Int64 = 1_700_000_000_000

    private func snapshot(ageMs: Int64,
                          consensus: ResolverConsensusEngine.PeerConsensusState,
                          reachable: Int = 3) -> ResolverConsensusSnapshot {
        ResolverConsensusSnapshot(
            domain: "example.com",
            timestamp: Date(timeIntervalSince1970: Double(nowMs - ageMs) / 1000.0),
            peerConsensus: consensus,
            reachableCount: reachable)
    }

    // MARK: - absent

    func testNilSnapshot_isAbsent() {
        XCTAssertEqual(ResolverContext.classify(nil, nowMs: nowMs), .absent)
    }

    // MARK: - confirmed

    func testFreshAgree_isConfirmed() {
        let s = snapshot(ageMs: 60_000, consensus: .agree)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .confirmed)
    }

    func testZeroAgeAgree_isConfirmed() {
        let s = snapshot(ageMs: 0, consensus: .agree)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .confirmed)
    }

    // MARK: - mismatched (fresh but consensus is not agreement)

    func testFreshDisagree_isMismatched() {
        let s = snapshot(ageMs: 60_000, consensus: .disagree)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .mismatched)
    }

    func testFreshInsufficient_isMismatched() {
        let s = snapshot(ageMs: 60_000, consensus: .insufficient, reachable: 1)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .mismatched)
    }

    func testFreshInconclusive_isMismatched() {
        let s = snapshot(ageMs: 60_000, consensus: .inconclusive, reachable: 0)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .mismatched)
    }

    // MARK: - stale

    func testOldAgree_isStale() {
        let s = snapshot(ageMs: ResolverContext.stalenessMs + 1, consensus: .agree)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .stale)
    }

    func testOldDisagree_isStale_neverMismatched() {
        // Staleness dominates: an hour-old disagreement is worded stale, not mismatched.
        let s = snapshot(ageMs: ResolverContext.stalenessMs + 60_000, consensus: .disagree)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .stale)
    }

    // MARK: - the pinned boundary at exactly 3_600_000 ms

    func testExactlyStalenessMsOld_isStale() {
        // PINNED: age >= stalenessMs is stale, so exactly 3_600_000 ms old is STALE.
        let s = snapshot(ageMs: ResolverContext.stalenessMs, consensus: .agree)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .stale)
    }

    func testOneMsInsideTheWindow_isNotStale() {
        // age == stalenessMs - 1 is the last fresh millisecond.
        let s = snapshot(ageMs: ResolverContext.stalenessMs - 1, consensus: .agree)
        XCTAssertEqual(ResolverContext.classify(s, nowMs: nowMs), .confirmed)
    }

    func testStalenessConstant_isOneHour() {
        XCTAssertEqual(ResolverContext.stalenessMs, 3_600_000)
    }
}
