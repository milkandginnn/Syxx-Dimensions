import Foundation
import SyxxKit

struct SyxxSpeedCLI {
    /// B253: 0=benchmark completed, 2=no resolvers to measure, 3=unused.
    @discardableResult
    static func run(_ argv: [String]) async -> Int32 {
        if SyxxConfig.handleGlobalFlags(argv, tool: "syxx speed",
            usage: "Usage: syxx speed [domain]   (ranks all configured resolvers by latency)") {
            return CLIExitStatus.completed.rawValue
        }
        let args = argv
        let domain = args.count >= 2 ? args[1] : "google.com"

        print("\n  Resolver Speed Benchmark for: \(domain)")
        print(String(repeating: "─", count: 60))

        let systemServers = NetworkKeyState.getSystemDNSServers()
        if systemServers.isEmpty {
            print("  No system DNS resolvers detected.")
            return CLIExitStatus.inconclusive.rawValue
        }

        let knownPublic: [(name: String, ip: String)] = [
            ("Cloudflare", "1.1.1.1"),
            ("Google", "8.8.8.8"),
            ("Quad9", "9.9.9.9")
        ]

        // De-duplicate: don't benchmark a public resolver twice if it's also a system resolver
        var allServers: [(label: String, ip: String)] = []
        for server in systemServers {
            allServers.append((label: server, ip: server))
        }
        for pub in knownPublic {
            if !systemServers.contains(pub.ip) {
                allServers.append((label: "\(pub.name) (\(pub.ip))", ip: pub.ip))
            }
        }

        let pad = { (s: String, n: Int) -> String in
            s.count >= n ? s : s + String(repeating: " ", count: n - s.count)
        }
        print()
        print("  \(pad("Resolver", 38))\(pad("Status", 16))\(pad("Median", 10))  Notes")
        print(String(repeating: "·", count: 60))

        typealias BenchResult = (label: String, ip: String, latency: ResolverLatency)
        var results: [BenchResult] = []

        for entry in allServers {
            let latency = await ResolverLatencyProbe.probe(
                domain: domain,
                server: entry.ip,
                samples: 4
            )
            results.append((label: entry.label, ip: entry.ip, latency: latency))
        }

        // Sort by median latency (fastest first), unreachable/degraded last
        results.sort { a, b in
            let aScore = a.latency.status == .untested || a.latency.status == .unreachable ? Double.infinity : a.latency.medianMs
            let bScore = b.latency.status == .untested || b.latency.status == .unreachable ? Double.infinity : b.latency.medianMs
            if aScore != bScore { return aScore < bScore }
            return a.latency.successCount > b.latency.successCount
        }

        var rank = 1
        var best: BenchResult?
        for result in results {
            let statusIcon: String
            switch result.latency.status {
            case .fast:       statusIcon = "⚡ fast"
            case .moderate:   statusIcon = "  ok"
            case .slow:       statusIcon = "🐌 slow"
            case .degraded:   statusIcon = "⚠️ degraded"
            case .unreachable: statusIcon = "❌ unreachable"
            case .untested:   statusIcon = "— untested"
            }
            let medianStr = result.latency.successCount > 0
                ? String(format: "%.1f ms", result.latency.medianMs)
                : "—"
            let notes = result.latency.successCount > 0 && result.latency.successCount < result.latency.sampleCount
                ? "\(result.latency.successCount)/\(result.latency.sampleCount) OK"
                : result.latency.status == .unreachable
                ? "\(result.latency.sampleCount) queries lost"
                : ""
            print("  #\(rank) \(pad(result.label, 38))\(pad(statusIcon, 16))\(pad(medianStr, 10))  \(notes)")
            if result.latency.status == .fast || result.latency.status == .moderate {
                if best == nil { best = result }
            }
            rank += 1
        }

        print(String(repeating: "─", count: 60))
        if let b = best {
            print("  Recommended: \(b.label) (median \(String(format: "%.1f", b.latency.medianMs)) ms)")
        }
        print()
        return CLIExitStatus.completed.rawValue
    }
}
