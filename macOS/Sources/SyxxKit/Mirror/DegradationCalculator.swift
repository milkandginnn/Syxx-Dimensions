import Foundation

/// Computes a `DegradationScore` from a `DiffResult` by measuring the ratio of changed lines
/// to total lines across all sections.
///
/// Formula:
///   score = totalDifferences / max(totalLines, 1)
///
/// Where `totalDifferences` = additions + removals and `totalLines` = total diff lines
/// (context + additions + removals). The score is clamped to [0.0, 1.0].
public enum DegradationCalculator {

    /// Calculate degradation from a completed diff result.
    public static func calculate(diff: DiffResult) -> DegradationScore {
        let allLines = diff.htmlDiff + diff.cssDiff + diff.jsDiff
        let totalLines = allLines.count
        let totalChanges = allLines.filter { $0.kind != .context }.count

        guard totalLines > 0 else {
            return DegradationScore(score: 0.0)
        }

        let raw = Double(totalChanges) / Double(totalLines)
        return DegradationScore(score: raw)
    }

    /// Calculate degradation from two bundles directly (convenience).
    public static func calculate(remote: MirrorBundle, local: MirrorBundle) -> DegradationScore {
        let diff = DiffEngine.compare(remote: remote, local: local)
        return calculate(diff: diff)
    }

    /// Calculate degradation from raw text pairs (convenience for testing).
    public static func calculate(remoteText: String, localText: String) -> DegradationScore {
        let lines = DiffEngine.diff(remote: remoteText, local: localText)
        let totalLines = lines.count
        let changes = lines.filter { $0.kind != .context }.count
        guard totalLines > 0 else {
            return DegradationScore(score: 0.0)
        }
        return DegradationScore(score: Double(changes) / Double(totalLines))
    }
}
