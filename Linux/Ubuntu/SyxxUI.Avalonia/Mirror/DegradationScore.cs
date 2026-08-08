namespace SyxxUI.Avalonia.Mirror;

/// <summary>
/// How different the remote and local bundles are, on a 0.0 (identical) to 1.0 (completely different) scale.
/// </summary>
public sealed class DegradationScore
{
    /// <summary>
    /// Human-readable classification bucket.
    /// </summary>
    public enum Classification
    {
        Identical,
        MinorDifferences,
        ModerateDifferences,
        SignificantDifferences,
        CriticalDifferences
    }

    /// The raw score: 0.0 = identical, 1.0 = completely different.
    public double Score { get; }

    /// Human-readable classification bucket.
    public Classification Bucket { get; }

    /// Percentage representation for display (e.g. "23%").
    public int Percentage => (int)Math.Round(Score * 100);

    public DegradationScore(double score)
    {
        Score = Math.Clamp(score, 0.0, 1.0);
        Bucket = Classify(Score);
    }

    /// Classify a raw score into a bucket.
    public static Classification Classify(double score)
    {
        if (score <= 0.0) return Classification.Identical;
        if (score <= 0.1) return Classification.MinorDifferences;
        if (score <= 0.3) return Classification.ModerateDifferences;
        if (score <= 0.6) return Classification.SignificantDifferences;
        return Classification.CriticalDifferences;
    }

    /// Human-readable label for a classification.
    public static string Label(Classification c) => c switch
    {
        Classification.Identical => "Identical",
        Classification.MinorDifferences => "Minor Differences",
        Classification.ModerateDifferences => "Moderate Differences",
        Classification.SignificantDifferences => "Significant Differences",
        Classification.CriticalDifferences => "Critical Differences",
        _ => "Unknown"
    };
}
