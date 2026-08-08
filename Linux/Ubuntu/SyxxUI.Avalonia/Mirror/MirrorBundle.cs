namespace SyxxUI.Avalonia.Mirror;

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
    /// Wall-clock time the fetch took (seconds).
    public double FetchDuration { get; }

    /// Total byte size of all concatenated content.
    public int ByteSize { get; }

    /// Number of external resources (CSS files, JS files) fetched.
    public int ResourceCount { get; }

    /// Ed25519 signature in hex (remote bundles only; null for local).
    public string? Signature { get; }

    /// SHA-256 hex digest of the concatenated content.
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
    /// Where this bundle was captured.
    public BundleSource Source { get; }

    /// The domain that was fetched.
    public string Domain { get; }

    /// When the capture occurred.
    public DateTimeOffset Timestamp { get; }

    /// Raw HTML content.
    public string HtmlContent { get; }

    /// Concatenated CSS (all stylesheets joined).
    public string CssContent { get; }

    /// Concatenated JavaScript (all scripts joined).
    public string JsContent { get; }

    /// Fetch metadata (timing, sizes, hashes).
    public BundleMetadata Metadata { get; }

    public MirrorBundle(
        BundleSource source,
        string domain,
        DateTimeOffset timestamp,
        string htmlContent,
        string cssContent,
        string jsContent,
        BundleMetadata metadata)
    {
        Source = source;
        Domain = domain;
        Timestamp = timestamp;
        HtmlContent = htmlContent;
        CssContent = cssContent;
        JsContent = jsContent;
        Metadata = metadata;
    }

    /// Canonical concatenation: HTML → CSS → JavaScript.
    public string Concatenated =>
        $"<!-- HTML -->\n{HtmlContent}\n\n<!-- CSS -->\n{CssContent}\n\n<!-- JavaScript -->\n{JsContent}";

    /// True if all content fields are empty (used to show fallback warnings).
    public bool IsEmpty =>
        string.IsNullOrEmpty(HtmlContent) &&
        string.IsNullOrEmpty(CssContent) &&
        string.IsNullOrEmpty(JsContent);

    /// A human-readable summary for the terminal header.
    public string SummaryLine
    {
        get
        {
            int htmlLines = CountLines(HtmlContent);
            int cssLines = CountLines(CssContent);
            int jsLines = CountLines(JsContent);
            return $"HTML: {htmlLines}L · CSS: {cssLines}L · JS: {jsLines}L · {Metadata.ByteSize} bytes";
        }
    }

    /// <summary>
    /// Count the number of lines in a text. D334: empty or null text returns 0, not 1 — an empty
    /// content section has zero lines, and a count of 1 fabricated a line that does not exist.
    /// </summary>
    private static int CountLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n') count++;
        }
        return count;
    }
}
