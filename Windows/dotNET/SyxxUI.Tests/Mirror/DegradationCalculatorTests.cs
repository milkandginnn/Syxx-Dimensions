using System;
using System.Collections.Generic;
using Xunit;
using SyxxUI.Mirror;

namespace SyxxUI.Tests.Mirror;

/// <summary>
/// Windows port of macOS DegradationCalculatorTests (17 tests).
/// Tests the score calculation from diff results.
/// </summary>
public class DegradationCalculatorTests
{
    // MARK: - Boundary classifications

    [Fact]
    public void IdenticalClassification()
    {
        var score = new DegradationScore(0.0);
        Assert.Equal(DegradationScore.Classification.Identical, score.Bucket);
        Assert.Equal(0, score.Percentage);
    }

    [Fact]
    public void MinorClassification()
    {
        var score = new DegradationScore(0.05);
        Assert.Equal(DegradationScore.Classification.MinorDifferences, score.Bucket);
    }

    [Fact]
    public void MinorUpperBound()
    {
        var score = new DegradationScore(0.1);
        Assert.Equal(DegradationScore.Classification.MinorDifferences, score.Bucket);
    }

    [Fact]
    public void ModerateClassification()
    {
        var score = new DegradationScore(0.2);
        Assert.Equal(DegradationScore.Classification.ModerateDifferences, score.Bucket);
    }

    [Fact]
    public void ModerateUpperBound()
    {
        var score = new DegradationScore(0.3);
        Assert.Equal(DegradationScore.Classification.ModerateDifferences, score.Bucket);
    }

    [Fact]
    public void SignificantClassification()
    {
        var score = new DegradationScore(0.45);
        Assert.Equal(DegradationScore.Classification.SignificantDifferences, score.Bucket);
    }

    [Fact]
    public void SignificantUpperBound()
    {
        var score = new DegradationScore(0.6);
        Assert.Equal(DegradationScore.Classification.SignificantDifferences, score.Bucket);
    }

    [Fact]
    public void CriticalClassification()
    {
        var score = new DegradationScore(0.8);
        Assert.Equal(DegradationScore.Classification.CriticalDifferences, score.Bucket);
    }

    // MARK: - Clamping

    [Fact]
    public void ScoreClampedToZero()
    {
        var score = new DegradationScore(-0.5);
        Assert.Equal(0.0, score.Score);
        Assert.Equal(DegradationScore.Classification.Identical, score.Bucket);
    }

    [Fact]
    public void ScoreClampedToOne()
    {
        var score = new DegradationScore(1.5);
        Assert.Equal(1.0, score.Score);
        Assert.Equal(DegradationScore.Classification.CriticalDifferences, score.Bucket);
    }

    // MARK: - Calculator from DiffResult

    [Fact]
    public void CalculatorWithNoDiff()
    {
        var diff = new DiffResult(
            new List<DiffLine>(), new List<DiffLine>(), new List<DiffLine>());
        var score = DegradationCalculator.Calculate(diff);
        Assert.Equal(0.0, score.Score);
        Assert.Equal(DegradationScore.Classification.Identical, score.Bucket);
    }

    [Fact]
    public void CalculatorWithAllChanges()
    {
        var lines = new List<DiffLine>
        {
            new(0, DiffLineKind.Added, "x", null, 1),
            new(1, DiffLineKind.Removed, "y", 1, null),
        };
        var diff = new DiffResult(lines, new List<DiffLine>(), new List<DiffLine>());
        var score = DegradationCalculator.Calculate(diff);
        Assert.Equal(1.0, score.Score); // All lines are changes
    }

    [Fact]
    public void CalculatorWithMixedLines()
    {
        // 2 context + 2 changes = score 0.5
        var lines = new List<DiffLine>
        {
            new(0, DiffLineKind.Context, "a", 1, 1),
            new(1, DiffLineKind.Added, "b", null, 2),
            new(2, DiffLineKind.Removed, "c", 2, null),
            new(3, DiffLineKind.Context, "d", 3, 3),
        };
        var diff = new DiffResult(lines, new List<DiffLine>(), new List<DiffLine>());
        var score = DegradationCalculator.Calculate(diff);
        Assert.Equal(0.5, score.Score, precision: 2);
        Assert.Equal(DegradationScore.Classification.SignificantDifferences, score.Bucket);
    }

    [Fact]
    public void CalculatorFromRawText()
    {
        var score = DegradationCalculator.CalculateFromText(
            "same\ndifferent\nsame",
            "same\nchanged\nsame");
        Assert.True(score.Score > 0.0);
        Assert.True(score.Score < 1.0);
    }

    [Fact]
    public void CalculatorFromIdenticalText()
    {
        var score = DegradationCalculator.CalculateFromText(
            "identical text",
            "identical text");
        Assert.Equal(0.0, score.Score);
    }

    // MARK: - Percentage

    [Fact]
    public void PercentageRounding()
    {
        var score = new DegradationScore(0.234);
        Assert.Equal(23, score.Percentage);

        var score2 = new DegradationScore(0.995);
        Assert.Equal(100, score2.Percentage);
    }

    // MARK: - Labels

    [Fact]
    public void ClassificationLabels()
    {
        Assert.Equal("Identical", DegradationScore.LabelFor(DegradationScore.Classification.Identical));
        Assert.Equal("Minor Differences", DegradationScore.LabelFor(DegradationScore.Classification.MinorDifferences));
        Assert.Equal("Moderate Differences", DegradationScore.LabelFor(DegradationScore.Classification.ModerateDifferences));
        Assert.Equal("Significant Differences", DegradationScore.LabelFor(DegradationScore.Classification.SignificantDifferences));
        Assert.Equal("Critical Differences", DegradationScore.LabelFor(DegradationScore.Classification.CriticalDifferences));
    }
}
