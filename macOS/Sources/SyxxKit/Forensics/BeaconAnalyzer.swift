// Ported into SyxxKit from the sibling LyxTools project (LyxToolsKit/Analysis).
// Capture/forensics: score how regular a destination's query timing is — a highly
// regular interval is a hallmark of automated beaconing (C2 / exfil), not human use.
import Foundation

public struct BeaconSeries: Hashable, Codable, Sendable {
    public let processName: String
    public let destination: String
    public let timestamps: [Date]

    public init(processName: String, destination: String, timestamps: [Date]) {
        self.processName = processName
        self.destination = destination
        self.timestamps = timestamps.sorted()
    }
}

public enum BeaconClassification: String, Codable, Sendable {
    case insufficientData = "Insufficient data"
    case highlyRegular = "Highly regular"
    case regular = "Regular"
    case irregular = "Irregular"
}

public struct BeaconScore: Hashable, Codable, Sendable {
    public let processName: String
    public let destination: String
    public let intervals: [TimeInterval]
    public let meanInterval: TimeInterval?
    public let coefficientOfVariation: Double?
    public let classification: BeaconClassification
}

public enum BeaconAnalyzer {
    public static func score(_ series: BeaconSeries) -> BeaconScore {
        let intervals = zip(series.timestamps, series.timestamps.dropFirst())
            .map { pair -> TimeInterval in pair.1.timeIntervalSince(pair.0) }

        guard intervals.count >= 2 else {
            return BeaconScore(
                processName: series.processName,
                destination: series.destination,
                intervals: intervals,
                meanInterval: nil,
                coefficientOfVariation: nil,
                classification: .insufficientData
            )
        }

        let mean = intervals.reduce(0, +) / Double(intervals.count)
        // Degenerate case: all identical timestamps → all intervals are zero → mean is zero.
        // A zero-mean interval set is not "highly regular"; it's insufficient data (B034).
        if mean == 0 {
            return BeaconScore(
                processName: series.processName,
                destination: series.destination,
                intervals: intervals,
                meanInterval: nil,
                coefficientOfVariation: nil,
                classification: .insufficientData
            )
        }
        let variance = intervals.map { pow($0 - mean, 2) }.reduce(0, +) / Double(intervals.count)
        let standardDeviation = sqrt(variance)
        let cv = standardDeviation / mean

        return BeaconScore(
            processName: series.processName,
            destination: series.destination,
            intervals: intervals,
            meanInterval: mean,
            coefficientOfVariation: cv,
            classification: classification(for: cv)
        )
    }

    private static func classification(for coefficientOfVariation: Double) -> BeaconClassification {
        if coefficientOfVariation < 0.05 { return .highlyRegular }
        if coefficientOfVariation < 0.20 { return .regular }
        return .irregular
    }
}
