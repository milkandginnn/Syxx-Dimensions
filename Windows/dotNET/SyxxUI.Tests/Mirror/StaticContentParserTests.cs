using System;
using System.Linq;
using Xunit;
using SyxxUI.Mirror;

namespace SyxxUI.Tests.Mirror;

/// <summary>
/// Windows port of macOS StaticContentParserTests (23 tests).
/// Tests HTML/CSS/JS extraction, partial detection, fallback, SHA-256, and bundle properties.
/// </summary>
public class StaticContentParserTests
{
    // MARK: - Inline style extraction

    [Fact]
    public void ExtractsInlineStyles()
    {
        var html = "<html><head>\n<style>body { color: red; }</style>\n<style>h1 { font-size: 24px; }</style>\n</head><body></body></html>";
        var result = StaticContentParser.Parse(html);
        Assert.Contains("color: red", result.Css);
        Assert.Contains("font-size: 24px", result.Css);
    }

    [Fact]
    public void NoInlineStylesReturnsEmpty()
    {
        var html = "<html><head></head><body>Hello</body></html>";
        var result = StaticContentParser.Parse(html);
        Assert.True(string.IsNullOrEmpty(result.Css));
    }

    // MARK: - Inline script extraction

    [Fact]
    public void ExtractsInlineScripts()
    {
        var html = "<html><body>\n<script>console.log('hello');</script>\n<script src=\"external.js\"></script>\n<script>document.title = 'test';</script>\n</body></html>";
        var result = StaticContentParser.Parse(html);
        Assert.Contains("console.log", result.Js);
        Assert.Contains("document.title", result.Js);
        // External scripts should NOT be in inline JS
        Assert.DoesNotContain("external.js", result.Js);
    }

    [Fact]
    public void EmptyScriptTagsIgnored()
    {
        var html = "<script></script><script>  </script>";
        var result = StaticContentParser.Parse(html);
        Assert.True(string.IsNullOrEmpty(result.Js), "Empty script tags should not produce output");
    }

    // MARK: - External URL extraction

    [Fact]
    public void ExtractsStylesheetUrls()
    {
        var html = "<link rel=\"stylesheet\" href=\"styles.css\">\n<link rel=\"stylesheet\" href=\"/assets/main.css\">\n<link rel=\"icon\" href=\"favicon.ico\">";
        var result = StaticContentParser.Parse(html);
        Assert.Equal(2, result.CssUrls.Count);
        Assert.Contains("styles.css", result.CssUrls);
        Assert.Contains("/assets/main.css", result.CssUrls);
    }

    [Fact]
    public void ExtractsScriptUrls()
    {
        var html = "<script src=\"app.js\"></script>\n<script src=\"https://cdn.example.com/lib.js\"></script>\n<script>inlineOnly();</script>";
        var result = StaticContentParser.Parse(html);
        Assert.Equal(2, result.JsUrls.Count);
        Assert.Contains("app.js", result.JsUrls);
        Assert.Contains("https://cdn.example.com/lib.js", result.JsUrls);
    }

    // MARK: - Partial content detection (G-02)

    [Fact]
    public void EmptyHtmlIsPartial()
    {
        var result = StaticContentParser.Parse("");
        Assert.True(result.IsPartial);
        Assert.NotNull(result.PartialReason);
    }

    [Fact]
    public void HtmlWithOnlyBodyIsPartial()
    {
        var result = StaticContentParser.Parse("<html><body>Just text</body></html>");
        Assert.True(result.IsPartial, "HTML with no CSS/JS and no external resources should be partial");
    }

    [Fact]
    public void HtmlWithCssIsNotPartial()
    {
        var html = "<html><style>body{}</style></html>";
        var result = StaticContentParser.Parse(html);
        Assert.False(result.IsPartial);
    }

    [Fact]
    public void HtmlWithExternalResourcesIsNotPartial()
    {
        var html = "<html>\n<link rel=\"stylesheet\" href=\"style.css\">\n<script src=\"app.js\"></script>\n</html>";
        var result = StaticContentParser.Parse(html);
        Assert.False(result.IsPartial);
    }

    // MARK: - makeBundle

    [Fact]
    public void MakeBundleProducesValidBundle()
    {
        var html = "<html><head>\n<style>body { margin: 0; }</style>\n</head><body>\n<script>alert('hi');</script>\n</body></html>";
        var bundle = StaticContentParser.MakeBundle(BundleSource.Remote, "test.com", html);
        Assert.Equal(BundleSource.Remote, bundle.Source);
        Assert.Equal("test.com", bundle.Domain);
        Assert.Contains("<html>", bundle.HtmlContent);
        Assert.Contains("margin", bundle.CssContent);
        Assert.Contains("alert", bundle.JsContent);
        Assert.False(string.IsNullOrEmpty(bundle.Metadata.ServerHash));
        Assert.True(bundle.Metadata.ByteSize > 0);
    }

    [Fact]
    public void MakeBundleHashIsDeterministic()
    {
        var html = "<html>same content</html>";
        var bundle1 = StaticContentParser.MakeBundle(BundleSource.Remote, "x.com", html);
        var bundle2 = StaticContentParser.MakeBundle(BundleSource.Local, "x.com", html);
        Assert.Equal(bundle1.Metadata.ServerHash, bundle2.Metadata.ServerHash);
    }

    // MARK: - G-02 Fallback content

    [Fact]
    public void ExtractsTemplateContent()
    {
        var html = "<html>\n<template><div>Template content here</div></template>\n</html>";
        var result = StaticContentParser.ExtractFallbackContent(html);
        Assert.Contains("Template content here", result);
    }

    [Fact]
    public void ExtractsSlotContent()
    {
        var html = "<html>\n<slot><span>Slot fallback</span></slot>\n</html>";
        var result = StaticContentParser.ExtractFallbackContent(html);
        Assert.Contains("Slot fallback", result);
    }

    [Fact]
    public void FallbackEmptyWhenNoTemplatesOrSlots()
    {
        var html = "<html><body>Plain content</body></html>";
        var result = StaticContentParser.ExtractFallbackContent(html);
        Assert.True(string.IsNullOrEmpty(result));
    }

    // MARK: - SHA-256

    [Fact]
    public void Sha256ProducesConsistentHash()
    {
        var hash1 = StaticContentParser.Sha256Hex("hello");
        var hash2 = StaticContentParser.Sha256Hex("hello");
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length);
    }

    [Fact]
    public void Sha256DifferentInputsDifferentHashes()
    {
        var hash1 = StaticContentParser.Sha256Hex("hello");
        var hash2 = StaticContentParser.Sha256Hex("world");
        Assert.NotEqual(hash1, hash2);
    }

    // MARK: - MirrorBundle properties

    [Fact]
    public void MirrorBundleIsEmpty()
    {
        var bundle = new MirrorBundle(
            BundleSource.Remote, "x.com", DateTimeOffset.Now,
            "", "", "",
            new BundleMetadata(0, 0, 0, null, ""));
        Assert.True(bundle.IsEmpty);
    }

    [Fact]
    public void MirrorBundleIsNotEmpty()
    {
        var bundle = new MirrorBundle(
            BundleSource.Remote, "x.com", DateTimeOffset.Now,
            "<html></html>", "", "",
            new BundleMetadata(0, 0, 0, null, ""));
        Assert.False(bundle.IsEmpty);
    }

    [Fact]
    public void MirrorBundleConcatenated()
    {
        var bundle = new MirrorBundle(
            BundleSource.Remote, "x.com", DateTimeOffset.Now,
            "<html>", "body{}", "var x;",
            new BundleMetadata(0, 0, 0, null, ""));
        Assert.Contains("<!-- HTML -->", bundle.Concatenated);
        Assert.Contains("<!-- CSS -->", bundle.Concatenated);
        Assert.Contains("<!-- JavaScript -->", bundle.Concatenated);
        Assert.Contains("<html>", bundle.Concatenated);
        Assert.Contains("body{}", bundle.Concatenated);
        Assert.Contains("var x;", bundle.Concatenated);
    }

    [Fact]
    public void MirrorBundleSummaryLine()
    {
        var bundle = new MirrorBundle(
            BundleSource.Remote, "x.com", DateTimeOffset.Now,
            "line1\nline2", "css1", "js1\njs2\njs3",
            new BundleMetadata(0, 42, 0, null, "abc"));
        Assert.Contains("HTML: 2L", bundle.SummaryLine);
        Assert.Contains("CSS: 1L", bundle.SummaryLine);
        Assert.Contains("JS: 3L", bundle.SummaryLine);
        Assert.Contains("42 bytes", bundle.SummaryLine);
    }

    // MARK: - DegradationScore

    [Fact]
    public void DegradationScorePercentage()
    {
        Assert.Equal(0, new DegradationScore(0.0).Percentage);
        Assert.Equal(50, new DegradationScore(0.5).Percentage);
        Assert.Equal(100, new DegradationScore(1.0).Percentage);
    }

    [Fact]
    public void DegradationScoreClassificationBoundary()
    {
        Assert.Equal(DegradationScore.Classification.Identical, DegradationScore.Classify(0.0));
        Assert.Equal(DegradationScore.Classification.MinorDifferences, DegradationScore.Classify(0.001));
        Assert.Equal(DegradationScore.Classification.ModerateDifferences, DegradationScore.Classify(0.101));
        Assert.Equal(DegradationScore.Classification.SignificantDifferences, DegradationScore.Classify(0.301));
        Assert.Equal(DegradationScore.Classification.CriticalDifferences, DegradationScore.Classify(0.601));
    }
}
