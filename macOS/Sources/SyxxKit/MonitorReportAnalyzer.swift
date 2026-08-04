import Foundation

// The bridge from the SyxxCage capture stream (a "Syxx Cage Capture" report) into the determinative
// engine.
//
// syxx-cage emits NDJSON — one `DNSCaptureRecord` per line. This ingests that stream and derives the
// capture-sourced datapoints the engine can legitimately compute from *passive* traffic:
//   • F3  StubEntropyProbe   → Prophet   (stub/OS randomization quality)
//   • F9  ResponseSizeProbe  → Cage      (response-size / amplification telemetry)
//   • exfil ExfilAnalyzer    → Cage      (per-domain tunnelling / beaconing)
//
// HONESTY CONTRACT — a passive capture does NOT contain the active cross-vantage premises the decisive
// results rest on (authoritative divergence, authenticated denial-of-existence, redirection,
// collector-confirmed leak). Those `SectionInputs` stay nil, so Mirror / Portal / Glutton / Witness stay
// UNTESTED, which hard-gates `.clean`. A capture alone therefore can NEVER produce a confident clean bill
// of health — it lands on inconclusive / refer. This is by design and is asserted in the tests.

public enum MonitorReportAnalyzer {

    public struct Analysis: Sendable {
        public let recordCount: Int
        public let outboundQueries: Int
        public let inboundResponses: Int
        public let stubEntropy: StubEntropyAudit
        public let responseSize: ResponseSizeAudit
        public let conclusion: Conclusion
    }

    /// Parse an NDJSON Syxx Cage Capture stream (one `DNSCaptureRecord` JSON object per line). Blank and
    /// unparseable lines are skipped — a single corrupt line never aborts the whole report.
    public static func parseNDJSON(_ text: String) -> [DNSCaptureRecord] {
        let dec = JSONDecoder()
        return text.split(whereSeparator: \.isNewline).compactMap { line -> DNSCaptureRecord? in
            let s = line.trimmingCharacters(in: .whitespaces)
            guard !s.isEmpty, let d = s.data(using: .utf8) else { return nil }
            return try? dec.decode(DNSCaptureRecord.self, from: d)
        }
    }

    /// Build the `SectionInputs` a capture can legitimately supply. ONLY F3 / F9 / exfil are populated;
    /// every active-probe section stays nil (see the honesty contract above).
    public static func sectionInputs(from records: [DNSCaptureRecord]) -> SectionInputs {
        var inputs = SectionInputs()
        inputs.stubEntropy = StubEntropyProbe.audit(records: records)
        inputs.responseSize = ResponseSizeProbe.audit(records: records)
        inputs.exfil = exfilAssessments(from: records)
        return inputs
    }

    /// Analyze an NDJSON report end to end: parse → capture-sourced SectionInputs → Conclusion.
    public static func analyze(ndjson text: String, now: Date = Date()) -> Analysis {
        let records = parseNDJSON(text)
        let inputs = sectionInputs(from: records)
        let outbound = records.filter { $0.network.direction.lowercased().contains("outbound") }.count
        return Analysis(
            recordCount: records.count,
            outboundQueries: outbound,
            inboundResponses: records.count - outbound,
            stubEntropy: inputs.stubEntropy ?? .untested,
            responseSize: inputs.responseSize ?? .untested,
            conclusion: Synthesizer.conclude(from: inputs, now: now))
    }

    // MARK: - internals

    /// Assess each distinct queried domain for tunnelling/beaconing, recovering per-domain query
    /// timestamps from the capture so cadence analysis works. Returns nil when no queries were seen, so
    /// Cage stays untested rather than falsely clean.
    /// L6 — `public` so the SyxxApp GUI live-tail path reuses this single canonical grouping instead of a
    /// private copy that could drift; Windows already delegates to `MonitorReportAnalyzer.ExfilAssessments`.
    public static func exfilAssessments(from records: [DNSCaptureRecord]) -> [ExfilAssessment]? {
        var byDomain: [String: [Date]] = [:]
        var order: [String] = []
        for rec in records {
            guard rec.network.direction.lowercased().contains("outbound") else { continue }
            guard let q = rec.packet.questions.first?.questionName, !q.isEmpty else { continue }
            if byDomain[q] == nil { byDomain[q] = []; order.append(q) }
            if let t = parseTimestamp(rec.timestamp) { byDomain[q]!.append(t) }
        }
        guard !order.isEmpty else { return nil }
        return order.map { ExfilAnalyzer.assess(domain: $0, timestamps: byDomain[$0] ?? []) }
    }

    /// L6 — `public` so the SyxxApp GUI live-tail path reuses this single canonical parser (see
    /// `exfilAssessments`). L1 — accept BOTH the millis and no-millis capture timestamp forms so the same
    /// capture file parses identically on macOS and Windows (Windows `ParseTimestamp` already dual-accepts).
    public static func parseTimestamp(_ s: String) -> Date? {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd HH:mm:ss.SSS"   // the SyxxCage capture timestamp format
        if let d = f.date(from: s) { return d }
        f.dateFormat = "yyyy-MM-dd HH:mm:ss"        // L1: also accept the no-millis form for Windows parity
        return f.date(from: s)
    }
}
