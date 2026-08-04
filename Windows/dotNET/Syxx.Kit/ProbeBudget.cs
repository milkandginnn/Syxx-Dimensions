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

    /// <summary>Default budget for Prophet (30s total, 2.5s per-probe, max 15 probes).</summary>
    public static ProbeBudget DefaultProphet => new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2.5), 15);

    /// <summary>Default budget for Mirror (30s total, 2.5s per-probe, max 10 probes).</summary>
    public static ProbeBudget DefaultMirror => new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(2.5), 10);

    /// <summary>Default budget for Portal (15s total, 2.5s per-probe, max 5 probes).</summary>
    public static ProbeBudget DefaultPortal => new(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(2.5), 5);

    /// <summary>Default budget for Glutton (20s total, 2.5s per-probe, max 5 probes).</summary>
    public static ProbeBudget DefaultGlutton => new(TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(2.5), 5);
}
