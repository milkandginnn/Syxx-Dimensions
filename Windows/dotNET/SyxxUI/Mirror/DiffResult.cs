using System.Collections.Generic;
using System.Linq;

namespace SyxxUI.Mirror;

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
    /// <summary>Stable identity for UI binding.</summary>
    public int Id { get; }
    /// <summary>What kind of line this is.</summary>
    public DiffLineKind Kind { get; }
    /// <summary>The text content of the line (without prefix).</summary>
    public string Text { get; }
    /// <summary>Line number in the "old" (remote) file, or null for added lines.</summary>
    public int? OldLineNumber { get; }
    /// <summary>Line number in the "new" (local) file, or null for removed lines.</summary>
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
    /// <summary>Diff lines for the HTML section.</summary>
    public List<DiffLine> HtmlDiff { get; }
    /// <summary>Diff lines for the CSS section.</summary>
    public List<DiffLine> CssDiff { get; }
    /// <summary>Diff lines for the JavaScript section.</summary>
    public List<DiffLine> JsDiff { get; }
    /// <summary>Total number of changed lines (additions + removals) across all sections.</summary>
    public int TotalDifferences { get; }
    /// <summary>True if any differences exist.</summary>
    public bool HasChanges => TotalDifferences > 0;

    public DiffResult(List<DiffLine> htmlDiff, List<DiffLine> cssDiff, List<DiffLine> jsDiff)
    {
        HtmlDiff = htmlDiff;
        CssDiff = cssDiff;
        JsDiff = jsDiff;
        int changes = htmlDiff.Count(l => l.Kind != DiffLineKind.Context)
                    + cssDiff.Count(l => l.Kind != DiffLineKind.Context)
                    + jsDiff.Count(l => l.Kind != DiffLineKind.Context);
        TotalDifferences = changes;
    }

    /// <summary>Returns the diff lines for a given section.</summary>
    public List<DiffLine> LinesFor(DiffSection section) => section switch
    {
        DiffSection.Html => HtmlDiff,
        DiffSection.Css => CssDiff,
        DiffSection.JavaScript => JsDiff,
        _ => new List<DiffLine>()
    };

    /// <summary>Count of additions (local-only lines) across all sections.</summary>
    public int TotalAdditions =>
        HtmlDiff.Count(l => l.Kind == DiffLineKind.Added)
      + CssDiff.Count(l => l.Kind == DiffLineKind.Added)
      + JsDiff.Count(l => l.Kind == DiffLineKind.Added);

    /// <summary>Count of removals (remote-only lines) across all sections.</summary>
    public int TotalRemovals =>
        HtmlDiff.Count(l => l.Kind == DiffLineKind.Removed)
      + CssDiff.Count(l => l.Kind == DiffLineKind.Removed)
      + JsDiff.Count(l => l.Kind == DiffLineKind.Removed);
}
