import Foundation
import SyxxKit
import Sentry

// MARK: - Scan dispatch, timeout, and warden orchestration

extension AppModel {

    // B235: typed outcome for `execute()`. The old `String` return conflated three distinct
    // states — real text, "tool has no runnable implementation", and "execution produced no
    // output" — and the empty-string branch in `run()` fabricated a timeout + a duration.
    // `.notRunnable` is now distinct from `.text("")`, so the "timed out" message only fires
    // on a real timeout.
    enum ExecOutcome: Sendable {
        case text(String)
        case notRunnable
    }

    /// Runs all six wardens in sequence. Each tool is run with its normal timeout.
    /// Results populate the per-warden published properties so the Dashboard can reflect state.
    /// B236: (1) Track which disciplines actually ran and emit an honest completion message.
    /// (2) Route each scan through withTimeout to prevent a hung probe from leaving runningTool
    /// set forever (same guard run() uses, per D068). (3) Clear runningTool in defer so all
    /// exit paths — cancel, timeout, error — leave the UI recoverable.
    func runAllWardens() {
        guard runningTool == nil else { return }
        let corrId = UUID().uuidString
        scanCorrelationId = corrId
        runningTool = .dashboard
        output = "Conducting all scans…"
        SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.state"); b.message = "All wardens scan started"; return b }())
        runTask = Task {
            var ran: [String] = []
            var skipped: [String] = []
            defer {
                // B236: emit an honest completion message listing what actually ran.
                let proSkipped = skipped.isEmpty ? "" : " Skipped: \(skipped.joined(separator: ", "))."
                self.selection = .dashboard
                self.output = "Completed: \(ran.joined(separator: ", ")).\(proSkipped)"
                SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.state"); b.message = "Wardens scan complete: \(ran.joined(separator: ","))"; return b }())
                self.runningTool = nil
                self.scanCorrelationId = nil
            }
            // Prophet
            selection = .prophet
            output = "Running Prophet…"
            let prophetOutcome = await Self.withTimeout(seconds: Self.timeout(for: .prophet)) { [weak self] in
                await self?.runLookup() ?? ""
            }
            guard !Task.isCancelled else { return }
            ran.append("Prophet")
            if prophetOutcome == nil { self.output = "Prophet timed out." }
            // Mirror (Pro only)
            if isPro {
                selection = .mirror
                output = "Running Mirror…"
                let mirrorOutcome = await Self.withTimeout(seconds: Self.timeout(for: .mirror)) { [weak self] in
                    await self?.runCompare() ?? ""
                }
                guard !Task.isCancelled else { return }
                ran.append("Mirror")
                if mirrorOutcome == nil { self.output = "Mirror timed out." }
            } else {
                skipped.append("Mirror")
            }
            // Portal
            selection = .portal
            output = "Running Portal…"
            let portalOutcome = await Self.withTimeout(seconds: Self.timeout(for: .portal)) { [weak self] in
                await self?.runNetworkState() ?? ""
            }
            guard !Task.isCancelled else { return }
            ran.append("Portal")
            if portalOutcome == nil { self.output = "Portal timed out." }
            // Glutton (Pro only)
            if isPro {
                selection = .glutton
                output = "Running Glutton…"
                let gluttonOutcome = await Self.withTimeout(seconds: Self.timeout(for: .glutton)) { [weak self] in
                    await self?.runLeak() ?? ""
                }
                guard !Task.isCancelled else { return }
                ran.append("Glutton")
                if gluttonOutcome == nil { self.output = "Glutton timed out." }
            } else {
                skipped.append("Glutton")
            }
            // Witness (Pro only)
            if isPro {
                selection = .witness
                output = "Running Witness…"
                let witnessOutcome = await Self.withTimeout(seconds: Self.timeout(for: .witness)) { [weak self] in
                    await self?.runWitness() ?? ""
                }
                guard !Task.isCancelled else { return }
                ran.append("Witness")
                if witnessOutcome == nil { self.output = "Witness timed out." }
            } else {
                skipped.append("Witness")
            }
            // Cage is intentionally not included — it requires a separate privileged capture flow.
            skipped.append("Cage")
        }
    }

    /// Flushes the system DNS cache (macOS: `dscacheutil -flushcache`).
    /// B238: (a) Read terminationStatus — report failure on non-zero. (b) Message reflects what
    /// was actually done: dscacheutil flushes the directory-service cache but not the
    /// mDNSResponder cache (which additionally requires `sudo killall -HUP mDNSResponder`).
    /// (c) Run off the main actor via Task.detached so the UI thread is not blocked.
    func flushDNSCache() {
        output = "Flushing DNS cache…"
        Task.detached { [weak self] in
            let proc = Process()
            proc.executableURL = URL(fileURLWithPath: "/usr/bin/dscacheutil")
            proc.arguments = ["-flushcache"]
            let pipe = Pipe()
            proc.standardOutput = pipe
            proc.standardError = pipe
            let message: String
            do {
                try proc.run()
                proc.waitUntilExit()
                if proc.terminationStatus == 0 {
                    message = "Directory-service DNS cache flushed. The system resolver cache (mDNSResponder) may still hold entries."
                } else {
                    let stderr = String(data: pipe.fileHandleForReading.readDataToEndOfFile(), encoding: .utf8)?
                        .trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                    message = "Flush command failed (exit \(proc.terminationStatus)).\(stderr.isEmpty ? "" : " \(stderr)")"
                }
            } catch {
                let detail = ErrorPresenter.userDetail(for: error, component: "dns.flush")
                message = detail.map { "Could not flush DNS cache: \($0)" } ?? "Could not flush DNS cache."
            }
            await MainActor.run { self?.output = message }
        }
    }

    func run() {
        guard runningTool == nil else { return }
        let tool = selection
        let corrId = UUID().uuidString
        scanCorrelationId = corrId
        runningTool = tool
        output = "Running \(tool.rawValue)…"
        SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.state"); b.message = "\(tool.rawValue) scan started"; return b }())
        // D068 fix: previously nothing was ever assigned to `runTask`, so resetAll()'s
        // `runTask?.cancel()` was silently a no-op. And with no timeout, a check whose
        // underlying network call hangs (no throw, just never returns) left `runningTool`
        // set forever — every panel showing that tool's spinner stayed stuck with no recovery
        // short of quitting the app. Race `execute(tool)` against a per-tool timeout instead.
        runTask = Task {
            // B235: reject non-runnable tools BEFORE the timeout race — a sidebar selection
            // for Cage/Conclusions/Dashboard/Settings/License/Beta must never fabricate a
            // timeout message with a fabricated duration.
            guard Self.isRunnable(tool) else {
                self.output = "\(tool.rawValue) is not run from this button."
                self.runningTool = nil
                self.scanCorrelationId = nil
                return
            }
            let outcome = await Self.withTimeout(seconds: Self.timeout(for: tool)) { [weak self] in
                await self?.execute(tool) ?? .notRunnable
            }
            guard !Task.isCancelled else { return }
            switch outcome {
            case .text(let t) where !t.isEmpty:
                self.output = t
                SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.scan"); b.message = "\(tool.rawValue) scan complete"; b.data = ["tool": tool.rawValue]; return b }())
            case .text:
                // Real run produced no output — honest empty result, not a timeout.
                self.output = "\(tool.rawValue) finished with no output."
                SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .info, category: "gui.scan"); b.message = "\(tool.rawValue) scan empty"; b.data = ["tool": tool.rawValue]; return b }())
            case .notRunnable:
                // Defensive: `isRunnable` above should have prevented this, but if `execute`
                // is extended and forgets to add a case, the honest line is "not runnable",
                // not a fabricated timeout with a fabricated duration.
                self.output = "\(tool.rawValue) is not run from this button."
            case nil:
                // Genuine timeout — the race was lost.
                self.output = "\(tool.rawValue) timed out — no response after \(Int(Self.timeout(for: tool)))s. Try again."
                SentrySDK.addBreadcrumb({ let b = Breadcrumb(level: .warning, category: "gui.scan"); b.message = "\(tool.rawValue) scan timed out after \(Int(Self.timeout(for: tool)))s"; return b }())
            }
            self.runningTool = nil
            self.scanCorrelationId = nil
        }
    }

    /// Per-tool ceiling matched to what each check actually does — Portal traces a route and
    /// Glutton runs a full leak collector round-trip, both slower than a single Prophet lookup.
    static func timeout(for tool: Tool) -> Double {
        switch tool {
        case .portal:  return 90
        case .glutton: return 120
        default:       return 60
        }
    }

    /// Races `operation` against a timeout; returns nil on timeout rather than leaving the
    /// caller awaiting forever. The loser of the race is cancelled, not just abandoned.
    static func withTimeout<T: Sendable>(seconds: Double, operation: @escaping @Sendable () async -> T) async -> T? {
        await withTaskGroup(of: T?.self) { group in
            group.addTask { await operation() }
            group.addTask {
                try? await Task.sleep(nanoseconds: UInt64(seconds * 1_000_000_000))
                return nil
            }
            let result = await group.next() ?? nil
            group.cancelAll()
            return result
        }
    }

    /// Which tools can `run()` dispatch? Kept as a single source of truth so the sidebar can
    /// never steer a non-runnable tool into the timeout branch.
    static func isRunnable(_ tool: Tool) -> Bool {
        switch tool {
        case .prophet, .mirror, .portal, .glutton, .witness:
            return true
        default:
            return false
        }
    }

    func execute(_ tool: Tool) async -> ExecOutcome {
        switch tool {
        case .prophet:      return .text(await runLookup())
        case .mirror:       return .text(await runCompare())
        case .portal:       return .text(await runNetworkState())
        case .glutton:      return .text(await runLeak())
        case .witness:      return .text(await runWitness())
        // B235: every unmapped case is .notRunnable — never "" (which the old run() branch
        // read as "timed out — no response after 60s"). Cage, Conclusions, Dashboard,
        // Settings, License, Beta all have their own dedicated UI surfaces and are not
        // dispatched by the single-shot run() button.
        default:
            return .notRunnable
        }
    }
}
