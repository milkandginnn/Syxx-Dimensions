import Foundation
import SyxxKit

// MARK: - Cage (Sniffer) logic

extension AppModel {

    func toggleCapture() {
        if cageCapturing {
            // B237: the privileged capture runs via XPC and cannot be terminated from here —
            // CaptureProcessBox.store was never called, so terminate() was always a no-op.
            // Cancel the live tail loop and be honest that the helper runs to its duration.
            captureTask?.cancel()
            // B244: cancel the hideCage countdown so it doesn't keep decrementing after stop.
            hideTask?.cancel()
            hideTask = nil
            cageCapturing = false
            cageHidden = false
            cageOutput = "Live tail stopped. The privileged capture continues to its configured duration."
            return
        }
        startRealCapture()
    }

    func hideCage() {
        if !cageCapturing { startRealCapture() }
        // B244: cancel any prior countdown so two calls don't race at 2× speed.
        hideTask?.cancel()
        cageHidden = true
        cageRemain = cageDuration
        hideTask = Task {
            while self.cageRemain > 0 && self.cageHidden && self.cageCapturing {
                try? await Task.sleep(for: .seconds(1))
                guard !Task.isCancelled else { return }
                self.cageRemain -= 1
            }
            guard !Task.isCancelled else { return }
            self.cageHidden = false   // reappear when the timer ends or capture stops
        }
    }

    /// Runs the REAL `syxx cage` capture (raw-socket, so it needs administrator authorization) and
    /// streams its actual captured queries live — never fabricated rows. On a missing binary, a
    /// declined prompt, or a quiet network, it says so honestly and shows nothing.
    ///
    /// STALE-COMMENT CORRECTION (2026-07-29). This used to say the privileged CLI runs under
    /// `osascript ... with administration privileges`. It does not, and has not since the capture
    /// moved to the SMJobBless helper: the real path below is
    /// `PrivilegeManager.shared.startCageCapture(...)` over XPC. The osascript claim is why the
    /// prompt-storm diagnosis listed a third prompt source that no longer exists in this file.
    ///
    /// The tailing design still stands, for a different reason: the helper replies with the
    /// capture's output only ONCE, after the child process exits, so there is no live stdout to
    /// watch. The CLI instead streams one JSON record per captured packet to the file we name with
    /// `--output`, and we TAIL that file every ~0.4s while the capture runs. The XPC call and the
    /// tail loop run concurrently, so the live feed is never blocked on the buffered reply.
    func startRealCapture() {
        guard let bin = Self.locateCageBinary() else {
            cageCapturing = false
            cageRows = []
            cageEventsCount = 0
            cageOutput = "Live capture is unavailable: the syxx capture binary isn't bundled with the app yet. For now, run `sudo syxx cage` from Terminal."
            return
        }
        cageCapturing = true
        cageEventsCount = 0
        cageRows = []
        cageOutput = ""
        let duration = cageDuration
        let outputURL = Self.liveCaptureURL()

        // B237: CaptureProcessBox removed — store() was never called, so terminate() was a no-op.
        // The privileged capture runs via XPC and cannot be terminated from here.
        let resultBox = CaptureResultBox()

        captureTask = Task {
            let runner = Task.detached(priority: .userInitiated) {
                do {
                    // D116: pass the resolved CLI path to the helper over XPC. The helper
                    // validates it (path prefix + code signature) before exec — never derive
                    // the path from Bundle.main inside a bare SMJobBless daemon.
                    let result = try await PrivilegeManager.shared.startCageCapture(
                        cliPath: bin.path,
                        duration: duration,
                        outputPath: outputURL.path,
                        ownerUID: getuid(),
                        ownerGID: getgid()
                    )

                    await resultBox.finish(result.output)
                } catch {
                    // Plain, actionable text to the screen; full technical detail to the diagnostic
                    // sink. A bare `error.localizedDescription` here is what rendered crash-reporter
                    // strings ("… CFErrorDomainLaunchd (8)", "… error 0.") as product copy.
                    let detail = ErrorPresenter.userDetail(for: error, component: "cage.capture") ?? ""
                    await resultBox.finish(cageLaunchErrorMarker + detail)
                }
            }
            defer { runner.cancel() }

            // Live tail: each poll reads only the bytes the file grew by, decodes the new JSON records
            // into rows, and updates the counter + feed. Monotonic, so a partial read never shrinks
            // the display.
            var offset: UInt64 = 0
            var pendingBytes = Data()   // L5: undecodable trailing bytes (split multi-byte char) carried between reads
            var pending = ""
            var accumulated: [CageRow] = []
            var records: [DNSCaptureRecord] = []          // raw records → attribution rate (C058) + per-domain cadence (B190)
            var byDomain: [String: [Date]] = [:]          // B190: per-domain OUTBOUND query timestamps, accumulated live
            var rejectedRows = 0                          // C034: JSON-decode failures (lost/corrupt records)
            var shown = 0
            while !Task.isCancelled {
                let done = await resultBox.finished
                Self.readNewRows(path: outputURL.path, offset: &offset, pendingBytes: &pendingBytes,
                                 pending: &pending, into: &accumulated, records: &records, byDomain: &byDomain,
                                 rejectedRows: &rejectedRows)
                if accumulated.count != shown {
                    shown = accumulated.count
                    // C033: bounded GUI ring buffer — only the newest N rows are rendered.
                    // The full capture stays on disk (NDJSON); the counter stays accurate.
                    let newest = accumulated.suffix(Self.cageMaxGUIRows)
                    self.cageRows = Array(newest.reversed())   // newest-first for the grid
                    self.cageEventsCount = accumulated.count
                }
                if done { break }
                try? await Task.sleep(for: .milliseconds(400))
            }

            // Cancelled (Stop / reset): leave state to the caller, just clean up.
            // B237: do NOT delete the live file here — the privileged helper may still be
            // appending to it. The file will be cleaned up on the next capture start.
            if Task.isCancelled {
                return
            }

            // One last incremental read to catch any bytes written between the final poll and exit.
            Self.readNewRows(path: outputURL.path, offset: &offset, pendingBytes: &pendingBytes,
                             pending: &pending, into: &accumulated, records: &records, byDomain: &byDomain,
                             rejectedRows: &rejectedRows)
            let rawText = await resultBox.result ?? ""
            // Live rows are authoritative when present. If the live file could not be read (e.g. the
            // admin prompt did not hand us ownership), fall back to parsing the buffered stdout so a
            // real capture is never mis-reported as empty.
            // C033: bound the final display rows too — the NDJSON on disk has everything,
            // but the GUI array stays bounded to prevent OOM on long captures.
            let liveRows = Array(accumulated.suffix(Self.cageMaxGUIRows).reversed()) // newest-first
            let finalRows: [CageRow] = liveRows.isEmpty ? Self.parseCageOutput(rawText) : liveRows

            guard !Task.isCancelled else {
                // B237: do NOT delete the live file — the privileged helper may still be writing.
                return
            }

            self.cageRows = finalRows
            self.cageEventsCount = finalRows.count
            // C034: thread rejected-row count so partial/lossy captures are disclosed, not clean.
            self.cageOutput = Self.finalMessage(rawText: rawText, rowCount: finalRows.count,
                                                rejectedRows: rejectedRows)
            self.cageCapturing = false
            self.cageHidden = false

            // C058 + B190: build the Cage section input from the decoded capture RECORDS, not just the
            // set of domain strings, so two signals the row grid used to discard reach synthesis:
            //   • per-domain OUTBOUND query cadence (B190) — the beacon leg is now actually evaluated
            //     instead of the dead `timestamps: []` path that capped `.likelyExfil` unreachable;
            //   • process-attribution completeness (C058) — a mostly-unattributed capture is DEGRADED,
            //     never a silent clean.
            // A zero-record / no-outbound capture still yields nil exfil so Cage stays UNTESTED rather
            // than falsely clean, matching MonitorReportAnalyzer.exfilAssessments' contract. When the
            // live file could not be read we fall back to the parsed stdout rows — a degraded but honest
            // source (clock-only times, parsed process name), never fabricated series.
            if records.isEmpty {
                let fallback = Self.cageSectionInputs(fromRows: finalRows)
                self.sectionInputs.exfil = fallback.exfil
                self.sectionInputs.cageAttributionRate = fallback.attributionRate
            } else {
                self.sectionInputs.exfil = MonitorReportAnalyzer.exfilAssessments(from: records)
                self.sectionInputs.cageAttributionRate = Self.attributionRate(from: records)
            }

            Self.cleanupLiveFile(outputURL)
        }
    }

    // MARK: - Cage static helpers

    /// A unique per-run path under the user's own captures dir for the GUI to tail. Created eagerly so
    /// the privileged CLI (running as root) can write into a user-owned 0700 directory. Also sweeps
    /// any live files a crashed prior run left behind, so the captures dir never accumulates strays.
    nonisolated static func liveCaptureURL() -> URL {
        let dir = FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent(".syxx/captures", isDirectory: true)
        try? FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true,
                                                 attributes: [.posixPermissions: 0o700])
        if let leftovers = try? FileManager.default.contentsOfDirectory(atPath: dir.path) {
            for f in leftovers where f.hasPrefix("gui-live-") && f.hasSuffix(".txt") {
                try? FileManager.default.removeItem(atPath: dir.appendingPathComponent(f).path)
            }
        }
        return dir.appendingPathComponent("gui-live-\(UUID().uuidString).txt")
    }

    nonisolated static func cleanupLiveFile(_ url: URL) {
        try? FileManager.default.removeItem(at: url)
    }

    /// Reads the bytes appended to `path` since `offset`, decodes each COMPLETE JSON line into a
    /// `DNSCaptureRecord`, maps it to a `CageRow`, and appends to `accumulated`. A trailing partial
    /// line (root still writing it) is buffered in `pending` and completed on the next read. Cheap:
    /// only new bytes are ever read and decoded. An unreadable file is a no-op (rows stay as they were).
    nonisolated static func readNewRows(path: String, offset: inout UInt64,
                                        pendingBytes: inout Data,
                                        pending: inout String, into accumulated: inout [CageRow],
                                        records: inout [DNSCaptureRecord],
                                        byDomain: inout [String: [Date]],
                                        rejectedRows: inout Int) {
        guard let size = fileSize(path), size > offset else { return }
        guard let handle = FileHandle(forReadingAtPath: path) else { return }
        defer { try? handle.close() }
        handle.seek(toFileOffset: offset)
        let payload = handle.readDataToEndOfFile()
        // Advance by the bytes actually read, not the stale stat size: the writer may have appended
        // between the stat and this read, and using `size` would re-read (and duplicate) that tail.
        offset += UInt64(payload.count)
        // L5 — a read can end in the middle of a multi-byte UTF-8 character (the writer is mid-write).
        // Decoding the raw payload would fail and silently drop the WHOLE chunk even though `offset`
        // already advanced past it, losing records from both the rows and the attribution/cadence series.
        // Prepend any bytes we could not decode last time, decode the longest valid UTF-8 prefix, and
        // carry the undecodable trailing bytes forward to complete on the next read.
        var buffer = pendingBytes
        buffer.append(payload)
        let (text, remainder) = decodeUTF8Prefix(buffer)
        pendingBytes = remainder
        guard !text.isEmpty else { return }
        pending += text
        // Split into complete lines; keep any unterminated remainder as the next pending buffer.
        var lines = pending.components(separatedBy: "\n")
        pending = lines.removeLast()   // text after the final "\n" (empty when it ended cleanly)
        let dec = JSONDecoder()
        for line in lines {
            let s = line.trimmingCharacters(in: .whitespaces)
            guard !s.isEmpty else { continue }
            // C034: count JSON-decode failures so lost/corrupt records are disclosed rather than
            // silently vanishing from the capture. A non-zero count marks evidence partial.
            guard let d = s.data(using: .utf8),
                  let rec = try? dec.decode(DNSCaptureRecord.self, from: d) else {
                rejectedRows += 1
                continue
            }
            records.append(rec)
            // B190: accumulate this record's per-domain OUTBOUND query time so the row (and the final
            // section input) can evaluate beacon cadence instead of the dead `timestamps: []` path.
            let dom = rec.packet.questions.first?.questionName ?? "unknown"
            if rec.network.direction.lowercased().contains("outbound"), !dom.isEmpty, dom != "unknown",
               let t = MonitorReportAnalyzer.parseTimestamp(rec.timestamp) {
                byDomain[dom, default: []].append(t)
            }
            accumulated.append(cageRow(from: rec, timestamps: byDomain[dom] ?? []))
        }
    }

    /// L5 — decode the longest valid UTF-8 prefix of `data`, returning that string and any undecodable
    /// trailing bytes (a split multi-byte character, at most 3 continuation bytes) to carry forward. A
    /// buffer that is genuinely corrupt (no valid prefix within the last 3 bytes) is decoded lossily so
    /// the tail loop always makes forward progress rather than re-buffering the same bytes forever.
    nonisolated static func decodeUTF8Prefix(_ data: Data) -> (String, Data) {
        if let s = String(data: data, encoding: .utf8) { return (s, Data()) }
        let bytes = [UInt8](data)
        var cut = bytes.count
        let minKeep = max(0, bytes.count - 3)
        while cut > minKeep {
            cut -= 1
            if let s = String(bytes: bytes[0..<cut], encoding: .utf8) {
                return (s, Data(bytes[cut...]))
            }
        }
        return (String(decoding: bytes, as: UTF8.self), Data())
    }

    nonisolated static func fileSize(_ path: String) -> UInt64? {
        guard let attrs = try? FileManager.default.attributesOfItem(atPath: path),
              let size = attrs[.size] as? UInt64 else { return nil }
        return size
    }

    /// Maps a decoded capture record to the same `CageRow` shape the stdout parser produces: severity
    /// comes from the REAL `ExfilAnalyzer` assessment (entropy / blocklist), never an invented one.
    nonisolated static func cageRow(from rec: DNSCaptureRecord, timestamps: [Date]) -> CageRow {
        let domain = rec.packet.questions.first?.questionName ?? "unknown"
        let proc = rec.process.name ?? "unknown"
        let resolver = rec.network.sourcePort == 53 ? rec.network.sourceIP : rec.network.destIP
        let direction = rec.network.direction
        let rcode = rec.packet.rcode
        // B190: assess with the REAL per-domain cadence so the beacon leg participates (not `timestamps: []`).
        let assessment = ExfilAnalyzer.assess(domain: domain, timestamps: timestamps)
        // C059: honest tri-state badge. An unjudgeable domain (`.inconclusive`) must NOT render as the
        // same reassuring INFO as an affirmatively-benign row — it maps to the neutral "not judged"
        // `.unknown` treatment (muted grey), never green, never info-purple.
        let severity: Severity
        switch assessment.suspicion {
        case .likelyExfil:  severity = .critical
        case .suspicious:   severity = .warning
        case .benign:       severity = .info
        case .inconclusive: severity = .unknown
        }
        var note = rcode.isEmpty ? direction : "\(direction) · \(rcode)"
        if assessment.suspicion == .likelyExfil || assessment.suspicion == .suspicious {
            note += " — \(assessment.rationale.first ?? assessment.suspicion.rawValue)"
        }
        return CageRow(time: shortTime(rec.timestamp), domain: domain, note: note,
                       process: proc, resolver: resolver, severity: severity)
    }

    /// "yyyy-MM-dd HH:mm:ss.SSS" -> "HH:mm:ss" for the fixed-width Time column.
    nonisolated static func shortTime(_ ts: String) -> String {
        let parts = ts.split(separator: " ")
        guard parts.count >= 2 else { return ts }
        return String(parts[1].split(separator: ".").first ?? parts[1])
    }

    // L6 — the former private `parseTimestamp` / `exfilAssessments` copies were removed; the GUI live-tail
    // path now calls `MonitorReportAnalyzer.parseTimestamp` / `.exfilAssessments` directly so the GUI and
    // log-analysis paths share one grouping+parsing implementation and cannot drift.

    /// "HH:mm:ss" clock time (all the stdout-parse fallback rows carry) -> a nominal same-reference-day
    /// `Date`. Only inter-arrival intervals matter to the beacon cadence, so the reference day is
    /// immaterial; used solely by the degraded stdout fallback, never the live capture path.
    nonisolated static func parseClockTime(_ time: String) -> Date? {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "HH:mm:ss"
        return f.date(from: time)
    }

    /// C058 — fraction (0...1) of capture records that carried a real process identity (`isAttributed`),
    /// or nil when there were no records. Threaded into `sectionInputs.cageAttributionRate` so a mostly-
    /// unattributed capture is DEGRADED at synthesis, never a silent clean. Computed over EVERY record
    /// (both directions) to match the `Synthesizer.cage` contract wording ("fraction of capture records");
    /// inbound responses are frequently unattributable, which biases the rate toward degraded — the
    /// honest-safe direction.
    nonisolated static func attributionRate(from records: [DNSCaptureRecord]) -> Double? {
        guard !records.isEmpty else { return nil }
        let attributed = records.filter { $0.process.isAttributed }.count
        return Double(attributed) / Double(records.count)
    }

    /// Degraded fallback Cage section input, used ONLY when the live capture file could not be read and we
    /// are left with the parsed stdout rows. Row clock-times ("HH:mm:ss") are reconstructed to nominal
    /// same-day Dates so the beacon cadence can still be evaluated where feasible; attribution comes from
    /// the parsed process name ("unknown"/empty = unattributed). Honest but lossy (no date, no millis, no
    /// direction filter) — never a fabricated series. Returns (nil, nil) for no rows so Cage stays UNTESTED.
    nonisolated static func cageSectionInputs(fromRows rows: [CageRow]) -> (exfil: [ExfilAssessment]?, attributionRate: Double?) {
        guard !rows.isEmpty else { return (nil, nil) }
        var byDomain: [String: [Date]] = [:]
        var order: [String] = []
        for r in rows {
            if byDomain[r.domain] == nil { byDomain[r.domain] = []; order.append(r.domain) }
            if let t = parseClockTime(r.time) { byDomain[r.domain]!.append(t) }
        }
        let exfil = order.map { ExfilAnalyzer.assess(domain: $0, timestamps: byDomain[$0] ?? []) }
        let attributed = rows.filter { $0.process != "unknown" && !$0.process.isEmpty }.count
        return (exfil.isEmpty ? nil : exfil, Double(attributed) / Double(rows.count))
    }

    /// The honest end-of-run status line, reusing the same wording the capture already used.
    nonisolated static func finalMessage(rawText: String, rowCount: Int,
                                          rejectedRows: Int = 0) -> String {
        if rawText.hasPrefix(cageLaunchErrorMarker) {
            let detail = String(rawText.dropFirst(cageLaunchErrorMarker.count))
                .trimmingCharacters(in: .whitespacesAndNewlines)
            // State the failure without a cause when the error carried no readable one, rather than
            // trailing a bare "Could not start capture: " or a crash-reporter fragment.
            return detail.isEmpty ? "Could not start capture." : "Could not start capture: \(detail)"
        }
        // C044: detect capture abort (pcap error mid-run) — any surviving rows are partial
        // evidence from an aborted session, never a normal clean report.
        let aborted = rawText.contains("CAGE_CAPTURE_ABORTED")
        if rowCount > 0 {
            // C034: any rejected records mark the capture as PARTIAL — a clean count would mask
            // evidence loss. The rejected count is surfaced so the user knows the capture is
            // incomplete; downstream synthesis treats this as degraded/inconclusive evidence.
            if aborted {
                return "⚠️ INCONCLUSIVE — Capture aborted after \(rowCount) packet(s) (pcap error). Evidence is partial and must not be treated as conclusive."
            }
            if rejectedRows > 0 {
                return "⚠️ PARTIAL — Captured \(rowCount) DNS packet(s), \(rejectedRows) record(s) rejected (decode failure). Evidence is incomplete."
            }
            return "Captured \(rowCount) DNS packet(s)."
        }
        let cancelled = rawText.contains("-128") || rawText.lowercased().contains("cancel")
        return cancelled
            ? "Capture cancelled — administrator authorization is required for live packet capture."
            : "No packets were detected (a quiet network, or authorization was not granted)."
    }

    nonisolated static func locateCageBinary() -> URL? {
        let fm = FileManager.default
        var candidates: [URL] = []
        // Case-variant fallback: try lowercase then capitalized (the C# twin searches
        // both "syxx.exe" and "Syxx.exe" — see CageView.xaml.cs:130-131).
        for name in ["syxx", "Syxx"] {
            if let res = Bundle.main.resourceURL { candidates.append(res.appendingPathComponent(name)) }
            if let exe = Bundle.main.executableURL { candidates.append(exe.deletingLastPathComponent().appendingPathComponent(name)) }
        }
        // SwiftPM dev build: walk up from the exe dir into .build/debug or .build/release.
        if let exe = Bundle.main.executableURL {
            var dir = exe.deletingLastPathComponent()
            for _ in 0..<5 {
                let debug = dir.appendingPathComponent(".build/debug/syxx")
                let release = dir.appendingPathComponent(".build/release/syxx")
                if fm.fileExists(atPath: debug.path) { candidates.append(debug) }
                if fm.fileExists(atPath: release.path) { candidates.append(release) }
                dir = dir.deletingLastPathComponent()
            }
        }
        // PATH fallback: `which syxx`
        if let path = Self.findInPATH("syxx") { candidates.append(path) }
        return candidates.first { fm.isExecutableFile(atPath: $0.path) }
    }

    /// Resolve a command name via `which` so we can find `syxx` on PATH even when
    /// running outside the app bundle (e.g. `swift run syxx-gui`).
    nonisolated static func findInPATH(_ name: String) -> URL? {
        let proc = Process()
        proc.executableURL = URL(fileURLWithPath: "/usr/bin/which")
        proc.arguments = [name]
        let pipe = Pipe()
        proc.standardOutput = pipe
        proc.standardError = pipe
        guard (try? proc.run()) != nil else { return nil }
        proc.waitUntilExit()
        guard proc.terminationStatus == 0 else { return nil }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        let path = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        guard !path.isEmpty else { return nil }
        return URL(fileURLWithPath: path)
    }

    /// Parses `syxx cage` real per-packet stdout lines — `• [HH:mm:ss] Direction: domain (via proc
    /// | Rcode: code)` — into rows, coloring each by the REAL `ExfilAnalyzer` assessment (entropy /
    /// blocklist), not an invented severity.
    nonisolated static func parseCageOutput(_ text: String) -> [CageRow] {
        var rows: [CageRow] = []
        var byDomain: [String: [Date]] = [:]   // B190: per-domain clock times, so the beacon leg is evaluated here too
        for raw in text.split(whereSeparator: \.isNewline) {
            let line = raw.trimmingCharacters(in: .whitespaces)
            guard line.hasPrefix("• ["), let tEnd = line.firstIndex(of: "]") else { continue }
            let time = String(line[line.index(line.startIndex, offsetBy: 3)..<tEnd])
            let rest = String(line[line.index(after: tEnd)...]).trimmingCharacters(in: .whitespaces)
            guard let colon = rest.firstIndex(of: ":") else { continue }
            let direction = String(rest[..<colon]).trimmingCharacters(in: .whitespaces)
            var tail = String(rest[rest.index(after: colon)...]).trimmingCharacters(in: .whitespaces)
            var proc = "unknown", rcode = "", resolver = ""
            if let paren = tail.firstIndex(of: "(") {
                let meta = String(tail[tail.index(after: paren)...]).replacingOccurrences(of: ")", with: "")
                tail = String(tail[..<paren]).trimmingCharacters(in: .whitespaces)
                for p in meta.split(separator: "|").map({ $0.trimmingCharacters(in: .whitespaces) }) {
                    if p.hasPrefix("via ") { proc = String(p.dropFirst(4)) }
                    if p.hasPrefix("Rcode:") { rcode = p.replacingOccurrences(of: "Rcode:", with: "").trimmingCharacters(in: .whitespaces) }
                    if p.hasPrefix("Resolver:") { resolver = p.replacingOccurrences(of: "Resolver:", with: "").trimmingCharacters(in: .whitespaces) }
                }
            }
            // B190: reconstruct this domain's clock-time series (feasible from the parsed time) so the
            // beacon leg is evaluated in the fallback path rather than silently scoring "no beacon."
            if let t = parseClockTime(time) { byDomain[tail, default: []].append(t) }
            let assessment = ExfilAnalyzer.assess(domain: tail, timestamps: byDomain[tail] ?? [])
            // C059: honest tri-state badge — `.inconclusive` maps to the neutral `.unknown` treatment,
            // never the reassuring INFO an affirmatively-benign row gets.
            let severity: Severity
            switch assessment.suspicion {
            case .likelyExfil:  severity = .critical
            case .suspicious:   severity = .warning
            case .benign:       severity = .info
            case .inconclusive: severity = .unknown
            }
            var note = rcode.isEmpty ? direction : "\(direction) · \(rcode)"
            if assessment.suspicion == .likelyExfil || assessment.suspicion == .suspicious {
                note += " — \(assessment.rationale.first ?? assessment.suspicion.rawValue)"
            }
            rows.append(CageRow(time: time, domain: tail, note: note, process: proc, resolver: resolver, severity: severity))
        }
        return rows.reversed()   // most-recent first
    }
}

/// Sentinel prefix marking a process-launch failure in the buffered stdout so the final-status
/// helper can distinguish "could not start" from a real (possibly empty) capture.
let cageLaunchErrorMarker = "__syxx_launch_error__"

// B237: CaptureProcessBox removed. store() had no callers (the XPC flow never returned a Process),
// so terminate() was always a no-op. The privileged capture cannot be stopped from the GUI; only
// the live tail loop is cancellable.

/// Holds the privileged capture's final stdout once the process exits, so the tail loop can detect
/// completion without blocking on the buffered process.
actor CaptureResultBox {
    private(set) var result: String?
    var finished: Bool { result != nil }
    func finish(_ msg: String) { result = msg }
}
