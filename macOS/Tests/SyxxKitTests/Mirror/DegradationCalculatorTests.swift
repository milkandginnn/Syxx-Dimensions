import XCTest
@testable import SyxxKit

final class DegradationCalculatorTests: XCTestCase {

    // MARK: - Boundary classifications

    func testIdenticalClassification() {
        let score = DegradationScore(score: 0.0)
        XCTAssertEqual(score.classification, .identical)
        XCTAssertEqual(score.percentage, 0)
    }

    func testMinorClassification() {
        let score = DegradationScore(score: 0.05)
        XCTAssertEqual(score.classification, .minorDifferences)
    }

    func testMinorUpperBound() {
        let score = DegradationScore(score: 0.1)
        XCTAssertEqual(score.classification, .minorDifferences)
    }

    func testModerateClassification() {
        let score = DegradationScore(score: 0.2)
        XCTAssertEqual(score.classification, .moderateDifferences)
    }

    func testModerateUpperBound() {
        let score = DegradationScore(score: 0.3)
        XCTAssertEqual(score.classification, .moderateDifferences)
    }

    func testSignificantClassification() {
        let score = DegradationScore(score: 0.45)
        XCTAssertEqual(score.classification, .significantDifferences)
    }

    func testSignificantUpperBound() {
        let score = DegradationScore(score: 0.6)
        XCTAssertEqual(score.classification, .significantDifferences)
    }

    func testCriticalClassification() {
        let score = DegradationScore(score: 0.8)
        XCTAssertEqual(score.classification, .criticalDifferences)
    }

    // MARK: - Clamping

    func testScoreClampedToZero() {
        let score = DegradationScore(score: -0.5)
        XCTAssertEqual(score.score, 0.0)
        XCTAssertEqual(score.classification, .identical)
    }

    func testScoreClampedToOne() {
        let score = DegradationScore(score: 1.5)
        XCTAssertEqual(score.score, 1.0)
        XCTAssertEqual(score.classification, .criticalDifferences)
    }

    // MARK: - Calculator from DiffResult

    func testCalculatorWithNoDiff() {
        let diff = DiffResult(htmlDiff: [], cssDiff: [], jsDiff: [])
        let score = DegradationCalculator.calculate(diff: diff)
        XCTAssertEqual(score.score, 0.0)
        XCTAssertEqual(score.classification, .identical)
    }

    func testCalculatorWithAllChanges() {
        let lines = [
            DiffLine(id: 0, kind: .added, text: "x", oldLineNumber: nil, newLineNumber: 1),
            DiffLine(id: 1, kind: .removed, text: "y", oldLineNumber: 1, newLineNumber: nil),
        ]
        let diff = DiffResult(htmlDiff: lines, cssDiff: [], jsDiff: [])
        let score = DegradationCalculator.calculate(diff: diff)
        XCTAssertEqual(score.score, 1.0) // All lines are changes
    }

    func testCalculatorWithMixedLines() {
        // 2 context + 2 changes = score 0.5
        let lines = [
            DiffLine(id: 0, kind: .context, text: "a", oldLineNumber: 1, newLineNumber: 1),
            DiffLine(id: 1, kind: .added, text: "b", oldLineNumber: nil, newLineNumber: 2),
            DiffLine(id: 2, kind: .removed, text: "c", oldLineNumber: 2, newLineNumber: nil),
            DiffLine(id: 3, kind: .context, text: "d", oldLineNumber: 3, newLineNumber: 3),
        ]
        let diff = DiffResult(htmlDiff: lines, cssDiff: [], jsDiff: [])
        let score = DegradationCalculator.calculate(diff: diff)
        XCTAssertEqual(score.score, 0.5, accuracy: 0.01)
        XCTAssertEqual(score.classification, .significantDifferences)
    }

    func testCalculatorFromRawText() {
        let score = DegradationCalculator.calculate(
            remoteText: "same\ndifferent\nsame",
            localText: "same\nchanged\nsame"
        )
        XCTAssertGreaterThan(score.score, 0.0)
        XCTAssertLessThan(score.score, 1.0)
    }

    func testCalculatorFromIdenticalText() {
        let score = DegradationCalculator.calculate(
            remoteText: "identical text",
            localText: "identical text"
        )
        XCTAssertEqual(score.score, 0.0)
    }

    // MARK: - Percentage

    func testPercentageRounding() {
        let score = DegradationScore(score: 0.234)
        XCTAssertEqual(score.percentage, 23)

        let score2 = DegradationScore(score: 0.995)
        XCTAssertEqual(score2.percentage, 100)
    }

    // MARK: - Labels

    func testClassificationLabels() {
        XCTAssertEqual(DegradationScore.Classification.identical.label, "Identical")
        XCTAssertEqual(DegradationScore.Classification.minorDifferences.label, "Minor Differences")
        XCTAssertEqual(DegradationScore.Classification.moderateDifferences.label, "Moderate Differences")
        XCTAssertEqual(DegradationScore.Classification.significantDifferences.label, "Significant Differences")
        XCTAssertEqual(DegradationScore.Classification.criticalDifferences.label, "Critical Differences")
    }
}
