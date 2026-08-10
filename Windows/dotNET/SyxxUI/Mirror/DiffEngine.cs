using System;
using System.Collections.Generic;
using System.Linq;
using Syxx.Kit;

namespace SyxxUI.Mirror;

/// <summary>
/// Line-based unified diff engine producing structured <see cref="DiffLine"/> arrays with line numbers.
///
/// Uses the same LCS algorithm as <see cref="TextDiff"/> but outputs <see cref="DiffLine"/> objects
/// suitable for the Mirror terminal-style diff view. Reuses <see cref="TextDiff.Unified"/> internally
/// for the LCS computation, then parses the prefixed output into structured values.
/// </summary>
public static class DiffEngine
{
    /// <summary>
    /// Compare two strings and produce a structured diff.
    /// </summary>
    /// <param name="remote">The "old" content (from the Dimension server).</param>
    /// <param name="local">The "new" content (from the user's device).</param>
    /// <param name="context">Number of unchanged context lines to keep around each change (default 2).</param>
    /// <returns>List of <see cref="DiffLine"/> with line numbers and kind tags.</returns>
    public static List<DiffLine> Diff(string remote, string local, int context = 2)
    {
        // Fast path: identical content
        if (remote == local) return new List<DiffLine>();

        // Delegate to the proven TextDiff for LCS computation
        var rawLines = TextDiff.Unified(local, remote, context: context, maxLines: 10_000);

        // Parse the "  ", "- ", "+ " prefixed lines into DiffLine structs with line numbers
        var result = new List<DiffLine>();
        int oldLine = 1;
        int newLine = 1;
        int id = 0;

        foreach (var raw in rawLines)
        {
            if (raw.Length < 2) continue;
            var prefix = raw[..2];
            var text = raw[2..];

            // TextDiff convention: x = local (first arg), y = remote (second arg).
            // "-" prefix = line from x (local) — only in local, NOT in remote.
            // "+" prefix = line from y (remote) — only in remote, NOT in local.
            //
            // Mirror convention: "old" = remote (server baseline), "new" = local (user's device).
            // So: line only in local = ADDED (present in new, missing from old) → Added
            //     line only in remote = REMOVED (present in old, missing from new) → Removed
            switch (prefix)
            {
                case "  ":
                    // Context line: present in both
                    result.Add(new DiffLine(id, DiffLineKind.Context, text, oldLine, newLine));
                    oldLine++;
                    newLine++;
                    break;
                case "- ":
                    // Line from local (x) — ADDED relative to remote baseline
                    result.Add(new DiffLine(id, DiffLineKind.Added, text, null, newLine));
                    newLine++;
                    break;
                case "+ ":
                    // Line from remote (y) — REMOVED relative to local
                    result.Add(new DiffLine(id, DiffLineKind.Removed, text, oldLine, null));
                    oldLine++;
                    break;
                default:
                    // Collapse marker "  ⋮" or truncation notice — keep as context
                    result.Add(new DiffLine(id, DiffLineKind.Context, raw, null, null));
                    break;
            }
            id++;
        }

        return result;
    }

    /// <summary>Compare two <see cref="MirrorBundle"/> instances across all three content sections.</summary>
    public static DiffResult Compare(MirrorBundle remote, MirrorBundle local)
    {
        var htmlDiff = Diff(remote.HtmlContent, local.HtmlContent);
        var cssDiff = Diff(remote.CssContent, local.CssContent);
        var jsDiff = Diff(remote.JsContent, local.JsContent);
        return new DiffResult(htmlDiff, cssDiff, jsDiff);
    }

    /// <summary>Count the number of changed lines (additions + removals) in a diff.</summary>
    public static int ChangeCount(List<DiffLine> lines) =>
        lines.Count(l => l.Kind != DiffLineKind.Context);

    /// <summary>Count additions only.</summary>
    public static int AdditionCount(List<DiffLine> lines) =>
        lines.Count(l => l.Kind == DiffLineKind.Added);

    /// <summary>Count removals only.</summary>
    public static int RemovalCount(List<DiffLine> lines) =>
        lines.Count(l => l.Kind == DiffLineKind.Removed);
}
