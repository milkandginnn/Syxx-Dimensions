import Foundation
import SyxxKit

/// `syxx unbound [verify|status|enable|disable]` — the local validating resolver ("be your own root of
/// trust"). `verify` is the reproducible, admin-free proof (runs Unbound on a high port, confirms
/// recursion + DNSSEC, stops). `enable`/`disable` perform the real system-resolver switch and need
/// administrator rights (run with `sudo`); the switch carries the snapshot/auto-revert failsafe.
///
/// NOTE (copy-gate): the human-readable strings below are PROPOSED wording pending owner approval.
struct SyxxUnboundCLI {
    /// B253: 0=pass/status printed, 2=inconclusive, 3=fail/not installed/elevation.
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx unbound",
            usage: "Usage: syxx unbound [verify|verify-dnscrypt|status|enable|disable]   (local DNSSEC-validating resolver)") {
            return CLIExitStatus.completed.rawValue
        }
        let sub = argv.count >= 2 ? argv[1].lowercased() : "verify"

        switch sub {
        case "verify":
            return await runVerify()
        case "verify-dnscrypt":
            return await runVerifyDNSCrypt()
        case "status":
            return runStatus()
        case "enable":
            return await runEnable()
        case "disable":
            return runDisable()
        default:
            print("Unknown 'unbound' action '\(sub)'. Use: verify | verify-dnscrypt | status | enable | disable")
            return CLIExitStatus.failure.rawValue
        }
    }

    /// PROPOSED wording pending owner approval. Verifies the Unbound→dnscrypt-proxy→Anonymized-DNSCrypt
    /// chain end to end on high ports (no system switch, admin-free), and reports the honest transport state.
    private static func runVerifyDNSCrypt() async -> Int32 {
        print("\n  Verifying DNSCrypt forward chain (Unbound → dnscrypt-proxy → Anonymized DNSCrypt)")
        print(String(repeating: "─", count: 52))
        if DNSCryptProxyConfig.locateBinary() == nil {
            print("  \(DNSCryptProxyConfig.installHint)")
            print("  CONCLUSION: FAIL — transport binary not installed.\n")
            return CLIExitStatus.failure.rawValue
        }
        guard let resolver = EncryptedDNSConfig.defaultDNSCryptResolver else {
            print("  No DNSCrypt resolver profile is configured.")
            print("  CONCLUSION: FAIL.\n")
            return CLIExitStatus.failure.rawValue
        }
        let mgr = UnboundManager()
        let report = await mgr.verifyDNSCryptChain(resolver: resolver)
        print("  Resolver profile : \(resolver.displayName)")
        print("  \(report.detail)")
        print(String(repeating: "─", count: 52))
        switch report.proxy {
        case .readyDirect:
            print("  NOTE: reached the resolver over DNSCrypt but WITHOUT a relay — the resolver can see")
            print("        this host's IP. Not the anonymized state.")
        case .unreachable(let reason):
            print("  TRANSPORT UNREACHABLE — \(reason). Not falling back to plain recursion.")
        default:
            break
        }
        if report.ok {
            print("  CONCLUSION: PASS — the chain resolves and DNSSEC validates over encrypted DNSCrypt transport.\n")
            return CLIExitStatus.completed.rawValue
        } else {
            print("  CONCLUSION: FAIL — see detail above (honest failure, not a fabricated pass).\n")
            return CLIExitStatus.failure.rawValue
        }
    }

    private static func runStatus() -> Int32 {
        print("\n  Local resolver (Unbound)")
        print(String(repeating: "─", count: 52))
        if let path = UnboundResolver.locateBinary() {
            print("  Binary        : found (\(path))")
        } else {
            print("  Binary        : NOT INSTALLED")
            print("  \(UnboundResolver.installHint)")
        }
        let servers = NetworkKeyState.getSystemDNSServers()
        print("  System DNS    : \(servers.isEmpty ? "(none reported)" : servers.joined(separator: ", "))")
        let usingLocal = servers.contains("127.0.0.1") || servers.contains("::1")
        print("  Points local? : \(usingLocal ? "yes (127.0.0.1 present)" : "no")")
        print(String(repeating: "─", count: 52))
        print("  Gated (not built): \(UnboundEntitlementSeam.deferredReason)\n")
        return CLIExitStatus.completed.rawValue
    }

    private static func runVerify() async -> Int32 {
        print("\n  Verifying local Unbound (recursion + DNSSEC, unprivileged high port)")
        print(String(repeating: "─", count: 52))
        let mgr = UnboundManager()
        let result = await mgr.verify(port: 5354)
        print("  \(result.detail)")
        print(String(repeating: "─", count: 52))
        if result.ok {
            print("  CONCLUSION: PASS — the local resolver recurses and validates DNSSEC.\n")
            return CLIExitStatus.completed.rawValue
        } else {
            print("  CONCLUSION: FAIL — see detail above (honest failure, not a fabricated pass).\n")
            return CLIExitStatus.failure.rawValue
        }
    }

    private static func runEnable() async -> Int32 {
        print("\n  Enabling local Unbound as the system resolver…")
        print("  (requires administrator rights; snapshot + auto-revert failsafe active)")
        let mgr = UnboundManager()
        let r = await mgr.enable()
        return printResult(r)
    }

    private static func runDisable() -> Int32 {
        print("\n  Disabling local Unbound; restoring the original system resolver…")
        let mgr = UnboundManager()
        let r = mgr.disable()
        return printResult(r)
    }

    @discardableResult
    private static func printResult(_ r: UnboundResolver.UnboundResult) -> Int32 {
        switch r {
        case .enabled(let port, let prior):
            print("  ENABLED — system DNS now points at 127.0.0.1:\(port).")
            print("  Prior servers (restored on disable): \(prior.isEmpty ? "(DHCP-provided)" : prior.joined(separator: ", "))")
            print("")
            return CLIExitStatus.completed.rawValue
        case .disabled(let restored):
            print("  DISABLED — restored: \(restored.isEmpty ? "(DHCP-provided)" : restored.joined(separator: ", "))")
            print("")
            return CLIExitStatus.completed.rawValue
        case .notInstalled(let hint):
            print("  NOT INSTALLED — \(hint)")
            print("")
            return CLIExitStatus.failure.rawValue
        case .elevationRequired(let step):
            print("  ADMIN REQUIRED — \(step). Re-run with sudo.")
            print("")
            return CLIExitStatus.failure.rawValue
        case .failed(let reason):
            print("  FAILED — \(reason)")
            print("")
            return CLIExitStatus.failure.rawValue
        case .inconclusive(let reason):
            print("  INCONCLUSIVE — \(reason)")
            print("")
            return CLIExitStatus.inconclusive.rawValue
        }
    }
}
