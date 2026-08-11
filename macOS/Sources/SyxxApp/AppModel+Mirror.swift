import Foundation
import SyxxKit

// MARK: - Mirror tool implementation

extension AppModel {

    func runCompare() async -> String {
        let name = domain.trimmingCharacters(in: .whitespaces)
        guard !name.isEmpty else { return "Enter a domain to compare." }
        let local = NetworkKeyState.getSystemDNSServers().first ?? server

        // B228: pass targetURL: nil so ProbePlan derives the warp target from the domain the
        // user actually typed, rather than always fetching https://github.com (the stale
        // default of `url`). The Mirror panel's "content matches" claim would otherwise be
        // evidence about a different site than the one on screen.
        let result = await ProbePlan.runMirror(domain: name, localResolver: local, isPro: isPro, targetURL: nil)
        sectionInputs.comparison = result.comparison
        sectionInputs.targetDomain = name   // Stage-5 answer-matrix query context (type defaults to A)
        sectionInputs.authoritative = result.authoritative
        sectionInputs.serverGroundTruth = result.serverGroundTruth
        sectionInputs.svcb = result.svcb
        sectionInputs.ttlAnomaly = result.ttlAnomaly
        sectionInputs.cnameChain = result.cnameChain
        // WARP_MANIFEST_V1 signed-manifest result (the live warp result). The legacy `warp`
        // (WarpResult) field is retired; the Mirror View surface (UI stream) migrates to read
        // `sectionInputs.warpManifest`.
        sectionInputs.warpManifest = result.warpManifest
        self.mirrorResult = result.comparison

        guard let comparison = result.comparison else {
            return "Comparison failed: vantage probe did not return a result."
        }

        let summary: String
        if comparison.isInconclusive { summary = "⚠️ INCONCLUSIVE — no trustworthy public baseline" }
        else if comparison.isRedirectionDetected { summary = "🚨 TAMPERING / REDIRECTION DETECTED" }
        else if comparison.isDegraded { summary = "⚠️ Inconclusive-leaning — baseline degraded (some resolvers failed); not a clean bill of health." }
        else { summary = "✅ No tampering detected" }
        var lines = ["Conclusion: \(summary)", "", "Local (\(comparison.localServer)): \(describe(comparison.localOutcome))"]
        for name in VantageComparer.publicResolvers.keys.sorted() {
            if let o = comparison.publicResolverOutcomes[name] { lines.append("\(name): \(describe(o))") }
        }
        return lines.joined(separator: "\n")
    }

    func describe(_ o: ResolverQueryOutcome) -> String {
        if let e = o.errorDescription { return "[failed: \(e)]" }
        if o.records.isEmpty { return "[no records — \(o.status.rawValue)]" }
        return o.records.map { $0.rdata }.joined(separator: ", ")
    }
}
