import Foundation

/// Identifies whether a bundle was captured from the remote Dimension server or locally.
public enum BundleSource: String, Sendable, Codable, Equatable {
    case remote
    case local
}

/// Metadata accompanying a captured web-content bundle.
public struct BundleMetadata: Sendable, Codable, Equatable {
    /// Wall-clock time the fetch took.
    public let fetchDuration: TimeInterval
    /// Total byte size of all concatenated content.
    public let byteSize: Int
    /// Number of external resources (CSS files, JS files) fetched.
    public let resourceCount: Int
    /// Ed25519 signature in hex (remote bundles only; nil for local).
    public let signature: String?
    /// SHA-256 hex digest of the concatenated content.
    public let serverHash: String

    public init(
        fetchDuration: TimeInterval,
        byteSize: Int,
        resourceCount: Int,
        signature: String?,
        serverHash: String
    ) {
        self.fetchDuration = fetchDuration
        self.byteSize = byteSize
        self.resourceCount = resourceCount
        self.signature = signature
        self.serverHash = serverHash
    }
}

/// An immutable snapshot of static web content (HTML + CSS + JS) captured from one vantage point.
///
/// Memory isolation: remote and local bundles are always separate `MirrorBundle` instances with
/// no shared mutable state. The diff engine receives immutable copies.
public struct MirrorBundle: Sendable, Codable, Equatable {
    /// Where this bundle was captured.
    public let source: BundleSource
    /// The domain that was fetched.
    public let domain: String
    /// When the capture occurred.
    public let timestamp: Date
    /// Raw HTML content.
    public let htmlContent: String
    /// Concatenated CSS (all stylesheets joined).
    public let cssContent: String
    /// Concatenated JavaScript (all scripts joined).
    public let jsContent: String
    /// Fetch metadata (timing, sizes, hashes).
    public let metadata: BundleMetadata

    public init(
        source: BundleSource,
        domain: String,
        timestamp: Date,
        htmlContent: String,
        cssContent: String,
        jsContent: String,
        metadata: BundleMetadata
    ) {
        self.source = source
        self.domain = domain
        self.timestamp = timestamp
        self.htmlContent = htmlContent
        self.cssContent = cssContent
        self.jsContent = jsContent
        self.metadata = metadata
    }

    /// Canonical concatenation: HTML → CSS → JavaScript.
    public var concatenated: String {
        """
        <!-- HTML -->
        \(htmlContent)

        <!-- CSS -->
        \(cssContent)

        <!-- JavaScript -->
        \(jsContent)
        """
    }

    /// True if all content fields are empty (used to show fallback warnings).
    public var isEmpty: Bool {
        htmlContent.isEmpty && cssContent.isEmpty && jsContent.isEmpty
    }

    /// A human-readable summary for the terminal header.
    public var summaryLine: String {
        let htmlLines = htmlContent.components(separatedBy: "\n").count
        let cssLines = cssContent.components(separatedBy: "\n").count
        let jsLines = jsContent.components(separatedBy: "\n").count
        return "HTML: \(htmlLines)L · CSS: \(cssLines)L · JS: \(jsLines)L · \(metadata.byteSize) bytes"
    }
}
