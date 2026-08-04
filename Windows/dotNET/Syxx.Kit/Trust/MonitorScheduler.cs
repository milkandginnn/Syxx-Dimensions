using System;
using System.Threading;
using System.Threading.Tasks;

namespace Syxx.Kit
{
    // A5 (ROADMAP "Implementation plan v2" — continuous monitoring; the subscription item). A driver
    // that runs ContinuousMonitor.TickOnce on an interval and publishes the latest result.
    //
    // WHY THIS EXISTS AND IS SEPARATE FROM TickOnce: the 2026-07-16 audit found TickOnce had NO caller
    // and NO test on either platform — the live I/O path of the subscription feature had never executed.
    // TickOnce itself is untestable by construction (it reads real network config and touches disk), so
    // the SCHEDULING logic — start/stop, interval, run-in-flight guarding, change latching — lives here,
    // behind injected `tick` and `now` seams, and is tested without a network or a wall clock.
    //
    // The scheduler owns cadence and lifecycle; it makes no result of its own. The honesty guarantees
    // are all in ContinuousMonitor: a first run is `.baselineEstablished`, never `.noChange`, so
    // "nothing changed" is never reported before there was anything to compare against.
    //
    // Windows-parity twin of macOS/Sources/SyxxKit/Trust/MonitorScheduler.swift.

    /// <summary>
    /// Drives periodic monitoring ticks. Not a view model — a Kit primitive the GUI (and, later, a
    /// background agent) can host. Thread-safe for property reads from any context.
    /// </summary>
    public sealed class MonitorScheduler
    {
        /// F035: replaces the bare bool with a five-state enum so a crashed background task
        /// leaves the scheduler reporting <c>Crashed</c>, not a lying <c>IsRunning=true</c>.
        /// <c>IsRunning</c> is retained as a computed backward-compatibility shim.
        public enum SchedulerState { Idle, Starting, Running, Stopping, Crashed }

        public SchedulerState State { get; private set; } = SchedulerState.Idle;

        /// Backward-compatibility shim: true when the scheduler is Starting or Running.
        public bool IsRunning => State is SchedulerState.Starting or SchedulerState.Running;
        public MonitorTick? LastTick { get; private set; }
        /// <summary>
        /// The last tick that actually reported a CHANGE, latched so a surface can show "something
        /// changed at HH:MM" until the user acknowledges it — a later no-change does not erase it.
        /// </summary>
        public MonitorTick? LastChange { get; private set; }
        public int TickCount { get; private set; }

        private readonly TimeSpan _interval;
        private readonly Func<MonitorTick> _tick;
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        private readonly object _runLock = new();
        private readonly Action<MonitorTick>? _onTick;

        /// <summary>
        /// - <paramref name="intervalSeconds"/>: seconds between ticks.
        /// - <paramref name="tick"/>: the work. Defaults to the real <see cref="ContinuousMonitor.TickOnce"/>;
        ///   a test injects a fake.
        /// - <paramref name="onTick"/>: called after each tick (e.g. to raise a notification on change).
        /// </summary>
        public MonitorScheduler(
            double intervalSeconds = 900,
            Func<MonitorTick>? tick = null,
            Action<MonitorTick>? onTick = null)
        {
            _interval = TimeSpan.FromSeconds(intervalSeconds);
            // C136: default to the async tick that performs DNS sampling and records in
            // AnswerLedger, matching macOS ContinuousMonitor.tickOnce. The sync TickOnce
            // is kept for backward compatibility but does not sample DNS.
            _tick = tick ?? (() => ContinuousMonitor.TickOnceAsync().GetAwaiter().GetResult());
            _onTick = onTick;
        }

        /// <summary>
        /// Run one tick now, synchronously. Safe to call directly (the "Check now" button) or from
        /// the loop. Guarded so two ticks never overlap and stomp the snapshot file.
        /// Returns null when a tick is already in flight.
        /// </summary>
        public MonitorTick? RunOnce()
        {
            MonitorTick result;
            lock (_runLock)
            {
                // Re-entrancy guard — tick() is I/O and must not overlap.
                if (_inFlight) return null;
                _inFlight = true;

                try
                {
                    result = _tick();
                }
                catch (Exception ex)
                {
                    // C150: a tick failure must NOT kill the loop silently. Surface the failure as
                    // a warning alert, record it as a tick, and let the loop continue. A transient
                    // disk error must not end monitoring.
                    _inFlight = false;
                    result = new MonitorTick(
                        MonitorState.Changed, // forces UI attention
                        Array.Empty<ConfigChange>(),
                        new[] { new MonitorAlert(Severity.Warning, "Monitoring tick failed",
                            ex.Message) },
                        $"Monitoring tick failed: {ex.Message}. Monitoring continues.",
                        RecheckRecommended: true);
                }
                finally
                {
                    _inFlight = false;
                }

                // C142: a tick that finishes after the user stopped monitoring must not be
                // counted, latched, or delivered. Direct port of MonitorScheduler.swift:62.
                if (_cts is { IsCancellationRequested: true }) return null;

                // C143: state mutations live inside _runLock so reads from any thread see a
                // consistent snapshot. Previously these sat outside the lock and were plain
                // auto-properties written from the loop thread with no barrier.
                TickCount++;
                LastTick = result;
                if (result.State == MonitorState.Changed)
                {
                    // Latch; never cleared by a later NoChange.
                    LastChange = result;
                }
                _onTick?.Invoke(result);
            }
            return result;
        }

        private bool _inFlight;

        /// <summary>
        /// Begin the interval loop. Runs one tick immediately (so the user sees a baseline at once),
        /// then every <see cref="_interval"/> seconds until <see cref="Stop"/>. Idempotent — a second
        /// start is a no-op.
        /// </summary>
        public void Start()
        {
            if (State is SchedulerState.Starting or SchedulerState.Running) return;
            State = SchedulerState.Starting;

            RunOnce();

            // F035 Fix 3: transition to Running only after the first tick completes.
            State = SchedulerState.Running;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _loopTask = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(_interval, token);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        if (token.IsCancellationRequested) break;
                        RunOnce();
                    }
                }
                // F035 Fix 2: catch any unhandled exception from RunOnce (e.g. _onTick throwing)
                // and transition to Crashed rather than silently terminating the loop.
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    State = SchedulerState.Crashed;
                    LastTick = new MonitorTick(
                        MonitorState.Changed,
                        Array.Empty<ConfigChange>(),
                        new[] { new MonitorAlert(Severity.Warning, "Monitor loop crashed",
                            ex.Message) },
                        $"Monitor loop terminated unexpectedly: {ex.Message}",
                        RecheckRecommended: true);
                }
            }, token);
        }

        /// <summary>Stop the interval loop and wait for the in-flight tick to finish.
        /// After StopAsync returns, no tick is running and State == Idle is truthful.</summary>
        public async Task StopAsync()
        {
            State = SchedulerState.Stopping;
            _cts?.Cancel();
            if (_loopTask is not null)
            {
                try { await _loopTask; } catch (OperationCanceledException) { }
            }
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
            State = SchedulerState.Idle;
        }

        /// <summary>Stop the interval loop. Idempotent. Prefer <see cref="StopAsync"/> when
        /// the caller can await — this fire-and-forget path cannot guarantee the loop has ceased.</summary>
        public void Stop()
        {
            State = SchedulerState.Stopping;
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
            _loopTask = null;
            State = SchedulerState.Idle;
        }

        /// <summary>Clear the latched change once the user has seen it.</summary>
        public void AcknowledgeChange() { LastChange = null; }
    }
}
