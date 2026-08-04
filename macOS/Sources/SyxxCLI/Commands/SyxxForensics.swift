import Foundation
import SyxxKit
struct SyxxForensicsCLI {
    /// B253: 0=completed analysis, 1=exfil/beacon threat signal, 2=inconclusive, 3=usage/error.
    @discardableResult
    static func run(_ argv: [String]) -> Int32 {
        let args = argv
        if SyxxConfig.handleGlobalFlags(args, tool: "syxx forensics",
            usage: "Usage: syxx forensics [--lists] [--classify <domain...>] [--exfil <domain> [--times t1,t2,...]] [--beacon t1,t2,...]") {
            return CLIExitStatus.completed.rawValue
        }
        guard args.count >= 2 else {
            printUsage()
            return CLIExitStatus.failure.rawValue
        }
        switch args[1] {
        case "--lists":
            return showLists()
        case "--classify":
            return classify(Array(args.dropFirst(2)))
        case "--beacon":
            return beacon(Array(args.dropFirst(2)))
        case "--exfil":
            return exfil(Array(args.dropFirst(2)))
        case "--analyze":
            return analyze(Array(args.dropFirst(2)))
        default:
            printUsage()
            return CLIExitStatus.failure.rawValue
        }
    }
    static func showLists() -> Int32 {
        print("Bundled ad/tracker blocklists:")
        for list in SieveBlocklistLoader.bundledLists() {
            print("  \(list.name): \(list.domains.count) domains")
        }
        return CLIExitStatus.completed.rawValue
    }
    static func classify(_ domains: [String]) -> Int32 {
        guard !domains.isEmpty else {
            print("Provide one or more domains to classify.")
            return CLIExitStatus.failure.rawValue
        }
        let lists = SieveBlocklistLoader.bundledLists()
        let coverage = BlocklistAnalyzer.classify(domains, against: lists)
        print("Domain classification against \(lists.count) bundled lists:")
        var anyFlagged = false
        for c in coverage {
            if c.coveredBy.isEmpty {
                print("  \(c.domain): not on any bundled list")
            } else {
                anyFlagged = true
                print("  \(c.domain): flagged by \(c.coveredBy.joined(separator: ", "))")
            }
        }
        // Flagged domains are a finding for automation, not an operational error.
        return anyFlagged ? CLIExitStatus.threat.rawValue : CLIExitStatus.completed.rawValue
    }
    static func beacon(_ parts: [String]) -> Int32 {
        let times = parseTimes(parts.first ?? "")
        guard times.count >= 2 else {
            print("Provide at least 2 comma-separated epoch timestamps.")
            return CLIExitStatus.failure.rawValue
        }
        let score = BeaconAnalyzer.score(BeaconSeries(processName: "cli", destination: "cli", timestamps: times))
        print("Beacon classification: \(score.classification.rawValue)")
        if let mean = score.meanInterval { print(String(format: "  mean interval: %.2fs", mean)) }
        if let cv = score.coefficientOfVariation { print(String(format: "  coefficient of variation: %.4f", cv)) }
        // Treat regular/beacon classifications as threat signals when the analyzer says so.
        let label = score.classification.rawValue.lowercased()
        if label.contains("beacon") || label.contains("regular") || label.contains("likely") {
            return CLIExitStatus.threat.rawValue
        }
        if label.contains("inconclusive") || label.contains("unknown") {
            return CLIExitStatus.inconclusive.rawValue
        }
        return CLIExitStatus.completed.rawValue
    }
    static func exfil(_ parts: [String]) -> Int32 {
        guard let domain = parts.first else {
            print("Provide a domain to assess.")
            return CLIExitStatus.failure.rawValue
        }
        var times: [Date] = []
        if let tIdx = parts.firstIndex(of: "--times"), tIdx + 1 < parts.count {
            times = parseTimes(parts[tIdx + 1])
        }
        let a = ExfilAnalyzer.assess(domain: domain, timestamps: times)
        print("Exfil assessment for \(a.domain):")
        print("  conclusion: \(a.suspicion.rawValue)")
        print(String(format: "  longest subdomain label: %d chars, %.2f bits/char", a.labelLength, a.entropyBitsPerChar))
        print("  cadence: \(a.beacon.rawValue)")
        if !a.flaggedByLists.isEmpty { print("  blocklists: \(a.flaggedByLists.joined(separator: ", "))") }
        for r in a.rationale { print("  • \(r)") }
        let suspicion = a.suspicion.rawValue.lowercased()
        if suspicion.contains("likely") || suspicion.contains("high") || suspicion.contains("suspicious") {
            return CLIExitStatus.threat.rawValue
        }
        if suspicion.contains("inconclusive") {
            return CLIExitStatus.inconclusive.rawValue
        }
        return CLIExitStatus.completed.rawValue
    }
    static func analyze(_ parts: [String]) -> Int32 {
        guard let path = parts.first else {
            print("Provide a capture query log or endpoint activity log to analyze.")
            return CLIExitStatus.failure.rawValue
        }
        let expanded = (path as NSString).expandingTildeInPath
        // Endpoint activity logs can be gigabytes — detect + stream by path, never
        // loading the whole file into memory.
        switch EndpointLogAnalyzer.analyze(path: expanded) {
        case .success(let report):
            print(report)
            return CLIExitStatus.completed.rawValue
        case .error(let reason):
            print("Endpoint log error: \(reason)")
            return CLIExitStatus.failure.rawValue
        case .notApplicable:
            // Capture query logs are small; safe to read whole.
            guard let text = try? String(contentsOfFile: expanded, encoding: .utf8) else {
                print("Could not read file: \(expanded)")
                return CLIExitStatus.failure.rawValue
            }
            let records = CaptureLogParser.parse(text)
            if records.isEmpty {
                print("No records parsed from \(expanded). (Expected a capture query log or endpoint activity log.)")
                return CLIExitStatus.inconclusive.rawValue
            }
            print(DNSLogAnalyzer.render(DNSLogAnalyzer.analyze(records), source: expanded))
            return CLIExitStatus.completed.rawValue
        }
    }
    static func parseTimes(_ s: String) -> [Date] {
        s.split(separator: ",").compactMap { Double($0.trimmingCharacters(in: .whitespaces)) }.map { Date(timeIntervalSince1970: $0) }
    }
    static func printUsage() {
        print("""
        Usage: syxx forensics <command>
        Commands:
          --lists                          Show bundled ad/tracker blocklists and their sizes
          --classify <domain...>           Flag domains that appear on a bundled blocklist
          --beacon t1,t2,t3,...            Score query-timing regularity (epoch seconds)
          --exfil <domain> [--times t1,t2,...]
                                           Fused DNS-tunneling assessment (entropy + cadence + lists)
          --analyze <logfile>              Translate a capture log into a full report. Auto-detects
                                           capture query logs and endpoint activity logs (streams
                                           multi-GB files); surfaces persistence writes, untrusted
                                           signers, unusual execs, tunneling candidates, Rcodes.
        Examples:
          syxx forensics --classify doubleclick.net example.com
          syxx forensics --beacon 1000,1060,1120,1180
          syxx forensics --exfil aGVsbG8gd29ybGQ.tunnel.evil.net --times 1000,1060,1120
        """)
    }
}
