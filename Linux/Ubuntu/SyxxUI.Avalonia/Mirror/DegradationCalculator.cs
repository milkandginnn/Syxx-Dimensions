namespace SyxxUI.Avalonia.Mirror;

/// <summary>
/// Computes a <see cref="DegradationScore"/> from a <see cref="DiffResult"/> by measuring the ratio
/// of changed lines to total lines across all sections.
///
/// Formula:
///   score = totalDifferences / max(totalLines, 1)
///
/// Where <c>totalDifferences</c> = additions + removals and <c>totalLines</c> = total diff lines
/// (context + additions + removals). The score is clamped to [0.0, 1.0].
/// </summary>
public static class DegradationCalculator
{
    /// <summary>
    /// Calculate degradation from a completed diff result.
    /// D333: the previous formula used <c>totalChanges / diffLineCount</c>, but diffLineCount
    /// is AFTER context trimming — so the same code change yields a different score depending on
    /// the context window size. The fix: accept the actual total source line counts so the score
    /// is normalized against the original content size, not the trimmed view.
    /// </summary>
    /// <param name="diff">The diff result (context-trimmed).</param>
    /// <param name="totalSourceLines">
    /// The sum of source lines across both remote and local for all sections (HTML + CSS + JS).
    /// Pass 0 to fall back to the diff line count (backward-compatible behavior).
    /// </param>
    public static DegradationScore Calculate(DiffResult diff, int totalSourceLines = 0)
    {
        int totalChanges = diff.TotalDifferences;

        // D333: use actual source line count as denominator, falling back to diff line count only
        // when source counts are unavailable (backward compatibility for callers that pass DiffResult
        // without bundle context).
        int denominator = totalSourceLines > 0
            ? totalSourceLines
            : diff.HtmlDiff.Count + diff.CssDiff.Count + diff.JsDiff.Count;

        if (denominator == 0)
            return new DegradationScore(0.0);

        double raw = (double)totalChanges / denominator;
        return new DegradationScore(raw);
    }

    /// <summary>
    /// Calculate degradation from two bundles directly (convenience).
    /// D333: computes the actual source line counts from both bundles so the score is
    /// independent of the diff context window size.
    /// </summary>
    public static DegradationScore Calculate(MirrorBundle remote, MirrorBundle local)
    {
        var diff = DiffEngine.Compare(remote, local);

        // D333: count actual source lines from both bundles across all sections
        int totalSourceLines = CountSourceLines(remote) + CountSourceLines(local);
        return Calculate(diff, totalSourceLines);
    }

    /// <summary>
    /// Calculate degradation from raw text pairs (convenience for testing).
    /// D333: uses the actual source line counts from both texts.
    /// </summary>
    public static DegradationScore Calculate(string remoteText, string localText)
    {
        var lines = DiffEngine.Diff(remoteText, localText);
        int changes = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Kind != DiffLineKind.Context) changes++;
        }

        // D333: use the maximum of the two input line counts as the denominator — this represents
        // the actual content size independent of context window trimming.
        int remoteLines = CountTextLines(remoteText);
        int localLines = CountTextLines(localText);
        int totalSourceLines = remoteLines + localLines;

        if (totalSourceLines == 0)
            return new DegradationScore(0.0);

        return new DegradationScore((double)changes / totalSourceLines);
    }

    /// <summary>Count the number of lines in a text (empty/null = 0).</summary>
    private static int CountTextLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') count++;
        }
        return count;
    }

    /// <summary>Count the total source lines in a bundle across all three sections.</summary>
    private static int CountSourceLines(MirrorBundle bundle)
    {
        return CountTextLines(bundle.HtmlContent)
             + CountTextLines(bundle.CssContent)
             + CountTextLines(bundle.JsContent);
    }
}
