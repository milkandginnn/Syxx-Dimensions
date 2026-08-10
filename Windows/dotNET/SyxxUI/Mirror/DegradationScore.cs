using System;

namespace SyxxUI.Mirror;

/// <summary>
/// How different the remote and local bundles are, on a 0.0 (identical) to 1.0 (completely different) scale.
/// </summary>
public sealed class DegradationScore : IEquatable<DegradationScore>
{
    /// <summary>The raw score: 0.0 = identical, 1.0 = completely different.</summary>
    public double Score { get; }

    /// <summary>Human-readable classification bucket.</summary>
    public Classification Bucket { get; }

    public DegradationScore(double score)
    {
        Score = Math.Clamp(score, 0.0, 1.0);
        Bucket = Classify(Score);
    }

    public DegradationScore(double score, Classification bucket)
    {
        Score = Math.Clamp(score, 0.0, 1.0);
        Bucket = bucket;
    }

    /// <summary>Percentage representation for display (e.g. "23%").</summary>
    public int Percentage => (int)Math.Round(Score * 100);

    /// <summary>Human-readable classification bucket.</summary>
    public enum Classification
    {
        Identical,
        MinorDifferences,
        ModerateDifferences,
        SignificantDifferences,
        CriticalDifferences
    }

    /// <summary>Human-readable label for a classification.</summary>
    public static string LabelFor(Classification c) => c switch
    {
        Classification.Identical => "Identical",
        Classification.MinorDifferences => "Minor Differences",
        Classification.ModerateDifferences => "Moderate Differences",
        Classification.SignificantDifferences => "Significant Differences",
        Classification.CriticalDifferences => "Critical Differences",
        _ => "Unknown"
    };

    /// <summary>Classify a raw score into a bucket.</summary>
    public static Classification Classify(double score)
    {
        if (score <= 0.0) return Classification.Identical;
        if (score <= 0.1) return Classification.MinorDifferences;
        if (score <= 0.3) return Classification.ModerateDifferences;
        if (score <= 0.6) return Classification.SignificantDifferences;
        return Classification.CriticalDifferences;
    }

    public bool Equals(DegradationScore? other) =>
        other is not null && Math.Abs(Score - other.Score) < 1e-9 && Bucket == other.Bucket;

    public override bool Equals(object? obj) => Equals(obj as DegradationScore);
    public override int GetHashCode() => HashCode.Combine(Score, Bucket);
}
