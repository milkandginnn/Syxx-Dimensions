import XCTest
@testable import SyxxKit

final class DiffEngineTests: XCTestCase {

    // MARK: - Identical content

    func testIdenticalContentProducesEmptyDiff() {
        let text = "line1\nline2\nline3"
        let result = DiffEngine.diff(remote: text, local: text)
        XCTAssertTrue(result.isEmpty, "Identical content should produce empty diff")
    }

    func testBothEmptyProducesEmptyDiff() {
        let result = DiffEngine.diff(remote: "", local: "")
        XCTAssertTrue(result.isEmpty)
    }

    // MARK: - Simple additions and removals

    func testAddedLine() {
        // "line3" exists in local but not in remote → added
        let remote = "line1\nline2"
        let local = "line1\nline2\nline3"
        let result = DiffEngine.diff(remote: remote, local: local)
        let added = result.filter { $0.kind == .added }
        XCTAssertGreaterThanOrEqual(added.count, 1, "Should detect the added line (local-only)")
    }

    func testRemovedLine() {
        // "line3" exists in remote but not in local → removed
        let remote = "line1\nline2\nline3"
        let local = "line1\nline2"
        let result = DiffEngine.diff(remote: remote, local: local)
        let removed = result.filter { $0.kind == .removed }
        XCTAssertGreaterThanOrEqual(removed.count, 1, "Should detect the removed line (remote-only)")
    }

    func testReplacedLine() {
        let remote = "line1\nold line\nline3"
        let local = "line1\nnew line\nline3"
        let result = DiffEngine.diff(remote: remote, local: local)
        let added = result.filter { $0.kind == .added }
        let removed = result.filter { $0.kind == .removed }
        XCTAssertGreaterThanOrEqual(added.count, 1)
        XCTAssertGreaterThanOrEqual(removed.count, 1)
    }

    // MARK: - Completely different content

    func testCompletelyDifferent() {
        let remote = "alpha\nbeta\ngamma"
        let local = "one\ntwo\nthree"
        let result = DiffEngine.diff(remote: remote, local: local)
        let changes = result.filter { $0.kind != .context }
        XCTAssertGreaterThan(changes.count, 0, "Completely different content should have changes")
    }

    // MARK: - Line numbers

    func testContextLinesHaveBothLineNumbers() {
        let remote = "same\nsame\nsame"
        let local = "same\nDIFFERENT\nsame"
        let result = DiffEngine.diff(remote: remote, local: local)
        let contextLines = result.filter { $0.kind == .context }
        for line in contextLines {
            XCTAssertNotNil(line.oldLineNumber, "Context lines should have old line number")
            XCTAssertNotNil(line.newLineNumber, "Context lines should have new line number")
        }
    }

    func testAddedLinesHaveOnlyNewLineNumber() {
        // "line2" exists in local but not remote → added
        let remote = "line1"
        let local = "line1\nline2"
        let result = DiffEngine.diff(remote: remote, local: local)
        let added = result.filter { $0.kind == .added }
        XCTAssertGreaterThanOrEqual(added.count, 1, "Should detect the added line")
        for line in added {
            XCTAssertNil(line.oldLineNumber, "Added lines should not have old (remote) line number")
            XCTAssertNotNil(line.newLineNumber, "Added lines should have new (local) line number")
        }
    }

    func testRemovedLinesHaveOnlyOldLineNumber() {
        // "line2" exists in remote but not local → removed
        let remote = "line1\nline2"
        let local = "line1"
        let result = DiffEngine.diff(remote: remote, local: local)
        let removed = result.filter { $0.kind == .removed }
        XCTAssertGreaterThanOrEqual(removed.count, 1, "Should detect the removed line")
        for line in removed {
            XCTAssertNotNil(line.oldLineNumber, "Removed lines should have old (remote) line number")
            XCTAssertNil(line.newLineNumber, "Removed lines should not have new (local) line number")
        }
    }

    // MARK: - Bundle comparison

    func testCompareBundles() {
        let remote = MirrorBundle(
            source: .remote, domain: "example.com", timestamp: Date(),
            htmlContent: "<html>remote</html>",
            cssContent: "body { color: red; }",
            jsContent: "console.log('remote');",
            metadata: BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "")
        )
        let local = MirrorBundle(
            source: .local, domain: "example.com", timestamp: Date(),
            htmlContent: "<html>local</html>",
            cssContent: "body { color: blue; }",
            jsContent: "console.log('local');",
            metadata: BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "")
        )
        let result = DiffEngine.compare(remote: remote, local: local)
        XCTAssertTrue(result.hasChanges)
        XCTAssertGreaterThan(result.totalDifferences, 0)
    }

    func testCompareIdenticalBundles() {
        let bundle = MirrorBundle(
            source: .remote, domain: "example.com", timestamp: Date(),
            htmlContent: "<html>same</html>",
            cssContent: "body { margin: 0; }",
            jsContent: "// same",
            metadata: BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "")
        )
        let localBundle = MirrorBundle(
            source: .local, domain: "example.com", timestamp: Date(),
            htmlContent: "<html>same</html>",
            cssContent: "body { margin: 0; }",
            jsContent: "// same",
            metadata: BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "")
        )
        let result = DiffEngine.compare(remote: bundle, local: localBundle)
        XCTAssertFalse(result.hasChanges)
        XCTAssertEqual(result.totalDifferences, 0)
    }

    // MARK: - Utility counts

    func testChangeCount() {
        let lines = [
            DiffLine(id: 0, kind: .context, text: "a", oldLineNumber: 1, newLineNumber: 1),
            DiffLine(id: 1, kind: .added, text: "b", oldLineNumber: nil, newLineNumber: 2),
            DiffLine(id: 2, kind: .removed, text: "c", oldLineNumber: 2, newLineNumber: nil),
            DiffLine(id: 3, kind: .context, text: "d", oldLineNumber: 3, newLineNumber: 3),
        ]
        XCTAssertEqual(DiffEngine.changeCount(lines), 2)
        XCTAssertEqual(DiffEngine.additionCount(lines), 1)
        XCTAssertEqual(DiffEngine.removalCount(lines), 1)
    }

    // MARK: - DiffResult properties

    func testDiffResultTotals() {
        let htmlDiff = [
            DiffLine(id: 0, kind: .added, text: "x", oldLineNumber: nil, newLineNumber: 1),
            DiffLine(id: 1, kind: .added, text: "y", oldLineNumber: nil, newLineNumber: 2),
        ]
        let cssDiff = [
            DiffLine(id: 2, kind: .removed, text: "z", oldLineNumber: 1, newLineNumber: nil),
        ]
        let result = DiffResult(htmlDiff: htmlDiff, cssDiff: cssDiff, jsDiff: [])
        XCTAssertEqual(result.totalAdditions, 2)
        XCTAssertEqual(result.totalRemovals, 1)
        XCTAssertEqual(result.totalDifferences, 3)
        XCTAssertTrue(result.hasChanges)
    }

    func testDiffResultSectionAccess() {
        let htmlDiff = [DiffLine(id: 0, kind: .context, text: "html", oldLineNumber: 1, newLineNumber: 1)]
        let cssDiff = [DiffLine(id: 1, kind: .context, text: "css", oldLineNumber: 1, newLineNumber: 1)]
        let jsDiff = [DiffLine(id: 2, kind: .context, text: "js", oldLineNumber: 1, newLineNumber: 1)]
        let result = DiffResult(htmlDiff: htmlDiff, cssDiff: cssDiff, jsDiff: jsDiff)
        XCTAssertEqual(result.lines(for: .html).count, 1)
        XCTAssertEqual(result.lines(for: .css).count, 1)
        XCTAssertEqual(result.lines(for: .javascript).count, 1)
        XCTAssertEqual(result.lines(for: .html).first?.text, "html")
    }
}
