using System.Collections.Generic;
using System.Linq;

namespace SyxxUI.Mirror;

/// <summary>
/// Computes a <see cref="DegradationScore"/> from a <see cref="DiffResult"/> by measuring the ratio
/// of changed lines to total lines across all sections.
///
/// Formula: score = totalDifferences / max(totalLines, 1)
///
/// Where totalDifferences = additions + removals and totalLines = total diff lines
/// (context + additions + removals). The score is clamped to [0.0, 1.0].
/// </summary>
public static class DegradationCalculator
{
    /// <summary>Calculate degradation from a completed diff result.</summary>
    public static DegradationScore Calculate(DiffResult diff)
    {
        var allLines = new List<DiffLine>();
        allLines.AddRange(diff.HtmlDiff);
        allLines.AddRange(diff.CssDiff);
        allLines.AddRange(diff.JsDiff);

        int totalLines = allLines.Count;
        int totalChanges = allLines.Count(l => l.Kind != DiffLineKind.Context);

        if (totalLines == 0)
            return new DegradationScore(0.0);

        double raw = (double)totalChanges / totalLines;
        return new DegradationScore(raw);
    }

    /// <summary>Calculate degradation from two bundles directly (convenience).</summary>
    public static DegradationScore Calculate(MirrorBundle remote, MirrorBundle local)
    {
        var diff = DiffEngine.Compare(remote, local);
        return Calculate(diff);
    }

    /// <summary>Calculate degradation from raw text pairs (convenience for testing).</summary>
    public static DegradationScore CalculateFromText(string remoteText, string localText)
    {
        var lines = DiffEngine.Diff(remoteText, localText);
        int totalLines = lines.Count;
        int changes = lines.Count(l => l.Kind != DiffLineKind.Context);
        if (totalLines == 0)
            return new DegradationScore(0.0);
        return new DegradationScore((double)changes / totalLines);
    }
}
