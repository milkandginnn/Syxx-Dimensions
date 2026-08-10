import Foundation

/// How different the remote and local bundles are, on a 0.0 (identical) to 1.0 (completely different) scale.
public struct DegradationScore: Sendable, Codable, Equatable {
    /// The raw score: 0.0 = identical, 1.0 = completely different.
    public let score: Double
    /// Human-readable classification bucket.
    public let classification: Classification

    public enum Classification: String, Sendable, Codable, Equatable, CaseIterable {
        case identical
        case minorDifferences
        case moderateDifferences
        case significantDifferences
        case criticalDifferences

        /// Human-readable label for display.
        public var label: String {
            switch self {
            case .identical: return "Identical"
            case .minorDifferences: return "Minor Differences"
            case .moderateDifferences: return "Moderate Differences"
            case .significantDifferences: return "Significant Differences"
            case .criticalDifferences: return "Critical Differences"
            }
        }

        /// Classify a raw score into a bucket.
        public static func classify(score: Double) -> Classification {
            if score <= 0.0 { return .identical }
            if score <= 0.1 { return .minorDifferences }
            if score <= 0.3 { return .moderateDifferences }
            if score <= 0.6 { return .significantDifferences }
            return .criticalDifferences
        }
    }

    public init(score: Double) {
        self.score = min(max(score, 0.0), 1.0)
        self.classification = Classification.classify(score: self.score)
    }

    public init(score: Double, classification: Classification) {
        self.score = min(max(score, 0.0), 1.0)
        self.classification = classification
    }

    /// Percentage representation for display (e.g. "23%").
    public var percentage: Int {
        Int((score * 100).rounded())
    }
}
