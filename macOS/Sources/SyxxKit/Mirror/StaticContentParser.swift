import Foundation

/// Extracts static web content (HTML structure, CSS, JavaScript) from raw HTML text.
///
/// This is a lightweight regex-based parser — NOT a full HTML parser. It extracts:
/// - The HTML skeleton (everything including `<style>` inline content)
/// - CSS from `<style>` tags and `<link rel="stylesheet">` href attributes
/// - JavaScript from `<script>` tags (inline bodies) and `<script src="...">` attributes
///
/// G-02 fallback: when resources can't be fetched (e.g. pre-JS-execution captures), the parser
/// falls back to concatenating whatever static elements are available (`<slot>`, `<template>`
/// tag contents, inline JS) and flags the result as partial.
public enum StaticContentParser {

    /// Result of parsing raw HTML into its constituent parts.
    public struct ParsedContent: Sendable, Equatable {
        /// The full HTML (unchanged).
        public let html: String
        /// Extracted CSS content (inline `<style>` blocks joined).
        public let css: String
        /// Extracted JavaScript content (inline `<script>` bodies joined).
        public let js: String
        /// External stylesheet URLs found in `<link>` tags.
        public let cssURLs: [String]
        /// External script URLs found in `<script src="...">` tags.
        public let jsURLs: [String]
        /// Whether any content was missing or unparseable (G-02 partial flag).
        public let isPartial: Bool
        /// Reason the content is partial, if applicable.
        public let partialReason: String?
    }

    /// Parse raw HTML text into its static content components.
    ///
    /// - Parameter html: Raw HTML text (as fetched from the web).
    /// - Returns: Parsed content with HTML, CSS, and JS separated.
    public static func parse(html: String) -> ParsedContent {
        let css = extractInlineStyles(from: html)
        let js = extractInlineScripts(from: html)
        let cssURLs = extractStylesheetURLs(from: html)
        let jsURLs = extractScriptURLs(from: html)

        // G-02: check if we have any meaningful content
        let isEmpty = html.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        let isPartial = isEmpty || (css.isEmpty && js.isEmpty && cssURLs.isEmpty && jsURLs.isEmpty)
        let reason: String? = isPartial
            ? "No extractable CSS or JavaScript found. Content may be dynamically loaded."
            : nil

        return ParsedContent(
            html: html,
            css: css,
            js: js,
            cssURLs: cssURLs,
            jsURLs: jsURLs,
            isPartial: isPartial,
            partialReason: reason
        )
    }

    /// Build a `MirrorBundle` from parsed content.
    public static func makeBundle(
        source: BundleSource,
        domain: String,
        html: String,
        fetchDuration: TimeInterval = 0,
        signature: String? = nil
    ) -> MirrorBundle {
        let parsed = parse(html: html)
        let concatenated = """
        <!-- HTML -->
        \(parsed.html)

        <!-- CSS -->
        \(parsed.css)

        <!-- JavaScript -->
        \(parsed.js)
        """
        let byteSize = concatenated.utf8.count

        return MirrorBundle(
            source: source,
            domain: domain,
            timestamp: Date(),
            htmlContent: parsed.html,
            cssContent: parsed.css,
            jsContent: parsed.js,
            metadata: BundleMetadata(
                fetchDuration: fetchDuration,
                byteSize: byteSize,
                resourceCount: parsed.cssURLs.count + parsed.jsURLs.count,
                signature: signature,
                serverHash: sha256Hex(concatenated)
            )
        )
    }

    // MARK: - Extraction helpers

    /// Extract inline `<style>...</style>` content.
    static func extractInlineStyles(from html: String) -> String {
        let pattern = #"<style[^>]*>([\s\S]*?)</style>"#
        guard let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive) else {
            return ""
        }
        let range = NSRange(html.startIndex..., in: html)
        let matches = regex.matches(in: html, range: range)
        return matches.compactMap { match -> String? in
            guard match.numberOfRanges >= 2 else { return nil }
            let contentRange = match.range(at: 1)
            guard let swiftRange = Range(contentRange, in: html) else { return nil }
            return String(html[swiftRange])
        }.joined(separator: "\n\n/* --- */\n\n")
    }

    /// Extract inline `<script>...</script>` content (no `src` attribute).
    static func extractInlineScripts(from html: String) -> String {
        // Match <script> tags WITHOUT a src attribute
        let pattern = #"<script(?![^>]*\bsrc\b)[^>]*>([\s\S]*?)</script>"#
        guard let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive) else {
            return ""
        }
        let range = NSRange(html.startIndex..., in: html)
        let matches = regex.matches(in: html, range: range)
        return matches.compactMap { match -> String? in
            guard match.numberOfRanges >= 2 else { return nil }
            let contentRange = match.range(at: 1)
            guard let swiftRange = Range(contentRange, in: html) else { return nil }
            let content = String(html[swiftRange]).trimmingCharacters(in: .whitespacesAndNewlines)
            return content.isEmpty ? nil : content
        }.joined(separator: "\n\n// --- \n\n")
    }

    /// Extract `href` values from `<link rel="stylesheet">` tags.
    static func extractStylesheetURLs(from html: String) -> [String] {
        let pattern = #"<link[^>]*rel\s*=\s*["']stylesheet["'][^>]*href\s*=\s*["']([^"']+)["'][^>]*>"#
        return extractAttribute(pattern: pattern, html: html)
    }

    /// Extract `src` values from `<script src="...">` tags.
    static func extractScriptURLs(from html: String) -> [String] {
        let pattern = #"<script[^>]*\bsrc\s*=\s*["']([^"']+)["'][^>]*>"#
        return extractAttribute(pattern: pattern, html: html)
    }

    /// Generic regex-based attribute extraction.
    private static func extractAttribute(pattern: String, html: String) -> [String] {
        guard let regex = try? NSRegularExpression(pattern: pattern, options: .caseInsensitive) else {
            return []
        }
        let range = NSRange(html.startIndex..., in: html)
        return regex.matches(in: html, range: range).compactMap { match -> String? in
            guard match.numberOfRanges >= 2 else { return nil }
            let urlRange = match.range(at: 1)
            guard let swiftRange = Range(urlRange, in: html) else { return nil }
            return String(html[swiftRange])
        }
    }

    // MARK: - G-02 Fallback

    /// Extract content from `<template>` and `<slot>` tags as a fallback when no JS/CSS is available.
    public static func extractFallbackContent(from html: String) -> String {
        var parts: [String] = []

        // Template contents
        let templatePattern = #"<template[^>]*>([\s\S]*?)</template>"#
        if let regex = try? NSRegularExpression(pattern: templatePattern, options: .caseInsensitive) {
            let range = NSRange(html.startIndex..., in: html)
            let matches = regex.matches(in: html, range: range)
            for match in matches {
                if match.numberOfRanges >= 2,
                   let swiftRange = Range(match.range(at: 1), in: html) {
                    let content = String(html[swiftRange]).trimmingCharacters(in: .whitespacesAndNewlines)
                    if !content.isEmpty {
                        parts.append("<!-- <template> -->\n\(content)")
                    }
                }
            }
        }

        // Slot contents
        let slotPattern = #"<slot[^>]*>([\s\S]*?)</slot>"#
        if let regex = try? NSRegularExpression(pattern: slotPattern, options: .caseInsensitive) {
            let range = NSRange(html.startIndex..., in: html)
            let matches = regex.matches(in: html, range: range)
            for match in matches {
                if match.numberOfRanges >= 2,
                   let swiftRange = Range(match.range(at: 1), in: html) {
                    let content = String(html[swiftRange]).trimmingCharacters(in: .whitespacesAndNewlines)
                    if !content.isEmpty {
                        parts.append("<!-- <slot> -->\n\(content)")
                    }
                }
            }
        }

        return parts.joined(separator: "\n\n")
    }

    // MARK: - SHA-256

    /// Compute SHA-256 hex digest of a string. Uses CryptoKit when available.
    static func sha256Hex(_ text: String) -> String {
        let data = Data(text.utf8)
        var hash = [UInt8](repeating: 0, count: 32)
        // Use CommonCrypto-compatible approach via Foundation
        data.withUnsafeBytes { bytes in
            _ = CC_SHA256(bytes.baseAddress, CC_LONG(data.count), &hash)
        }
        return hash.map { String(format: "%02x", $0) }.joined()
    }
}

// Bridge to CommonCrypto for SHA-256
import var CommonCrypto.CC_SHA256_DIGEST_LENGTH
import func CommonCrypto.CC_SHA256
import typealias CommonCrypto.CC_LONG
