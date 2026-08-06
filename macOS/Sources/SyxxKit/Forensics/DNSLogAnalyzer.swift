// Offline capture-log translator (the "synthesis" brain, client-side and deterministic).
//
// Parses a PROCESS/PACKET text capture-log format — alternating `PROCESS: {…}` and `PACKET: …`
// blocks — and applies a deterministic heuristic ruleset to produce a human report with process
// attribution, record types, and reply Rcodes. Fully offline; no model.
import Foundation

public struct DNSLogRecord {
    public let process: String?
    public let processPath: String?
    public let signingID: String?
    public let pid: Int?
    public let isReply: Bool
    public let rcode: String?
    public let questionName: String?
    public let questionType: String?
    public let answers: [String]

    public init(process: String?, processPath: String?, signingID: String?, pid: Int?,
                isReply: Bool, rcode: String?, questionName: String?, questionType: String?, answers: [String]) {
        self.process = process
        self.processPath = processPath
        self.signingID = signingID
        self.pid = pid
        self.isReply = isReply
        self.rcode = rcode
        self.questionName = questionName
        self.questionType = questionType
        self.answers = answers
    }
}

public enum CaptureLogParser {
    /// Parse a `PROCESS`/`PACKET` text capture-log format into records. Robust to interleaved
    /// `ERROR:`, `method …`, and binary-garbage lines a capture may emit.
    public static func parse(_ text: String) -> [DNSLogRecord] {
        var records: [DNSLogRecord] = []
        let lines = text.components(separatedBy: "\n")
        var i = 0
        let n = lines.count

        func trimmed(_ s: String) -> String { s.trimmingCharacters(in: .whitespaces) }

        while i < n {
            // A process block begins on a line ending in "PROCESS:" (sometimes prefixed by stray
            // bytes like "]PROCESS:").
            guard trimmed(lines[i]).hasSuffix("PROCESS:") else { i += 1; continue }
            i += 1

            // Process dict: "{" then key = value; lines until "}".
            while i < n && trimmed(lines[i]) != "{" { i += 1 }
            if i < n { i += 1 } // skip "{"
            var name: String?, path: String?, signingID: String?, pid: Int?
            while i < n {
                let l = trimmed(lines[i])
                if l == "}" { i += 1; break }
                if let (k, v) = parseKV(l) {
                    switch k {
                    case "name": name = v
                    case "path": path = v
                    case "signing ID": signingID = v
                    case "pid": pid = Int(v)
                    default: break
                    }
                }
                i += 1
            }

            // Skip noise until the PACKET header (or bail if the next PROCESS arrives first).
            while i < n {
                let l = trimmed(lines[i])
                if l.hasPrefix("PACKET:") || l.hasSuffix("PROCESS:") { break }
                i += 1
            }
            guard i < n, trimmed(lines[i]).hasPrefix("PACKET:") else { continue }
            i += 1

            var isReply = false, rcode: String?, qName: String?, qType: String?
            var answers: [String] = []
            var section = ""
            while i < n {
                let l = trimmed(lines[i])
                if l.hasSuffix("PROCESS:") || l.hasPrefix("method ") { break }
                if l.hasPrefix("QR:") { isReply = l.contains("Reply") }
                else if l.hasPrefix("Rcode:") { rcode = trimmed(String(l.dropFirst("Rcode:".count))) }
                else if l.hasPrefix("Question (") { section = "question" }
                else if l.hasPrefix("Answer (") { section = "answer" }
                else if l.hasPrefix("Authority (") { section = "authority" }
                else if l.hasPrefix("Additional records (") { section = "additional" }
                else if l.hasPrefix("Xid:") || l.hasPrefix("Opcode:") || l.hasPrefix("AA:")
                          || l.hasPrefix("TC:") || l.hasPrefix("RD:") || l.hasPrefix("RA:") || l.hasPrefix("Server:") {
                    // header noise, ignore
                } else if !l.isEmpty {
                    if section == "question" && qName == nil {
                        (qName, qType) = parseQuestion(l)
                    } else if section == "answer" {
                        answers.append(l)
                    }
                }
                i += 1
            }

            records.append(DNSLogRecord(process: name, processPath: path, signingID: signingID, pid: pid,
                                        isReply: isReply, rcode: rcode, questionName: qName,
                                        questionType: qType, answers: answers))
        }
        return records
    }

    private static func parseKV(_ line: String) -> (String, String)? {
        var l = line
        if l.hasSuffix(";") { l = String(l.dropLast()) }
        guard let r = l.range(of: " = ") else { return nil }
        let k = unquote(String(l[..<r.lowerBound]))
        let v = unquote(String(l[r.upperBound...]))
        return (k, v)
    }

    private static func unquote(_ s: String) -> String {
        var t = s.trimmingCharacters(in: .whitespaces)
        if t.hasPrefix("\"") && t.hasSuffix("\"") && t.count >= 2 { t = String(t.dropFirst().dropLast()) }
        return t
    }

    /// "init.itunes.apple.com IN A" -> ("init.itunes.apple.com", "A"); "x IN ?????" -> ("x", "?????").
    private static func parseQuestion(_ line: String) -> (String?, String?) {
        guard let r = line.range(of: " IN ") else { return (line.trimmingCharacters(in: .whitespaces), nil) }
        let name = String(line[..<r.lowerBound]).trimmingCharacters(in: .whitespaces)
        let rest = String(line[r.upperBound...]).trimmingCharacters(in: .whitespaces)
        let type = rest.split(separator: " ").first.map(String.init)
        return (name.isEmpty ? nil : name, type)
    }
}

public struct DNSLogAnalysis {
    public let recordCount: Int
    public let uniqueDomains: Int
    public let uniqueRegistrable: Int
    public let domainCounts: [(String, Int)]
    public let registrableCounts: [(String, Int)]
    public let typeCounts: [(String, Int)]
    public let statusCounts: [(String, Int)]
    public let processCounts: [(String, Int)]
    public let longDomains: [String]
    public let encodedDomains: [String]
    public let exfilCandidates: [String]
    /// C6 — domains in the stream that impersonate a known brand (homoglyph / typosquat).
    public let lookalikeFindings: [LookalikeDetector.Finding]
    /// C083 — count of records whose question name failed normalizeDomain and were excluded
    /// from every forensic tally. Surfaced so the coverage gap is disclosed rather than implied away.
    public let namesExcludedCount: Int
    /// D022 — count of records whose key could not be recorded because its tally had already hit
    /// the 50,000-distinct-key memory cap. **Non-zero means the affected tallies are TRUNCATED:**
    /// `uniqueDomains` / `uniqueRegistrable` are then FLOORS ("at least N"), not measurements, and
    /// the "top N" lists are drawn only from the keys seen before the cap was reached.
    public let keysDroppedAtCapCount: Int
    /// D022 — which tallies actually hit the cap, e.g. `["domains", "registrable domains"]`.
    /// Empty when nothing was truncated.
    public let cappedTallies: [String]
    public let interpretation: [String]
}

public enum DNSLogAnalyzer {
    public static func analyze(_ records: [DNSLogRecord]) -> DNSLogAnalysis {
        var domain = [String: Int](), registrable = [String: Int](), type = [String: Int]()
        var status = [String: Int](), process = [String: Int]()
        var long = Set<String>(), encoded = Set<String>()
        var namesExcluded = 0  // C083: records whose question name failed normalizeDomain
        // D022: cap distinct keys to prevent unbounded memory growth on high-cardinality input
        // (e.g., exfil captures with millions of unique domains). Once the cap is reached, new
        // keys are not added, but existing keys continue to accumulate counts.
        let maxDistinctKeys = 50_000
        // D022 DISCLOSURE — the cap stays (it exists for a real memory reason), but a truncated
        // tally must ANNOUNCE itself. Dropping keys silently makes `uniqueDomains` a fabricated
        // measurement: past the cap the figure sticks at 50,000 while true cardinality keeps
        // climbing, and nothing tells the reader. That is honesty spine #2 (missing data is not
        // clean data) and #4 (no fabricated value shown as measured) — and it bites exactly where
        // the product matters most, since the cap's own stated trigger is an exfil capture with
        // millions of unique domains. `bump` records every drop and names the tally, so `render`
        // can report the affected counts as FLOORS rather than measurements.
        var keysDroppedAtCap = 0
        var cappedTallySet = Set<String>()
        func bump(_ map: inout [String: Int], _ key: String, _ tally: String) {
            if map.count < maxDistinctKeys || map[key] != nil {
                map[key, default: 0] += 1
            } else {
                keysDroppedAtCap += 1
                cappedTallySet.insert(tally)
            }
        }

        for r in records {
            if let p = r.process, !p.isEmpty { bump(&process, p, "processes") }
            if let t = r.questionType, t != "?????" { bump(&type, t.uppercased(), "record types") }
            // Rcode is only meaningful on replies (a query carries a placeholder "No error").
            if r.isReply, let rc = r.rcode, !rc.isEmpty { bump(&status, statusBucket(rc), "statuses") }

            // C083: count names excluded by normalizeDomain so the coverage gap is disclosed.
            guard let raw = r.questionName else { continue }
            guard let d = normalizeDomain(raw) else { namesExcluded += 1; continue }
            bump(&domain, d, "domains")
            if let reg = DNSCaptureReportGenerator.registrableDomain(d) {
                bump(&registrable, reg, "registrable domains")
            }
            let labels = d.split(separator: ".").map(String.init)
            if d.count >= 100 || labels.contains(where: { $0.count >= 48 }) { long.insert(d) }
            if labels.contains(where: looksEncodedLabel) { encoded.insert(d) }
        }

        // Tunneling/exfil candidates: fused entropy + blocklist over each unique domain (no
        // timestamps in these capture logs, so cadence isn't scored here).
        var exfil: [String] = []
        var lookalikes: [LookalikeDetector.Finding] = []
        for d in domain.keys.sorted() {
            let a = ExfilAnalyzer.assess(domain: d)
            if a.suspicion == .likelyExfil || a.suspicion == .suspicious { exfil.append(d) }
            // C6 — flag brand-impersonating names in the observed stream.
            if let f = LookalikeDetector.analyze(d) { lookalikes.append(f) }
        }

        let statusPairs = top(status, 15)
        let interp = interpret(recordCount: records.count,
                               domainTop: top(domain, 1).first,
                               statusPairs: statusPairs,
                               typeCounts: type,
                               longCount: long.count, encodedCount: encoded.count, exfilCount: exfil.count,
                               lookalikeCount: lookalikes.count)

        return DNSLogAnalysis(
            recordCount: records.count,
            uniqueDomains: domain.count,
            uniqueRegistrable: registrable.count,
            domainCounts: top(domain, 20),
            registrableCounts: top(registrable, 20),
            typeCounts: top(type, 15),
            statusCounts: statusPairs,
            processCounts: top(process, 20),
            longDomains: Array(long).sorted(),
            encodedDomains: Array(encoded).sorted(),
            exfilCandidates: exfil,
            lookalikeFindings: lookalikes,
            namesExcludedCount: namesExcluded,
            keysDroppedAtCapCount: keysDroppedAtCap,
            cappedTallies: cappedTallySet.sorted(),
            interpretation: interp
        )
    }

    public static func render(_ a: DNSLogAnalysis, source: String) -> String {
        var out: [String] = []
        func section(_ title: String, _ pairs: [(String, Int)]) {
            out.append(title); out.append(String(repeating: "-", count: title.count))
            if pairs.isEmpty { out.append("  none observed") }
            else {
                let w = pairs.map { String($0.1).count }.max() ?? 1
                for (name, count) in pairs { out.append("  \(String(count).leftPadded(to: w))  \(name)") }
            }
            out.append("")
        }
        func list(_ title: String, _ items: [String]) {
            out.append(title); out.append(String(repeating: "-", count: title.count))
            if items.isEmpty { out.append("  none observed") } else { for i in items.prefix(20) { out.append("  \(i)") } }
            out.append("")
        }

        out.append("Syxx DNS Log Analysis")
        out.append("=====================")
        out.append("")
        out.append("Source: \(source)")
        out.append("Parsed records: \(a.recordCount)")
        // D022: a tally that hit the distinct-key cap yields a FLOOR, not a measurement. Say
        // "at least" rather than printing a truncated number as though it were the real count.
        let domainFloor = a.cappedTallies.contains("domains") ? "at least " : ""
        let registrableFloor = a.cappedTallies.contains("registrable domains") ? "at least " : ""
        out.append("Unique domains: \(domainFloor)\(a.uniqueDomains)  |  Unique registrable domains: \(registrableFloor)\(a.uniqueRegistrable)")
        // C083: disclose names excluded from analysis by normalizeDomain — the coverage gap
        // must be visible so the record count's completeness claim is honest.
        if a.namesExcludedCount > 0 {
            out.append("Names excluded from analysis: \(a.namesExcludedCount) (non-ASCII, single-label, or invalid characters)")
        }
        // D022: disclose cap-driven truncation. Without this the counts above silently stop
        // growing at 50,000 and are read as measured values — the exact defect shape as K-17.
        if a.keysDroppedAtCapCount > 0 {
            out.append("TRUNCATED — distinct-key cap (50,000) reached for: \(a.cappedTallies.joined(separator: ", "))")
            out.append("  \(a.keysDroppedAtCapCount) record(s) could not be tallied. The counts and the top-N lists")
            out.append("  above are INCOMPLETE lower bounds, not measurements. Re-run on a narrowed capture to measure.")
        }
        out.append("")
        section("Top Domains", a.domainCounts)
        section("Top Registrable Domains", a.registrableCounts)
        section("Record Types", a.typeCounts)
        section("Reply Rcodes / Statuses", a.statusCounts)
        section("Observed Processes", a.processCounts)
        list("Long Domains", a.longDomains)
        list("Encoded / High-Entropy Labels", a.encodedDomains)
        list("Tunneling / Exfil Candidates (entropy + blocklist)", a.exfilCandidates)
        // C6 — brand-impersonation findings, each naming the brand and how it was spoofed.
        if !a.lookalikeFindings.isEmpty {
            out.append("Lookalike / Brand-Impersonation Domains")
            out.append("---------------------------------------")
            for f in a.lookalikeFindings {
                out.append("- \(f.display)  →  impersonates \(f.brand)  [\(f.reason.rawValue)]")
            }
            out.append("")
        }
        out.append("Interpretation")
        out.append("--------------")
        for line in a.interpretation { out.append("- \(line)") }
        out.append("")
        out.append("Notes")
        out.append("-----")
        out.append("- Deterministic heuristic summary of DNS activity, not a definitive conclusion.")
        out.append("- Process attribution + repeated captures are the strongest confirmation steps.")
        out.append("- This output is reproducible and can be signed as evidence with syxx-witness.")
        return out.joined(separator: "\n")
    }

    // MARK: - Heuristics (ported + improved)

    static func normalizeDomain(_ input: String) -> String? {
        var s = input.trimmingCharacters(in: .whitespaces)
            .trimmingCharacters(in: CharacterSet(charactersIn: ".,;:[](){}<>\"'"))
            .lowercased()
        if s.hasPrefix("*.") { s = String(s.dropFirst(2)) }
        while s.hasSuffix(".") { s = String(s.dropLast()) }
        guard !s.isEmpty, s.contains("."), s.count <= 253 else { return nil }
        let labels = s.split(separator: ".", omittingEmptySubsequences: false)
        if labels.contains(where: { $0.isEmpty || $0.count > 63 }) { return nil }
        // C083: allow underscores — valid in DNS (RFC 8552) and ubiquitous in SRV, DKIM,
        // DMARC, mDNS service discovery (_dmarc.example.com, _sip._udp.example.com).
        // Windows parity: this comment previously claimed "WSK-015 already added underscore
        // support on the Windows twin." That was FALSE — `Syxx.Kit/Forensics/DNSLogAnalyzer.cs`
        // rejected `_` outright, so those names returned null and the caller dropped the record
        // from every tally including exfil detection. K-17 added underscore support and the
        // matching `NamesExcludedCount` disclosure to the Windows twin on 2026-08-04; the two
        // character sets and the exclusion counter now genuinely agree.
        let allowed = Set("abcdefghijklmnopqrstuvwxyz0123456789._-")
        if s.contains(where: { !allowed.contains($0) }) { return nil }
        return s
    }

    static func looksEncodedLabel(_ label: String) -> Bool {
        if label.count < 24 { return false }
        let alnum = label.filter { $0.isLetter || $0.isNumber }.count
        let digits = label.filter { $0.isNumber }.count
        if Double(alnum) / Double(max(label.count, 1)) < 0.85 { return false }
        return ExfilAnalyzer.shannonEntropyBitsPerChar(label) >= 3.6 || digits >= 8
    }

    static func statusBucket(_ rcode: String) -> String {
        let t = rcode.lowercased()
        if t.contains("name error") || t.contains("nxdomain") { return "nxdomain" }
        if t.contains("server failure") || t.contains("servfail") { return "servfail" }
        if t.contains("refused") { return "refused" }
        if t.contains("no error") || t.contains("noerror") || t.contains("success") { return "successful" }
        return String(t.prefix(60))
    }

    private static func top(_ dict: [String: Int], _ n: Int) -> [(String, Int)] {
        dict.sorted { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }
            .prefix(n).map { ($0.key, $0.value) }
    }

    private static func interpret(recordCount: Int, domainTop: (String, Int)?,
                                  statusPairs: [(String, Int)], typeCounts: [String: Int],
                                  longCount: Int, encodedCount: Int, exfilCount: Int,
                                  lookalikeCount: Int = 0) -> [String] {
        var interp: [String] = []
        interp.append("Parsed \(recordCount) DNS event record(s) from the log.")
        if let top = domainTop { interp.append("Most-queried domain: \(top.0) (\(top.1) event(s)).") }
        let totalStatus = statusPairs.reduce(0) { $0 + $1.1 }
        let failures = statusPairs.filter { ["nxdomain", "servfail", "refused"].contains($0.0) }.reduce(0) { $0 + $1.1 }
        if totalStatus > 0 && Double(failures) / Double(totalStatus) >= 0.25 {
            interp.append("A substantial share of replies were failures/refusals (\(failures)/\(totalStatus)) — possible blocked tracking, broken config, captive portal, or retry-heavy software.")
        }
        if typeCounts["TXT"] != nil { interp.append("TXT lookups present — common for verification/service discovery; repeated high-volume TXT deserves attribution.") }
        if typeCounts["HTTPS"] != nil || typeCounts["SVCB"] != nil { interp.append("HTTPS/SVCB records present — normal for modern browsers/CDNs.") }
        if longCount > 0 { interp.append("Unusually long names observed — often CDN keys, but worth checking for tunneling/telemetry.") }
        if encodedCount > 0 { interp.append("High-entropy/encoded-looking labels observed — confirm the owning process before treating as suspicious.") }
        if exfilCount > 0 { interp.append("\(exfilCount) domain(s) flagged as tunneling/exfil candidates by the fused entropy+blocklist check.") }
        if lookalikeCount > 0 { interp.append("\(lookalikeCount) domain(s) impersonate a known brand (look-alike characters or a one-character typo) — a phishing/credential-theft hallmark; confirm the owning process.") }
        return interp
    }
}

private extension String {
    func leftPadded(to width: Int) -> String {
        count >= width ? self : String(repeating: " ", count: width - count) + self
    }
}
