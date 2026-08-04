import Foundation
import SyxxKit

/// C2 — `syxx filter`: verifies that a DNS filter (Pi-hole / NextDNS / AdGuard / router
/// blocklist) is actually enforcing, and whether it can be bypassed. Output is tri-state
/// honest: an unreadable probe never reads as a working filter.
struct SyxxFilterCLI {
    /// B253: 0=filter active and bypass covered, 1=not filtering / leaky / bypass open,
    /// 2=inconclusive, 3=usage (none currently — missing resolver is inconclusive).
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx filter",
            usage: "Usage: syxx filter [@resolver]   (verifies DNS blocklist enforcement + bypass paths)") {
            return CLIExitStatus.completed.rawValue
        }

        // Optional @resolver override, nslook-style; default is the system resolver.
        var resolver = NetworkKeyState.getSystemDNSServers().first ?? ""
        for arg in argv where arg.hasPrefix("@") {
            resolver = String(arg.dropFirst())
        }
        guard !resolver.isEmpty else {
            print("[INCONCLUSIVE] No system resolver could be determined and none was given (@resolver). The filter check did not run.")
            return CLIExitStatus.inconclusive.rawValue
        }

        print("Probing blocklist enforcement via \(resolver)...")
        let result = await FilteringProbe.probe(resolver: resolver)

        print("\n--- DNS Filtering Verification ---")
        for domain in FilteringProbe.probeDomains {
            let outcome = result.perDomain[domain] ?? .unknown
            let label: String
            switch outcome {
            case .sinkholed: label = "[BLOCKED]   filter acted"
            case .resolved:  label = "[RESOLVED]  got through — the filter did not act"
            case .unknown:   label = "[NO READING] query failed — not evidence either way"
            }
            print("  \(domain): \(label)")
        }

        print("\nEnforcement:")
        switch result.enforcement {
        case .active:
            print("  [ACTIVE] Every readable probe came back sinkholed — a DNS filter is enforcing on this resolver.")
        case .inactive:
            print("  [NOT FILTERING] Every readable probe resolved publicly. No DNS filtering was observed on this resolver.")
        case .partial:
            print("  [LEAKY] Some probes were blocked, some got through. The blocklist is enforcing but incomplete for these domains.")
        case .inconclusive:
            print("  [INCONCLUSIVE] No probe produced a readable answer. Whether a filter is working could not be determined.")
        }

        print("\nBypass paths:")
        switch result.directBypassOpen {
        case .yes:
            print("  [OPEN] A direct query to 8.8.8.8 over plain UDP:53 resolved a blocked domain — an app with a hardcoded resolver can bypass the filter.")
        case .no:
            print("  [COVERED] Direct external UDP:53 queries are blocked or also filtered — hardcoded-resolver apps cannot slip past.")
        case .unknown:
            print("  [NO READING] The direct-resolver canary did not complete; the bypass question is unanswered.")
        }

        // DoH sidestep — reuses the existing encrypted-transport probe (A8).
        let doh = await EncryptedTransportProbe.probe()
        switch doh.status {
        case .working:
            print("  [OPEN] A public DoH endpoint is reachable — a browser using its own encrypted DNS can sidestep the filter.")
        case .blocked:
            print("  [COVERED] Public DoH is blocked on this network — browsers cannot silently sidestep the filter that way.")
        case .inconclusive, .untested:
            print("  [NO READING] The DoH sidestep check did not complete; that path is unverified.")
        }
        print("\n\(result.detail)")

        // Map to process result after all evidence is printed.
        switch result.enforcement {
        case .inconclusive:
            return CLIExitStatus.inconclusive.rawValue
        case .inactive, .partial:
            return CLIExitStatus.threat.rawValue
        case .active:
            // Open bypass is still a finding even when the filter is active.
            if result.directBypassOpen == .yes || doh.status == .working {
                return CLIExitStatus.threat.rawValue
            }
            return CLIExitStatus.completed.rawValue
        }
    }
}
