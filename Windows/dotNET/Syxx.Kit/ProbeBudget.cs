namespace Syxx.Kit;

/// <summary>
/// Tracks budget for running a set of probes within a section.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="ProbeBudget"/> is a <b>class</b> (reference type) so that mutations made
/// by <see cref="ProbeRunner"/> are visible to the caller. If this were a struct, mutations
/// inside <c>RunAsync</c> would be lost on the copy.
/// </para>
/// <para>
/// Budget exhaustion is checked before each probe. Once <see cref="Elapsed"/> >=
/// <see cref="TotalTimeout"/> or <see cref="ProbesRun"/> >= <see cref="MaxProbes"/>,
/// <see cref="IsExhausted"/> returns <c>true</c> and the runner skips any remaining probes.
/// </para>
/// <para>
/// <c>Elapsed</c> records real wall-clock time for both success and timeout (F006).
/// </para>
/// </remarks>
public class ProbeBudget
{
    /// <summary>Hard wall-clock cap for the entire section.</summary>
    public TimeSpan TotalTimeout { get; }

    /// <summary>Individual probe timeout.</summary>
    public TimeSpan PerProbeTimeout { get; }

    /// <summary>Maximum probes to run before early termination.</summary>
    public int MaxProbes { get; }

    /// <summary>Accumulated elapsed time from completed probes.</summary>
    public TimeSpan Elapsed { get; private set; }

    /// <summary>Number of probes that have been run (including timed-out ones).</summary>
    public int ProbesRun { get; private set; }

    /// <summary><c>true</c> when the budget can accept no more probes.</summary>
    public bool IsExhausted => ProbesRun >= MaxProbes || Elapsed >= TotalTimeout;

    /// <summary>
    /// Create a probe budget with explicit limits.
    /// </summary>
    public ProbeBudget(TimeSpan totalTimeout, TimeSpan perProbeTimeout, int maxProbes)
    {
        TotalTimeout = totalTimeout;
        PerProbeTimeout = perProbeTimeout;
        MaxProbes = maxProbes;
    }

    /// <summary>
    /// Record a completed probe's duration and increment the probe counter.
    /// </summary>
    /// <param name="duration">The real wall-clock time the probe consumed.</param>
    public void Add(TimeSpan duration)
    {
        ProbesRun++;
        Elapsed += duration;
    }

    // ── Factory methods for section defaults ──

    /// <remarks>
    /// C058: the per-probe timeout is derived from the transport worst case (UDP:
    /// <c>MaxUDPAttempts</c> × the 2000 ms receive timeout in <c>DNSResolver.SendUDPAsync</c> =
    /// 4.0s, plus slack; DoH: request timeout + slack) instead of the flat 2.5s that sat
    /// <b>below every transport timeout beneath it</b>. A probe whose deadline fires before the
    /// transport completes discards arriving evidence — the UDP retry's answer, the DoH
    /// blocked-state observation — while still charging the full transport wall-clock to the
    /// section budget. At 2.5s a UDP probe that needed its retry could never return an answer.
    /// Values are the macOS <c>ProbeBudget.swift</c> twin's, exactly.
    /// </remarks>

    /// <summary>Default budget for Prophet (45s total, 5s per-probe, max 15 probes).</summary>
    public static ProbeBudget DefaultProphet => new(TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(5.0), 15);

    /// <summary>
    /// Default budget for Mirror (50s total, 6s per-probe, max 10 probes).
    /// Mirror includes DoH vantage comparisons whose request timeout is 5s.
    /// </summary>
    public static ProbeBudget DefaultMirror => new(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(6.0), 10);

    /// <summary>Default budget for Portal (30s total, 5s per-probe, max 5 probes).</summary>
    public static ProbeBudget DefaultPortal => new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5.0), 5);

    /// <summary>Default budget for Glutton (25s total, 5s per-probe, max 5 probes).</summary>
    public static ProbeBudget DefaultGlutton => new(TimeSpan.FromSeconds(25), TimeSpan.FromSeconds(5.0), 5);
}
