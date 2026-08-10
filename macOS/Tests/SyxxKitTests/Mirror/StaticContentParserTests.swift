import XCTest
@testable import SyxxKit

final class StaticContentParserTests: XCTestCase {

    // MARK: - Inline style extraction

    func testExtractsInlineStyles() {
        let html = """
        <html><head>
        <style>body { color: red; }</style>
        <style>h1 { font-size: 24px; }</style>
        </head><body></body></html>
        """
        let result = StaticContentParser.parse(html: html)
        XCTAssertTrue(result.css.contains("color: red"))
        XCTAssertTrue(result.css.contains("font-size: 24px"))
    }

    func testNoInlineStylesReturnsEmpty() {
        let html = "<html><head></head><body>Hello</body></html>"
        let result = StaticContentParser.parse(html: html)
        XCTAssertTrue(result.css.isEmpty)
    }

    // MARK: - Inline script extraction

    func testExtractsInlineScripts() {
        let html = """
        <html><body>
        <script>console.log('hello');</script>
        <script src="external.js"></script>
        <script>document.title = 'test';</script>
        </body></html>
        """
        let result = StaticContentParser.parse(html: html)
        XCTAssertTrue(result.js.contains("console.log"))
        XCTAssertTrue(result.js.contains("document.title"))
        // External scripts should NOT be in inline JS
        XCTAssertFalse(result.js.contains("external.js"))
    }

    func testEmptyScriptTagsIgnored() {
        let html = "<script></script><script>  </script>"
        let result = StaticContentParser.parse(html: html)
        XCTAssertTrue(result.js.isEmpty, "Empty script tags should not produce output")
    }

    // MARK: - External URL extraction

    func testExtractsStylesheetURLs() {
        let html = """
        <link rel="stylesheet" href="styles.css">
        <link rel="stylesheet" href="/assets/main.css">
        <link rel="icon" href="favicon.ico">
        """
        let result = StaticContentParser.parse(html: html)
        XCTAssertEqual(result.cssURLs.count, 2)
        XCTAssertTrue(result.cssURLs.contains("styles.css"))
        XCTAssertTrue(result.cssURLs.contains("/assets/main.css"))
    }

    func testExtractsScriptURLs() {
        let html = """
        <script src="app.js"></script>
        <script src="https://cdn.example.com/lib.js"></script>
        <script>inlineOnly();</script>
        """
        let result = StaticContentParser.parse(html: html)
        XCTAssertEqual(result.jsURLs.count, 2)
        XCTAssertTrue(result.jsURLs.contains("app.js"))
        XCTAssertTrue(result.jsURLs.contains("https://cdn.example.com/lib.js"))
    }

    // MARK: - Partial content detection (G-02)

    func testEmptyHTMLIsPartial() {
        let result = StaticContentParser.parse(html: "")
        XCTAssertTrue(result.isPartial)
        XCTAssertNotNil(result.partialReason)
    }

    func testHTMLWithOnlyBodyIsPartial() {
        let result = StaticContentParser.parse(html: "<html><body>Just text</body></html>")
        XCTAssertTrue(result.isPartial, "HTML with no CSS/JS and no external resources should be partial")
    }

    func testHTMLWithCSSIsNotPartial() {
        let html = "<html><style>body{}</style></html>"
        let result = StaticContentParser.parse(html: html)
        XCTAssertFalse(result.isPartial)
    }

    func testHTMLWithExternalResourcesIsNotPartial() {
        let html = """
        <html>
        <link rel="stylesheet" href="style.css">
        <script src="app.js"></script>
        </html>
        """
        let result = StaticContentParser.parse(html: html)
        XCTAssertFalse(result.isPartial)
    }

    // MARK: - makeBundle

    func testMakeBundleProducesValidBundle() {
        let html = """
        <html><head>
        <style>body { margin: 0; }</style>
        </head><body>
        <script>alert('hi');</script>
        </body></html>
        """
        let bundle = StaticContentParser.makeBundle(source: .remote, domain: "test.com", html: html)
        XCTAssertEqual(bundle.source, .remote)
        XCTAssertEqual(bundle.domain, "test.com")
        XCTAssertTrue(bundle.htmlContent.contains("<html>"))
        XCTAssertTrue(bundle.cssContent.contains("margin"))
        XCTAssertTrue(bundle.jsContent.contains("alert"))
        XCTAssertFalse(bundle.metadata.serverHash.isEmpty)
        XCTAssertGreaterThan(bundle.metadata.byteSize, 0)
    }

    func testMakeBundleHashIsDeterministic() {
        let html = "<html>same content</html>"
        let bundle1 = StaticContentParser.makeBundle(source: .remote, domain: "x.com", html: html)
        let bundle2 = StaticContentParser.makeBundle(source: .local, domain: "x.com", html: html)
        XCTAssertEqual(bundle1.metadata.serverHash, bundle2.metadata.serverHash,
                       "Same content should produce same hash regardless of source")
    }

    // MARK: - G-02 Fallback content

    func testExtractsTemplateContent() {
        let html = """
        <html>
        <template><div>Template content here</div></template>
        </html>
        """
        let result = StaticContentParser.extractFallbackContent(from: html)
        XCTAssertTrue(result.contains("Template content here"))
    }

    func testExtractsSlotContent() {
        let html = """
        <html>
        <slot><span>Slot fallback</span></slot>
        </html>
        """
        let result = StaticContentParser.extractFallbackContent(from: html)
        XCTAssertTrue(result.contains("Slot fallback"))
    }

    func testFallbackEmptyWhenNoTemplatesOrSlots() {
        let html = "<html><body>Plain content</body></html>"
        let result = StaticContentParser.extractFallbackContent(from: html)
        XCTAssertTrue(result.isEmpty)
    }

    // MARK: - SHA-256

    func testSHA256ProducesConsistentHash() {
        let hash1 = StaticContentParser.sha256Hex("hello")
        let hash2 = StaticContentParser.sha256Hex("hello")
        XCTAssertEqual(hash1, hash2)
        XCTAssertEqual(hash1.count, 64, "SHA-256 hex should be 64 characters")
    }

    func testSHA256DifferentInputsDifferentHashes() {
        let hash1 = StaticContentParser.sha256Hex("hello")
        let hash2 = StaticContentParser.sha256Hex("world")
        XCTAssertNotEqual(hash1, hash2)
    }

    // MARK: - MirrorBundle properties

    func testMirrorBundleIsEmpty() {
        let bundle = MirrorBundle(
            source: .remote, domain: "x.com", timestamp: Date(),
            htmlContent: "", cssContent: "", jsContent: "",
            metadata: BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "")
        )
        XCTAssertTrue(bundle.isEmpty)
    }

    func testMirrorBundleIsNotEmpty() {
        let bundle = MirrorBundle(
            source: .remote, domain: "x.com", timestamp: Date(),
            htmlContent: "<html></html>", cssContent: "", jsContent: "",
            metadata: BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "")
        )
        XCTAssertFalse(bundle.isEmpty)
    }

    func testMirrorBundleConcatenated() {
        let bundle = MirrorBundle(
            source: .remote, domain: "x.com", timestamp: Date(),
            htmlContent: "<html>", cssContent: "body{}", jsContent: "var x;",
            metadata: BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "")
        )
        XCTAssertTrue(bundle.concatenated.contains("<!-- HTML -->"))
        XCTAssertTrue(bundle.concatenated.contains("<!-- CSS -->"))
        XCTAssertTrue(bundle.concatenated.contains("<!-- JavaScript -->"))
        XCTAssertTrue(bundle.concatenated.contains("<html>"))
        XCTAssertTrue(bundle.concatenated.contains("body{}"))
        XCTAssertTrue(bundle.concatenated.contains("var x;"))
    }

    func testMirrorBundleSummaryLine() {
        let bundle = MirrorBundle(
            source: .remote, domain: "x.com", timestamp: Date(),
            htmlContent: "line1\nline2", cssContent: "css1", jsContent: "js1\njs2\njs3",
            metadata: BundleMetadata(fetchDuration: 0, byteSize: 42, resourceCount: 0, signature: nil, serverHash: "abc")
        )
        XCTAssertTrue(bundle.summaryLine.contains("HTML: 2L"))
        XCTAssertTrue(bundle.summaryLine.contains("CSS: 1L"))
        XCTAssertTrue(bundle.summaryLine.contains("JS: 3L"))
        XCTAssertTrue(bundle.summaryLine.contains("42 bytes"))
    }

    // MARK: - DegradationScore

    func testDegradationScorePercentage() {
        XCTAssertEqual(DegradationScore(score: 0.0).percentage, 0)
        XCTAssertEqual(DegradationScore(score: 0.5).percentage, 50)
        XCTAssertEqual(DegradationScore(score: 1.0).percentage, 100)
    }

    func testDegradationScoreClassificationBoundary() {
        XCTAssertEqual(DegradationScore.Classification.classify(score: 0.0), .identical)
        XCTAssertEqual(DegradationScore.Classification.classify(score: 0.001), .minorDifferences)
        XCTAssertEqual(DegradationScore.Classification.classify(score: 0.101), .moderateDifferences)
        XCTAssertEqual(DegradationScore.Classification.classify(score: 0.301), .significantDifferences)
        XCTAssertEqual(DegradationScore.Classification.classify(score: 0.601), .criticalDifferences)
    }
}
