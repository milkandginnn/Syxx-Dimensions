using Syxx.Kit;

namespace SyxxUI.Shared.Mirror;

/// <summary>
/// Line-based unified diff engine producing structured <see cref="DiffLine"/> arrays with line numbers.
/// Delegates LCS computation to <see cref="TextDiff.Unified"/> (shared Syxx.Kit), then parses the
/// prefixed output into structured values — same pattern as the Windows SyxxUI.Mirror.DiffEngine.
/// </summary>
public static class DiffEngine
{
    /// <summary>
    /// Compare two strings asynchronously on a background thread so the UI thread is never blocked.
    /// </summary>
    public static Task<IReadOnlyList<DiffLine>> DiffAsync(
        string remote, string local, int context = 2,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() => (IReadOnlyList<DiffLine>)Diff(remote, local, context), cancellationToken);
    }

    /// <summary>
    /// Compare two strings and produce a structured diff.
    /// </summary>
    public static List<DiffLine> Diff(string remote, string local, int context = 2)
    {
        if (remote == local) return new List<DiffLine>();

        var rawLines = TextDiff.Unified(local, remote, context: context, maxLines: 10_000);

        var result = new List<DiffLine>();
        int oldLine = 1;
        int newLine = 1;
        int id = 0;

        foreach (var raw in rawLines)
        {
            if (raw.Length < 2) continue;
            var prefix = raw[..2];
            var text = raw[2..];

            switch (prefix)
            {
                case "  ":
                    result.Add(new DiffLine(id, DiffLineKind.Context, text, oldLine, newLine));
                    oldLine++;
                    newLine++;
                    break;
                case "- ":
                    result.Add(new DiffLine(id, DiffLineKind.Added, text, null, newLine));
                    newLine++;
                    break;
                case "+ ":
                    result.Add(new DiffLine(id, DiffLineKind.Removed, text, oldLine, null));
                    oldLine++;
                    break;
                default:
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
    public static int ChangeCount(IReadOnlyList<DiffLine> lines)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Kind != DiffLineKind.Context) count++;
        return count;
    }

    /// <summary>Count additions only.</summary>
    public static int AdditionCount(IReadOnlyList<DiffLine> lines)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Kind == DiffLineKind.Added) count++;
        return count;
    }

    /// <summary>Count removals only.</summary>
    public static int RemovalCount(IReadOnlyList<DiffLine> lines)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Kind == DiffLineKind.Removed) count++;
        return count;
    }
}
