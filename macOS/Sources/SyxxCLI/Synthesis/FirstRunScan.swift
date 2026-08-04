// macOS FirstRun CLI command (Phase A-4)
// macOS/Sources/SyxxCLI/Synthesis/FirstRunScan.swift

import Foundation
import SyxxKit

// MARK: - A4 (ROADMAP "Implementation plan v2" — five-minute first-run)

/// CLI entry point for the first-run scan (Phase A-4). Orchestrates a coordinated scan
/// across all disciplines that need no configuration, delivering a graded result before
/// any user configuration is required.
///
/// HONESTY CONTRACT (Protocol §1/§10, DoW #3): a probe that fails, times out, or cannot run
/// leaves its input nil — the synthesis engine then raises the section untested/inconclusive
/// and the result is hard-gated. Nothing here fabricates a datapoint to make the scan "complete".
///
/// Byte-parity twin of Windows `Syxx/Commands/FirstRunCommand.cs`.
public struct FirstRunCommand {

    // MARK: - CLI Entry Point

    /// Orchestrates the first-run scan via CLI. Progress is reported as terminal output.
    ///
    /// Usage: `syxx firstrun [--domain <domain>] [--local <resolver>]`
    ///
    /// B253: returns the shared process result — 0 clean, 1 threatDetected, 2 inconclusive/untested,
    /// 3 operational failure (bad args). Maps from `conclusion.determination` when present.
    @discardableResult
    public static func run(args: [String]) async -> Int32 {
        // Parse arguments
        var domain: String? = nil
        var i = 0
        while i < args.count {
            let arg = args[i].lowercased()
            if (arg == "--domain" || arg == "-d") && i + 1 < args.count {
                domain = args[i + 1]
                i += 2
            } else if (arg == "--local" || arg == "-l") && i + 1 < args.count {
                // B239: --local was parsed but never threaded to FirstRunScan.runAsync (which has
                // no resolver parameter). Reject rather than silently audit the wrong resolver.
                print("Error: --local is not yet supported. The scan uses the system resolver.")
                return CLIExitStatus.failure.rawValue
            } else {
                i += 1
            }
        }

        // Validate and canonicalize domain if provided.
        // B239: use the canonical form — the raw string was previously passed to the DNS layer
        // un-canonicalized (IDN/uppercase/trailing-dot reached the wire).
        var canonicalDomain: String? = nil
        if let domain = domain {
            guard !domain.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
                print("Error: domain cannot be empty")
                return CLIExitStatus.failure.rawValue
            }
            guard let canonical = try? DomainCanonical.canonicalize(domain) else {
                print("Error: Invalid domain '\(domain)' — could not canonicalize")
                return CLIExitStatus.failure.rawValue
            }
            canonicalDomain = canonical
        }

        // Print header
        print("╔══════════════════════════════════════════════════════════════════╗")
        print("║                    Syxx First-Run Scan                         ║")
        print("║       One-button full scan — graded result before config        ║")
        print("║   © J.J. Warne d/b/a MilkandGin · feedback@milkandgin.com      ║")
        print("╚══════════════════════════════════════════════════════════════════╝")
        print()

        // Progress callback for terminal output
        let progress: @Sendable (String) -> Void = { message in
            print("  ⏳ \(message)")
        }

        // Run the scan
        // B239: pass canonical domain and isPro so the scan covers the full Pro subset when licensed.
        print("🔍 Starting first-run scan...")
        print()

        let licenseResult = await LicenseManager.isFeatureUnlocked()
        let result = await FirstRunScan.runAsync(
            domain: canonicalDomain,
            isPro: licenseResult.unlocked,
            progress: progress)

        // Print results
        print()
        print("═══════════════════════════════════════════════════════════════════")
        print("  CONCLUSIONS")
        print("═══════════════════════════════════════════════════════════════════")
        print()

        // Grade
        let gradeEmoji: String
        switch result.card.grade {
        case .a: gradeEmoji = "🟢"
        case .b: gradeEmoji = "🟢"
        case .c: gradeEmoji = "🟡"
        case .d: gradeEmoji = "🟠"
        case .f: gradeEmoji = "🔴"
        case .ungraded: gradeEmoji = "⚪"
        }
        print("  Grade: \(gradeEmoji) \(result.card.gradeLabel)")
        print()
        print("  \(result.card.headline)")
        print()

        // Top reasons
        if !result.card.topReasons.isEmpty {
            print("  Top reasons:")
            for reason in result.card.topReasons {
                print("    • \(reason)")
            }
            print()
        }

        // Ran/skipped
        print("  Disciplines ran: \(result.ran.joined(separator: ", "))")
        if !result.skipped.isEmpty {
            print("  Disciplines skipped: \(result.skipped.joined(separator: ", "))")
        }
        print()

        // Result action
        let action = ResultActions.For(result.conclusion)
        print("  Meaning: \(action.meaning)")
        print()
        print("  Action:  \(action.primaryAction)")
        if action.qualified {
            print()
            print("  ⚠️  \(ResultActions.confidenceCaveat)")
        }
        print()

        // Plain-language explanation
        let explanation = PlainExplanations.For(result.conclusion)
        print("  Explanation: \(explanation.narrative)")
        print()

        // Unverified signals — threat patterns seen but not confirmable (honesty overhang).
        if let det = result.conclusion.determination, !det.suppressed.isEmpty {
            print("  ⚠️  Unverified signals (seen but not confirmable):")
            for s in det.suppressed {
                print("     - \(s.displayName): \(s.detail)")
            }
        }
        print()
        print("═══════════════════════════════════════════════════════════════════")

        // B253: machine-readable result from the determination, never hardcoded 0.
        guard let det = result.conclusion.determination else {
            return CLIExitStatus.inconclusive.rawValue
        }
        switch det.state {
        case .clean:
            return CLIExitStatus.completed.rawValue
        case .threatDetected:
            return CLIExitStatus.threat.rawValue
        case .inconclusive, .untested:
            return CLIExitStatus.inconclusive.rawValue
        }
    }

    // MARK: - GUI Integration Point

    /// Non-CLI entry point for GUI integration. Returns the result without printing.
    public static func orchestrate(domain: String?, progress: (@Sendable (String) -> Void)?) async -> FirstRunResult {
        await FirstRunScan.runAsync(domain: domain, progress: progress)
    }
}