import Foundation

/// Line-based unified diff engine producing structured `DiffLine` arrays with line numbers.
///
/// Uses the same LCS algorithm as `TextDiff` but outputs `DiffLine` objects suitable for the
/// Mirror terminal-style diff view (with line numbers, kind tags, and section awareness).
///
/// Reuses `TextDiff.unified` internally for the LCS computation, then parses the prefixed
/// output into structured `DiffLine` values. This keeps a single proven diff implementation.
public enum DiffEngine {

    /// Compare two strings and produce a structured diff.
    /// - Parameters:
    ///   - remote: The "old" content (from the Dimension server).
    ///   - local: The "new" content (from the user's device).
    ///   - context: Number of unchanged context lines to keep around each change (default 2).
    /// - Returns: Array of `DiffLine` with line numbers and kind tags.
    public static func diff(remote: String, local: String, context: Int = 2) -> [DiffLine] {
        // Fast path: identical content
        if remote == local { return [] }

        // Delegate to the proven TextDiff for LCS computation
        let rawLines = TextDiff.unified(local, remote, context: context, maxLines: 10_000)

        // Parse the "  ", "- ", "+ " prefixed lines into DiffLine structs with line numbers
        var result: [DiffLine] = []
        var oldLine = 1
        var newLine = 1
        var id = 0

        for raw in rawLines {
            guard raw.count >= 2 else { continue }
            let prefix = String(raw.prefix(2))
            let text = String(raw.dropFirst(2))

            // TextDiff convention: x = local (first arg), y = remote (second arg).
            // "-" prefix = line from x (local) — only in local, NOT in remote.
            // "+" prefix = line from y (remote) — only in remote, NOT in local.
            //
            // Mirror convention: "old" = remote (server baseline), "new" = local (user's device).
            // So: line only in local = ADDED (present in new, missing from old) → .added
            //     line only in remote = REMOVED (present in old, missing from new) → .removed
            switch prefix {
            case "  ":
                // Context line: present in both
                result.append(DiffLine(
                    id: id, kind: .context, text: text,
                    oldLineNumber: oldLine, newLineNumber: newLine
                ))
                oldLine += 1
                newLine += 1
            case "- ":
                // Line from local (x) — ADDED relative to remote baseline
                result.append(DiffLine(
                    id: id, kind: .added, text: text,
                    oldLineNumber: nil, newLineNumber: newLine
                ))
                newLine += 1
            case "+ ":
                // Line from remote (y) — REMOVED relative to local
                result.append(DiffLine(
                    id: id, kind: .removed, text: text,
                    oldLineNumber: oldLine, newLineNumber: nil
                ))
                oldLine += 1
            default:
                // Collapse marker "  ⋮" or truncation notice — keep as context
                result.append(DiffLine(
                    id: id, kind: .context, text: raw,
                    oldLineNumber: nil, newLineNumber: nil
                ))
            }
            id += 1
        }

        return result
    }

    /// Compare two `MirrorBundle` instances across all three content sections.
    public static func compare(remote: MirrorBundle, local: MirrorBundle) -> DiffResult {
        let htmlDiff = diff(remote: remote.htmlContent, local: local.htmlContent)
        let cssDiff = diff(remote: remote.cssContent, local: local.cssContent)
        let jsDiff = diff(remote: remote.jsContent, local: local.jsContent)
        return DiffResult(htmlDiff: htmlDiff, cssDiff: cssDiff, jsDiff: jsDiff)
    }

    /// Count the number of changed lines (additions + removals) in a diff.
    public static func changeCount(_ lines: [DiffLine]) -> Int {
        lines.filter { $0.kind != .context }.count
    }

    /// Count additions only.
    public static func additionCount(_ lines: [DiffLine]) -> Int {
        lines.filter { $0.kind == .added }.count
    }

    /// Count removals only.
    public static func removalCount(_ lines: [DiffLine]) -> Int {
        lines.filter { $0.kind == .removed }.count
    }
}
