using System;

namespace SyxxUI.Mirror;

/// <summary>
/// The complete result of a Mirror comparison: both bundles, their diff, and the degradation score.
/// </summary>
public sealed class MirrorResult
{
    /// <summary>The remote (server-side) bundle.</summary>
    public MirrorBundle RemoteBundle { get; }
    /// <summary>The local (device-side) bundle.</summary>
    public MirrorBundle LocalBundle { get; }
    /// <summary>Line-by-line diff across HTML, CSS, and JS.</summary>
    public DiffResult DiffResult { get; }
    /// <summary>Overall degradation score (0.0 = identical, 1.0 = completely different).</summary>
    public DegradationScore DegradationScore { get; }
    /// <summary>When the comparison was performed.</summary>
    public DateTimeOffset ComparisonTimestamp { get; }
    /// <summary>Whether the result was computed from partial data (G-02 fallback).</summary>
    public bool IsPartial { get; }
    /// <summary>Human-readable reason if the result is partial.</summary>
    public string? PartialReason { get; }

    public MirrorResult(
        MirrorBundle remoteBundle,
        MirrorBundle localBundle,
        DiffResult diffResult,
        DegradationScore degradationScore,
        DateTimeOffset comparisonTimestamp,
        bool isPartial = false,
        string? partialReason = null)
    {
        RemoteBundle = remoteBundle;
        LocalBundle = localBundle;
        DiffResult = diffResult;
        DegradationScore = degradationScore;
        ComparisonTimestamp = comparisonTimestamp;
        IsPartial = isPartial;
        PartialReason = partialReason;
    }
}
