// Ported into SyxxKit from the sibling LyxTools project (LyxToolsKit/Analysis + Core).
// Capture/forensics: classify captured domains against known ad/tracker blocklists so a
// raw query log becomes "which of these are trackers, and which lists already cover them."
import Foundation

public struct NamedDomainList: Hashable, Codable, Sendable {
    public let name: String
    public let domains: Set<String>

    public init(name: String, domains: Set<String>) {
        self.name = name
        self.domains = domains
    }

    public init(name: String, rawText: String) {
        self.name = name
        self.domains = Set(
            rawText.split(whereSeparator: \.isNewline)
                .compactMap { Self.normalize(String($0)) }
        )
    }

    /// True when `domain` (or any of its parent domains) is on this list.
    public func matches(_ domain: String) -> Bool {
        let d = domain.lowercased()
        if domains.contains(d) { return true }
        var labels = d.split(separator: ".").map(String.init)
        while labels.count > 2 {
            labels.removeFirst()
            if domains.contains(labels.joined(separator: ".")) { return true }
        }
        if labels.count == 2, domains.contains(labels.joined(separator: ".")) { return true }
        return false
    }

    private static func normalize(_ line: String) -> String? {
        let trimmed = line.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }
        guard !trimmed.hasPrefix("#") else { return nil }
        guard !trimmed.hasPrefix("!") else { return nil }

        let withoutInlineComment = trimmed.split(separator: "#", maxSplits: 1).first.map(String.init) ?? trimmed
        let tokens = withoutInlineComment.split(separator: " ").map(String.init)
        let candidate = tokens.last ?? withoutInlineComment
        let cleaned = candidate
            .replacingOccurrences(of: "0.0.0.0", with: "")
            .replacingOccurrences(of: "127.0.0.1", with: "")
            .trimmingCharacters(in: CharacterSet(charactersIn: ". ").union(.whitespacesAndNewlines))
            .lowercased()

        guard cleaned.contains(".") else { return nil }
        return cleaned
    }
}

public struct DomainCoverage: Identifiable, Hashable, Codable, Sendable {
    public var id: String { domain }
    public let domain: String
    public let coveredBy: [String]
    public let missingFrom: [String]

    public var coverageRatio: Double {
        let total = coveredBy.count + missingFrom.count
        guard total > 0 else { return 0 }
        return Double(coveredBy.count) / Double(total)
    }
}

public enum BlocklistAnalyzer {
    public static func coverage(for lists: [NamedDomainList]) -> [DomainCoverage] {
        let allDomains = Set(lists.flatMap(\.domains)).sorted()
        return allDomains.map { domain in
            let coveredBy = lists.filter { $0.domains.contains(domain) }.map(\.name).sorted()
            let missingFrom = lists.filter { !$0.domains.contains(domain) }.map(\.name).sorted()
            return DomainCoverage(domain: domain, coveredBy: coveredBy, missingFrom: missingFrom)
        }
    }

    /// For each observed domain, the lists that flag it (empty == flagged by none).
    public static func classify(_ observed: [String], against lists: [NamedDomainList]) -> [DomainCoverage] {
        observed.map { domain in
            let coveredBy = lists.filter { $0.matches(domain) }.map(\.name).sorted()
            let missingFrom = lists.filter { !$0.matches(domain) }.map(\.name).sorted()
            return DomainCoverage(domain: domain.lowercased(), coveredBy: coveredBy, missingFrom: missingFrom)
        }
    }

    public static func overlapMatrix(for lists: [NamedDomainList]) -> [String: [String: Int]] {
        var matrix: [String: [String: Int]] = [:]
        for lhs in lists {
            var row: [String: Int] = [:]
            for rhs in lists {
                row[rhs.name] = lhs.domains.intersection(rhs.domains).count
            }
            matrix[lhs.name] = row
        }
        return matrix
    }
}
