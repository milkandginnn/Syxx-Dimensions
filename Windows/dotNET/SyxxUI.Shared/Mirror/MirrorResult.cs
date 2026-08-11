namespace SyxxUI.Shared.Mirror;

/// <summary>
/// The complete result of a Mirror comparison: both bundles, their diff, and the degradation score.
/// </summary>
public sealed class MirrorResult
{
    /// The remote (server-side) bundle.
    public MirrorBundle RemoteBundle { get; }

    /// The local (device-side) bundle.
    public MirrorBundle LocalBundle { get; }

    /// Line-by-line diff across HTML, CSS, and JS.
    public DiffResult DiffResult { get; }

    /// Overall degradation score (0.0 = identical, 1.0 = completely different).
    public DegradationScore DegradationScore { get; }

    /// When the comparison was performed.
    public DateTimeOffset ComparisonTimestamp { get; }

    /// Whether the result was computed from partial data (G-02 fallback).
    public bool IsPartial { get; }

    /// Human-readable reason if the result is partial.
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
