import Foundation

/// A minimal LCS-based unified line diff, used to show *what* changed between the locally
/// downloaded content and the independently fetched remote copy — not just that a hash differs.
/// Output lines are prefixed `  ` (context), `- ` (only local), `+ ` (only remote). Runs of
/// unchanged context are collapsed to `  ⋮`. Empty result means the two inputs are identical.
public enum TextDiff {
    public static func unified(_ local: String, _ remote: String, context: Int = 2, maxLines: Int = 500) -> [String] {
        var x = local.components(separatedBy: "\n")
        var y = remote.components(separatedBy: "\n")
        if x == y { return [] }

        // D062: cap input lines BEFORE building the O(mn) LCS matrix. The previous
        // implementation allocated the full matrix then truncated only the output,
        // so a hostile page of ~10^5 lines produced a ~10^10-cell matrix — OOM/freeze
        // before any output cap applied. 4000 lines per side bounds the matrix to
        // ~16M cells (~128 MB Int storage), well within safe limits.
        let maxInputLines = 4000
        let inputTruncated = x.count > maxInputLines || y.count > maxInputLines
        if inputTruncated {
            if x.count > maxInputLines { x = Array(x.prefix(maxInputLines)) }
            if y.count > maxInputLines { y = Array(y.prefix(maxInputLines)) }
        }

        let m = x.count, n = y.count
        var dp = Array(repeating: Array(repeating: 0, count: n + 1), count: m + 1)
        for i in stride(from: m - 1, through: 0, by: -1) {
            for j in stride(from: n - 1, through: 0, by: -1) {
                dp[i][j] = x[i] == y[j] ? dp[i + 1][j + 1] + 1 : max(dp[i + 1][j], dp[i][j + 1])
            }
        }

        var i = 0, j = 0
        var out: [String] = []
        while i < m && j < n {
            if x[i] == y[j] { out.append("  " + x[i]); i += 1; j += 1 }
            else if dp[i + 1][j] >= dp[i][j + 1] { out.append("- " + x[i]); i += 1 }
            else { out.append("+ " + y[j]); j += 1 }
        }
        while i < m { out.append("- " + x[i]); i += 1 }
        while j < n { out.append("+ " + y[j]); j += 1 }

        var collapsed = collapse(out, context: context)
        if inputTruncated {
            collapsed.append("  ⋮ (input truncated — content exceeded \(maxInputLines)-line diff limit)")
        }
        if collapsed.count > maxLines {
            return Array(collapsed.prefix(maxLines)) + ["  … (truncated \(collapsed.count - maxLines) more diff lines)"]
        }
        return collapsed
    }

    private static func collapse(_ lines: [String], context: Int) -> [String] {
        let changeIdx = lines.enumerated()
            .filter { $0.element.hasPrefix("+ ") || $0.element.hasPrefix("- ") }
            .map { $0.offset }
        if changeIdx.isEmpty { return [] }
        var keep = Array(repeating: false, count: lines.count)
        for idx in changeIdx {
            for k in max(0, idx - context)...min(lines.count - 1, idx + context) { keep[k] = true }
        }
        var result: [String] = []
        var skipping = false
        for (idx, line) in lines.enumerated() {
            if keep[idx] { result.append(line); skipping = false }
            else if !skipping { result.append("  ⋮"); skipping = true }
        }
        return result
    }
}
