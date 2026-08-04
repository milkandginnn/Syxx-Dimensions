import XCTest
@testable import SyxxKit

private func makeTick(_ state: MonitorState) -> MonitorTick {
    MonitorTick(state: state, changes: [], alerts: [], summary: "\(state)", recheckRecommended: state == .changed)
}

/// A Sendable queue of states the injected @Sendable tick can drain. A plain captured `var` is
/// (correctly) rejected as a data race; this is the minimal thread-safe stand-in.
private final class StateQueue: @unchecked Sendable {
    private let lock = NSLock()
    private var states: [MonitorState]
    init(_ states: [MonitorState]) { self.states = states }
    func next() -> MonitorState {
        lock.lock(); defer { lock.unlock() }
        return states.isEmpty ? .noChange : states.removeFirst()
    }
}

/// Thread-safe counter + parking gate used by B256 deterministic start/stop tests.
private final class TickGate: @unchecked Sendable {
    private let lock = NSLock()
    private var entries = 0
    private var entriesAfterStop = 0
    private var stopped = false
    private var releaseSecond = false
    private var secondEntered = false
    private let secondEnteredSem = DispatchSemaphore(value: 0)

    @discardableResult
    func enter() -> Int {
        lock.lock()
        entries += 1
        let n = entries
        let afterStop = stopped
        lock.unlock()
        if afterStop {
            lock.lock(); entriesAfterStop += 1; lock.unlock()
        }
        if n == 2 {
            lock.lock(); secondEntered = true; lock.unlock()
            secondEnteredSem.signal()
            // Park until the test releases us (Stop was called while this tick is in flight).
            while true {
                lock.lock()
                let go = releaseSecond
                lock.unlock()
                if go { break }
                Thread.sleep(forTimeInterval: 0.005)
            }
        }
        return n
    }

    func waitForSecondEntry(timeout: TimeInterval = 30) -> Bool {
        secondEnteredSem.wait(timeout: .now() + timeout) == .success
    }

    func markStopped() {
        lock.lock(); stopped = true; lock.unlock()
    }

    func releaseParked() {
        lock.lock(); releaseSecond = true; lock.unlock()
    }

    var entryCount: Int { lock.lock(); defer { lock.unlock() }; return entries }
    var afterStopCount: Int { lock.lock(); defer { lock.unlock() }; return entriesAfterStop }
}

/// A5 scheduler tests. tickOnce (real network + disk) is untestable by construction, so the SCHEDULING
/// logic lives behind an injected tick and is proven here — the live path the 2026-07-16 audit found
/// had never executed. Injected ticks let us drive start/stop, the re-entrancy guard, and the change
/// latch deterministically.
///
/// B256 / Phase 4.1: start/stop must not depend on wall-clock sleeps (the old form used 20ms/90ms/60ms
/// Task.sleep and flaked under load). The injected tick is the clock: it parks so Stop lands at a
/// known in-flight moment, and exact due-monitor counts are asserted.
///
/// D-5: tick closure is now async (to support DNS queries in tickOnce); tests await runOnce().
@MainActor
final class MonitorSchedulerTests: XCTestCase {

    func testRunOnceRecordsTheResult() async {
        let s = MonitorScheduler(interval: 999, tick: { makeTick(.baselineEstablished) })
        let r = await s.runOnce()
        XCTAssertEqual(r?.state, .baselineEstablished)
        XCTAssertEqual(s.lastTick?.state, .baselineEstablished)
        XCTAssertEqual(s.tickCount, 1)
    }

    func testAChangeIsLatchedAndNotErasedByALaterNoChange() async {
        // The property that makes the feature trustworthy: "your DNS changed at 3pm" must survive the
        // next quiet tick. If a .noChange cleared lastChange, the user could miss the one event that
        // mattered simply by not looking within one interval.
        let q = StateQueue([.baselineEstablished, .changed, .noChange])
        let s = MonitorScheduler(interval: 999, tick: { makeTick(q.next()) })
        await s.runOnce()  // baseline
        await s.runOnce()  // changed
        XCTAssertEqual(s.lastChange?.state, .changed)
        await s.runOnce()  // noChange
        XCTAssertEqual(s.lastChange?.state, .changed, "a later noChange must NOT erase the latched change")
        s.acknowledgeChange()
        XCTAssertNil(s.lastChange, "acknowledge clears it")
    }

    func testOnTickFiresPerTickForNotifications() async {
        // onTick is how a surface raises a notification. It must fire once per tick, with the result.
        var seen: [MonitorState] = []
        let s = MonitorScheduler(interval: 999, tick: { makeTick(.changed) },
                                 onTick: { seen.append($0.state) })
        await s.runOnce(); await s.runOnce()
        XCTAssertEqual(seen, [.changed, .changed])
    }

    /// B256: exact due-monitor count via injected tick parking. No Task.sleep(90ms).
    /// Failure case: if Stop does not halt the loop, entriesAfterStop > 0 or tickCount drifts.
    func testStartRunsImmediatelyThenStopHalts() async {
        let gate = TickGate()
        // interval is short so the loop's second tick arrives quickly; the park makes timing exact.
        let s = MonitorScheduler(interval: 0.02, tick: {
            _ = gate.enter()
            return makeTick(.noChange)
        })

        s.start()
        // Await the loop's first interval tick (call 1 is start's immediate tick inside the Task).
        // The gate parks on entry 2 so Stop is called with that tick provably in flight.
        XCTAssertTrue(gate.waitForSecondEntry(timeout: 30),
                      "loop must enter a second tick; start() did not schedule the interval loop")
        XCTAssertTrue(s.isRunning)

        s.stop()
        gate.markStopped()
        XCTAssertFalse(s.isRunning)

        gate.releaseParked()
        // Wait until the parked tick is counted (must happen; fail only on timeout).
        let deadline = Date().addingTimeInterval(30)
        while s.tickCount < 2 && Date() < deadline {
            try? await Task.sleep(nanoseconds: 5_000_000)
        }

        let afterStop = s.tickCount
        // Exact due-monitor contract: immediate tick + one loop tick that was in flight = 2.
        // (A broken Stop that lets more interval ticks land would push this above 2 after the window.)
        XCTAssertEqual(afterStop, 2,
                       "B256: exact due-monitor count is 2 (immediate + one in-flight loop tick), got \(afterStop)")

        // Nothing more may enter the tick after stop. Window spans many intervals (0.02s * 25).
        let windowEnd = Date().addingTimeInterval(0.5)
        while Date() < windowEnd {
            XCTAssertEqual(gate.afterStopCount, 0,
                           "B256: loop must not enter tick after Stop; got \(gate.afterStopCount) post-stop entries")
            try? await Task.sleep(nanoseconds: 10_000_000)
        }
        XCTAssertEqual(gate.afterStopCount, 0)
        XCTAssertEqual(s.tickCount, afterStop, "no ticks after stop()")
    }

    /// B256 unit: RunOnce-only path yields exact tick counts without any wall clock.
    /// Failure case: break TickCount increment → assertion red.
    func testInjectedTicksYieldExactDueCount() async {
        let s = MonitorScheduler(interval: 999, tick: { makeTick(.noChange) })
        XCTAssertEqual(s.tickCount, 0)
        _ = await s.runOnce()
        _ = await s.runOnce()
        _ = await s.runOnce()
        XCTAssertEqual(s.tickCount, 3, "exact due-monitor count after three RunOnce calls")
    }
}
