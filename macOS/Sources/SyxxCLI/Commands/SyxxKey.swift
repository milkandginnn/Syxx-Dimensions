import Foundation
import SyxxKit
struct SyxxKeyCLI {
    /// B253: key is a diagnostic report. Completes with 0; help is 0.
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx key",
            usage: "Usage: syxx key   (reports resolver / interface / VPN / captive-portal / encrypted-DNS state)") {
            return CLIExitStatus.completed.rawValue
        }
        print("Gathering resolver, proxy, interface, VPN, and captive-portal state...")
        let report = await NetworkKeyState.generateReport()
        print("\n--- Network DNS & Key State ---")
        print("System DNS Resolvers:")
        if report.dnsServers.isEmpty {
            print("  None detected (scutil/resolv.conf returned no usable nameservers)")
        } else {
            for server in report.dnsServers {
                print("  - \(server)")
            }
        }
        if !report.scopedResolvers.isEmpty {
            print("\nScoped/Supplemental Resolvers:")
            for resolver in report.scopedResolvers {
                var descriptor = resolver.address
                if let iface = resolver.interface {
                    descriptor += " (interface: \(iface))"
                }
                if resolver.isScoped {
                    descriptor += " [scoped]"
                }
                if !resolver.matchDomains.isEmpty {
                    descriptor += " [domains: \(resolver.matchDomains.joined(separator: ", "))]"
                }
                print("  - \(descriptor)")
            }
        }
        print("\nActive Interfaces:")
        for iface in report.activeInterfaces {
            print("  - \(iface)")
        }
        print("\nVPN Tunnel Status:")
        print("  \(describe(report.vpn.state, yes: "🔴 ACTIVE", no: "🟢 INACTIVE", unknown: "❓ UNKNOWN"))")
        print("  Evidence: \(report.vpn.evidence)")
        print("\nCaptive Portal Status:")
        print("  \(describe(report.captivePortal.state, yes: "⚠️  DETECTED", no: "✅ CLEAR", unknown: "❓ UNKNOWN"))")
        print("  Evidence: \(report.captivePortal.evidence)")
        print("\nEncrypted DNS Status:")
        print("  \(describe(report.encryptedDNS.state, yes: "🔒 ENCRYPTED", no: "🔓 UNENCRYPTED", unknown: "❓ UNKNOWN"))")
        print("  Evidence: \(report.encryptedDNS.evidence)")
        // C1 — gateway/router audit. The detail string is the probe's own composed evidence;
        // a failed detection prints its honest reason, never a fabricated gateway.
        print("\nGateway / Router:")
        if let gw = report.gateway {
            print("  \(gw.detail)")
        } else {
            print("  Not probed in this run.")
        }
        // Captive portal DETECTED is a security-relevant finding for automation.
        if report.captivePortal.state == .yes {
            return CLIExitStatus.threat.rawValue
        }
        return CLIExitStatus.completed.rawValue
    }
    static func describe(_ state: ConditionState, yes: String, no: String, unknown: String) -> String {
        switch state {
        case .yes: return yes
        case .no: return no
        case .unknown: return unknown
        }
    }
}
