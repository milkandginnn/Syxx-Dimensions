using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using SyxxUI.Mirror;

namespace SyxxUI.Tests.Mirror;

/// <summary>
/// Windows port of macOS DiffEngineTests (14 tests).
/// Tests the line-based unified diff engine that produces structured DiffLine arrays.
/// </summary>
public class DiffEngineTests
{
    // MARK: - Identical content

    [Fact]
    public void IdenticalContentProducesEmptyDiff()
    {
        var text = "line1\nline2\nline3";
        var result = DiffEngine.Diff(text, text);
        Assert.Empty(result);
    }

    [Fact]
    public void BothEmptyProducesEmptyDiff()
    {
        var result = DiffEngine.Diff("", "");
        Assert.Empty(result);
    }

    // MARK: - Simple additions and removals

    [Fact]
    public void AddedLine()
    {
        // "line3" exists in local but not in remote → added
        var remote = "line1\nline2";
        var local = "line1\nline2\nline3";
        var result = DiffEngine.Diff(remote, local);
        var added = result.Where(l => l.Kind == DiffLineKind.Added).ToList();
        Assert.True(added.Count >= 1, "Should detect the added line (local-only)");
    }

    [Fact]
    public void RemovedLine()
    {
        // "line3" exists in remote but not in local → removed
        var remote = "line1\nline2\nline3";
        var local = "line1\nline2";
        var result = DiffEngine.Diff(remote, local);
        var removed = result.Where(l => l.Kind == DiffLineKind.Removed).ToList();
        Assert.True(removed.Count >= 1, "Should detect the removed line (remote-only)");
    }

    [Fact]
    public void ReplacedLine()
    {
        var remote = "line1\nold line\nline3";
        var local = "line1\nnew line\nline3";
        var result = DiffEngine.Diff(remote, local);
        var added = result.Where(l => l.Kind == DiffLineKind.Added).ToList();
        var removed = result.Where(l => l.Kind == DiffLineKind.Removed).ToList();
        Assert.True(added.Count >= 1);
        Assert.True(removed.Count >= 1);
    }

    // MARK: - Completely different content

    [Fact]
    public void CompletelyDifferent()
    {
        var remote = "alpha\nbeta\ngamma";
        var local = "one\ntwo\nthree";
        var result = DiffEngine.Diff(remote, local);
        var changes = result.Where(l => l.Kind != DiffLineKind.Context).ToList();
        Assert.True(changes.Count > 0, "Completely different content should have changes");
    }

    // MARK: - Line numbers

    [Fact]
    public void ContextLinesHaveBothLineNumbers()
    {
        var remote = "same\nsame\nsame";
        var local = "same\nDIFFERENT\nsame";
        var result = DiffEngine.Diff(remote, local);
        var contextLines = result.Where(l => l.Kind == DiffLineKind.Context).ToList();
        foreach (var line in contextLines)
        {
            Assert.NotNull(line.OldLineNumber);
            Assert.NotNull(line.NewLineNumber);
        }
    }

    [Fact]
    public void AddedLinesHaveOnlyNewLineNumber()
    {
        // "line2" exists in local but not remote → added
        var remote = "line1";
        var local = "line1\nline2";
        var result = DiffEngine.Diff(remote, local);
        var added = result.Where(l => l.Kind == DiffLineKind.Added).ToList();
        Assert.True(added.Count >= 1, "Should detect the added line");
        foreach (var line in added)
        {
            Assert.Null(line.OldLineNumber);
            Assert.NotNull(line.NewLineNumber);
        }
    }

    [Fact]
    public void RemovedLinesHaveOnlyOldLineNumber()
    {
        // "line2" exists in remote but not local → removed
        var remote = "line1\nline2";
        var local = "line1";
        var result = DiffEngine.Diff(remote, local);
        var removed = result.Where(l => l.Kind == DiffLineKind.Removed).ToList();
        Assert.True(removed.Count >= 1, "Should detect the removed line");
        foreach (var line in removed)
        {
            Assert.NotNull(line.OldLineNumber);
            Assert.Null(line.NewLineNumber);
        }
    }

    // MARK: - Bundle comparison

    [Fact]
    public void CompareBundles()
    {
        var remote = new MirrorBundle(
            BundleSource.Remote, "example.com", DateTimeOffset.Now,
            "<html>remote</html>", "body { color: red; }", "console.log('remote');",
            new BundleMetadata(0, 0, 0, null, ""));
        var local = new MirrorBundle(
            BundleSource.Local, "example.com", DateTimeOffset.Now,
            "<html>local</html>", "body { color: blue; }", "console.log('local');",
            new BundleMetadata(0, 0, 0, null, ""));
        var result = DiffEngine.Compare(remote, local);
        Assert.True(result.HasChanges);
        Assert.True(result.TotalDifferences > 0);
    }

    [Fact]
    public void CompareIdenticalBundles()
    {
        var bundle = new MirrorBundle(
            BundleSource.Remote, "example.com", DateTimeOffset.Now,
            "<html>same</html>", "body { margin: 0; }", "// same",
            new BundleMetadata(0, 0, 0, null, ""));
        var localBundle = new MirrorBundle(
            BundleSource.Local, "example.com", DateTimeOffset.Now,
            "<html>same</html>", "body { margin: 0; }", "// same",
            new BundleMetadata(0, 0, 0, null, ""));
        var result = DiffEngine.Compare(bundle, localBundle);
        Assert.False(result.HasChanges);
        Assert.Equal(0, result.TotalDifferences);
    }

    // MARK: - Utility counts

    [Fact]
    public void ChangeCount()
    {
        var lines = new List<DiffLine>
        {
            new(0, DiffLineKind.Context, "a", 1, 1),
            new(1, DiffLineKind.Added, "b", null, 2),
            new(2, DiffLineKind.Removed, "c", 2, null),
            new(3, DiffLineKind.Context, "d", 3, 3),
        };
        Assert.Equal(2, DiffEngine.ChangeCount(lines));
        Assert.Equal(1, DiffEngine.AdditionCount(lines));
        Assert.Equal(1, DiffEngine.RemovalCount(lines));
    }

    // MARK: - DiffResult properties

    [Fact]
    public void DiffResultTotals()
    {
        var htmlDiff = new List<DiffLine>
        {
            new(0, DiffLineKind.Added, "x", null, 1),
            new(1, DiffLineKind.Added, "y", null, 2),
        };
        var cssDiff = new List<DiffLine>
        {
            new(2, DiffLineKind.Removed, "z", 1, null),
        };
        var result = new DiffResult(htmlDiff, cssDiff, new List<DiffLine>());
        Assert.Equal(2, result.TotalAdditions);
        Assert.Equal(1, result.TotalRemovals);
        Assert.Equal(3, result.TotalDifferences);
        Assert.True(result.HasChanges);
    }

    [Fact]
    public void DiffResultSectionAccess()
    {
        var htmlDiff = new List<DiffLine> { new(0, DiffLineKind.Context, "html", 1, 1) };
        var cssDiff = new List<DiffLine> { new(1, DiffLineKind.Context, "css", 1, 1) };
        var jsDiff = new List<DiffLine> { new(2, DiffLineKind.Context, "js", 1, 1) };
        var result = new DiffResult(htmlDiff, cssDiff, jsDiff);
        Assert.Single(result.LinesFor(DiffSection.Html));
        Assert.Single(result.LinesFor(DiffSection.Css));
        Assert.Single(result.LinesFor(DiffSection.JavaScript));
        Assert.Equal("html", result.LinesFor(DiffSection.Html)[0].Text);
    }
}
