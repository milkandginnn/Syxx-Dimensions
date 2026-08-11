import XCTest
import AppKit
@testable import SyxxApp

/// Phase 3.8: deterministic geometry and source-guard contracts for PurpleScrollView.
/// Failure cases prove the suite goes red when geometry or call-site discipline regresses.
///
/// `@MainActor`: several tests call `PurpleScroller.knobRect(…)` — a static method on an
/// `NSScroller` subclass, which is main-actor-isolated in the macOS 15 SDK.
@MainActor
final class PurpleScrollViewTests: XCTestCase {

    func testFullPanelPreferredLengthIs420() {
        XCTAssertEqual(PurpleScrollbarGeometry.fullPanel.preferredKnobLength, 420)
        XCTAssertEqual(PurpleScrollbarGeometry.fullPanel.knobWidth, 10)
        XCTAssertEqual(PurpleScrollbarGeometry.fullPanel.cornerRadius, 4)
    }

    func testCompactPreferredLengthIs50() {
        XCTAssertEqual(PurpleScrollbarGeometry.compact.preferredKnobLength, 50)
        XCTAssertEqual(PurpleScrollbarGeometry.compact.knobWidth, 10)
    }

    func testClampDoesNotSwitchGeometry() {
        let short: CGFloat = 30
        let full = PurpleScrollbarGeometry.fullPanel.clampedKnobLength(trackLength: short)
        let compact = PurpleScrollbarGeometry.compact.clampedKnobLength(trackLength: short)
        XCTAssertEqual(full, 30, "fullPanel clamps length, does not become compact")
        XCTAssertEqual(compact, 30)
        XCTAssertNotEqual(
            PurpleScrollbarGeometry.fullPanel.preferredKnobLength,
            PurpleScrollbarGeometry.compact.preferredKnobLength,
            "failure: geometries must remain distinct preferences"
        )
    }

    func testClampNeverNegative() {
        XCTAssertEqual(PurpleScrollbarGeometry.fullPanel.clampedKnobLength(trackLength: -10), 0)
        XCTAssertEqual(PurpleScrollbarGeometry.compact.clampedKnobLength(trackLength: 0), 0)
    }

    func testKnobRectUsesFixedLengthAndWidth() {
        let bounds = NSRect(x: 0, y: 0, width: 10, height: 600)
        let slot = bounds
        let rect = PurpleScroller.knobRect(
            geometry: .fullPanel,
            bounds: bounds,
            slot: slot,
            doubleValue: 0
        )
        XCTAssertEqual(rect.width, 10, accuracy: 0.01)
        XCTAssertEqual(rect.height, 420, accuracy: 0.01)
    }

    func testCompactKnobRectHeightIs50WhenTrackAllows() {
        let bounds = NSRect(x: 0, y: 0, width: 10, height: 400)
        let rect = PurpleScroller.knobRect(
            geometry: .compact,
            bounds: bounds,
            slot: bounds,
            doubleValue: 0.5
        )
        XCTAssertEqual(rect.height, 50, accuracy: 0.01)
        XCTAssertEqual(rect.width, 10, accuracy: 0.01)
    }

    func testKnobRectClampsWhenTrackShorterThanPreferred() {
        let bounds = NSRect(x: 0, y: 0, width: 10, height: 80)
        let rect = PurpleScroller.knobRect(
            geometry: .fullPanel,
            bounds: bounds,
            slot: bounds,
            doubleValue: 0
        )
        XCTAssertEqual(rect.height, 80, accuracy: 0.01)
        XCTAssertLessThan(rect.height, 420)
    }

    func testDoubleValueZeroAndOneReachTrackEnds() {
        let bounds = NSRect(x: 0, y: 0, width: 10, height: 500)
        let top = PurpleScroller.knobRect(geometry: .compact, bounds: bounds, slot: bounds, doubleValue: 0)
        let bottom = PurpleScroller.knobRect(geometry: .compact, bounds: bounds, slot: bounds, doubleValue: 1)
        XCTAssertEqual(top.minY, bounds.minY, accuracy: 0.01)
        XCTAssertEqual(bottom.maxY, bounds.maxY, accuracy: 0.01)
    }

    /// Failure case: approved fill alpha must stay 0.35 (not a bright status color).
    func testApprovedKnobFillAlpha() {
        var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        PurpleScrollbarStyle.knobFill.usingColorSpace(.sRGB)?.getRed(&r, green: &g, blue: &b, alpha: &a)
        XCTAssertEqual(a, 0.35, accuracy: 0.02)
        XCTAssertEqual(r, 150 / 255.0, accuracy: 0.02)
        XCTAssertEqual(g, 116 / 255.0, accuracy: 0.02)
        XCTAssertEqual(b, 235 / 255.0, accuracy: 0.02)
        XCTAssertEqual(PurpleScrollbarStyle.glowBlurRadius, 6, accuracy: 0.01)
    }

    /// Source guard: production SyxxApp must not introduce raw SwiftUI ScrollView for content panes.
    func testNoRawScrollViewInPrimaryViews() throws {
        let root = URL(fileURLWithPath: #filePath)
            .deletingLastPathComponent() // SyxxAppTests
            .deletingLastPathComponent() // Tests
            .deletingLastPathComponent() // macOS
            .appendingPathComponent("Sources/SyxxApp", isDirectory: true)
        let fm = FileManager.default
        guard let enumerator = fm.enumerator(at: root, includingPropertiesForKeys: nil) else {
            return XCTFail("cannot enumerate SyxxApp sources")
        }
        var offenders: [String] = []
        let allowlist = Set([
            "PurpleScrollView.swift", // defines the component
        ])
        while let url = enumerator.nextObject() as? URL {
            guard url.pathExtension == "swift" else { continue }
            let name = url.lastPathComponent
            if allowlist.contains(name) { continue }
            let text = try String(contentsOf: url, encoding: .utf8)
            // Bare SwiftUI ScrollView { ... } — not PurpleScrollView
            if text.range(of: #"\bScrollView\s*\{"#, options: .regularExpression) != nil {
                offenders.append(name)
            }
        }
        XCTAssertTrue(
            offenders.isEmpty,
            "raw ScrollView found in \(offenders.joined(separator: ", ")); use PurpleScrollView(geometry:)"
        )
    }

    /// Mutation proof: if geometry preferred lengths collapse, tests fail.
    func testGeometryMutationWouldFail() {
        XCTAssertNotEqual(
            PurpleScrollbarGeometry.fullPanel.preferredKnobLength,
            50,
            "failure case: fullPanel must not equal compact length"
        )
        XCTAssertNotEqual(
            PurpleScrollbarGeometry.compact.preferredKnobLength,
            420,
            "failure case: compact must not equal fullPanel length"
        )
    }
}
