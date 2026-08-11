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

    public private(set) var probesRun: Int = 0
    public private(set) var elapsed: TimeInterval = 0

    /// `true` when the budget can accept no more probes.
    public var isExhausted: Bool {
        probesRun >= maxProbes || elapsed >= totalTimeout
    }

    /// F132: read budgets from environment variables when available, falling back to defaults.
    /// This allows CI and calibration runs to override without recompiling.
    /// Default budget for the Prophet section (45s total, 5s per probe, 15 probes).
    public static var defaultProphet: ProbeBudget {
        .init(
            totalTimeout: envDouble("SYXX_BUDGET_PROPHET_TOTAL") ?? 45,
            perProbeTimeout: envDouble("SYXX_BUDGET_PROPHET_PER_PROBE") ?? 5.0,
            maxProbes: envInt("SYXX_BUDGET_PROPHET_MAX") ?? 15
        )
    }
    /// Default budget for the Mirror section (50s total, 6s per probe, 10 probes).
    public static var defaultMirror: ProbeBudget {
        .init(
            totalTimeout: envDouble("SYXX_BUDGET_MIRROR_TOTAL") ?? 50,
            perProbeTimeout: envDouble("SYXX_BUDGET_MIRROR_PER_PROBE") ?? 6.0,
            maxProbes: envInt("SYXX_BUDGET_MIRROR_MAX") ?? 10
        )
    }
    /// Default budget for the Portal section (30s total, 5s per probe, 5 probes).
    public static var defaultPortal: ProbeBudget {
        .init(
            totalTimeout: envDouble("SYXX_BUDGET_PORTAL_TOTAL") ?? 30,
            perProbeTimeout: envDouble("SYXX_BUDGET_PORTAL_PER_PROBE") ?? 5.0,
            maxProbes: envInt("SYXX_BUDGET_PORTAL_MAX") ?? 5
        )
    }
    /// Default budget for the Glutton section (25s total, 5s per probe, 5 probes).
    public static var defaultGlutton: ProbeBudget {
        .init(
            totalTimeout: envDouble("SYXX_BUDGET_GLUTTON_TOTAL") ?? 25,
            perProbeTimeout: envDouble("SYXX_BUDGET_GLUTTON_PER_PROBE") ?? 5.0,
            maxProbes: envInt("SYXX_BUDGET_GLUTTON_MAX") ?? 5
        )
    }

    public init(totalTimeout: TimeInterval, perProbeTimeout: TimeInterval, maxProbes: Int) {
        self.totalTimeout = totalTimeout
        self.perProbeTimeout = perProbeTimeout
        self.maxProbes = maxProbes
    }

    /// Record a completed probe. D406: `add()` is internal (not public) — only ProbeRunner
    /// should charge the budget. Callers passing post-processing time inflate the budget
    /// and could starve subsequent probes.
    mutating func add(duration: TimeInterval) {
        probesRun += 1
        elapsed += duration
    }

    // F132: environment variable helpers for budget configuration
    private static func envDouble(_ key: String) -> Double? {
        guard let str = ProcessInfo.processInfo.environment[key],
              let val = Double(str), val.isFinite, val > 0 else { return nil }
        return val
    }

    private static func envInt(_ key: String) -> Int? {
        guard let str = ProcessInfo.processInfo.environment[key],
              let val = Int(str), val > 0 else { return nil }
        return val
    }
}
