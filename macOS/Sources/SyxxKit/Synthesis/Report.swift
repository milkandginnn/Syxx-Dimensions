import Foundation

/// A raw fact a section observed — distinct from an interpreted `EvidenceItem`. Several sections
/// legitimately observe the same fact (your local resolver shows up in Prophet, Mirror, and
/// Portal), so the report merges these: identical values collapse to one line, but a value that
/// **disagrees** between sections is kept and flagged — a datapoint that "changed" between checks
/// is itself meaningful (often a split-horizon / interception signal).
public struct Datapoint: Codable, Sendable, Hashable {
    public let key: String        // canonical id, e.g. "local_resolver"
    public let label: String      // human label, e.g. "Local DNS resolver"
    public let value: String
    public let section: Section
    public init(key: String, label: String, value: String, section: Section) {
        self.key = key; self.label = label; self.value = value; self.section = section
    }
}

/// One distinct value for a datapoint, plus which sections reported it.
public struct DatapointValue: Codable, Sendable, Hashable {
    public let value: String
    public let sections: [Section]
    public init(value: String, sections: [Section]) { self.value = value; self.sections = sections }
}

/// A datapoint after de-duplication. `values.count == 1` means every reporter agreed; `> 1` is a
/// discrepancy (each value carries the sections that reported it). For set-valued keys like
/// `target_answer` (which hold multiple A/AAAA records per section), `isConflicting` requires
/// ZERO overlap between sections' value sets — benign multi-A/AAAA domains must not false-fire.
public struct MergedDatapoint: Codable, Sendable, Hashable {
    public let key: String
    public let label: String
    public let values: [DatapointValue]
    public init(key: String, label: String, values: [DatapointValue]) {
        self.key = key; self.label = label; self.values = values
    }
    /// True when sections disagree. For scalar keys (e.g. `local_resolver`), any value mismatch
    /// is a conflict. For set-valued keys (`target_answer`), conflict requires non-overlapping sets
    /// — a multi-A/AAAA domain naturally has multiple values and should not false-fire.
    public var isConflicting: Bool {
        guard values.count > 1 else { return false }
        // Scalar keys: any mismatch is a conflict
        guard key == "target_answer" else { return true }
        // Set-valued: conflict only when sections have ZERO overlap (disjoint answer sets).
        // Multi-A/AAAA domains with partially overlapping IPs between sections are benign.
        // B170: compare pairwise, not anchored on the first value — with values A1:[mirror],
        // A2:[mirror,prophet], A3:[prophet] the old code flagged A3's {prophet} as disjoint
        // from A1's {mirror} even though A2 bridges them (answer sets overlap on A2).
        let allSets = values.map { Set($0.sections) }
        for i in 0..<allSets.count {
            guard !allSets[i].isEmpty else { continue }
            for j in (i + 1)..<allSets.count {
                guard !allSets[j].isEmpty else { continue }
                if allSets[i].isDisjoint(with: allSets[j]) { return true }
            }
        }
        return false
    }
}

public enum DatapointBag {
    /// De-duplicate a bag of datapoints by key. Identical values collapse to one entry (recording
    /// every section that agreed); differing values for the same key are preserved as a conflict.
    /// First-seen key order is kept so the report is stable.
    public static func merge(_ bag: [Datapoint]) -> [MergedDatapoint] {
        var keyOrder: [String] = []
        var labels: [String: String] = [:]
        var valuesByKey: [String: [String]] = [:]           // distinct values, first-seen order
        var sectionsByKeyValue: [String: [Section]] = [:]   // "key\u{1}value" -> sections

        for d in bag {
            if valuesByKey[d.key] == nil { keyOrder.append(d.key); valuesByKey[d.key] = []; labels[d.key] = d.label }
            if !valuesByKey[d.key]!.contains(d.value) { valuesByKey[d.key]!.append(d.value) }
            let vk = d.key + "\u{1}" + d.value
            if sectionsByKeyValue[vk] == nil { sectionsByKeyValue[vk] = [] }
            if !sectionsByKeyValue[vk]!.contains(d.section) { sectionsByKeyValue[vk]!.append(d.section) }
        }

        return keyOrder.map { key in
            let vals = valuesByKey[key]!.map { v in
                DatapointValue(value: v, sections: sectionsByKeyValue[key + "\u{1}" + v] ?? [])
            }
            return MergedDatapoint(key: key, label: labels[key] ?? key, values: vals)
        }
    }
}

/// Pulls the raw facts out of each section's typed result. Focused on the datapoints that recur
/// across sections (resolver, DNS encryption, answers) — exactly the ones that would otherwise be
/// duplicated in the report.
public enum DatapointExtractor {
    public static func extract(from inputs: SectionInputs) -> [Datapoint] {
        var dp: [Datapoint] = []
        func add(_ key: String, _ label: String, _ value: String, _ section: Section) {
            let v = value.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !v.isEmpty else { return }
            dp.append(Datapoint(key: key, label: label, value: v, section: section))
        }
        func tri(_ s: ConditionState, yes: String, no: String) -> String { s == .yes ? yes : (s == .no ? no : "unknown") }

        if let c = inputs.comparison {
            add("local_resolver", "Local DNS resolver", c.localServer, .mirror)
            for r in c.localRecords where r.type == 1 || r.type == 28 {
                add("target_answer", "Answer for the tested domain", r.rdata, .mirror)
            }
        }
        if let n = inputs.network {
            if let first = n.dnsServers.first { add("local_resolver", "Local DNS resolver", first, .portal) }
            add("dns_encryption", "DNS encryption", tri(n.encryptedDNS.state, yes: "encrypted", no: "cleartext (Do53)"), .portal)
            add("vpn", "VPN", tri(n.vpn.state, yes: "active", no: "inactive"), .portal)
            add("captive_portal", "Captive portal", tri(n.captivePortal.state, yes: "present", no: "none"), .portal)
        }
        if let r = inputs.resolution {
            for rec in r.records where rec.type == 1 || rec.type == 28 {
                add("target_answer", "Answer for the tested domain", rec.rdata, .prophet)
            }
        }
        if let d = inputs.dnssec { add("dnssec", "DNSSEC posture", d.rawValue, .prophet) }
        if let l = inputs.leak {
            for res in l.observedResolvers { add("observed_resolver", "Resolver that observed your query", res, .glutton) }
        }
        return dp
    }
}

/// The finished report: the result plus a **de-duplicated** view of every fact the run gathered.
public struct SynthesisReport: Sendable {
    public let conclusion: Conclusion
    public let datapoints: [MergedDatapoint]
    public init(conclusion: Conclusion, datapoints: [MergedDatapoint]) {
        self.conclusion = conclusion; self.datapoints = datapoints
    }
    public var conflicts: [MergedDatapoint] { datapoints.filter { $0.isConflicting } }

    public func renderText() -> String {
        var out: [String] = []
        out.append("SYXX: DNS analysis report")
        // Result now comes from the matrix determinative engine (`conclusion.determination`).
        if let det = conclusion.determination {
            let resultStr: String
            switch det.state {
            case .untested:       resultStr = "Not Yet Tested"
            case .inconclusive:   resultStr = "Inconclusive"
            case .clean:          resultStr = "Clean"
            case .threatDetected: resultStr = det.diagnoses.first?.displayName ?? "Tampering detected"
            }
            out.append("Conclusion: \(resultStr)")
            out.append(det.explanation)
            if det.diagnoses.count > 1 {
                out.append("Also detected: " + det.diagnoses.dropFirst()
                    .map { "\($0.displayName) [\($0.confidence.rawValue)]" }.joined(separator: ", "))
            }
            if !det.suppressed.isEmpty {
                out.append("Unverified signals: " + det.suppressed
                    .map { "\($0.displayName) (\($0.reason.rawValue))" }.joined(separator: ", "))
            }
        } else {
            out.append("Conclusion: Unfinished. The determinative engine could not evaluate this run.")
        }

        out.append("")
        out.append("FACTS (de-duplicated across all checks)")
        for d in datapoints {
            if d.isConflicting {
                let variants = d.values.map { "\($0.value) [\($0.sections.map(\.rawValue).joined(separator: ","))]" }.joined(separator: "  |  ")
                out.append("  ⚠ \(d.label): DISCREPANCY — \(variants)")
            } else if let only = d.values.first {
                out.append("  • \(d.label): \(only.value)  [\(only.sections.map(\.rawValue).joined(separator: ","))]")
            }
        }

        if !conclusion.evidence.isEmpty {
            out.append("")
            out.append("WHY")
            for e in conclusion.evidence { out.append("  • [\(e.section.rawValue)] \(e.observation)") }
        }

        out.append("")
        out.append("SECTIONS")
        for s in conclusion.sectionStatuses { out.append("  • \(s.section.rawValue): \(s.status.rawValue)") }

        if !conclusion.advisories.isEmpty {
            out.append("")
            out.append("EXPOSURE FINDINGS")
            for a in conclusion.advisories { out.append("  • \(a.title): \(a.detail)") }
        }
        out.append("")
        out.append("NEXT STEPS")
        out.append("  • Review the findings above and verify with a second vantage point if any section is inconclusive.")
        out.append("  • Re-run Syxx after changing network conditions (VPN, different Wi-Fi) if threats are detected.")
        return out.joined(separator: "\n")
    }
}

public enum ReportBuilder {
    /// Full report: synthesize the result AND compile the de-duplicated datapoint view.
    public static func build(from inputs: SectionInputs,
                             config: SynthesisEngine.Config = .init(),
                             now: Date = Date()) -> SynthesisReport {
        let conclusion = Synthesizer.conclude(from: inputs, config: config, now: now)
        let merged = DatapointBag.merge(DatapointExtractor.extract(from: inputs))
        return SynthesisReport(conclusion: conclusion, datapoints: merged)
    }
}
