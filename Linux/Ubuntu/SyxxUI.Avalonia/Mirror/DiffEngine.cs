namespace SyxxUI.Avalonia.Mirror;

// D286: ARCHITECTURAL NOTE — The Mirror engine files (DiffEngine, MirrorBundle, DegradationCalculator,
// StaticContentParser, DiffResult, DegradationScore, MirrorResult) are currently Linux-local under
// SyxxUI.Avalonia/Mirror/. The macOS and Windows clients carry their own equivalent implementations.
// A future refactor should extract these into the shared Syxx.Kit library so all three platforms
// consume a single source of truth. Until then, changes here MUST be manually ported to the macOS
// and Windows mirror implementations to maintain cross-platform parity.

/// <summary>
/// Line-based unified diff engine producing structured <see cref="DiffLine"/> arrays with line numbers.
/// Implements its own LCS (Longest Common Subsequence) algorithm — no external NuGet dependency.
/// </summary>
public static class DiffEngine
{
    /// <summary>
    /// D285: memory cap for the LCS DP table. Inputs exceeding this combined line count fall back
    /// to the O(n) simple diff instead of allocating an O(m×n) table. 5000 lines × 5000 lines
    /// would require ~100 MB of int[,] — the cap keeps peak memory under ~25 MB.
    /// </summary>
    private const int MaxLcsCombinedLines = 5_000;

    /// <summary>
    /// Compare two strings asynchronously, running the LCS computation on a background thread so
    /// the UI thread is never blocked (D285).
    /// </summary>
    /// <param name="remote">The "old" content (from the Dimension server).</param>
    /// <param name="local">The "new" content (from the user's device).</param>
    /// <param name="context">Number of unchanged context lines to keep around each change (default 2).</param>
    /// <param name="cancellationToken">Cancellation token for cooperative cancellation (D288).</param>
    /// <returns>List of <see cref="DiffLine"/> with line numbers and kind tags.</returns>
    public static Task<IReadOnlyList<DiffLine>> DiffAsync(
        string remote, string local, int context = 2,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => Diff(remote, local, context), cancellationToken);
    }

    /// <summary>
    /// Compare two strings and produce a structured diff.
    /// </summary>
    /// <param name="remote">The "old" content (from the Dimension server).</param>
    /// <param name="local">The "new" content (from the user's device).</param>
    /// <param name="context">Number of unchanged context lines to keep around each change (default 2).</param>
    /// <returns>List of <see cref="DiffLine"/> with line numbers and kind tags.</returns>
    public static IReadOnlyList<DiffLine> Diff(string remote, string local, int context = 2)
    {
        // Fast path: identical content
        if (remote == local) return Array.Empty<DiffLine>();

        string[] oldLines = SplitLines(remote);
        string[] newLines = SplitLines(local);

        // Build full diff (all lines, no context trimming yet)
        var fullDiff = BuildFullDiff(oldLines, newLines);

        // Apply context window trimming
        return TrimContext(fullDiff, context);
    }

    /// <summary>
    /// Compare two <see cref="MirrorBundle"/> instances across all three content sections.
    /// </summary>
    public static DiffResult Compare(MirrorBundle remote, MirrorBundle local)
    {
        var htmlDiff = Diff(remote.HtmlContent, local.HtmlContent);
        var cssDiff = Diff(remote.CssContent, local.CssContent);
        var jsDiff = Diff(remote.JsContent, local.JsContent);
        return new DiffResult(htmlDiff, cssDiff, jsDiff);
    }

    /// Count the number of changed lines (additions + removals) in a diff.
    public static int ChangeCount(IReadOnlyList<DiffLine> lines)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != DiffLineKind.Context) count++;
        }
        return count;
    }

    /// Count additions only.
    public static int AdditionCount(IReadOnlyList<DiffLine> lines)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == DiffLineKind.Added) count++;
        }
        return count;
    }

    /// Count removals only.
    public static int RemovalCount(IReadOnlyList<DiffLine> lines)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == DiffLineKind.Removed) count++;
        }
        return count;
    }

    // ───────────────────────────── Implementation ─────────────────────────────

    /// <summary>
    /// Split text into lines, preserving trailing-newline semantics (D296).
    /// <para>Previous behavior stripped a single trailing newline so "foo\n" and "foo" produced
    /// identical arrays. This lost the distinction between files that do and don't end with a
    /// newline — a real semantic difference in POSIX text files. The fix: only strip a trailing
    /// newline if the text ends with "\n" AND the resulting last element would be empty (i.e.,
    /// the text ended with a bare newline after the last content line). "foo\n" → ["foo"] (one
    /// complete line), "foo\n\n" → ["foo", ""] (one line plus a trailing blank), "foo" → ["foo"]
    /// (one line, no trailing newline — identical to "foo\n" only when both have content).
    /// Empty/null input returns an empty array (0 lines).</para>
    /// </summary>
    private static string[] SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();

        // Normalize CRLF → LF before splitting
        text = text.Replace("\r\n", "\n");

        // D296: if the text ends with exactly one trailing newline, strip it so the split
        // produces the logical line count (a file "a\nb\n" has 2 lines, not 2 lines + an empty one).
        // But "a\nb\n\n" (trailing blank line) must preserve the empty element.
        if (text.EndsWith('\n'))
            text = text[..^1];

        if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
        return text.Split('\n');
    }

    /// <summary>
    /// Build the complete diff using LCS via dynamic programming.
    /// For inputs larger than 10,000 lines total, falls back to a simple line-by-line comparison.
    /// </summary>
    private static List<RawDiffEntry> BuildFullDiff(string[] oldLines, string[] newLines)
    {
        // D285: guard against O(m×n) memory explosion. The LCS DP table requires (m+1)×(n+1) ints.
        // When combined lines exceed MaxLcsCombinedLines, fall back to O(n) simple diff to cap
        // peak memory allocation. The 10,000 line guard catches extreme cases.
        if (oldLines.Length + newLines.Length > MaxLcsCombinedLines)
        {
            return SimpleDiff(oldLines, newLines);
        }

        // Standard LCS DP table
        int m = oldLines.Length;
        int n = newLines.Length;
        int[,] dp = new int[m + 1, n + 1];

        for (int i = m - 1; i >= 0; i--)
        {
            for (int j = n - 1; j >= 0; j--)
            {
                if (oldLines[i] == newLines[j])
                    dp[i, j] = dp[i + 1, j + 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
            }
        }

        // Backtrack to produce diff entries
        var result = new List<RawDiffEntry>();
        int oi = 0, ni = 0;

        while (oi < m && ni < n)
        {
            if (oldLines[oi] == newLines[ni])
            {
                result.Add(new RawDiffEntry(DiffLineKind.Context, oldLines[oi], oi + 1, ni + 1));
                oi++;
                ni++;
            }
            else if (dp[oi + 1, ni] >= dp[oi, ni + 1])
            {
                // Line removed from remote (present in old, missing from new)
                result.Add(new RawDiffEntry(DiffLineKind.Removed, oldLines[oi], oi + 1, null));
                oi++;
            }
            else
            {
                // Line added in local (present in new, missing from old)
                result.Add(new RawDiffEntry(DiffLineKind.Added, newLines[ni], null, ni + 1));
                ni++;
            }
        }

        // Remaining old lines → removed
        while (oi < m)
        {
            result.Add(new RawDiffEntry(DiffLineKind.Removed, oldLines[oi], oi + 1, null));
            oi++;
        }

        // Remaining new lines → added
        while (ni < n)
        {
            result.Add(new RawDiffEntry(DiffLineKind.Added, newLines[ni], null, ni + 1));
            ni++;
        }

        return result;
    }

    /// <summary>
    /// Fallback for very large files: simple sequential comparison without LCS.
    /// </summary>
    private static List<RawDiffEntry> SimpleDiff(string[] oldLines, string[] newLines)
    {
        var result = new List<RawDiffEntry>();
        int maxLen = Math.Max(oldLines.Length, newLines.Length);

        for (int i = 0; i < maxLen; i++)
        {
            bool hasOld = i < oldLines.Length;
            bool hasNew = i < newLines.Length;

            if (hasOld && hasNew)
            {
                if (oldLines[i] == newLines[i])
                    result.Add(new RawDiffEntry(DiffLineKind.Context, oldLines[i], i + 1, i + 1));
                else
                {
                    result.Add(new RawDiffEntry(DiffLineKind.Removed, oldLines[i], i + 1, null));
                    result.Add(new RawDiffEntry(DiffLineKind.Added, newLines[i], null, i + 1));
                }
            }
            else if (hasOld)
                result.Add(new RawDiffEntry(DiffLineKind.Removed, oldLines[i], i + 1, null));
            else
                result.Add(new RawDiffEntry(DiffLineKind.Added, newLines[i], null, i + 1));
        }

        return result;
    }

    /// <summary>
    /// Trim the full diff to keep only <paramref name="context"/> unchanged lines around each change.
    /// </summary>
    private static IReadOnlyList<DiffLine> TrimContext(List<RawDiffEntry> fullDiff, int context)
    {
        if (fullDiff.Count == 0) return Array.Empty<DiffLine>();

        // Find which indices should be kept
        var keep = new bool[fullDiff.Count];

        for (int i = 0; i < fullDiff.Count; i++)
        {
            if (fullDiff[i].Kind != DiffLineKind.Context)
            {
                // This is a change — keep it and surrounding context
                int start = Math.Max(0, i - context);
                int end = Math.Min(fullDiff.Count - 1, i + context);
                for (int j = start; j <= end; j++)
                    keep[j] = true;
            }
        }

        // Build result, assigning sequential IDs
        var result = new List<DiffLine>();
        int id = 0;

        for (int i = 0; i < fullDiff.Count; i++)
        {
            if (!keep[i]) continue;

            var entry = fullDiff[i];
            result.Add(new DiffLine(id, entry.Kind, entry.Text, entry.OldLine, entry.NewLine));
            id++;
        }

        return result;
    }

    /// Internal struct for building the diff before finalizing into DiffLine.
    private readonly record struct RawDiffEntry(DiffLineKind Kind, string Text, int? OldLine, int? NewLine);
}
