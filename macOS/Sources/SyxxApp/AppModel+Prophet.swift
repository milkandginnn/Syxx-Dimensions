import Foundation
import SyxxKit

// MARK: - Prophet tool implementation

extension AppModel {

    /// Run a Prophet lookup against `server`. Internal (not private) so the D278 gate is
    /// driveable hermetically by tests: an unmeasured resolver must never be audited.
    func runLookup() async -> String {
        let name = domain.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return "Enter a domain to look up." }
        // A new run means the previous DNSSEC posture is stale: hide the card until the
        // fresh query at the end of this run lands. Never show last scan's measurement as this scan's.
        dnssecStatus = nil
        // D278: never audit a resolver that was never measured. While the startup probe is
        // still in flight, or after it failed, Prophet must not present an unknown resolver
        // as audited (an empty `server` would otherwise reach the `via` output line).
        if resolverState == .checking {
            return "System resolver not yet measured. Prophet cannot audit an unknown resolver. Retry in a moment."
        }
        if resolverState == .unavailable {
            return "System resolver could not be determined. Check your network connection, then retry."
        }
        let typeMap: [String: UInt16] = ["A": 1, "NS": 2, "CNAME": 5, "SOA": 6, "PTR": 12, "MX": 15,
                                         "TXT": 16, "AAAA": 28, "SRV": 33, "CAA": 257, "HTTPS": 65, "DNSKEY": 48]
        let type = typeMap[recordType] ?? 1
        let srv = server  // local capture for @Sendable ProbeRunner closures

        // Display query against the user-selected server
        var displayBudget = ProbeBudget.defaultProphet
        let resp = await ProbeRunner.run(label: "query", budget: &displayBudget) {
            await DNSResolver.queryUDPDetailed(DNSQuery(name: name, type: type, server: srv))
        } ?? DNSResponse(status: .timeout, errorMessage: "Probe timed out or failed")

        // C049: use the user-selected resolver as the single resolver-under-test.
        // The old code defaulted to the system resolver, discarding the user's choice.
        let auditServer = server
        let planResult = await ProbePlan.runProphet(domain: name, resolver: auditServer)
        sectionInputs.resolution = planResult.resolution
        sectionInputs.dnssec = planResult.dnssec
        sectionInputs.nxCanary = planResult.nxCanary
        sectionInputs.dnssecCanary = planResult.dnssecCanary
        sectionInputs.latency = planResult.latency
        sectionInputs.stability = planResult.stability
        sectionInputs.wildcardSynthesis = planResult.wildcardSynthesis
        sectionInputs.denial = planResult.denial
        sectionInputs.dualStack = planResult.dualStack
        sectionInputs.edns0 = planResult.edns0
        sectionInputs.versionBind = planResult.versionBind

        var lines = ["\(recordType) records for \(name) via \(server)", "Status: \(resp.status.rawValue)"]
        if resp.records.isEmpty {
            lines.append("(no records)")
        } else {
            for r in resp.records { lines.append("  \(r.rdata)   TTL \(r.ttl)   type \(r.type)") }
        }
        // Query local resolver + reference public resolvers for Prophet multi-oracle consensus
        let oracles = ProphetOracleRoster.roster(localServer: srv)

        var resolverResponses: [ProphetResult.ResolverResponse] = []
        // B231: ResolverLatency.medianMs is already in milliseconds — do NOT multiply by 1000.
        // Preserve nil when the probe was skipped; a fabricated 0ms reads as a perfect score.
        let latencyMs = sectionInputs.latency.map { Int($0.medianMs.rounded()) }

        await withTaskGroup(of: (String, String).self) { group in
            for oracle in oracles {
                group.addTask {
                    let res = await DNSResolver.queryUDPDetailed(DNSQuery(name: name, type: type, server: oracle.ip))
                    let ip = res.records.first?.rdata ?? (res.status == .success ? "no records" : "[unreachable]")
                    return (oracle.name, ip)
                }
            }
            for await (oracleName, ip) in group {
                resolverResponses.append(ProphetResult.ResolverResponse(resolver: oracleName, ip: ip))
            }
        }

        // Sort so Local resolver is first
        resolverResponses.sort { r1, r2 in
            if r1.resolver.hasPrefix("Local") { return true }
            if r2.resolver.hasPrefix("Local") { return false }
            return r1.resolver < r2.resolver
        }

        // B230: carry the honest count — 0 reachable oracles renders as "no oracle answered"
        // in the view; fabricating a floor of 1 made the unreachable state look measured.
        let activeCount = resolverResponses.filter { !$0.ip.contains("[unreachable]") }.count
        self.prophetResult = ProphetResult(
            resolverResponses: resolverResponses,
            consensus: ProphetResult.Consensus(resolversQueried: activeCount, medianLatency: latencyMs)
        )
        // DNSSEC posture for the duplicate result card: a REAL DO-enabled DNSKEY query against
        // the same audited resolver. Hidden pre-scan (nil), tri-state after — never asserted.
        self.dnssecStatus = await DNSSECVerifier.checkStatus(domain: name, resolver: srv)
        return lines.joined(separator: "\n")
    }
}
