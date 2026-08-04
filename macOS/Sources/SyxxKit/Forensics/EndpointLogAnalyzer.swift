// Offline analyzer for endpoint file/process activity logs (line-delimited JSON in the macOS
// Endpoint Security event shape). Companion to DNSLogAnalyzer — deterministic, offline,
// witness-signable — and it emphasizes what matters forensically instead of drowning in noise.
//
// A file-activity log can be multiple GIGABYTES (mostly OPEN/CLOSE reads), so this STREAMS the file
// line-by-line with bounded memory and cheaply skips the read-only flood before parsing JSON.
import Foundation

/// D081 - Explicit outcome for endpoint-log analysis. "nil" is no longer an ambiguous sentinel.
/// Errors are never confused with "no matching patterns" or "not this log type."
public enum EndpointLogAnalysisResult: Sendable {
    case success(String)        // Valid endpoint log analyzed
    case error(String)          // File could not be opened/read (inconclusive — operational failure)
    case notApplicable          // File exists but is not an endpoint security log
}

public enum EndpointLogAnalyzer {

    /// Detect + analyze an endpoint-activity log by path. Returns distinct outcomes:
    /// - `.success(report)` on valid endpoint log analysis
    /// - `.error(reason)` on file-open/read failure (inconclusive)
    /// - `.notApplicable` if the file is not an endpoint security log
    /// **D081:** Error must be distinguishable from "negative" to prevent false negatives.
    public static func analyze(path: String) -> EndpointLogAnalysisResult {
        // D081: Check file existence and readability - errors must be distinguishable
        guard FileManager.default.fileExists(atPath: path) else {
            return .error("File not found: \(path)")
        }
        // Attempt to open the file; streamLines returns nil on failure
        guard FileHandle(forReadingAtPath: path) != nil else {
            return .error("Could not open file for reading: \(path)")
        }
        switch detect(path: path) {
        case "file": return .success(analyzeFile(path: path))
        case "process": return .success(analyzeProcess(path: path))
        default: return .notApplicable
        }
    }

    /// Peek at the first lines (bounded) to classify the log.
    public static func detect(path: String) -> String? {
        var result: String? = nil
        var scanned = 0
        streamLines(path: path) { line in
            let l = line.trimmingCharacters(in: .whitespaces)
            // C086: count every non-empty line scanned, not just JSON-looking ones —
            // the old code left `scanned` at 0 for non-JSON input, causing the "bounded
            // peek" to walk the entire file when the input was not endpoint-log JSON.
            if l.isEmpty { return scanned < 400 }
            scanned += 1
            if !l.hasPrefix("{") { return scanned < 400 }
            if l.contains("\"file\":{") || l.contains("\"file\": {") { result = "file"; return false }
            if l.contains("ES_EVENT_TYPE_NOTIFY_EXEC") || l.contains("ES_EVENT_TYPE_NOTIFY_FORK")
                || l.contains("ES_EVENT_TYPE_NOTIFY_EXIT") { result = "process"; return false }
            return scanned < 400
        }
        return result
    }

    /// Bounded-memory line streamer. `body` returns false to stop early. One buffer realloc per
    /// 1 MB chunk (not per line), so it stays fast on gigabyte files.
    /// D022: caps the buffer at 2 MB to prevent OOM on newline-free input. Lines exceeding
    /// the cap are truncated and marked as such.
    static func streamLines(path: String, _ body: (String) -> Bool) {
        guard let fh = FileHandle(forReadingAtPath: path) else { return }
        defer { try? fh.close() }
        let newline: UInt8 = 0x0A
        let maxBufferBytes = 2_097_152 // 2 MB cap to prevent OOM on hostile input
        var buffer = Data()
        while true {
            let chunk = fh.readData(ofLength: 1 << 20)
            if chunk.isEmpty { break }
            buffer.append(chunk)

            // D022: if buffer exceeds the cap and no newline found, truncate and emit.
            if buffer.count > maxBufferBytes, buffer.lastIndex(of: newline) == nil {
                let truncated = buffer.prefix(maxBufferBytes)
                if var line = String(data: truncated, encoding: .utf8) {
                    line += " [TRUNCATED: line exceeded 2 MB buffer]"
                    if !body(line) { return }
                }
                buffer.removeAll(keepingCapacity: true)
                continue
            }

            guard let lastNL = buffer.lastIndex(of: newline) else { continue }
            let processable = buffer.prefix(through: lastNL)
            var start = processable.startIndex
            while let nl = processable[start...].firstIndex(of: newline) {
                if let s = String(data: processable[start..<nl], encoding: .utf8) {
                    if !body(s) { return }
                }
                start = processable.index(after: nl)
            }
            buffer = Data(buffer.suffix(from: buffer.index(after: lastNL)))
        }
        if !buffer.isEmpty, let s = String(data: buffer, encoding: .utf8) { _ = body(s) }
    }

    // MARK: - Shared

    private static let trustedSigners: Set<String> = ["Apple", "AppStore", "App Store", "DevID", "Developer ID", "Apple System"]
    private static let persistenceMarkers = [
        "/LaunchAgents/", "/LaunchDaemons/", "/StartupItems/", "/usr/lib/cron/", "/private/var/at/",
        "/Library/Preferences/com.apple.loginitems", "/etc/", "/Library/Application Support/com.apple.TCC"
    ]
    private static let unusualExecMarkers = ["/tmp/", "/private/tmp/", "/private/var/tmp/", "/var/tmp/", "/Downloads/", "/Library/Caches/"]
    private static let suspiciousArgMarkers = ["base64", "curl ", "wget ", "| sh", "|sh", "| bash", "osascript -e", "python -c", "python3 -c", " eval ", "/tmp/"]

    private static func obj(_ line: String) -> [String: Any]? {
        guard let data = line.data(using: .utf8) else { return nil }
        return (try? JSONSerialization.jsonObject(with: data)) as? [String: Any]
    }

    private static func signing(_ actor: [String: Any]) -> (String, Int) {
        if let c = actor["signing info (computed)"] as? [String: Any] {
            return (c["signatureSigner"] as? String ?? "Unknown", c["signatureStatus"] as? Int ?? -1)
        }
        return ("Unknown", -1)
    }

    /// C080: distinguish absent signing metadata from a confirmed untrusted signer.
    /// Absent signing info is not evidence of a finding — it means the endpoint log did not
    /// carry signing metadata for this actor. Reporting it as "untrusted" manufactures a
    /// finding from missing data.
    private static func signingAbsent(_ signer: String, _ status: Int) -> Bool {
        signer == "Unknown" && status == -1
    }

    private static func untrusted(_ signer: String, _ status: Int) -> Bool {
        // C080: absent signing metadata is not untrusted — it is unknown.
        if signingAbsent(signer, status) { return false }
        return status != 0 || !trustedSigners.contains(signer)
    }

    private static func shortEvent(_ e: String) -> String { e.replacingOccurrences(of: "ES_EVENT_TYPE_NOTIFY_", with: "") }

    private static func top(_ dict: [String: Int], _ n: Int) -> [(String, Int)] {
        dict.sorted { $0.value != $1.value ? $0.value > $1.value : $0.key < $1.key }.prefix(n).map { ($0.key, $0.value) }
    }

    private static func section(_ out: inout [String], _ title: String, _ pairs: [(String, Int)]) {
        out.append(title); out.append(String(repeating: "-", count: title.count))
        if pairs.isEmpty { out.append("  none observed") }
        else {
            let w = pairs.map { String($0.1).count }.max() ?? 1
            for (name, count) in pairs { out.append("  \(String(count).leftPad(w))  \(name)") }
        }
        out.append("")
    }

    private static func list(_ out: inout [String], _ title: String, _ items: [String], limit: Int = 25) {
        out.append(title); out.append(String(repeating: "-", count: title.count))
        if items.isEmpty { out.append("  none observed") } else { for i in items.prefix(limit) { out.append("  \(i)") } }
        if items.count > limit { out.append("  … and \(items.count - limit) more") }
        out.append("")
    }

    // MARK: - File events

    static func analyzeFile(path: String) -> String {
        var openClose = 0, mutations = 0
        // B094: track parse loss so corrupted/schema-drifted records cannot silently produce
        // "nothing stands out" forensic prose from an incomplete input.
        var totalNonEmpty = 0, rejected = 0
        var eventTypes = [String: Int](), byProcess = [String: Int](), byDir = [String: Int]()
        var untrustedWriters = [String: Int]()
        // C080: separate bucket for writers whose signing metadata was absent — not a finding,
        // but disclosed so the operator knows signing info was unavailable for these actors.
        var signingUnknownWriters = [String: Int]()
        var persistenceHits = Set<String>()

        streamLines(path: path) { line in
            let l = line.trimmingCharacters(in: .whitespaces)
            if l.isEmpty { return true }
            totalNonEmpty += 1
            if l.contains("NOTIFY_OPEN") || l.contains("NOTIFY_CLOSE") { openClose += 1; return true }
            guard let j = obj(l), let event = j["event"] as? String, let file = j["file"] as? [String: Any] else {
                rejected += 1
                return true
            }
            mutations += 1
            eventTypes[shortEvent(event), default: 0] += 1
            let dest = (file["destination"] as? String) ?? (file["source"] as? String) ?? "(unknown)"
            let actor = file["process"] as? [String: Any] ?? [:]
            let procName = (actor["name"] as? String) ?? "unknown"
            byProcess[procName, default: 0] += 1
            byDir[(dest as NSString).deletingLastPathComponent, default: 0] += 1
            let (signer, status) = signing(actor)
            if untrusted(signer, status) { untrustedWriters["\(procName) [\(signer)]", default: 0] += 1 }
            // C080: absent signing metadata goes to a separate unknown bucket, not untrusted.
            if signingAbsent(signer, status) { signingUnknownWriters[procName, default: 0] += 1 }
            if persistenceMarkers.contains(where: { dest.contains($0) }) {
                persistenceHits.insert("\(shortEvent(event))  \(dest)   ⟵ \(procName) [\(signer)]")
            }
            return true
        }

        var out: [String] = []
        out.append("Syxx Endpoint Analysis — File events")
        out.append("====================================")
        out.append("")
        out.append("Source: \(path)")
        out.append("Read events skipped (OPEN/CLOSE): \(openClose)")
        out.append("Mutation events analyzed (CREATE/WRITE/DELETE/RENAME/LINK): \(mutations)")
        // B094: surface parse loss — rejected records may indicate corruption, schema drift,
        // or hostile input designed to hide findings behind skipped lines.
        if rejected > 0 {
            out.append("Records rejected (unparseable or schema mismatch): \(rejected) of \(totalNonEmpty)")
        }
        out.append("")
        section(&out, "Mutation Types", top(eventTypes, 10))
        section(&out, "Top Mutating Processes", top(byProcess, 20))
        list(&out, "⚠️  Writes to Persistence Locations (LaunchAgents/Daemons/cron/etc.)", persistenceHits.sorted())
        section(&out, "⚠️  Untrusted / Ad-hoc Signers That Modified Files", top(untrustedWriters, 20))
        // C080: surface writers whose signing metadata was absent — neutral wording, not a finding.
        section(&out, "Signing Info Unavailable (writers)", top(signingUnknownWriters, 20))
        section(&out, "Top Written Directories", top(byDir, 15))
        out.append("Interpretation")
        out.append("--------------")
        var interp = ["Analyzed \(mutations) file-mutation event(s); ignored \(openClose) read-only OPEN/CLOSE events."]
        // B094: a report built on rejected records is explicitly incomplete — say so.
        if rejected > 0 { interp.append("⚠️  \(rejected) record(s) could not be parsed — this report is PARTIAL. Corrupt, schema-drifted, or hostile records may hide persistence or execution events.") }
        if !persistenceHits.isEmpty { interp.append("\(persistenceHits.count) write(s) landed in persistence locations — inspect the process + file; this is where launch-persistence installs.") }
        if !untrustedWriters.isEmpty { interp.append("\(untrustedWriters.count) untrusted/ad-hoc signer(s) modified files — confirm each is expected; Apple/DevID-signed writers are the baseline.") }
        // C080: disclose signing-unknown count with neutral wording — not a finding, not a clean bill.
        if !signingUnknownWriters.isEmpty { interp.append("\(signingUnknownWriters.count) writer(s) had no signing metadata — signing status is unknown, not untrusted.") }
        // B094: "nothing stands out" only when zero records were rejected — an incomplete
        // parse must not produce reassuring prose.
        if persistenceHits.isEmpty && untrustedWriters.isEmpty && rejected == 0 { interp.append("No writes to persistence locations and no untrusted signers — nothing stands out at the heuristic level.") }
        for i in interp { out.append("- \(i)") }
        out.append("")
        out.append("Notes\n-----")
        out.append("- Deterministic heuristic summary; not a malware conclusion. Persistence-path + signer are the strongest first signals.")
        out.append("- Reproducible and signable with syxx-witness.")
        return out.joined(separator: "\n")
    }

    // MARK: - Process events

    static func analyzeProcess(path: String) -> String {
        var execs = 0, forks = 0, exits = 0
        // B094: track parse loss so corrupted/schema-drifted records cannot silently produce
        // "nothing stands out" forensic prose from an incomplete input.
        var totalNonEmpty = 0, rejected = 0
        var byProcess = [String: Int]()
        var untrustedExecs = Set<String>(), unusualPathExecs = Set<String>()
        var suspiciousArgExecs: [String] = []
        // C080: separate bucket for launches whose signing metadata was absent.
        var signingUnknownExecs = Set<String>()

        streamLines(path: path) { line in
            let l = line.trimmingCharacters(in: .whitespaces)
            if l.isEmpty { return true }
            totalNonEmpty += 1
            if l.contains("NOTIFY_FORK") { forks += 1; return true }
            if l.contains("NOTIFY_EXIT") { exits += 1; return true }
            guard l.contains("NOTIFY_EXEC") else { return true }
            guard let j = obj(l), let actor = j["process"] as? [String: Any] else {
                rejected += 1
                return true
            }
            execs += 1
            let path = (actor["path"] as? String) ?? "(unknown)"
            let procName = (actor["name"] as? String) ?? (path as NSString).lastPathComponent
            byProcess[procName, default: 0] += 1
            let (signer, status) = signing(actor)
            if untrusted(signer, status) { untrustedExecs.insert("\(path) [\(signer)]") }
            // C080: absent signing metadata goes to a separate unknown bucket, not untrusted.
            if signingAbsent(signer, status) { signingUnknownExecs.insert(path) }
            if unusualExecMarkers.contains(where: { path.contains($0) }) { unusualPathExecs.insert("\(path) [\(signer)]") }
            if let args = actor["arguments"] as? [String] {
                let joined = args.joined(separator: " ").lowercased()
                if suspiciousArgMarkers.contains(where: { joined.contains($0) }) {
                    suspiciousArgExecs.append("\(procName): \(String(joined.prefix(120)))")
                }
            }
            return true
        }

        var out: [String] = []
        out.append("Syxx Endpoint Analysis — Process events")
        out.append("=======================================")
        out.append("")
        out.append("Source: \(path)")
        out.append("Launches (EXEC): \(execs)   forks: \(forks)   exits: \(exits)")
        // B094: surface parse loss — rejected records may indicate corruption, schema drift,
        // or hostile input designed to hide findings behind skipped lines.
        if rejected > 0 {
            out.append("Records rejected (unparseable or schema mismatch): \(rejected) of \(totalNonEmpty)")
        }
        out.append("")
        section(&out, "Top Launched Processes", top(byProcess, 20))
        list(&out, "⚠️  Untrusted / Ad-hoc Signed Launches", untrustedExecs.sorted())
        // C080: surface launches whose signing metadata was absent — neutral wording, not a finding.
        list(&out, "Signing Info Unavailable (launches)", signingUnknownExecs.sorted())
        list(&out, "⚠️  Launches From Unusual Paths (/tmp, Downloads, Caches)", unusualPathExecs.sorted())
        list(&out, "⚠️  Launches With Suspicious Arguments", suspiciousArgExecs)
        out.append("Interpretation")
        out.append("--------------")
        var interp = ["Observed \(execs) process launch(es)."]
        // B094: a report built on rejected records is explicitly incomplete — say so.
        if rejected > 0 { interp.append("⚠️  \(rejected) record(s) could not be parsed — this report is PARTIAL. Corrupt, schema-drifted, or hostile records may hide execution events.") }
        if !untrustedExecs.isEmpty { interp.append("\(untrustedExecs.count) distinct untrusted/ad-hoc launch path(s) — anything not Apple/DevID-signed executing deserves a look.") }
        if !unusualPathExecs.isEmpty { interp.append("Executions from /tmp, Downloads, or Caches observed — classic drop-and-run locations.") }
        if !suspiciousArgExecs.isEmpty { interp.append("Launch arguments contained download/eval/base64 patterns — inspect the full command lines.") }
        // C080: disclose signing-unknown count with neutral wording — not a finding, not a clean bill.
        if !signingUnknownExecs.isEmpty { interp.append("\(signingUnknownExecs.count) launch(es) had no signing metadata — signing status is unknown, not untrusted.") }
        // B094: "nothing stands out" only when zero records were rejected — an incomplete
        // parse must not produce reassuring prose.
        if untrustedExecs.isEmpty && unusualPathExecs.isEmpty && suspiciousArgExecs.isEmpty && rejected == 0 { interp.append("No untrusted signers, odd paths, or suspicious arguments — nothing stands out at the heuristic level.") }
        for i in interp { out.append("- \(i)") }
        out.append("")
        out.append("Notes\n-----")
        out.append("- Deterministic heuristic summary; not a malware conclusion. Signer + exec path + arguments are the strongest first signals.")
        out.append("- Reproducible and signable with syxx-witness.")
        return out.joined(separator: "\n")
    }
}

private extension String {
    func leftPad(_ width: Int) -> String { count >= width ? self : String(repeating: " ", count: width - count) + self }
}
