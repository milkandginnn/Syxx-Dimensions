import Foundation

public enum DNSCaptureReportGenerator {

    /// Tri-state result for report writes: success / failure / unknown.
    /// Mirrors the project-wide tri-state contract (AGENTS.md: "TRI-STATE, NEVER BOOLEAN").
    public enum ReportWriteResult: Equatable {
        case success
        case failure(String)
        case unknown
    }

    /// The return value of `generateReport`: the report text plus a tri-state write result.
    public struct ReportOutput {
        public let report: String
        public let writeResult: ReportWriteResult
        public init(report: String, writeResult: ReportWriteResult) {
            self.report = report
            self.writeResult = writeResult
        }
    }

    /// Streams the capture file line-by-line via a chunked `FileHandle` reader instead of
    /// loading the entire file into memory (M30) — a long capture's NDJSON can be large, and the
    /// old `String(contentsOfFile:)` approach risked OOM on exactly the longest, most
    /// evidence-rich sessions.
    /// C075: capture-loss counters from the capture loop, surfaced in PARSE SUMMARY so the
    /// persisted report reflects what was captured and lost, not just what survived.
    public struct CaptureCounters: Sendable {
        public let packetsSeen: Int
        public let recordsWritten: Int
        public let malformedFrames: Int
        public let unsupportedLinkPackets: Int
        public let dnsParseFailures: Int
        public let encodingFailures: Int
        public init(packetsSeen: Int = 0, recordsWritten: Int = 0, malformedFrames: Int = 0,
                    unsupportedLinkPackets: Int = 0, dnsParseFailures: Int = 0, encodingFailures: Int = 0) {
            self.packetsSeen = packetsSeen
            self.recordsWritten = recordsWritten
            self.malformedFrames = malformedFrames
            self.unsupportedLinkPackets = unsupportedLinkPackets
            self.dnsParseFailures = dnsParseFailures
            self.encodingFailures = encodingFailures
        }
    }

    public static func generateReport(
        inputFile: String,
        outputFile: String,
        reportFile: String,
        startTime: Date,
        durationSeconds: Double,
        interface: String = "unknown",
        captureCounters: CaptureCounters? = nil
    ) -> ReportOutput {
        guard let fileSize = (try? FileManager.default.attributesOfItem(atPath: inputFile))?[.size] as? Int else {
            let msg = "Error: Could not read capture file at \(inputFile)"
            return ReportOutput(report: msg, writeResult: .failure("Input file unreadable"))
        }
        guard let fileHandle = FileHandle(forReadingAtPath: inputFile) else {
            let msg = "Error: Could not open capture file at \(inputFile)"
            return ReportOutput(report: msg, writeResult: .failure("Input file unopenable"))
        }
        defer { try? fileHandle.close() }

        var parsedRecords = 0
        var unparsedCount = 0

        var domains: [String: Int] = [:]
        var parentDomains: [String: Int] = [:]
        var processes: [String: Int] = [:]
        var recordTypes: [String: Int] = [:]
        var statuses: [String: Int] = [:]
        var resolvers: [String: Int] = [:]

        var longDomains: [String: Int] = [:]
        var encodedDomains: [String: Int] = [:]
        // C7 — per-process resolver usage (process → resolver IPs), joined at report time to expose
        // apps talking to a resolver the system did not configure.
        var resolverUsage: [String: Set<String>] = [:]

        let decoder = JSONDecoder()

        forEachLine(in: fileHandle) { line in
            let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !trimmed.isEmpty else { return }

            guard let data = trimmed.data(using: .utf8),
                  let record = try? decoder.decode(DNSCaptureRecord.self, from: data) else {
                unparsedCount += 1
                return
            }
            parsedRecords += 1

            let procName = record.process.name ?? "unknown"
            processes[procName, default: 0] += 1

            // C042: the resolver is the PORT-53 side of the exchange (not the QR header,
            // which is attacker-controlled payload metadata). Matches the C7 port-keyed
            // attribution below.
            let resolverIP = record.network.sourcePort == 53 ? record.network.sourceIP : record.network.destIP
            resolvers[resolverIP, default: 0] += 1

            // C7 — join process ↔ resolver, keying the resolver on the PORT-53 side (D063: the QR
            // bit is forgeable, the port is not).
            let portKeyedResolver = record.network.sourcePort == 53 ? record.network.sourceIP : record.network.destIP
            if !portKeyedResolver.isEmpty {
                resolverUsage[procName, default: []].insert(portKeyedResolver)
            }

            for q in record.packet.questions {
                let domainName = q.questionName
                domains[domainName, default: 0] += 1

                if let parent = registrableDomain(domainName) {
                    parentDomains[parent, default: 0] += 1
                }

                recordTypes[q.questionType, default: 0] += 1

                if domainName.count > 40 {
                    longDomains[domainName, default: 0] += 1
                }
                if isEncodedLooking(domainName) {
                    encodedDomains[domainName, default: 0] += 1
                }
            }

            statuses[record.packet.rcode.lowercased(), default: 0] += 1
        }

        let durationMinutes = durationSeconds / 60.0
        let eventRate = durationMinutes > 0 ? (Double(parsedRecords) / durationMinutes) : 0.0

        let dateFormatter = DateFormatter()
        dateFormatter.dateFormat = "yyyy-MM-dd HH:mm:ss 'UTC'"
        dateFormatter.timeZone = TimeZone(secondsFromGMT: 0)
        let captureDateStr = dateFormatter.string(from: startTime)

        let relOutput = relativeCapturePath(outputFile)
        let relReport = relativeCapturePath(reportFile)

        func formatNumber(_ n: Int) -> String {
            let formatter = NumberFormatter()
            formatter.numberStyle = .decimal
            return formatter.string(from: NSNumber(value: n)) ?? "\(n)"
        }

        func formatTopList(_ dict: [String: Int], limit: Int) -> String {
            // C087: tie-break by key so Dictionary's randomised iteration order cannot
            // produce a different rendering of the same capture on every run.
            let sorted = dict.sorted(by: { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }).prefix(limit)
            guard !sorted.isEmpty else { return "" }
            let maxCount = sorted.first!.value
            let countWidth = String(maxCount).count
            return sorted.map { String(format: "%\(countWidth)d  %@", $0.value, $0.key) }
                .joined(separator: "\n")
        }

        let topDomainsStr = formatTopList(domains, limit: 8)
        let topParentDomainsStr = formatTopList(parentDomains, limit: 8)
        let topProcessesStr = formatTopList(processes, limit: 8)
        let recordTypesStr = formatTopList(recordTypes, limit: Int.max)
        let statusesStr = formatTopList(statuses, limit: Int.max)
        let resolversStr = formatTopList(resolvers, limit: 8)

        // C7 — per-app resolver attribution. Flag processes using a resolver the system did not
        // configure (hardcoded-DNS bypass). Coverage is UDP:53 only; that limit is stated, not implied.
        let systemResolvers = Set(NetworkKeyState.getSystemDNSServers())
        let attribution = ResolverAttribution.classify(usage: resolverUsage, systemResolvers: systemResolvers)
        let attributionStr: String = {
            if attribution.attributions.isEmpty { return "None observed" }
            var lines: [String] = []
            if attribution.baselineUnknown {
                lines.append("System resolver set unknown — cannot judge bypass; process→resolver map only:")
            }
            for a in attribution.attributions {
                let mark = a.usesHardcodedResolver ? "  [OFF-SYSTEM RESOLVER]" : ""
                lines.append("\(a.process): \(a.resolvers.joined(separator: ", "))\(mark)")
            }
            if !attribution.flagged.isEmpty {
                lines.append("")
                lines.append("Flagged (used a resolver the system did not configure):")
                for a in attribution.flagged {
                    lines.append("  \(a.process) → \(a.offSystemResolvers.joined(separator: ", "))")
                }
            }
            lines.append("")
            lines.append("Coverage: cleartext UDP:53 only. Encrypted DNS (DoH/DoT/DoQ) is not captured, so")
            lines.append("\"no off-system resolver\" means none over UDP:53, not none at all.")
            return lines.joined(separator: "\n")
        }()

        let fileSizeMB = Double(fileSize) / 1_048_576.0
        let fileSizeStr: String
        if fileSizeMB >= 1024.0 {
            fileSizeStr = String(format: "%.1f GB", fileSizeMB / 1024.0)
        } else {
            fileSizeStr = String(format: "%.1f MB", fileSizeMB)
        }

        guard parsedRecords > 0 else {
            // C075: distinguish "0 packets seen" (untested) from "N packets seen, 0 decoded"
            // (inconclusive with loss). The old report asserted "No packets were captured" as
            // fact when it could not know that — packets may have been captured and 100% dropped.
            let packetsSeen = captureCounters?.packetsSeen ?? 0
            let statusLine: String
            let statusDetail: String
            if packetsSeen > 0 {
                statusLine = "STATUS: INCONCLUSIVE — PARTIAL CAPTURE"
                statusDetail = """
                \(formatNumber(packetsSeen)) packets were seen but 0 DNS records decoded.
                This means capture worked but DNS parsing failed for every packet
                (malformed frames: \(captureCounters?.malformedFrames ?? 0), DNS parse failures: \(captureCounters?.dnsParseFailures ?? 0),
                unsupported link: \(captureCounters?.unsupportedLinkPackets ?? 0), encode failures: \(captureCounters?.encodingFailures ?? 0)).
                This is NOT a quiet network — it is evidence loss.
                """
            } else {
                statusLine = "STATUS: INCONCLUSIVE"
                statusDetail = """
                No packets were captured. This is NOT evidence of a
                quiet network. The capture may have failed (missing
                privileges, wrong interface, BPF blocked) or no DNS
                traffic occurred during the sampling window.
                """
            }
            let msg = """
            \(header)

                         DIAGNOSTIC REPORT


            Date:       \(captureDateStr)
            Duration:   \(String(format: "%.1f", durationSeconds))s
            Interface:  \(interface)

            \(statusLine)
            --------------------
            \(statusDetail)

            PARSE SUMMARY
            -------------
            Output size:         \(formatNumber(fileSize)) bytes
            Records parsed:      0
            Unparseable lines:   \(unparsedCount)

            TROUBLESHOOTING
            ---------------
            - Run with sudo to ensure BPF access
            - Generate DNS traffic during capture (dig example.com)
            - Increase the capture duration
            - Check that the capture interface is correct
            """
            let writeResult: ReportWriteResult
            do {
                try msg.write(toFile: reportFile, atomically: true, encoding: .utf8)
                writeResult = .success
            } catch {
                writeResult = .failure(error.localizedDescription)
            }
            return ReportOutput(report: msg, writeResult: writeResult)
        }

        let longDomainCount = longDomains.count
        let encodedDomainCount = encodedDomains.count
        // C087: tie-break by key so the same capture renders the same top-process every time.
        let topProcess = processes.sorted(by: { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }).first
        let topProcessName = topProcess?.key ?? "None"
        let topProcessCount = topProcess?.value ?? 0
        let topProcessPct = parsedRecords > 0 ? Double(topProcessCount) / Double(parsedRecords) * 100.0 : 0.0

        // C038: completeness gate — when parse loss exceeds 50% of total lines, the surviving
        // records cannot represent the capture faithfully. Downgrade the interpretation to
        // PARTIAL/INCONCLUSIVE so one valid record cannot mask arbitrarily many rejected ones.
        let totalLines = parsedRecords + unparsedCount
        let lossRatio = totalLines > 0 ? Double(unparsedCount) / Double(totalLines) : 0.0
        let parseLossPartial = lossRatio > 0.5

        var interpretation = """
        Capture collected \(formatNumber(parsedRecords)) DNS events over \(Int(durationSeconds))s \
        (\(String(format: "%.1f", eventRate)) events/min). Top process: \(topProcessName) \
        (\(topProcessCount) queries, \(Int(topProcessPct))% of traffic).
        """

        // C038: surface the loss ratio and mark evidence incomplete when the threshold is breached.
        if parseLossPartial {
            interpretation = """
            ⚠️ PARTIAL EVIDENCE — \(unparsedCount) of \(totalLines) lines (\(Int(lossRatio * 100))%) could not be parsed.
            The surviving \(parsedRecords) record(s) cannot faithfully represent the full capture.
            Conclusions drawn below are INCONCLUSIVE and must not be treated as a clean bill of health.

            """ + interpretation
        } else if unparsedCount > 0 {
            interpretation += "\n\nNote: \(unparsedCount) of \(totalLines) lines could not be parsed (minor loss, \(Int(lossRatio * 100))%)."
        }

        if longDomainCount > 0 {
            interpretation += """

            \(longDomainCount) long subdomains detected. Long subdomains are normal
            for CDN cache keys and telemetry, but also appear in
            DNS tunneling. Verify the process owner before dismissing.
            """
        }

        if encodedDomainCount > 0 {
            interpretation += """

            \(encodedDomainCount) encoded-looking labels (hex patterns or labels >16
            chars). Some legitimate services use generated hostnames;
            cross-reference with the process list above.
            """
        }

        var nextSteps = "Review the anomalous indicators against the process list."
        if longDomainCount > 0 || encodedDomainCount > 0 {
            nextSteps += "\nRun syxx-mirror against flagged domains for cross-vantage comparison."
        }
        nextSteps += "\nRe-run with --keep to retain raw capture for deeper analysis."

        let report = """
        \(header)

                         DIAGNOSTIC REPORT


        Date:       \(captureDateStr)
        Duration:   \(String(format: "%.1f", durationSeconds))s
        Interface:  \(interface)
        Output:     \(relOutput)
        Report:     \(relReport)

        PARSE SUMMARY
        -------------
        Records parsed:      \(formatNumber(parsedRecords))
        Unparseable lines:   \(unparsedCount)
        File size:           \(fileSizeStr)\(fileSize >= 1_073_741_824 ? "  (consider --minify to reduce file size)" : "")
        Event rate:          \(String(format: "%.1f", eventRate)) events/min\(captureCounters.map { cc in """

        CAPTURE COMPLETENESS
        --------------------
        Packets seen:        \(formatNumber(cc.packetsSeen))
        Records written:     \(formatNumber(cc.recordsWritten))
        Malformed frames:    \(cc.malformedFrames)
        Unsupported link:    \(cc.unsupportedLinkPackets)
        DNS parse failed:    \(cc.dnsParseFailures)
        Encode failures:     \(cc.encodingFailures)
        """ } ?? "")

        TOP DOMAINS
        -----------
        \(topDomainsStr.isEmpty ? "None observed" : topDomainsStr)

        TOP PARENT DOMAINS
        ------------------
        \(topParentDomainsStr.isEmpty ? "None observed" : topParentDomainsStr)

        RECORD TYPES
        ------------
        \(recordTypesStr.isEmpty ? "None observed" : recordTypesStr)

        RESPONSE STATUSES
        -----------------
        \(statusesStr.isEmpty ? "None observed" : statusesStr)

        OBSERVED PROCESSES
        ------------------
        \(topProcessesStr.isEmpty ? "None observed" : topProcessesStr)

        RESOLVERS CONTACTED
        -------------------
        \(resolversStr.isEmpty ? "None observed" : resolversStr)

        PER-APP RESOLVER ATTRIBUTION
        ----------------------------
        \(attributionStr)

        ANOMALOUS INDICATORS
        --------------------
        Long domains (>40 chars):       \(longDomainCount)
        Encoded-looking domains:         \(encodedDomainCount)

        INTERPRETATION
        --------------
        \(interpretation)

        NEXT STEPS
        ----------
        \(nextSteps)
        """

        let writeResult: ReportWriteResult
        do {
            try report.write(toFile: reportFile, atomically: true, encoding: .utf8)
            writeResult = .success
        } catch {
            writeResult = .failure(error.localizedDescription)
        }
        return ReportOutput(report: report, writeResult: writeResult)
    }

    // MARK: - Header

    private static let header: String = {
        let eq = String(repeating: "=", count: 72)
        let dash = String(repeating: "-", count: 72)
        return [
            eq,
            "                             Syxx",
            "                      Cage Query Capture",
            eq,
            "          © J.J. Warne d/b/a MilkandGin · All rights reserved",
            "              feedback@milkandgin.com · https://syxx.app",
            dash
        ].joined(separator: "\n")
    }()

    // MARK: - Helpers

    /// Strips the home directory prefix from a path, returning a shorter relative path
    /// suitable for the report (e.g. `.syxx/captures/...` instead of `/Users/.../.syxx/captures/...`).
    private static func relativeCapturePath(_ path: String) -> String {
        let home = FileManager.default.homeDirectoryForCurrentUser.path
        if path.hasPrefix(home) {
            let rel = String(path.dropFirst(home.count))
            return rel.hasPrefix("/") ? String(rel.dropFirst()) : rel
        }
        return path
    }

    /// Reads `fileHandle` in fixed-size chunks and invokes `body` once per newline-delimited
    /// line, so the whole file is never held in memory at once.
    /// D022: caps the carry buffer at 1 MB to prevent OOM on newline-free input. Lines exceeding
    /// the cap are truncated and marked as such, not silently dropped.
    private static func forEachLine(in fileHandle: FileHandle, _ body: (String) -> Void) {
        let chunkSize = 64 * 1024
        let maxCarryBytes = 1_048_576 // 1 MB cap to prevent OOM on hostile input
        var carry = Data()
        var truncatedCount = 0

        while true {
            let chunk = fileHandle.readData(ofLength: chunkSize)
            if chunk.isEmpty { break }
            carry.append(chunk)

            // D022: if carry exceeds the cap, truncate the overlong line and emit it as truncated.
            if carry.count > maxCarryBytes, let newlineRange = carry.range(of: Data([0x0A])) {
                // Process up to the first newline even if over cap.
                let lineData = carry.subdata(in: carry.startIndex..<newlineRange.lowerBound)
                if let line = String(data: lineData, encoding: .utf8) {
                    body(line)
                }
                carry.removeSubrange(carry.startIndex..<newlineRange.upperBound)
            } else if carry.count > maxCarryBytes {
                // No newline found and over cap — emit truncated line and reset.
                let truncated = carry.prefix(maxCarryBytes)
                if var line = String(data: truncated, encoding: .utf8) {
                    line += " [TRUNCATED: line exceeded 1 MB buffer]"
                    truncatedCount += 1
                    body(line)
                }
                carry.removeAll(keepingCapacity: true)
            }

            while let newlineRange = carry.range(of: Data([0x0A])) {
                let lineData = carry.subdata(in: carry.startIndex..<newlineRange.lowerBound)
                if let line = String(data: lineData, encoding: .utf8) {
                    body(line)
                }
                carry.removeSubrange(carry.startIndex..<newlineRange.upperBound)
            }
        }
        if !carry.isEmpty, let line = String(data: carry, encoding: .utf8) {
            body(line)
        }
    }

    /// Two-level public suffixes where the registrable domain is the last *three* labels
    /// (e.g. `bbc.co.uk`, not `co.uk`). A compact list of the common ones — not the full Public
    /// Suffix List, but enough to stop `suffix(2)` from merging unrelated `*.co.uk` sites together
    /// or splitting a single registrable domain apart (MAC-032).
    /// C036: exposed so ExfilAnalyzer (same module) can use the same suffix list for
    /// public-suffix-aware subdomain extraction instead of a hard `dropLast(2)`.
    static let twoLevelPublicSuffixes: Set<String> = [
        "co.uk", "org.uk", "gov.uk", "ac.uk", "me.uk", "ltd.uk", "plc.uk", "net.uk", "sch.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au", "id.au",
        "co.nz", "net.nz", "org.nz", "govt.nz", "ac.nz",
        "co.jp", "or.jp", "ne.jp", "ac.jp", "go.jp",
        "com.br", "net.br", "org.br", "gov.br",
        "com.cn", "net.cn", "org.cn", "gov.cn",
        "co.in", "net.in", "org.in", "gov.in", "co.za", "org.za", "com.mx", "com.tr", "com.sg",
        "com.hk", "com.tw", "co.kr", "or.kr", "co.il", "com.ar", "com.co", "co.th", "in.th"
    ]

    /// Returns the registrable ("parent") domain for a name, public-suffix aware.
    static func registrableDomain(_ name: String) -> String? {
        let parts = name.lowercased().split(separator: ".").map(String.init)
        guard parts.count >= 2 else { return nil }
        if parts.count >= 3 {
            let lastTwo = parts.suffix(2).joined(separator: ".")
            if twoLevelPublicSuffixes.contains(lastTwo) {
                return parts.suffix(3).joined(separator: ".")
            }
        }
        return parts.suffix(2).joined(separator: ".")
    }

    private static func isEncodedLooking(_ domain: String) -> Bool {
        let pattern = "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}"
        if domain.range(of: pattern, options: .regularExpression) != nil {
            return true
        }
        let parts = domain.split(separator: ".")
        for part in parts {
            if part.count > 16 {
                return true
            }
        }
        return false
    }
}
