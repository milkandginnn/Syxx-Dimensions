import Foundation

/// The kind of a single diff line.
public enum DiffLineKind: String, Sendable, Codable, Equatable {
    /// Line present in both sides (no change).
    case context
    /// Line added in the local side (not in remote).
    case added
    /// Line removed from the local side (present in remote only).
    case removed
}

/// A single line in a unified diff output.
public struct DiffLine: Sendable, Codable, Equatable, Identifiable {
    /// Stable identity for SwiftUI ForEach.
    public let id: Int
    /// What kind of line this is.
    public let kind: DiffLineKind
    /// The text content of the line (without prefix).
    public let text: String
    /// Line number in the "old" (remote) file, or nil for added lines.
    public let oldLineNumber: Int?
    /// Line number in the "new" (local) file, or nil for removed lines.
    public let newLineNumber: Int?

    public init(id: Int, kind: DiffLineKind, text: String, oldLineNumber: Int?, newLineNumber: Int?) {
        self.id = id
        self.kind = kind
        self.text = text
        self.oldLineNumber = oldLineNumber
        self.newLineNumber = newLineNumber
    }
}

/// Which section of the web content a diff belongs to.
public enum DiffSection: String, Sendable, Codable, Equatable, CaseIterable {
    case html = "HTML"
    case css = "CSS"
    case javascript = "JavaScript"
}

/// The complete diff result across all three content sections (HTML, CSS, JS).
public struct DiffResult: Sendable, Codable, Equatable {
    /// Diff lines for the HTML section.
    public let htmlDiff: [DiffLine]
    /// Diff lines for the CSS section.
    public let cssDiff: [DiffLine]
    /// Diff lines for the JavaScript section.
    public let jsDiff: [DiffLine]
    /// Total number of changed lines (additions + removals) across all sections.
    public let totalDifferences: Int
    /// True if any differences exist.
    public let hasChanges: Bool

    public init(htmlDiff: [DiffLine], cssDiff: [DiffLine], jsDiff: [DiffLine]) {
        self.htmlDiff = htmlDiff
        self.cssDiff = cssDiff
        self.jsDiff = jsDiff
        let changes = htmlDiff.filter { $0.kind != .context }.count
                      + cssDiff.filter { $0.kind != .context }.count
                      + jsDiff.filter { $0.kind != .context }.count
        self.totalDifferences = changes
        self.hasChanges = changes > 0
    }

    /// Returns the diff lines for a given section.
    public func lines(for section: DiffSection) -> [DiffLine] {
        switch section {
        case .html: return htmlDiff
        case .css: return cssDiff
        case .javascript: return jsDiff
        }
    }

    /// Count of additions (local-only lines) across all sections.
    public var totalAdditions: Int {
        (htmlDiff + cssDiff + jsDiff).filter { $0.kind == .added }.count
    }

    /// Count of removals (remote-only lines) across all sections.
    public var totalRemovals: Int {
        (htmlDiff + cssDiff + jsDiff).filter { $0.kind == .removed }.count
    }
}
