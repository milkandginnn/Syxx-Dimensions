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

        private readonly TimeSpan _interval;
        // warp-hardening_20260811 Task 4.3: the tick is async (macOS-parity, D-5). The C# default
        // previously called ContinuousMonitor.TickOnceAsync().GetAwaiter().GetResult() — sync-over-async
        // on the UI thread via Start(). Start() no longer ticks synchronously at all.
        private readonly Func<Task<MonitorTick>> _tick;
        private readonly Action<MonitorTick>? _onTick;

        // TWO LOCKS, NEVER NESTED (B3/B4). The ordering invariant is that no code path acquires one
        // while holding the other, which is why neither can participate in a cycle:
        //   _lifecycleLock — Start/Stop/StopAsync, _cts, _loopTask, _generation, _state.
        //   _runLock       — the in-flight claim and the published tick state.
        // B3 must NOT be fixed by folding lifecycle into _runLock: the tick commit runs under
        // _runLock, so a Stop() that took the same lock would sit behind live tick work, and the
        // B4 deadlock shape (a caller blocked inside the lock) would become certain rather than
        // conditional. They are separate concerns and get separate locks.
        private readonly object _lifecycleLock = new();
        private readonly object _runLock = new();

        // --- lifecycle state: written only under _lifecycleLock ---
        private CancellationTokenSource? _cts;
        private Task? _loopTask;
        /// B3: bumped on every lifecycle transition (each Start, each Stop/StopAsync). A loop task
        /// captures the generation it was launched with and may only publish state while that
        /// generation is still current, so one increment invalidates every assignment a still-running
        /// loop task has not made yet.
        private int _generation;
        /// Backing field for <see cref="State"/>. Written under <c>_lifecycleLock</c>, read with a
        /// volatile load so a reader on any thread sees a published transition, not a cached one.
        private int _state = (int)SchedulerState.Idle;

        // --- tick state: written only under _runLock ---
        private bool _inFlight;
        private MonitorTick? _lastTick;
        private MonitorTick? _lastChange;
        private int _tickCount;
        private int _skippedTickCount;
        private DateTimeOffset? _lastTickAttemptedAt;
        private DateTimeOffset? _lastTickCompletedAt;

        public SchedulerState State => (SchedulerState)Volatile.Read(ref _state);

        /// Backward-compatibility shim: true when the scheduler is Starting or Running.
        public bool IsRunning => State is SchedulerState.Starting or SchedulerState.Running;

        public MonitorTick? LastTick { get { lock (_runLock) { return _lastTick; } } }

        /// <summary>
        /// The last tick that actually reported a CHANGE, latched so a surface can show "something
        /// changed at HH:MM" until the user acknowledges it — a later no-change does not erase it.
        /// </summary>
        public MonitorTick? LastChange { get { lock (_runLock) { return _lastChange; } } }

        public int TickCount { get { lock (_runLock) { return _tickCount; } } }

        /// <summary>
        /// C111 (macOS parity): how many ticks were asked for and never ran because one was already
        /// in flight. A surface showing "last checked" must be able to distinguish "checked, nothing
        /// changed" from "did not check" — a skipped tick is a missing measurement, not a passing one.
        /// </summary>
        public int SkippedTickCount { get { lock (_runLock) { return _skippedTickCount; } } }

        /// <summary>C111 (macOS parity): when a tick was last ATTEMPTED, set even when the attempt
        /// was skipped or its result discarded.</summary>
        public DateTimeOffset? LastTickAttemptedAt { get { lock (_runLock) { return _lastTickAttemptedAt; } } }

        /// <summary>
        /// C111 (macOS parity): when a tick last COMPLETED and was committed.
        /// <para>
        /// Do NOT treat "<see cref="LastTickAttemptedAt"/> is ahead of this" as the test for a
        /// missed check. That comparison is only true while the tick that caused the skip is still
        /// in flight; the moment it commits, this catches up and the pair reads healthy again even
        /// though a check really was refused. <see cref="SkippedTickCount"/> is the only durable
        /// evidence a check did not run, and it is what a surface must key off.
        /// </para>
        /// </summary>
        public DateTimeOffset? LastTickCompletedAt { get { lock (_runLock) { return _lastTickCompletedAt; } } }

        /// <summary>
        /// - <paramref name="intervalSeconds"/>: seconds between ticks.
        /// - <paramref name="tick"/>: the work. Defaults to the real <see cref="ContinuousMonitor.TickOnceAsync"/>;
        ///   a test injects a fake.
        /// - <paramref name="onTick"/>: called after each tick (e.g. to raise a notification on change).
        /// </summary>
        public MonitorScheduler(
            double intervalSeconds = 900,
            Func<Task<MonitorTick>>? tick = null,
            Action<MonitorTick>? onTick = null)
        {
            _interval = TimeSpan.FromSeconds(intervalSeconds);
            // C136: default to the async tick that performs DNS sampling and records in
            // AnswerLedger, matching macOS ContinuousMonitor.tickOnce. The sync TickOnce
            // is kept for backward compatibility but does not sample DNS.
            _tick = tick ?? (() => ContinuousMonitor.TickOnceAsync());
            _onTick = onTick;
        }

        /// <summary>
        /// Run one tick now, asynchronously — the "Check now" entry point. Guarded so two ticks
        /// never overlap and stomp the snapshot file. Returns null when a tick is already in
        /// flight, or when the tick finished after the scheduler was stopped.
        /// </summary>
        /// <remarks>
        /// WHICH CANCELLATION THIS CALL INHERITS, precisely — the loop does NOT come through here:
        /// <list type="bullet">
        /// <item>No loop running: the capture is <see cref="CancellationToken.None"/>, which is
        /// never cancelled, so the tick always commits.</item>
        /// <item>Loop running: the caller inherits THE LOOP'S token. A Stop() before this tick
        /// finishes therefore discards it, and the null return is indistinguishable from the
        /// already-in-flight refusal — the caller cannot tell the two apart.</item>
        /// </list>
        /// That second case is a known divergence from macOS, recorded rather than fixed here:
        /// `Task.isCancelled` at MonitorScheduler.swift:91 reflects the CALLING task, so a direct
        /// `runOnce()` from a button commits on that side even if the loop is stopped mid-tick.
        /// Closing it is a one-line change — pass CancellationToken.None below — but it is a
        /// user-visible behavior decision, not a refactor, so it is left to the owner.
        /// </remarks>
        public Task<MonitorTick?> RunOnceAsync()
        {
            CancellationToken token;
            lock (_lifecycleLock) { token = _cts?.Token ?? CancellationToken.None; }
            return RunOnceCoreAsync(token);
        }

        /// <summary>
        /// The tick body. The token is a PARAMETER, never a field read.
        /// </summary>
        /// <remarks>
        /// B1: this parameter is the whole fix. Capturing at entry closed the awaited path but not
        /// the loop's, because the loop captured the right token and then called the parameterless
        /// overload, which threw it away and re-read the `_cts` FIELD. Stop() nulls that field
        /// BEFORE it cancels, so a loop tick entering in that region captured
        /// <see cref="CancellationToken.None"/> — never cancellable — and went on to be counted,
        /// latched into LastChange, and delivered to _onTick: a toast for a round the user had
        /// switched off. Passing the loop's own token down restores the macOS semantics, where
        /// `Task.isCancelled` is the running task's own cancellation state and survives `stop()`
        /// setting `task = nil`.
        /// </remarks>
        private async Task<MonitorTick?> RunOnceCoreAsync(CancellationToken token)
        {
            // Re-entrancy guard — tick() is I/O and must not overlap. Claim under the lock; the lock
            // must NOT be held across the await (C# does not allow it and it would serialize I/O).
            lock (_runLock)
            {
                // C111 (macOS parity): record the ATTEMPT before the guard, so a skipped tick still
                // moves `LastTickAttemptedAt`. Without this the surface cannot tell a quiet network
                // from a check that never happened.
                _lastTickAttemptedAt = DateTimeOffset.UtcNow;
                if (_inFlight)
                {
                    // C111 (macOS parity): a skipped tick leaves a trace. The C# port had dropped
                    // this accounting and returned null silently.
                    _skippedTickCount++;
                    return null;
                }
                _inFlight = true;
            }

            try
            {
                MonitorTick result;
                try
                {
                    result = await _tick();
                }
                catch (Exception ex)
                {
                    // C150: a tick failure must NOT kill the loop silently. Surface the failure as
                    // a warning alert, record it as a tick, and let the loop continue. A transient
                    // disk error must not end monitoring.
                    result = new MonitorTick(
                        MonitorState.Changed, // forces UI attention
                        Array.Empty<ConfigChange>(),
                        new[] { new MonitorAlert(Severity.Warning, "Monitoring tick failed",
                            ex.Message) },
                        $"Monitoring tick failed: {ex.Message}. Monitoring continues.",
                        RecheckRecommended: true);
                }

                // C142: a tick that finishes after the user stopped monitoring must not be
                // counted, latched, or delivered. Direct port of MonitorScheduler.swift:91.
                // The query is already spent — it just must not be surfaced.
                if (token.IsCancellationRequested) return null;

                // C143: state mutations live inside _runLock so reads from any thread see a
                // consistent snapshot. Previously these sat outside the lock and were plain
                // auto-properties written from the loop thread with no barrier.
                lock (_runLock)
                {
                    _tickCount++;
                    _lastTick = result;
                    _lastTickCompletedAt = DateTimeOffset.UtcNow;
                    if (result.State == MonitorState.Changed)
                    {
                        // Latch; never cleared by a later NoChange.
                        _lastChange = result;
                    }
                }

                // B4: _onTick runs with NO lock held. It reaches disk (SyxxSettingsStore) and builds
                // toasts, and a handler that blocks — on StopAsync, or on any code that reads a
                // property of this scheduler — used to do so while holding _runLock. It stays inside
                // the in-flight claim, though, so notifications keep tick order and two handlers can
                // never overlap on the snapshot file.
                _onTick?.Invoke(result);
                return result;
            }
            finally
            {
                // B5: the claim is released only AFTER the commit. Releasing it in a finally that ran
                // before the commit left a window in which a second tick could start, finish, and
                // commit ahead of the first, so TickCount/LastTick could end up describing an older
                // round than the one just published.
                lock (_runLock) { _inFlight = false; }
            }
        }

        /// <summary>
        /// Begin the interval loop. Runs one tick immediately (so the user sees a baseline as soon as
        /// it completes), then every <see cref="_interval"/> seconds until <see cref="Stop"/>.
        /// Idempotent — a second start is a no-op. The immediate first tick runs inside the loop task
        /// (macOS parity, MonitorScheduler.swift) — Start() itself never blocks the caller on the
        /// async tick, so the first tick has NOT necessarily completed when Start() returns.
        /// </summary>
        public void Start()
        {
            // B3: the check and every assignment it guards are ONE critical section. Previously the
            // test-then-assign pair was unsynchronised, so two Starts could both pass the check, and
            // — worse — a Stop() landing mid-Start left the scheduler permanently wedged.
            lock (_lifecycleLock)
            {
                if (State is SchedulerState.Starting or SchedulerState.Running) return;
                SetStateLocked(SchedulerState.Starting);

                var cts = new CancellationTokenSource();
                var token = cts.Token;
                var generation = ++_generation;
                _cts = cts;
                // Published inside the same critical section as _cts, so a Stop() can never observe
                // a live token source with no loop task to wait on. Task.Run only queues the work —
                // the body cannot run to completion before this lock is released, and it never waits
                // on the starting thread, so there is no cycle.
                _loopTask = Task.Run(() => RunLoopAsync(token, generation), token);
            }
        }

        private async Task RunLoopAsync(CancellationToken token, int generation)
        {
            try
            {
                // F035 Fix 3: transition to Running only after the first tick completes. A
                // Stop() racing the loop start (before the first tick begins) must leave State
                // as Stop set it — the token pre-checks preserve the old synchronous semantics
                // where no tick could be produced after Stop() for a not-yet-started loop.
                if (token.IsCancellationRequested) return;
                // B1: the CORE overload, carrying this loop's own token. Calling the parameterless
                // entry here would re-read the `_cts` field, which Stop() nulls before it cancels.
                await RunOnceCoreAsync(token);
                if (token.IsCancellationRequested) return;
                // B3: the token check above is not enough on its own — a Stop() landing between it
                // and the assignment used to be overwritten, leaving State == Running with _cts and
                // _loopTask null and nothing left alive to ever write Idle back. That wedge lasted
                // the process lifetime: IsRunning reported true while no tick would ever run again.
                // TryCommitLoopState re-tests the generation under the lifecycle lock, so the
                // assignment and the check cannot be separated.
                if (!TryCommitLoopState(SchedulerState.Running, generation)) return;

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
                    await RunOnceCoreAsync(token);   // B1: this loop's token, not the field
                }
            }
            // F035 Fix 2: catch any unhandled exception from RunOnceAsync (e.g. _onTick throwing)
            // and transition to Crashed rather than silently terminating the loop.
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var crashed = new MonitorTick(
                    MonitorState.Changed,
                    Array.Empty<ConfigChange>(),
                    new[] { new MonitorAlert(Severity.Warning, "Monitor loop crashed",
                        ex.Message) },
                    $"Monitor loop terminated unexpectedly: {ex.Message}",
                    RecheckRecommended: true);
                // B3: a loop the user already stopped must not stamp Crashed over Idle, and a loop
                // superseded by a later Start() must not stamp it over the live one.
                if (TryCommitLoopState(SchedulerState.Crashed, generation))
                {
                    lock (_runLock) { _lastTick = crashed; }
                }
            }
        }

        /// <summary>Stop the interval loop and wait for the in-flight tick to finish.
        /// After StopAsync returns, no tick is running and State == Idle is truthful.</summary>
        public async Task StopAsync()
        {
            CancellationTokenSource? cts;
            Task? loop;
            int generation;
            lock (_lifecycleLock)
            {
                // B3: one increment invalidates every state assignment the loop has not made yet.
                generation = ++_generation;
                cts = _cts;
                loop = _loopTask;
                _cts = null;
                _loopTask = null;
                SetStateLocked(SchedulerState.Stopping);
            }

            // Cancel OUTSIDE the lock. Cancel() runs registered continuations synchronously on this
            // thread, and a resumed loop continuation must never observe a half-applied transition.
            cts?.Cancel();
            if (loop is not null)
            {
                try { await loop; } catch (OperationCanceledException) { }
            }
            cts?.Dispose();

            lock (_lifecycleLock)
            {
                // A Start() may have landed while we awaited the loop; do not stamp Idle over it.
                if (_generation == generation) SetStateLocked(SchedulerState.Idle);
            }
        }

        /// <summary>Stop the interval loop. Idempotent. Prefer <see cref="StopAsync"/> when
        /// the caller can await — this fire-and-forget path cannot guarantee the loop has ceased.</summary>
        public void Stop()
        {
            CancellationTokenSource? cts;
            Task? loop;
            lock (_lifecycleLock)
            {
                // B3: bump the generation FIRST, then take Idle in the same critical section. There
                // is no observable Stopping phase here because the whole transition is synchronous;
                // StopAsync, which really does have a window, publishes Stopping across it.
                _generation++;
                cts = _cts;
                loop = _loopTask;
                _cts = null;
                _loopTask = null;
                SetStateLocked(SchedulerState.Idle);
            }

            cts?.Cancel();
            DisposeWhenLoopEnds(cts, loop);
        }

        /// <summary>Clear the latched change once the user has seen it.</summary>
        public void AcknowledgeChange() { lock (_runLock) { _lastChange = null; } }

        /// <summary>Publish a lifecycle state. Callers must already hold <c>_lifecycleLock</c>.</summary>
        private void SetStateLocked(SchedulerState next) => Volatile.Write(ref _state, (int)next);

        /// <summary>
        /// B3: commit a lifecycle state from a loop task, but only while that loop is still the live
        /// one. Returns false when the loop has been superseded by a Stop or a later Start, in which
        /// case the caller must publish nothing at all.
        /// </summary>
        private bool TryCommitLoopState(SchedulerState next, int generation)
        {
            lock (_lifecycleLock)
            {
                if (_generation != generation) return false;
                SetStateLocked(next);
                return true;
            }
        }

        /// <summary>
        /// Dispose the token source once the loop that reads its token has ended. Stop() cannot
        /// await, and disposing a source while the loop is registering on its token
        /// (<c>Task.Delay(_interval, token)</c>) throws ObjectDisposedException into the loop —
        /// which the Crashed handler would then report as a crash the user caused by stopping.
        /// It also keeps the token captured at RunOnceAsync entry readable for as long as the tick
        /// that captured it is still running.
        /// </summary>
        private static void DisposeWhenLoopEnds(CancellationTokenSource? cts, Task? loop)
        {
            if (cts is null) return;
            if (loop is null) { cts.Dispose(); return; }
            loop.ContinueWith(
                static (_, state) => ((CancellationTokenSource)state!).Dispose(),
                cts,
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }
}
