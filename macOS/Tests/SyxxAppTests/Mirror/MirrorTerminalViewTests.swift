import XCTest
import SwiftUI
@testable import SyxxApp
@testable import SyxxKit

@MainActor
final class MirrorTerminalViewTests: XCTestCase {

    // MARK: - View instantiation

    func testMirrorTerminalViewCanBeCreated() {
        let view = MirrorTerminalView(
            title: "Remote (Server)",
            domain: "example.com",
            bundle: nil,
            warning: nil
        )
        // Verify it can be rendered without crashing
        let _ = view.body
    }

    func testMirrorTerminalViewWithBundle() {
        let bundle = MirrorBundle(
            source: .remote,
            domain: "example.com",
            timestamp: Date(),
            htmlContent: "<html><body>Hello</body></html>",
            cssContent: "body { color: red; }",
            jsContent: "console.log('test');",
            metadata: BundleMetadata(
                fetchDuration: 0.5,
                byteSize: 100,
                resourceCount: 2,
                signature: "abc123",
                serverHash: "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456"
            )
        )
        let view = MirrorTerminalView(
            title: "Remote (Server)",
            domain: "example.com",
            bundle: bundle,
            warning: nil
        )
        let _ = view.body
    }

    func testMirrorTerminalViewWithWarning() {
        let view = MirrorTerminalView(
            title: "Local (Your Mac)",
            domain: "example.com",
            bundle: nil,
            warning: "No static content extracted. Content may be dynamically loaded."
        )
        let _ = view.body
    }

    // MARK: - MirrorDiffView instantiation

    func testMirrorDiffViewCanBeCreated() {
        let diff = DiffResult(htmlDiff: [], cssDiff: [], jsDiff: [])
        let view = MirrorDiffView(diffResult: diff)
        let _ = view.body
    }

    func testMirrorDiffViewWithChanges() {
        let lines = [
            DiffLine(id: 0, kind: .context, text: "same", oldLineNumber: 1, newLineNumber: 1),
            DiffLine(id: 1, kind: .removed, text: "old", oldLineNumber: 2, newLineNumber: nil),
            DiffLine(id: 2, kind: .added, text: "new", oldLineNumber: nil, newLineNumber: 2),
        ]
        let diff = DiffResult(htmlDiff: lines, cssDiff: [], jsDiff: [])
        let view = MirrorDiffView(diffResult: diff)
        let _ = view.body
    }

    // MARK: - MirrorDegradationBadge instantiation

    func testDegradationBadgeCanBeCreated() {
        let score = DegradationScore(score: 0.23)
        let badge = MirrorDegradationBadge(score: score)
        let _ = badge.body
    }

    func testDegradationBadgeAllClassifications() {
        for classification in DegradationScore.Classification.allCases {
            let score: Double
            switch classification {
            case .identical: score = 0.0
            case .minorDifferences: score = 0.05
            case .moderateDifferences: score = 0.2
            case .significantDifferences: score = 0.45
            case .criticalDifferences: score = 0.8
            }
            let badge = MirrorDegradationBadge(score: DegradationScore(score: score))
            let _ = badge.body
        }
    }

    // MARK: - MirrorBundle Equatable

    func testMirrorBundleEquatable() {
        let ts = Date()
        let meta = BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "abc")
        let b1 = MirrorBundle(source: .remote, domain: "x.com", timestamp: ts,
                              htmlContent: "a", cssContent: "b", jsContent: "c", metadata: meta)
        let b2 = MirrorBundle(source: .remote, domain: "x.com", timestamp: ts,
                              htmlContent: "a", cssContent: "b", jsContent: "c", metadata: meta)
        XCTAssertEqual(b1, b2)
    }

    // MARK: - MirrorResult

    func testMirrorResultConstruction() {
        let meta = BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "abc")
        let remote = MirrorBundle(source: .remote, domain: "x.com", timestamp: Date(),
                                  htmlContent: "a", cssContent: "", jsContent: "", metadata: meta)
        let local = MirrorBundle(source: .local, domain: "x.com", timestamp: Date(),
                                 htmlContent: "a", cssContent: "", jsContent: "", metadata: meta)
        let diff = DiffResult(htmlDiff: [], cssDiff: [], jsDiff: [])
        let score = DegradationScore(score: 0.0)
        let result = MirrorResult(
            remoteBundle: remote, localBundle: local,
            diffResult: diff, degradationScore: score,
            comparisonTimestamp: Date()
        )
        XCTAssertFalse(result.isPartial)
        XCTAssertNil(result.partialReason)
    }

    func testMirrorResultPartial() {
        let meta = BundleMetadata(fetchDuration: 0, byteSize: 0, resourceCount: 0, signature: nil, serverHash: "abc")
        let remote = MirrorBundle(source: .remote, domain: "x.com", timestamp: Date(),
                                  htmlContent: "", cssContent: "", jsContent: "", metadata: meta)
        let local = MirrorBundle(source: .local, domain: "x.com", timestamp: Date(),
                                 htmlContent: "", cssContent: "", jsContent: "", metadata: meta)
        let diff = DiffResult(htmlDiff: [], cssDiff: [], jsDiff: [])
        let score = DegradationScore(score: 0.0)
        let result = MirrorResult(
            remoteBundle: remote, localBundle: local,
            diffResult: diff, degradationScore: score,
            comparisonTimestamp: Date(),
            isPartial: true,
            partialReason: "Dynamic content only"
        )
        XCTAssertTrue(result.isPartial)
        XCTAssertEqual(result.partialReason, "Dynamic content only")
    }
}
