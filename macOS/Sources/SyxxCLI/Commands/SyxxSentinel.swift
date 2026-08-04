import Foundation
import SyxxKit
struct SyxxSentinelCLI {
    /// B253: 0=no change / successful admin action, 1=configuration change detected,
    /// 2=corrupt/unreadable baseline or empty resolvers, 3=persist failure.
    @discardableResult
    static func run(_ argv: [String]) -> Int32 {
        let args = argv
        if SyxxConfig.handleGlobalFlags(args, tool: "syxx sentinel",
            usage: "Usage: syxx sentinel [--reset] [--forget] [--set-home] [--home]   (detect changes to your DNS-relevant network configuration; compare against your home network)") {
            return CLIExitStatus.completed.rawValue
        }
        if args.contains("--forget") {
            Sentinel.forget()
            HomeBaseline.forget()
            AnswerLedger().forget()
            print("🧹 Forgotten: Sentinel baseline, home baseline, and the passive-DNS answer ledger erased (~/.syxx).")
            return CLIExitStatus.completed.rawValue
        }
        let current = NetworkConfigSnapshot.capture()
        print("Current DNS configuration:")
        print("  resolvers          : \(current.resolvers.isEmpty ? "(none detected)" : current.resolvers.joined(separator: ", "))")
        print("  active interfaces  : \(current.activeInterfaces.joined(separator: ", "))")
        print("  VPN active         : \(current.vpnActive.rawValue)")
        // C090: tri-state display — .unknown is a distinct outcome, not "no".
        print("  DNS encrypted      : \(current.dnsEncrypted.rawValue)")

        // C5 — home network baseline.
        if args.contains("--set-home") {
            do {
                try HomeBaseline.save(current)
                print("\n🏠 Saved this network as your home baseline. On other networks, run `syxx sentinel --home` to see how they differ.")
                return CLIExitStatus.completed.rawValue
            } catch {
                // C094: report the real error instead of silently claiming success.
                print("\n⚠️ Could not save home baseline: \(error.localizedDescription)")
                return CLIExitStatus.failure.rawValue
            }
        }
        if args.contains("--home") {
            // C093: use loadOutcome to distinguish corrupt from absent.
            let homeOutcome = HomeBaseline.loadOutcome()
            switch homeOutcome {
            case .absent:
                let comparison = HomeBaseline.compare(home: nil, current: current)
                print("\n\(comparison.summary)")
                return CLIExitStatus.inconclusive.rawValue
            case .corrupt(let reason):
                print("\n⚠️ Home baseline is unreadable: \(reason). Run `syxx sentinel --set-home` on your home network to re-establish it.")
                return CLIExitStatus.inconclusive.rawValue
            case .loaded(let home):
                let comparison = HomeBaseline.compare(home: home, current: current)
                if comparison.matchesHome {
                    print("\n🏠 \(comparison.summary)")
                    return CLIExitStatus.completed.rawValue
                } else {
                    print("\n🏠 \(comparison.summary)")
                    for c in comparison.differences {
                        print("  • \(c.kind.rawValue): \(c.detail)")
                    }
                    return CLIExitStatus.threat.rawValue
                }
            }
        }
        if args.contains("--reset") {
            do {
                try Sentinel.saveSnapshot(current)
                print("\nBaseline reset. Future runs will compare against this snapshot.")
                return CLIExitStatus.completed.rawValue
            } catch {
                // C094: report the real error instead of silently claiming success.
                print("\n⚠️ Could not reset baseline: \(error.localizedDescription)")
                return CLIExitStatus.failure.rawValue
            }
        }
        // C093: use loadSnapshotOutcome to distinguish absent from corrupt baselines.
        let outcome = Sentinel.loadSnapshotOutcome()
        switch outcome {
        case .absent:
            do {
                try Sentinel.saveSnapshot(current)
                print("\nNo prior baseline — saved this snapshot as the baseline. Run again after your network changes.")
                return CLIExitStatus.completed.rawValue
            } catch {
                print("\n⚠️ Could not save initial baseline: \(error.localizedDescription). Changes will not be detected until this is fixed.")
                return CLIExitStatus.failure.rawValue
            }
        case .corrupt(let reason):
            Sentinel.quarantineCorruptBaseline(at: Sentinel.defaultURL)
            print("\n⚠️ The stored baseline is unreadable or tampered: \(reason). The corrupt file has been quarantined.")
            do {
                try Sentinel.saveSnapshot(current)
                print("A fresh baseline has been established. Run again after your network changes.")
                return CLIExitStatus.inconclusive.rawValue
            } catch {
                print("Could not save new baseline: \(error.localizedDescription)")
                return CLIExitStatus.failure.rawValue
            }
        case .loaded(let previous):
            let changes = Sentinel.diff(previous: previous, current: current)
            let code: Int32
            if changes.isEmpty {
                // C109: two point samples matching is not "stable" — and an empty resolvers list
                // means the configuration could not be read, which is inconclusive, not clean.
                if current.resolvers.isEmpty {
                    print("\n⚠️ No change since last run, but no resolvers were detected this run — the DNS configuration could not be read. This is inconclusive, not clean.")
                    code = CLIExitStatus.inconclusive.rawValue
                } else {
                    print("\n✅ No change since last run — DNS configuration is unchanged.")
                    code = CLIExitStatus.completed.rawValue
                }
            } else {
                print("\n🚨 Configuration change detected — you should re-run tampering/leak checks:")
                for c in changes {
                    print("  • \(c.kind.rawValue): \(c.detail)")
                }
                print("\n  Suggested: syxx (Option A vantage compare), syxx leak --collector <url>, syxx lan")
                // C109: append detected changes to a persistent change log before rolling the
                // baseline, so a change detected while no one is watching is not lost when the
                // baseline rolls forward.
                SyxxSentinelCLI.appendChangeLog(changes)
                code = CLIExitStatus.threat.rawValue
            }
            // Roll the baseline forward so each run reports deltas since the previous run.
            do {
                try Sentinel.saveSnapshot(current)
            } catch {
                // C094: do not silently swallow a persist failure on the roll-forward.
                print("\n⚠️ Could not update baseline: \(error.localizedDescription). Next run may show stale comparisons.")
                return CLIExitStatus.failure.rawValue
            }
            return code
        }
    }

    // C109: append detected changes to an append-only JSONL log so a change detected while
    // no one is watching stdout is not lost when the baseline rolls forward. The log lives
    // at ~/.syxx/sentinel-changes.jsonl (0600) and is never truncated by the sentinel itself.
    static func appendChangeLog(_ changes: [ConfigChange]) {
        let base = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent(".syxx", isDirectory: true)
        let url = base.appendingPathComponent("sentinel-changes.jsonl")
        try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true)
        let timestamp = ISO8601DateFormatter().string(from: Date())
        for change in changes {
            let entry = "{\"timestamp\":\"\(timestamp)\",\"kind\":\"\(change.kind.rawValue)\",\"detail\":\"\(change.detail.replacingOccurrences(of: "\"", with: "\\\""))\"}\n"
            if let data = entry.data(using: .utf8) {
                if FileManager.default.fileExists(atPath: url.path) {
                    if let handle = try? FileHandle(forWritingTo: url) {
                        handle.seekToEndOfFile()
                        handle.write(data)
                        handle.closeFile()
                    }
                } else {
                    try? data.write(to: url)
                    try? FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: url.path)
                }
            }
        }
    }
}
