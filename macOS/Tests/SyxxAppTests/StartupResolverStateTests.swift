import XCTest
import Foundation
@testable import SyxxApp
@testable import SyxxKit

/// D278 (App lane): the macOS app must never block first paint on the system-resolver
/// subprocess probes, and no surface may render a checking/unknown resolver as measured.
///
/// These tests drive `AppModel` through the `systemResolverProbe` seam — never the real Kit
/// probe, so the suite is hermetic (no scutil subprocess, no network). Every test carries an
/// explicit failure path: a mutation that reintroduces the D278 defect (inline synchronous
/// probes), the old `?? "8.8.8.8"` fallback, a gate removal, or an unconditional
/// "No DNS servers discovered" branch must turn its test red.
@MainActor
final class StartupResolverStateTests: XCTestCase {

    // MARK: - A1: checkStartup is non-blocking and publishes on landing

    func testCheckStartupReturnsImmediatelyAndPublishesMeasuredResolver() async {
        let model = AppModel()
        let gate = ProbeGate()
        model.systemResolverProbe = {
            await gate.wait()
            return .measured(
                dnsServers: ["192.168.1.1", "1.1.1.1"],
                encryptedStatus: EncryptedDNSStatus(state: .yes, evidence: "test fixture")
            )
        }
        model.server = ""   // D278: no invented resolver IP; only a measurement may set it

        // checkStartup must return without awaiting the probe. If the probes ran inline (the
        // D278 defect) this call would still be suspended on the gate and the test goes red.
        let returned = await returnsWithin(0.5) { model.checkStartup() }
        XCTAssertTrue(
            returned,
            "checkStartup must return without awaiting the system-resolver probe (D278 inline-probe regression)"
        )

        // The probe has NOT landed yet: the honest unknown must hold, and no cache may read
        // as a measured resolver.
        XCTAssertEqual(model.resolverState, .checking)
        XCTAssertTrue(model.cachedSystemDNSServers.isEmpty)
        XCTAssertNil(model.cachedEncryptedDNSStatus)

        gate.open()
        let landed = await waitUntil { model.resolverState != .checking }
        XCTAssertTrue(landed, "the detached probe must publish its result within the deadline")

        XCTAssertEqual(model.resolverState, .measured(["192.168.1.1", "1.1.1.1"]))
        XCTAssertEqual(model.cachedSystemDNSServers, ["192.168.1.1", "1.1.1.1"])
        XCTAssertEqual(model.cachedEncryptedDNSStatus?.state, .yes)
        // B229 default moved into the single writer: server takes the first measured resolver.
        XCTAssertEqual(model.server, "192.168.1.1")
        XCTAssertNotEqual(model.server, "8.8.8.8", "failure case: an invented 8.8.8.8 default must never resurface")
    }

    // MARK: - A2: empty / unavailable probes are honest .unavailable, never a measured resolver

    func testEmptyMeasuredProbeIsUnavailableAndKeepsServer() async {
        let model = AppModel()
        model.server = "9.9.9.9"   // a prior measured value that must be preserved
        model.systemResolverProbe = {
            .measured(dnsServers: [], encryptedStatus: EncryptedDNSStatus(state: .unknown, evidence: "fixture"))
        }

        model.checkStartup()
        let landed = await waitUntil { model.resolverState != .checking }
        XCTAssertTrue(landed, "the detached probe must publish its result within the deadline")

        // Failure path: applying an empty measurement as measured (writing .measured([]) or
        // deriving server from it) makes these assertions fail.
        XCTAssertEqual(model.resolverState, .unavailable)
        XCTAssertTrue(model.cachedSystemDNSServers.isEmpty)
        XCTAssertNil(model.cachedEncryptedDNSStatus)
        XCTAssertEqual(model.server, "9.9.9.9", "an empty measurement must never set server")
        XCTAssertNotEqual(model.server, "8.8.8.8")
    }

    func testUnavailableProbeIsUnavailableAndKeepsServer() async {
        let model = AppModel()
        model.server = "9.9.9.9"
        model.systemResolverProbe = { .unavailable }

        model.checkStartup()
        let landed = await waitUntil { model.resolverState != .checking }
        XCTAssertTrue(landed, "the detached probe must publish its result within the deadline")

        XCTAssertEqual(model.resolverState, .unavailable)
        XCTAssertTrue(model.cachedSystemDNSServers.isEmpty)
        XCTAssertNil(model.cachedEncryptedDNSStatus)
        XCTAssertEqual(model.server, "9.9.9.9", "a failed measurement must never set server")
        XCTAssertNotEqual(model.server, "8.8.8.8")
    }

    // MARK: - A3: resetAll preserves the measured resolver, never invents an IP

    func testResetAllKeepsMeasuredResolver() {
        let model = AppModel()
        model.resolverState = .measured(["192.168.1.1", "1.1.1.1"])
        model.cachedSystemDNSServers = ["192.168.1.1", "1.1.1.1"]
        model.server = "192.168.1.1"

        model.resetAll()

        XCTAssertEqual(model.server, "192.168.1.1")
        // Failure path: reintroducing the old `?? "8.8.8.8"` fallback in resetAll goes red.
        XCTAssertNotEqual(model.server, "8.8.8.8", "resetAll must never fall back to an invented 8.8.8.8")
    }

    func testResetAllKeepsCurrentServerWhenNothingMeasured() {
        let model = AppModel()
        model.resolverState = .unavailable
        model.cachedSystemDNSServers = []
        model.server = ""

        model.resetAll()

        XCTAssertEqual(model.server, "", "with no measurement, reset must keep the current value, never invent an IP")
        XCTAssertNotEqual(model.server, "8.8.8.8")
    }

    // MARK: - A4: runLookup gate blocks auditing an unmeasured resolver

    func testRunLookupGateBlocksWhileChecking() async {
        let model = AppModel()
        model.resolverState = .checking
        model.domain = "github.com"

        let result = await model.runLookup()

        // Hermetic: the gate returns before any DNSResolver/ProbeRunner call, so no network is
        // touched. Failure path: removing the gate means this exact approved string can never
        // be returned (runLookup would audit the empty `server` instead).
        XCTAssertEqual(
            result,
            "System resolver not yet measured. Prophet cannot audit an unknown resolver. Retry in a moment."
        )
        XCTAssertFalse(result.contains("via \(model.server)"), "an unmeasured resolver must never reach the output line")
    }

    func testRunLookupGateBlocksWhenUnavailable() async {
        let model = AppModel()
        model.resolverState = .unavailable
        model.domain = "github.com"

        let result = await model.runLookup()

        XCTAssertEqual(
            result,
            "System resolver could not be determined. Check your network connection, then retry."
        )
    }

    // MARK: - A5: PortalView Resolvers pane must not render checking as measured-empty

    func testPortalResolversPaneCheckingCopy() {
        // Failure path: an unconditional "No DNS servers discovered" branch (or any branch
        // that renders .checking as measured) fails these assertions.
        XCTAssertEqual(
            PortalView.resolversPaneEmptyCopy(state: .checking),
            "Checking system DNS servers…"
        )
        XCTAssertNotEqual(
            PortalView.resolversPaneEmptyCopy(state: .checking),
            "No DNS servers discovered",
            "a checking resolver must never render as a measured no-servers message"
        )
        XCTAssertEqual(
            PortalView.resolversPaneEmptyCopy(state: .unavailable),
            "No DNS servers discovered"
        )
        // Measured-empty: a measured list that came back empty still reads as none discovered.
        XCTAssertEqual(
            PortalView.resolversPaneEmptyCopy(state: .measured(["1.1.1.1"])),
            "No DNS servers discovered"
        )
    }

    // MARK: - Helpers

    /// Polls until `condition` is true or the deadline passes. The bounded loop (with a sleep
    /// that suspends the main actor) lets the detached probe's MainActor.run write-back land.
    private func waitUntil(
        timeout: TimeInterval = 2.0,
        _ condition: @escaping @MainActor () -> Bool
    ) async -> Bool {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if condition() { return true }
            try? await Task.sleep(nanoseconds: 5_000_000)
        }
        return condition()
    }

    /// Runs `body` on the main actor and returns whether it returned within `timeout`.
    /// Failure path: a checkStartup regression that awaits the probe inline (the D278 defect)
    /// keeps `body` suspended past the deadline, so this returns false instead of hanging the
    /// suite.
    private func returnsWithin(
        _ timeout: TimeInterval,
        _ body: @escaping @MainActor () -> Void
    ) async -> Bool {
        let flag = CompletionFlag()
        let task = Task { @MainActor in
            body()
            flag.markDone()
        }
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            if flag.isDone { return true }
            try? await Task.sleep(nanoseconds: 5_000_000)
        }
        task.cancel()
        return flag.isDone
    }
}

/// Continuation-backed gate: the injected seam awaits `wait()` until the test opens the gate,
/// so the probe provably has not landed while the immediate assertions run.
private final class ProbeGate: @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<Void, Never>?
    private var isOpen = false

    func wait() async {
        await withCheckedContinuation { (continuation: CheckedContinuation<Void, Never>) in
            lock.lock()
            if isOpen {
                lock.unlock()
                continuation.resume()
            } else {
                self.continuation = continuation
                lock.unlock()
            }
        }
    }

    func open() {
        lock.lock()
        isOpen = true
        let pending = continuation
        continuation = nil
        lock.unlock()
        pending?.resume()
    }
}

/// Lock-protected completion flag for `returnsWithin`.
private final class CompletionFlag: @unchecked Sendable {
    private let lock = NSLock()
    private var done = false

    var isDone: Bool {
        lock.lock(); defer { lock.unlock() }
        return done
    }

    func markDone() {
        lock.lock(); defer { lock.unlock() }
        done = true
    }
}
