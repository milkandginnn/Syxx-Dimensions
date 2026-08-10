using System;
using System.Linq;

namespace SyxxUI.Mirror;

/// <summary>
/// Identifies whether a bundle was captured from the remote Dimension server or locally.
/// </summary>
public enum BundleSource
{
    Remote,
    Local
}

/// <summary>
/// Metadata accompanying a captured web-content bundle.
/// </summary>
public sealed class BundleMetadata
{
    /// <summary>Wall-clock time the fetch took (seconds).</summary>
    public double FetchDuration { get; }
    /// <summary>Total byte size of all concatenated content.</summary>
    public int ByteSize { get; }
    /// <summary>Number of external resources (CSS files, JS files) fetched.</summary>
    public int ResourceCount { get; }
    /// <summary>Ed25519 signature in hex (remote bundles only; null for local).</summary>
    public string? Signature { get; }
    /// <summary>SHA-256 hex digest of the concatenated content.</summary>
    public string ServerHash { get; }

    public BundleMetadata(double fetchDuration, int byteSize, int resourceCount, string? signature, string serverHash)
    {
        FetchDuration = fetchDuration;
        ByteSize = byteSize;
        ResourceCount = resourceCount;
        Signature = signature;
        ServerHash = serverHash;
    }
}

/// <summary>
/// An immutable snapshot of static web content (HTML + CSS + JS) captured from one vantage point.
/// </summary>
public sealed class MirrorBundle
{
    /// <summary>Where this bundle was captured.</summary>
    public BundleSource Source { get; }
    /// <summary>The domain that was fetched.</summary>
    public string Domain { get; }
    /// <summary>When the capture occurred.</summary>
    public DateTimeOffset Timestamp { get; }
    /// <summary>Raw HTML content.</summary>
    public string HtmlContent { get; }
    /// <summary>Concatenated CSS (all stylesheets joined).</summary>
    public string CssContent { get; }
    /// <summary>Concatenated JavaScript (all scripts joined).</summary>
    public string JsContent { get; }
    /// <summary>Fetch metadata (timing, sizes, hashes).</summary>
    public BundleMetadata Metadata { get; }

    public MirrorBundle(
        BundleSource source, string domain, DateTimeOffset timestamp,
        string htmlContent, string cssContent, string jsContent, BundleMetadata metadata)
    {
        Source = source;
        Domain = domain;
        Timestamp = timestamp;
        HtmlContent = htmlContent;
        CssContent = cssContent;
        JsContent = jsContent;
        Metadata = metadata;
    }

    /// <summary>Canonical concatenation: HTML → CSS → JavaScript.</summary>
    public string Concatenated =>
        $"<!-- HTML -->\n{HtmlContent}\n\n<!-- CSS -->\n{CssContent}\n\n<!-- JavaScript -->\n{JsContent}";

    /// <summary>True if all content fields are empty (used to show fallback warnings).</summary>
    public bool IsEmpty =>
        string.IsNullOrEmpty(HtmlContent) && string.IsNullOrEmpty(CssContent) && string.IsNullOrEmpty(JsContent);

    /// <summary>A human-readable summary for the terminal header.</summary>
    public string SummaryLine
    {
        get
        {
            int htmlLines = string.IsNullOrEmpty(HtmlContent) ? 0 : HtmlContent.Split('\n').Length;
            int cssLines = string.IsNullOrEmpty(CssContent) ? 0 : CssContent.Split('\n').Length;
            int jsLines = string.IsNullOrEmpty(JsContent) ? 0 : JsContent.Split('\n').Length;
            return $"HTML: {htmlLines}L · CSS: {cssLines}L · JS: {jsLines}L · {Metadata.ByteSize} bytes";
        }
    }
}
