using System;
using System.Collections.Generic;
using System.Linq;

namespace Syxx.Kit
{
    /// <summary>
    /// Minimal LCS-based unified line diff — shows *what* changed between the locally downloaded
    /// content and the independently fetched remote copy, not just that a hash differs. Lines are
    /// prefixed "  " (context), "- " (only local), "+ " (only remote); context runs collapse to
    /// "  ⋮". Empty result means identical. Mirrors the macOS SyxxKit TextDiff.
    /// </summary>
    public static class TextDiff
    {
        public static List<string> Unified(string local, string remote, int context = 2, int maxLines = 500)
        {
            var x = local.Split('\n').ToList();
            var y = remote.Split('\n').ToList();
            if (x.SequenceEqual(y)) return new List<string>();

            // D062: cap input lines BEFORE building the O(mn) LCS matrix. The previous
            // implementation allocated the full matrix then truncated only the output,
            // so a hostile page of ~10^5 lines produced a ~10^10-cell matrix — OOM/freeze
            // before any output cap applied. 4000 lines per side bounds the matrix to
            // ~16M cells (~128 MB int storage), well within safe limits.
            const int MaxInputLines = 4000;
            bool inputTruncated = x.Count > MaxInputLines || y.Count > MaxInputLines;
            if (inputTruncated)
            {
                if (x.Count > MaxInputLines) x = x.GetRange(0, MaxInputLines);
                if (y.Count > MaxInputLines) y = y.GetRange(0, MaxInputLines);
            }

            int m = x.Count, n = y.Count;
            var dp = new int[m + 1, n + 1];
            for (int i = m - 1; i >= 0; i--)
                for (int j = n - 1; j >= 0; j--)
                    dp[i, j] = x[i] == y[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);

            var outp = new List<string>();
            int a = 0, b = 0;
            while (a < m && b < n)
            {
                if (x[a] == y[b]) { outp.Add("  " + x[a]); a++; b++; }
                else if (dp[a + 1, b] >= dp[a, b + 1]) { outp.Add("- " + x[a]); a++; }
                else { outp.Add("+ " + y[b]); b++; }
            }
            while (a < m) { outp.Add("- " + x[a]); a++; }
            while (b < n) { outp.Add("+ " + y[b]); b++; }

            var collapsed = Collapse(outp, context);
            if (inputTruncated)
            {
                collapsed.Add($"  ⋮ (input truncated — content exceeded {MaxInputLines}-line diff limit)");
            }
            if (collapsed.Count > maxLines)
            {
                var trimmed = collapsed.GetRange(0, maxLines);
                trimmed.Add($"  … (truncated {collapsed.Count - maxLines} more diff lines)");
                return trimmed;
            }
            return collapsed;
        }

        private static List<string> Collapse(List<string> lines, int context)
        {
            var changeIdx = new List<int>();
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].StartsWith("+ ", StringComparison.Ordinal) || lines[i].StartsWith("- ", StringComparison.Ordinal)) changeIdx.Add(i);
            if (changeIdx.Count == 0) return new List<string>();

            var keep = new bool[lines.Count];
            foreach (var idx in changeIdx)
                for (int k = Math.Max(0, idx - context); k <= Math.Min(lines.Count - 1, idx + context); k++) keep[k] = true;

            var result = new List<string>();
            bool skipping = false;
            for (int i = 0; i < lines.Count; i++)
            {
                if (keep[i]) { result.Add(lines[i]); skipping = false; }
                else if (!skipping) { result.Add("  ⋮"); skipping = true; }
            }
            return result;
        }
    }
}
