import Foundation
#if canImport(AppKit)
import AppKit
#endif

// A5 (ROADMAP "Implementation plan v2" — continuous monitoring; the subscription item). A driver
// that runs ContinuousMonitor.tickOnce on an interval and publishes the latest result.
//
// WHY THIS EXISTS AND IS SEPARATE FROM tickOnce: the 2026-07-16 audit found tickOnce had NO caller
// and NO test on either platform — the live I/O path of the subscription feature had never executed.
// tickOnce itself is untestable by construction (it reads real network config and touches disk), so
// the SCHEDULING logic — start/stop, interval, run-in-flight guarding, change latching — lives here,
// behind injected `tick` and `now` seams, and is tested without a network or a wall clock.
//
// The scheduler owns cadence and lifecycle; it makes no result of its own. The honesty guarantees
// are all in ContinuousMonitor: a first run is `.baselineEstablished`, never `.noChange`, so
// "nothing changed" is never reported before there was anything to compare against.
//
// D-5: tick is now async to support DNS queries inside ContinuousMonitor.tickOnce.

/// Drives periodic monitoring ticks. Not a view model — a Kit primitive the GUI (and, later, a
/// background agent) can host. `@MainActor` because it publishes state a SwiftUI surface observes.
@MainActor
public final class MonitorScheduler {

    public private(set) var isRunning = false
    public private(set) var lastTick: MonitorTick?
    /// The last tick that actually reported a CHANGE, latched so a surface can show "something
    /// changed at HH:MM" until the user acknowledges it — a later `.noChange` does not erase it.
    public private(set) var lastChange: MonitorTick?
    public private(set) var tickCount = 0
    // C111: track when ticks were attempted and completed, and how many were skipped (re-entrant
    // or cancelled). A surface showing "last checked" must distinguish "checked, nothing changed"
    // from "did not check".
    public private(set) var lastTickAttemptedAt: Date?
    public private(set) var lastTickCompletedAt: Date?
    public private(set) var skippedTickCount = 0

    private let interval: TimeInterval
    private let tick: @Sendable () async -> MonitorTick
    private var task: Task<Void, Never>?
    private var running = false                     // re-entrancy guard for a single tick
    private let onTick: (@MainActor (MonitorTick) -> Void)?
    // C111: observer token for NSWorkspace.didWakeNotification so we can remove it on stop/deinit.
    private var wakeObserver: NSObjectProtocol?

    /// C111: location of the persisted change latch. Quitting the app used to clear an
    /// unacknowledged change permanently — the latch is now saved to disk so a restart
    /// cannot erase it.
    static var latchURL: URL {
        let base = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".syxx", isDirectory: true)
        return base.appendingPathComponent("monitor-last-change.json")
    }

    /// - Parameters:
    ///   - interval: seconds between ticks.
    ///   - tick: the work. Defaults to the real `ContinuousMonitor.tickOnce`; a test injects a fake.
    ///   - onTick: called on the main actor after each tick (e.g. to raise a notification on change).
    public init(interval: TimeInterval = 900,
                tick: @escaping @Sendable () async -> MonitorTick = { await ContinuousMonitor.tickOnce() },
                onTick: (@MainActor (MonitorTick) -> Void)? = nil) {
        self.interval = interval
        self.tick = tick
        self.onTick = onTick
        // C111: restore the persisted change latch so a restart cannot erase an unacknowledged
        // change. The tick is already rolled into the new baseline, so without this the next
        // run reports .noChange and the change is unrecoverable.
        self.lastChange = MonitorScheduler.loadLatchedChange()
    }

    /// Run one tick now, synchronously on the main actor. Safe to call directly (the "Check now"
    /// button) or from the loop. Guarded so two ticks never overlap and stomp the snapshot file.
    @discardableResult
    public func runOnce() async -> MonitorTick? {
        lastTickAttemptedAt = Date()
        guard !running else {
            // C111: a skipped tick leaves a trace so a surface can tell "checked, nothing
            // changed" from "did not check".
            skippedTickCount += 1
            return nil
        }
        running = true
        defer { running = false }

        let result = await tick()
        // `tick()` is an await point: `stop()` can land while it is in flight, having already
        // passed the loop's `Task.isCancelled` check. Committing here anyway meant one further
        // tick was counted, latched, and delivered to `onTick` AFTER the user stopped monitoring
        // — the app kept reporting from a round the user had ended. Discard a result that
        // finished after cancellation; the query is already spent, but it must not be surfaced.
        if Task.isCancelled { return nil }
        tickCount += 1
        lastTick = result
        lastTickCompletedAt = Date()
        if result.state == .changed {
            lastChange = result   // latch; never cleared by a later noChange
            // C111: persist the latch so a restart cannot erase an unacknowledged change.
            MonitorScheduler.saveLatchedChange(result)
        }
        onTick?(result)
        return result
    }

    /// Begin the interval loop. Runs one tick immediately (so the user sees a baseline at once),
    /// then every `interval` seconds until `stop()`. Idempotent — a second `start` is a no-op.
    public func start() {
        guard !isRunning else { return }
        isRunning = true
        // C111: observe sleep/wake so a catch-up tick runs when the laptop wakes — the moment
        // it is most likely to have joined a different network.
        #if canImport(AppKit)
        wakeObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didWakeNotification, object: nil, queue: .main
        ) { [weak self] _ in
            Task { @MainActor [weak self] in
                guard let self, self.isRunning else { return }
                _ = await self.runOnce()
            }
        }
        #endif
        task = Task { [weak self] in
            guard let self else { return }
            // First tick immediately so the user sees a baseline right away.
            _ = await self.runOnce()
            while !Task.isCancelled {
                // interval is seconds; Task.sleep wants nanoseconds. A cancelled sleep throws, which
                // ends the loop cleanly.
                let ns = UInt64(self.intervalSeconds * 1_000_000_000)
                do { try await Task.sleep(nanoseconds: ns) } catch { break }
                if Task.isCancelled { break }
                _ = await self.runOnce()
            }
        }
    }

    public func stop() {
        isRunning = false
        task?.cancel()
        task = nil
        // C111: remove the wake observer.
        #if canImport(AppKit)
        if let observer = wakeObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(observer)
            wakeObserver = nil
        }
        #endif
    }

    /// Clear the latched change once the user has seen it.
    public func acknowledgeChange() {
        lastChange = nil
        MonitorScheduler.clearLatchedChange()
    }

    private var intervalSeconds: TimeInterval { interval }

    // MARK: - C111: latch persistence

    /// Encode/decode wrapper for the latched change tick. Uses JSONEncoder/Decoder with
    /// a Codable shim since MonitorTick is not Codable (it carries ConfigChange which is).
    private struct LatchPayload: Codable {
        let state: String
        let summary: String
        let capturedAt: Date
    }

    private static func saveLatchedChange(_ tick: MonitorTick) {
        let payload = LatchPayload(state: tick.state.rawValue, summary: tick.summary, capturedAt: tick.capturedAt)
        guard let data = try? JSONEncoder().encode(payload) else { return }
        try? FileManager.default.createDirectory(at: latchURL.deletingLastPathComponent(),
                                                withIntermediateDirectories: true)
        try? data.write(to: latchURL, options: .atomic)
        try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: latchURL.path)
    }

    private static func loadLatchedChange() -> MonitorTick? {
        guard let data = try? Data(contentsOf: latchURL),
              let payload = try? JSONDecoder().decode(LatchPayload.self, from: data),
              let state = MonitorState(rawValue: payload.state) else { return nil }
        return MonitorTick(state: state, changes: [], alerts: [],
                           summary: payload.summary, recheckRecommended: true,
                           capturedAt: payload.capturedAt)
    }

    private static func clearLatchedChange() {
        try? FileManager.default.removeItem(at: latchURL)
    }
}
