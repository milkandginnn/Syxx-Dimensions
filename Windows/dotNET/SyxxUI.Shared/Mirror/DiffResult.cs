namespace SyxxUI.Shared.Mirror;

/// <summary>
/// The kind of a single diff line.
/// </summary>
public enum DiffLineKind
{
    /// Line present in both sides (no change).
    Context,
    /// Line added in the local side (not in remote).
    Added,
    /// Line removed from the local side (present in remote only).
    Removed
}

/// <summary>
/// A single line in a unified diff output.
/// </summary>
public sealed class DiffLine
{
    public int Id { get; }
    public DiffLineKind Kind { get; }
    public string Text { get; }
    /// Line number in the "old" (remote) file, or null for added lines.
    public int? OldLineNumber { get; }
    /// Line number in the "new" (local) file, or null for removed lines.
    public int? NewLineNumber { get; }

    public DiffLine(int id, DiffLineKind kind, string text, int? oldLineNumber, int? newLineNumber)
    {
        Id = id;
        Kind = kind;
        Text = text;
        OldLineNumber = oldLineNumber;
        NewLineNumber = newLineNumber;
    }
}

/// <summary>
/// Which section of the web content a diff belongs to.
/// </summary>
public enum DiffSection
{
    Html,
    Css,
    JavaScript
}

/// <summary>
/// The complete diff result across all three content sections (HTML, CSS, JS).
/// </summary>
public sealed class DiffResult
{
    public IReadOnlyList<DiffLine> HtmlDiff { get; }
    public IReadOnlyList<DiffLine> CssDiff { get; }
    public IReadOnlyList<DiffLine> JsDiff { get; }

    /// Total number of changed lines (additions + removals) across all sections.
    public int TotalDifferences { get; }

    /// True if any differences exist.
    public bool HasChanges => TotalDifferences > 0;

    public DiffResult(IReadOnlyList<DiffLine> htmlDiff, IReadOnlyList<DiffLine> cssDiff, IReadOnlyList<DiffLine> jsDiff)
    {
        HtmlDiff = htmlDiff;
        CssDiff = cssDiff;
        JsDiff = jsDiff;
        TotalDifferences = CountChanges(htmlDiff) + CountChanges(cssDiff) + CountChanges(jsDiff);
    }

    /// Returns the diff lines for a given section.
    public IReadOnlyList<DiffLine> LinesFor(DiffSection section) => section switch
    {
        DiffSection.Html => HtmlDiff,
        DiffSection.Css => CssDiff,
        DiffSection.JavaScript => JsDiff,
        _ => Array.Empty<DiffLine>()
    };

    /// Count of additions (local-only lines) across all sections.
    public int TotalAdditions =>
        CountByKind(HtmlDiff, DiffLineKind.Added) +
        CountByKind(CssDiff, DiffLineKind.Added) +
        CountByKind(JsDiff, DiffLineKind.Added);

    /// Count of removals (remote-only lines) across all sections.
    public int TotalRemovals =>
        CountByKind(HtmlDiff, DiffLineKind.Removed) +
        CountByKind(CssDiff, DiffLineKind.Removed) +
        CountByKind(JsDiff, DiffLineKind.Removed);

    private static int CountChanges(IReadOnlyList<DiffLine> lines)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != DiffLineKind.Context) count++;
        }
        return count;
    }

    private static int CountByKind(IReadOnlyList<DiffLine> lines, DiffLineKind kind)
    {
        int count = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind == kind) count++;
        }
        return count;
    }
}
