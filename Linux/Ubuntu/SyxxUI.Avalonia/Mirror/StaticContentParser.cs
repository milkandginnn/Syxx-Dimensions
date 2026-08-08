using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SyxxUI.Avalonia.Mirror;

/// <summary>
/// Extracts static web content (HTML structure, CSS, JavaScript) from raw HTML text.
///
/// This is a lightweight regex-based parser — NOT a full HTML parser. It extracts:
/// - The HTML skeleton (everything including inline style content)
/// - CSS from style tags
/// - JavaScript from script tags (inline bodies) and script src attributes
///
/// G-02 fallback: when resources can't be fetched (e.g. pre-JS-execution captures), the parser
/// falls back to concatenating whatever static elements are available (slot, template tag contents,
/// inline JS) and flags the result as partial.
/// </summary>
public static class StaticContentParser
{
    /// <summary>
    /// Result of parsing raw HTML into its constituent parts.
    /// </summary>
    public sealed class ParsedContent
    {
        /// The full HTML (unchanged).
        public string Html { get; }

        /// Extracted CSS content (inline style blocks joined).
        public string Css { get; }

        /// Extracted JavaScript content (inline script bodies joined).
        public string Js { get; }

        /// External stylesheet URLs found in link tags.
        public IReadOnlyList<string> CssUrls { get; }

        /// External script URLs found in script src tags.
        public IReadOnlyList<string> JsUrls { get; }

        /// Whether any content was missing or unparseable (G-02 partial flag).
        public bool IsPartial { get; }

        /// Reason the content is partial, if applicable.
        public string? PartialReason { get; }

        public ParsedContent(string html, string css, string js,
            IReadOnlyList<string> cssUrls, IReadOnlyList<string> jsUrls,
            bool isPartial, string? partialReason)
        {
            Html = html;
            Css = css;
            Js = js;
            CssUrls = cssUrls;
            JsUrls = jsUrls;
            IsPartial = isPartial;
            PartialReason = partialReason;
        }
    }

    /// <summary>
    /// Parse raw HTML text into its static content components.
    /// </summary>
    public static ParsedContent Parse(string html)
    {
        string css = ExtractInlineStyles(html);
        string js = ExtractInlineScripts(html);
        var cssUrls = ExtractStylesheetUrls(html);
        var jsUrls = ExtractScriptUrls(html);

        // G-02: check if we have any meaningful content
        bool isEmpty = string.IsNullOrWhiteSpace(html);
        bool isPartial = isEmpty || (string.IsNullOrEmpty(css) && string.IsNullOrEmpty(js)
                                      && cssUrls.Count == 0 && jsUrls.Count == 0);
        string? reason = isPartial
            ? "No extractable CSS or JavaScript found. Content may be dynamically loaded."
            : null;

        return new ParsedContent(html, css, js, cssUrls, jsUrls, isPartial, reason);
    }

    /// <summary>
    /// Build a <see cref="MirrorBundle"/> from parsed content.
    /// </summary>
    public static MirrorBundle MakeBundle(
        BundleSource source,
        string domain,
        string html,
        double fetchDuration = 0,
        string? signature = null)
    {
        var parsed = Parse(html);
        string concatenated = $"<!-- HTML -->\n{parsed.Html}\n\n<!-- CSS -->\n{parsed.Css}\n\n<!-- JavaScript -->\n{parsed.Js}";
        int byteSize = Encoding.UTF8.GetByteCount(concatenated);

        return new MirrorBundle(
            source: source,
            domain: domain,
            timestamp: DateTimeOffset.UtcNow,
            htmlContent: parsed.Html,
            cssContent: parsed.Css,
            jsContent: parsed.Js,
            metadata: new BundleMetadata(
                fetchDuration: fetchDuration,
                byteSize: byteSize,
                resourceCount: parsed.CssUrls.Count + parsed.JsUrls.Count,
                signature: signature,
                serverHash: Sha256Hex(concatenated)
            )
        );
    }

    // ───────────────────────── Extraction helpers ─────────────────────────

    /// <summary>
    /// L2-018: timeout for all regex operations to prevent ReDoS on adversarial input.
    /// </summary>
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Extract inline style block content.
    /// <para>L2-012 regex limitations: this pattern does NOT handle nested &lt;style&gt; tags,
    /// CDATA sections, or HTML comments inside style blocks. It matches the first closing
    /// &lt;/style&gt; tag via the lazy quantifier [\s\S]*?.</para>
    /// </summary>
    public static string ExtractInlineStyles(string html)
    {
        var matches = Regex.Matches(html,
            @"<style[^>]*>([\s\S]*?)</style>",
            RegexOptions.IgnoreCase, RegexTimeout);

        if (matches.Count == 0) return string.Empty;

        var parts = new List<string>();
        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 2)
                parts.Add(match.Groups[1].Value);
        }
        return string.Join("\n\n/* --- */\n\n", parts);
    }

    /// <summary>
    /// Extract inline script content (no src attribute).
    /// <para>L2-012 regex limitations: negative lookahead for src does not handle
    /// dynamically-added script elements or data-URI src attributes.</para>
    /// </summary>
    public static string ExtractInlineScripts(string html)
    {
        // Match script tags WITHOUT a src attribute
        var matches = Regex.Matches(html,
            @"<script(?![^>]*\bsrc\b)[^>]*>([\s\S]*?)</script>",
            RegexOptions.IgnoreCase, RegexTimeout);

        if (matches.Count == 0) return string.Empty;

        var parts = new List<string>();
        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 2)
            {
                string content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    parts.Add(content);
            }
        }
        return string.Join("\n\n// --- \n\n", parts);
    }

    /// <summary>
    /// Extract href values from link rel="stylesheet" tags.
    /// <para>L2-012 regex limitations: only matches link tags where rel appears before href
    /// in the attribute order. Does not handle rel values with extra whitespace or multiple
    /// rel values (e.g., rel="preload stylesheet").</para>
    /// </summary>
    public static IReadOnlyList<string> ExtractStylesheetUrls(string html)
    {
        var matches = Regex.Matches(html,
            @"<link[^>]*rel\s*=\s*[""']stylesheet[""'][^>]*href\s*=\s*[""']([^""]+)[""'][^>]*>",
            RegexOptions.IgnoreCase, RegexTimeout);

        var urls = new List<string>();
        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 2)
                urls.Add(match.Groups[1].Value);
        }
        return urls;
    }

    /// <summary>
    /// Extract src values from script src="..." tags.
    /// <para>L2-012 regex limitations: only matches double-quoted or single-quoted src values;
    /// unquoted src attributes are not matched.</para>
    /// </summary>
    public static IReadOnlyList<string> ExtractScriptUrls(string html)
    {
        var matches = Regex.Matches(html,
            @"<script[^>]*\bsrc\s*=\s*[""']([^""]+)[""'][^>]*>",
            RegexOptions.IgnoreCase, RegexTimeout);

        var urls = new List<string>();
        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 2)
                urls.Add(match.Groups[1].Value);
        }
        return urls;
    }

    // ───────────────────────── G-02 Fallback ─────────────────────────

    /// <summary>
    /// Extract content from template and slot tags as a fallback when no JS/CSS is available.
    /// </summary>
    public static string ExtractFallbackContent(string html)
    {
        var parts = new List<string>();

        // Template contents
        var templateMatches = Regex.Matches(html,
            @"<template[^>]*>([\s\S]*?)</template>",
            RegexOptions.IgnoreCase, RegexTimeout);
        foreach (Match match in templateMatches)
        {
            if (match.Groups.Count >= 2)
            {
                string content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    parts.Add($"<!-- <template> -->\n{content}");
            }
        }

        // Slot contents
        var slotMatches = Regex.Matches(html,
            @"<slot[^>]*>([\s\S]*?)</slot>",
            RegexOptions.IgnoreCase, RegexTimeout);
        foreach (Match match in slotMatches)
        {
            if (match.Groups.Count >= 2)
            {
                string content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    parts.Add($"<!-- <slot> -->\n{content}");
            }
        }

        return string.Join("\n\n", parts);
    }

    // ───────────────────────── SHA-256 ─────────────────────────

    /// <summary>
    /// Compute SHA-256 hex digest of a string.
    /// </summary>
    public static string Sha256Hex(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }
}
