using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SyxxUI.Mirror;

/// <summary>
/// Extracts static web content (HTML structure, CSS, JavaScript) from raw HTML text.
///
/// This is a lightweight regex-based parser — NOT a full HTML parser. It extracts:
/// - The HTML skeleton (everything including inline style content)
/// - CSS from style tags and link rel="stylesheet" href attributes
/// - JavaScript from script tags (inline bodies) and script src="..." attributes
///
/// G-02 fallback: when resources can't be fetched (e.g. pre-JS-execution captures), the parser
/// falls back to concatenating whatever static elements are available (slot, template tag contents,
/// inline JS) and flags the result as partial.
/// </summary>
public static class StaticContentParser
{
    /// <summary>Result of parsing raw HTML into its constituent parts.</summary>
    public sealed class ParsedContent
    {
        /// <summary>The full HTML (unchanged).</summary>
        public string Html { get; }
        /// <summary>Extracted CSS content (inline style blocks joined).</summary>
        public string Css { get; }
        /// <summary>Extracted JavaScript content (inline script bodies joined).</summary>
        public string Js { get; }
        /// <summary>External stylesheet URLs found in link tags.</summary>
        public List<string> CssUrls { get; }
        /// <summary>External script URLs found in script src="..." tags.</summary>
        public List<string> JsUrls { get; }
        /// <summary>Whether any content was missing or unparseable (G-02 partial flag).</summary>
        public bool IsPartial { get; }
        /// <summary>Reason the content is partial, if applicable.</summary>
        public string? PartialReason { get; }

        public ParsedContent(string html, string css, string js,
            List<string> cssUrls, List<string> jsUrls, bool isPartial, string? partialReason)
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

    /// <summary>Parse raw HTML text into its static content components.</summary>
    public static ParsedContent Parse(string html)
    {
        var css = ExtractInlineStyles(html);
        var js = ExtractInlineScripts(html);
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

    /// <summary>Build a <see cref="MirrorBundle"/> from parsed content.</summary>
    public static MirrorBundle MakeBundle(
        BundleSource source, string domain, string html,
        double fetchDuration = 0, string? signature = null)
    {
        var parsed = Parse(html);
        string concatenated = $"<!-- HTML -->\n{parsed.Html}\n\n<!-- CSS -->\n{parsed.Css}\n\n<!-- JavaScript -->\n{parsed.Js}";
        int byteSize = Encoding.UTF8.GetByteCount(concatenated);

        return new MirrorBundle(
            source, domain, DateTimeOffset.Now,
            parsed.Html, parsed.Css, parsed.Js,
            new BundleMetadata(
                fetchDuration, byteSize,
                parsed.CssUrls.Count + parsed.JsUrls.Count,
                signature, Sha256Hex(concatenated)));
    }

    // MARK: - Extraction helpers

    /// <summary>Extract inline style block content.</summary>
    public static string ExtractInlineStyles(string html)
    {
        var pattern = @"<style[^>]*>([\s\S]*?)</style>";
        var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
        var parts = new List<string>();
        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 2)
                parts.Add(match.Groups[1].Value);
        }
        return string.Join("\n\n/* --- */\n\n", parts);
    }

    /// <summary>Extract inline script content (no src attribute).</summary>
    public static string ExtractInlineScripts(string html)
    {
        // Match <script> tags WITHOUT a src attribute
        var pattern = @"<script(?![^>]*\bsrc\b)[^>]*>([\s\S]*?)</script>";
        var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
        var parts = new List<string>();
        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 2)
            {
                var content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    parts.Add(content);
            }
        }
        return string.Join("\n\n// --- \n\n", parts);
    }

    /// <summary>Extract href values from link rel="stylesheet" tags.</summary>
    public static List<string> ExtractStylesheetUrls(string html)
    {
        var pattern = @"<link[^>]*rel\s*=\s*[""']stylesheet[""'][^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>";
        return ExtractAttribute(pattern, html);
    }

    /// <summary>Extract src values from script src="..." tags.</summary>
    public static List<string> ExtractScriptUrls(string html)
    {
        var pattern = @"<script[^>]*\bsrc\s*=\s*[""']([^""']+)[""'][^>]*>";
        return ExtractAttribute(pattern, html);
    }

    /// <summary>Generic regex-based attribute extraction.</summary>
    private static List<string> ExtractAttribute(string pattern, string html)
    {
        var results = new List<string>();
        var matches = Regex.Matches(html, pattern, RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            if (match.Groups.Count >= 2)
                results.Add(match.Groups[1].Value);
        }
        return results;
    }

    // MARK: - G-02 Fallback

    /// <summary>Extract content from template and slot tags as a fallback when no JS/CSS is available.</summary>
    public static string ExtractFallbackContent(string html)
    {
        var parts = new List<string>();

        // Template contents
        var templatePattern = @"<template[^>]*>([\s\S]*?)</template>";
        foreach (Match match in Regex.Matches(html, templatePattern, RegexOptions.IgnoreCase))
        {
            if (match.Groups.Count >= 2)
            {
                var content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    parts.Add($"<!-- <template> -->\n{content}");
            }
        }

        // Slot contents
        var slotPattern = @"<slot[^>]*>([\s\S]*?)</slot>";
        foreach (Match match in Regex.Matches(html, slotPattern, RegexOptions.IgnoreCase))
        {
            if (match.Groups.Count >= 2)
            {
                var content = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(content))
                    parts.Add($"<!-- <slot> -->\n{content}");
            }
        }

        return string.Join("\n\n", parts);
    }

    // MARK: - SHA-256

    /// <summary>Compute SHA-256 hex digest of a string.</summary>
    public static string Sha256Hex(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        byte[] hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(64);
        foreach (byte b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
