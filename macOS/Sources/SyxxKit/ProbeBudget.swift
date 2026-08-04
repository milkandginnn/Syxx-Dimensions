import Foundation

/// Tracks budget for running a set of probes within a section.
///
/// Usage:
/// ```swift
/// var budget = ProbeBudget(totalTimeout: 30, perProbeTimeout: 5, maxProbes: 15)
/// let result = await ProbeRunner.run(label: "latency", budget: &budget) {
///     await ResolverLatencyProbe.probe(domain: domain, server: server)
/// }
/// ```
///
/// Budget exhaustion is checked before each probe. Once `elapsed >= totalTimeout` or
/// `probesRun >= maxProbes`, `isExhausted` returns `true` and the runner skips any
/// remaining probes.
///
/// - Note: `elapsed` records real wall-clock time for both success and timeout (F006).
public struct ProbeBudget {
    public let totalTimeout: TimeInterval
    public let perProbeTimeout: TimeInterval
    public let maxProbes: Int

    public private(set) var elapsed: TimeInterval = 0
    public private(set) var probesRun: Int = 0

    /// `true` when the budget can accept no more probes.
    public var isExhausted: Bool {
        probesRun >= maxProbes || elapsed >= totalTimeout
    }

    /// C058: per-probe timeout is derived from the transport worst case (UDP: maxUDPAttempts ×
    /// udpTimeout + slack; DoH: request timeout + slack) instead of a flat 2.5s that was below
    /// every transport timeout beneath it. A probe whose deadline fires before the transport
    /// completes discards arriving evidence (the UDP retry's answer, the DoH blocked-state
    /// observation) while still charging the full transport wall-clock to the section budget.
    /// Default budget for the Prophet section (45s total, 5s per probe, 15 probes).
    public static var defaultProphet: ProbeBudget { .init(totalTimeout: 45, perProbeTimeout: 5.0, maxProbes: 15) }
    /// Default budget for the Mirror section (50s total, 6s per probe, 10 probes).
    /// Mirror includes DoH vantage comparisons whose request timeout is 5s.
    public static var defaultMirror: ProbeBudget { .init(totalTimeout: 50, perProbeTimeout: 6.0, maxProbes: 10) }
    /// Default budget for the Portal section (30s total, 5s per probe, 5 probes).
    public static var defaultPortal: ProbeBudget { .init(totalTimeout: 30, perProbeTimeout: 5.0, maxProbes: 5) }
    /// Default budget for the Glutton section (25s total, 5s per probe, 5 probes).
    public static var defaultGlutton: ProbeBudget { .init(totalTimeout: 25, perProbeTimeout: 5.0, maxProbes: 5) }

    public init(totalTimeout: TimeInterval, perProbeTimeout: TimeInterval, maxProbes: Int) {
        self.totalTimeout = totalTimeout
        self.perProbeTimeout = perProbeTimeout
        self.maxProbes = maxProbes
    }

    /// Record a completed probe's duration and increment the probe counter.
    ///
    /// - Parameter duration: The real wall-clock time the probe consumed.
    public mutating func add(duration: TimeInterval) {
        probesRun += 1
        elapsed += duration
    }
}
