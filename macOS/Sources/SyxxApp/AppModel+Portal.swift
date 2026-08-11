import Foundation
import SyxxKit

// MARK: - Portal tool implementation & traceroute

extension AppModel {

    func runNetworkState() async -> String {
        let result = await ProbePlan.runPortal()
        self.portalReport = result.network
        sectionInputs.network = result.network
        sectionInputs.encryptedTransport = result.encryptedTransport
        sectionInputs.openResolver = result.openResolver

        guard let report = result.network else {
            return "Network state report could not be generated."
        }

        func tri(_ s: ConditionState) -> String { s == .yes ? "yes" : (s == .no ? "no" : "unknown") }
        var lines = ["System DNS resolvers:"]
        lines.append(report.dnsServers.isEmpty ? "  (none detected)" : report.dnsServers.map { "  \($0)" }.joined(separator: "\n"))
        lines.append("")
        lines.append("Active interfaces: \(report.activeInterfaces.joined(separator: ", "))")
        lines.append("")
        lines.append("VPN: \(tri(report.vpn.state)) — \(report.vpn.evidence)")
        lines.append("Captive portal: \(tri(report.captivePortal.state)) — \(report.captivePortal.evidence)")
        lines.append("Encrypted DNS: \(tri(report.encryptedDNS.state)) — \(report.encryptedDNS.evidence)")
        lines.append("")

        // Open Resolver & Amplification exposure (advisory) — scoped to the user's own LAN devices only.
        if let openRes = result.openResolver {
            switch openRes.status {
            case .recursiveHighAmp: lines.append("Open resolver: 🚨 \(openRes.detail)")
            case .recursiveLowAmp:  lines.append("Open resolver: ⚠️ \(openRes.detail)")
            case .notRecursive:     lines.append("Open resolver: ✓ your LAN resolver does not openly recurse for us")
            case .untested:         lines.append("Open resolver: (unmeasured — probe did not complete)")
            }
        } else {
            lines.append("Open resolver: (skipped — configured resolver is public)")
        }

        return lines.joined(separator: "\n")
    }

    // MARK: - Portal traceroute (GUI wrapper around the system `traceroute`)

    /// Start a system traceroute to the current `domain`, off the main thread, collecting the
    /// tool's real hop lines into `traceHops`. Gated by `isSafeTraceTarget` so we never hand
    /// hostile input to the subprocess. Honest states only: `.invalidTarget` (empty/unsafe),
    /// `.running`, `.completed` (real hops), `.failed` (could not run / no output). Never fabricates.
    func startTraceroute() {
        let target = domain.trimmingCharacters(in: .whitespaces)
        traceHops = []
        guard !target.isEmpty, AppModel.isSafeTraceTarget(target) else {
            traceState = .invalidTarget
            return
        }
        // B245: cancel any prior trace so a stale completion can't clobber the new target's result
        // or resurrect state after resetAll().
        traceTask?.cancel()
        traceState = .running
        traceTask = Task.detached(priority: .userInitiated) {
            let outcome = await AppModel.performTraceroute(target: target)
            await MainActor.run {
                // B245: guard the write-back — if cancelled (reset or newer trace), discard.
                guard !Task.isCancelled else { return }
                self.traceHops = outcome.hops
                self.traceState = outcome.state
            }
        }
    }

    /// Run the shared `TracerouteRunner` for `target` and map its real output lines to a tri-state
    /// outcome. Matches the guided-CLI Portal trace's tool invocation (`-q 1 -w 1 -m 30`, 45s guard).
    /// An empty result is an honest failure, never a clean path.
    nonisolated static func performTraceroute(target: String) async -> (hops: [String], state: TraceState) {
        let hops = await TracerouteRunner.trace(target: target, maxHops: 30)
        if hops.isEmpty {
            return ([], .failed("the traceroute produced no output"))
        }
        return (hops, .completed)
    }

    /// Reject empty, flag-like, whitespace-bearing, or non-hostname/IP input before it reaches the
    /// traceroute subprocess. Mirrors the guided CLI's `isSafeTraceTarget`.
    nonisolated static func isSafeTraceTarget(_ target: String) -> Bool {
        if target.isEmpty || target.hasPrefix("-") || target.rangeOfCharacter(from: .whitespacesAndNewlines) != nil {
            return false
        }
        let allowed = CharacterSet(charactersIn: "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-:")
        return target.rangeOfCharacter(from: allowed.inverted) == nil
    }
}
