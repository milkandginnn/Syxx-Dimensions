using SyxxUI.Shared.Mirror;
using Xunit;

namespace SyxxUI.Shared.Tests.Mirror;

public class DiffEngineTests
{
    // ───────────────────── Identical content ─────────────────────

    [Fact]
    public void IdenticalContentProducesEmptyDiff()
    {
        string text = "line1\nline2\nline3";
        var result = DiffEngine.Diff(text, text);
        Assert.Empty(result);
    }

    [Fact]
    public void BothEmptyProducesEmptyDiff()
    {
        var result = DiffEngine.Diff("", "");
        Assert.Empty(result);
    }

    // ───────────────────── Simple additions and removals ─────────────────────

    [Fact]
    public void AddedLine()
    {
        // "line3" exists in local but not in remote → added
        string remote = "line1\nline2";
        string local = "line1\nline2\nline3";
        var result = DiffEngine.Diff(remote, local);
        var added = result.Where(l => l.Kind == DiffLineKind.Added).ToList();
        Assert.True(added.Count >= 1, "Should detect the added line (local-only)");
    }

    [Fact]
    public void RemovedLine()
    {
        // "line3" exists in remote but not in local → removed
        string remote = "line1\nline2\nline3";
        string local = "line1\nline2";
        var result = DiffEngine.Diff(remote, local);
        var removed = result.Where(l => l.Kind == DiffLineKind.Removed).ToList();
        Assert.True(removed.Count >= 1, "Should detect the removed line (remote-only)");
    }

    [Fact]
    public void ReplacedLine()
    {
        string remote = "line1\nold line\nline3";
        string local = "line1\nnew line\nline3";
        var result = DiffEngine.Diff(remote, local);
        var added = result.Where(l => l.Kind == DiffLineKind.Added).ToList();
        var removed = result.Where(l => l.Kind == DiffLineKind.Removed).ToList();
        Assert.True(added.Count >= 1);
        Assert.True(removed.Count >= 1);
    }

    // ───────────────────── Completely different content ─────────────────────

    [Fact]
    public void CompletelyDifferent()
    {
        string remote = "alpha\nbeta\ngamma";
        string local = "one\ntwo\nthree";
        var result = DiffEngine.Diff(remote, local);
        var changes = result.Where(l => l.Kind != DiffLineKind.Context).ToList();
        Assert.True(changes.Count > 0, "Completely different content should have changes");
    }

    // ───────────────────── Line numbers ─────────────────────

    [Fact]
    public void ContextLinesHaveBothLineNumbers()
    {
        string remote = "same\nsame\nsame";
        string local = "same\nDIFFERENT\nsame";
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
        string remote = "line1";
        string local = "line1\nline2";
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
        string remote = "line1\nline2";
        string local = "line1";
        var result = DiffEngine.Diff(remote, local);
        var removed = result.Where(l => l.Kind == DiffLineKind.Removed).ToList();
        Assert.True(removed.Count >= 1, "Should detect the removed line");
        foreach (var line in removed)
        {
            Assert.NotNull(line.OldLineNumber);
            Assert.Null(line.NewLineNumber);
        }
    }

    // ───────────────────── Bundle comparison ─────────────────────

    [Fact]
    public void CompareBundles()
    {
        var remote = MakeBundle(BundleSource.Remote, "<html>remote</html>", "body { color: red; }", "console.log('remote');");
        var local = MakeBundle(BundleSource.Local, "<html>local</html>", "body { color: blue; }", "console.log('local');");
        var result = DiffEngine.Compare(remote, local);
        Assert.True(result.HasChanges);
        Assert.True(result.TotalDifferences > 0);
    }

    [Fact]
    public void CompareIdenticalBundles()
    {
        var remote = MakeBundle(BundleSource.Remote, "<html>same</html>", "body { margin: 0; }", "// same");
        var local = MakeBundle(BundleSource.Local, "<html>same</html>", "body { margin: 0; }", "// same");
        var result = DiffEngine.Compare(remote, local);
        Assert.False(result.HasChanges);
        Assert.Equal(0, result.TotalDifferences);
    }

    // ───────────────────── Utility counts ─────────────────────

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

    // ───────────────────── DiffResult properties ─────────────────────

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

    // ───────────────────── Helpers ─────────────────────

    private static MirrorBundle MakeBundle(BundleSource source, string html, string css, string js)
    {
        return new MirrorBundle(
            source, "example.com", DateTimeOffset.UtcNow,
            html, css, js,
            new BundleMetadata(0, 0, 0, null, ""));
    }
}
