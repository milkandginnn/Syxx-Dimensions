import Foundation

/// The complete result of a Mirror comparison: both bundles, their diff, and the degradation score.
public struct MirrorResult: Sendable, Equatable {
    /// The remote (server-side) bundle.
    public let remoteBundle: MirrorBundle
    /// The local (device-side) bundle.
    public let localBundle: MirrorBundle
    /// Line-by-line diff across HTML, CSS, and JS.
    public let diffResult: DiffResult
    /// Overall degradation score (0.0 = identical, 1.0 = completely different).
    public let degradationScore: DegradationScore
    /// When the comparison was performed.
    public let comparisonTimestamp: Date
    /// Whether the result was computed from partial data (G-02 fallback).
    public let isPartial: Bool
    /// Human-readable reason if the result is partial.
    public let partialReason: String?

    public init(
        remoteBundle: MirrorBundle,
        localBundle: MirrorBundle,
        diffResult: DiffResult,
        degradationScore: DegradationScore,
        comparisonTimestamp: Date,
        isPartial: Bool = false,
        partialReason: String? = nil
    ) {
        self.remoteBundle = remoteBundle
        self.localBundle = localBundle
        self.diffResult = diffResult
        self.degradationScore = degradationScore
        self.comparisonTimestamp = comparisonTimestamp
        self.isPartial = isPartial
        self.partialReason = partialReason
    }
}
