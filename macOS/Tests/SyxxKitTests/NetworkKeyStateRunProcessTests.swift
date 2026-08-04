import XCTest
import Darwin
@testable import SyxxKit

/// D278 regression suite for `NetworkKeyState.runProcess(_:_:timeout:)`.
///
/// The shipped implementation waited on a semaphore that only a cancellable `DispatchWorkItem`
/// could signal; the fast path cancelled the work item and then waited on the semaphore
/// unconditionally, so any subprocess that exited before the deadline deadlocked the calling
/// thread (measured in the app launch path with 3379 of 4144 main-thread samples in
/// `semaphore_wait_trap`). The comment claimed the main thread "skips done.wait() entirely";
/// no such skip existed.
///
/// Every test invokes `runProcess` on a background thread and bounds the test itself with an
/// `XCTWaiter` timeout, so a regression fails the test instead of hanging the runner. Every
/// test names the mutation that must turn it red.
final class NetworkKeyStateRunProcessTests: XCTestCase {

    /// Pid files written by child shells, removed in tearDown.
    private var pidFiles: [String] = []

    override func tearDown() {
        for path in pidFiles {
            try? FileManager.default.removeItem(atPath: path)
        }
        pidFiles.removeAll()
        super.tearDown()
    }

    private func newPIDFile(named base: String) -> String {
        let path = FileManager.default.temporaryDirectory
            .appendingPathComponent("\(base)-\(UUID().uuidString).pid").path
        pidFiles.append(path)
        return path
    }

    private func readPID(_ path: String) -> pid_t? {
        guard let text = try? String(contentsOfFile: path, encoding: .utf8),
              let pid = pid_t(text.trimmingCharacters(in: .whitespacesAndNewlines)) else {
            return nil
        }
        return pid
    }

    // MARK: - T1: fast path

    /// A subprocess that exits before the deadline must return its output immediately, never
    /// burn the full timeout and never deadlock the caller.
    ///
    /// Happy path: `/bin/echo hello` returns `"hello\n"` well inside the 1.0 s waiter bound,
    /// even though `runProcess` is given a 3.0 s timeout.
    ///
    /// Failure path (the D278 mutation): delete `process.terminationHandler = { _ in
    /// state.exited.signal() }`. `exited` never signals on the fast path, the bounded wait burns
    /// the full 3.0 s, the result becomes nil, and the 1.0 s waiter times out. Red.
    func testFastPathReturnsOutputWithoutBurningTimeout() {
        let exp = expectation(description: "runProcess(/bin/echo) returns before the 3s timeout")
        let started = Date()
        var result: String? = nil
        DispatchQueue.global(qos: .userInitiated).async {
            result = NetworkKeyState.runProcess("/bin/echo", ["hello"], timeout: 3.0)
            exp.fulfill()
        }
        let outcome = XCTWaiter.wait(for: [exp], timeout: 1.0)
        let elapsed = Date().timeIntervalSince(started)
        XCTAssertEqual(outcome, .completed,
            "fast process must return within 1.0 s, not burn the 3 s timeout (elapsed \(elapsed) s)")
        XCTAssertEqual(result, "hello\n")
        XCTAssertLessThan(elapsed, 1.0, "returned in \(elapsed) s")
    }

    // MARK: - T2: timeout path reaps the child

    /// A subprocess that outlives the deadline must surface as nil at the deadline and its child
    /// must be reaped, so no zombie or survivor remains.
    ///
    /// Happy path: `sleep 10` with a 0.5 s timeout returns nil near the deadline, and
    /// `kill(pid, 0)` fails (ESRCH) for the recorded child pid.
    ///
    /// Failure path (mutation): delete the `.timedOut` kill sequence (both SIGTERM and SIGKILL).
    /// The caller then blocks in `waitUntilExit` until the sleep finishes naturally (~10 s), the
    /// waiter times out at 3.0 s, `result` is never delivered, and `kill(pid, 0)` succeeds while
    /// the child is still alive. Red.
    func testTimeoutReturnsNilAndReapsChild() {
        let pidPath = newPIDFile(named: "syxx-t2")
        let exp = expectation(description: "runProcess returns at the 0.5s deadline")
        var result: String? = "unset"
        let started = Date()
        DispatchQueue.global(qos: .userInitiated).async {
            result = NetworkKeyState.runProcess(
                "/bin/sh", ["-c", "echo $$ > \"\(pidPath)\"; exec sleep 10"], timeout: 0.5)
            exp.fulfill()
        }
        let outcome = XCTWaiter.wait(for: [exp], timeout: 3.0)
        let elapsed = Date().timeIntervalSince(started)
        XCTAssertEqual(outcome, .completed, "runProcess must return at the deadline, not block 10 s")
        XCTAssertNil(result, "timeout must surface as nil, got \(String(describing: result))")
        XCTAssertLessThan(elapsed, 2.5, "must return near the 0.5 s deadline (elapsed \(elapsed) s)")
        guard let pid = readPID(pidPath) else {
            return XCTFail("child did not write its pid file at \(pidPath)")
        }
        XCTAssertNotEqual(kill(pid, 0), 0, "child \(pid) must be reaped after the timeout")
    }

    // MARK: - T3: SIGKILL escalation against a SIGTERM-ignoring child

    /// A child that traps/ignores SIGTERM must still die: SIGTERM first, 0.5 s grace, then
    /// SIGKILL.
    ///
    /// Happy path: `trap '' TERM; ...; while :; do sleep 1; done` with a 0.5 s timeout returns
    /// nil and the recorded child pid no longer exists.
    ///
    /// Failure path (mutation): keep SIGTERM, delete the SIGKILL escalation. The trap-ignoring
    /// child survives the SIGTERM, `waitUntilExit` never returns, the waiter times out at 3.0 s,
    /// and `kill(pid, 0)` succeeds for the live survivor. Red.
    func testSIGKILLEscalationKillsTrapIgnoringChild() {
        let pidPath = newPIDFile(named: "syxx-t3")
        let exp = expectation(description: "runProcess escalates to SIGKILL for a TERM-ignoring child")
        var result: String? = "unset"
        DispatchQueue.global(qos: .userInitiated).async {
            result = NetworkKeyState.runProcess(
                "/bin/sh",
                ["-c", "trap '' TERM; echo $$ > \"\(pidPath)\"; while :; do sleep 1; done"],
                timeout: 0.5)
            exp.fulfill()
        }
        let outcome = XCTWaiter.wait(for: [exp], timeout: 3.0)
        XCTAssertEqual(outcome, .completed, "runProcess must return after SIGKILL escalation")
        XCTAssertNil(result, "timeout must surface as nil, got \(String(describing: result))")
        guard let pid = readPID(pidPath) else {
            return XCTFail("child did not write its pid file at \(pidPath)")
        }
        XCTAssertNotEqual(kill(pid, 0), 0, "SIGTERM-ignoring child \(pid) must be SIGKILLed and reaped")
    }

    // MARK: - T4: chatty child (>64 KB) proves the concurrent drain

    /// A child that writes far more than the 64 KB OS pipe buffer must not deadlock: the drain
    /// thread reads stdout concurrently with the wait.
    ///
    /// Happy path: ~3000 lines (~260 KB) with a 5.0 s timeout returns the complete output, with
    /// the first line `line-0-...` and the last line `line-2999-...` intact.
    ///
    /// Failure path (mutation): delete the drain `DispatchQueue.global(qos: .userInitiated).async`
    /// block. Nothing reads the pipe, the child blocks in write once the 64 KB buffer fills, the
    /// bounded exit wait times out at 5.0 s, the kill sequence runs, the defensive `drained` wait
    /// times out, and runProcess returns nil. Red.
    func testChattyChildDoesNotDeadlockOnPipeBuffer() {
        let exp = expectation(description: "runProcess drains >64KB without pipe deadlock")
        var result: String? = nil
        DispatchQueue.global(qos: .userInitiated).async {
            let command = "i=0; while [ $i -lt 3000 ]; do echo \"line-$i-01234567890123456789012345678901234567890123456789012345678901234567890123\"; i=$((i+1)); done"
            result = NetworkKeyState.runProcess("/bin/sh", ["-c", command], timeout: 5.0)
            exp.fulfill()
        }
        let outcome = XCTWaiter.wait(for: [exp], timeout: 8.0)
        XCTAssertEqual(outcome, .completed, "chatty child must be drained, not deadlocked")
        guard let output = result else {
            return XCTFail("chatty child output must be non-nil")
        }
        let lines = output.split(separator: "\n")
        XCTAssertGreaterThanOrEqual(lines.count, 2000, "got \(lines.count) lines")
        XCTAssertEqual(lines.first.map(String.init), "line-0-01234567890123456789012345678901234567890123456789012345678901234567890123")
        XCTAssertEqual(lines.last.map(String.init), "line-2999-01234567890123456789012345678901234567890123456789012345678901234567890123")
    }

    // MARK: - T5: launch failure

    /// A nonexistent executable must surface as nil (the `run()` throw path), never as an empty
    /// string that a caller could mistake for a completed measurement.
    ///
    /// Happy path: `/nonexistent/syxx-no-such-binary` returns nil.
    ///
    /// Failure path (mutation): change the `catch` to `return ""`. The nil assertion turns red.
    func testLaunchFailureReturnsNil() {
        let exp = expectation(description: "runProcess of a nonexistent binary returns nil")
        var result: String? = "unset"
        DispatchQueue.global(qos: .userInitiated).async {
            result = NetworkKeyState.runProcess("/nonexistent/syxx-no-such-binary", [], timeout: 3.0)
            exp.fulfill()
        }
        let outcome = XCTWaiter.wait(for: [exp], timeout: 5.0)
        XCTAssertEqual(outcome, .completed)
        XCTAssertNil(result, "launch failure must surface as nil, got \(String(describing: result))")
    }

    // MARK: - T6: non-UTF-8 output

    /// Output that is not valid UTF-8 must surface as nil, never as a lossily decoded string
    /// whose bytes a caller would misread.
    ///
    /// Happy path: `printf '\377\376'` (bytes FF FE) returns nil.
    ///
    /// Failure path (mutation): replace `String(data:encoding:.utf8)` with a lossy decode such
    /// as `String(decoding:as: UTF8.self)`. Invalid bytes become U+FFFD replacement characters
    /// and the nil assertion turns red.
    func testNonUTF8OutputReturnsNil() {
        let exp = expectation(description: "runProcess of non-UTF-8 output returns nil")
        var result: String? = "unset"
        DispatchQueue.global(qos: .userInitiated).async {
            result = NetworkKeyState.runProcess("/bin/sh", ["-c", "printf '\\377\\376'"], timeout: 3.0)
            exp.fulfill()
        }
        let outcome = XCTWaiter.wait(for: [exp], timeout: 5.0)
        XCTAssertEqual(outcome, .completed)
        XCTAssertNil(result, "non-UTF-8 output must surface as nil, got \(String(describing: result))")
    }
}
