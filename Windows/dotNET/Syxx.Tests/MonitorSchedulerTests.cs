using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Syxx.Kit;
using Xunit;

namespace Syxx.Tests
{
    // A5 scheduler tests. TickOnce (real network + disk) is untestable by construction, so the
    // SCHEDULING logic lives behind an injected tick and is proven here — the live path the 2026-07-16
    // audit found had never executed. Injected ticks let us drive start/stop, the re-entrancy guard,
    // and the change latch deterministically.
    //
    // Windows-parity twin of macOS/Tests/SyxxKitTests/MonitorSchedulerTests.swift.
    public class MonitorSchedulerTests
    {
        private static MonitorTick MakeTick(MonitorState state) => new(
            state,
            Array.Empty<ConfigChange>(),
            Array.Empty<MonitorAlert>(),
            state.ToString(),
            state == MonitorState.Changed);

        [Fact]
        public async Task RunOnce_RecordsTheResult()
        {
            var s = new MonitorScheduler(intervalSeconds: 999, tick: () => Task.FromResult(MakeTick(MonitorState.BaselineEstablished)));
            var r = await s.RunOnceAsync();

            Assert.Equal(MonitorState.BaselineEstablished, r?.State);
            Assert.Equal(MonitorState.BaselineEstablished, s.LastTick?.State);
            Assert.Equal(1, s.TickCount);
        }

        [Fact]
        public async Task AChangeIsLatched_AndNotErasedByALaterNoChange()
        {
            // The property that makes the feature trustworthy: "your DNS changed at 3pm" must survive
            // the next quiet tick. If a NoChange cleared LastChange, the user could miss the one event
            // that mattered simply by not looking within one interval.
            var states = new Queue<MonitorState>(new[]
            {
                MonitorState.BaselineEstablished,
                MonitorState.Changed,
                MonitorState.NoChange
            });

            var s = new MonitorScheduler(intervalSeconds: 999,
                tick: () => Task.FromResult(MakeTick(states.Dequeue())));

            await s.RunOnceAsync();  // baseline
            await s.RunOnceAsync();  // changed
            Assert.Equal(MonitorState.Changed, s.LastChange?.State);

            await s.RunOnceAsync();  // noChange
            Assert.Equal(MonitorState.Changed, s.LastChange?.State);
            // A later NoChange must NOT erase the latched change.

            s.AcknowledgeChange();
            Assert.Null(s.LastChange);
        }

        [Fact]
        public async Task OnTick_FiresPerTick_ForNotifications()
        {
            // onTick is how a surface raises a notification. It must fire once per tick, with the result.
            var seen = new List<MonitorState>();
            var s = new MonitorScheduler(intervalSeconds: 999,
                tick: () => Task.FromResult(MakeTick(MonitorState.Changed)),
                onTick: r => seen.Add(r.State));

            await s.RunOnceAsync();
            await s.RunOnceAsync();

            Assert.Equal(new[] { MonitorState.Changed, MonitorState.Changed }, seen);
        }

        [Fact]
        public async Task StartRunsImmediately_ThenStopHalts()
        {
            // start() must tick at once (a user should not wait a full interval to see a baseline), and
            // stop() must actually end the loop. BOTH halves are still proven here — what changed
            // (B256) is that neither is proven by a wall-clock guess any more.
            //
            // The old form slept 90ms hoping >=1 loop tick had landed, then slept 60ms hoping none
            // had. Under `dotnet test`'s parallel collections it lost 1 run in 4 on an unchanged
            // binary, in two distinct ways: (a) a starved loop produced no tick inside 90ms, so
            // `afterStop > 1` failed; (b) a loop tick that was ALREADY in flight when Stop() was
            // called landed after the `afterStop` snapshot, so `Equal(afterStop, TickCount)` failed.
            // A longer sleep fixes neither — (b) is a race, not a duration — it just makes the flake
            // rarer and the suite slower.
            //
            // The injected tick is the seam, so it is used as the clock: the test advances only on
            // signals from it, and it PARKS the loop's second tick so Stop() is called at a moment
            // when the in-flight tick is known, which removes race (b) by construction.
            var entries = 0;                // every entry into the injected tick
            var entriesAfterStop = 0;       // entries seen after Stop() returned — must stay 0
            var stopped = false;
            var loopTickEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loopTickExited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim(false);

            var s = new MonitorScheduler(intervalSeconds: 0.02, tick: async () =>
            {
                var n = Interlocked.Increment(ref entries);
                if (Volatile.Read(ref stopped)) { Interlocked.Increment(ref entriesAfterStop); }
                if (n == 2)
                {
                    // The loop's second tick (call 1 is the first tick, which now runs inside the
                    // loop Task per macOS parity — not on Start()'s thread). Park so the test can
                    // Stop() with this tick provably in flight. RunOnceAsync does not hold
                    // `_runLock` across the tick and Stop() does not take that lock, so this
                    // cannot deadlock.
                    loopTickEntered.SetResult();
                    release.Wait(TimeSpan.FromSeconds(30));
                    loopTickExited.SetResult();
                }
                return MakeTick(MonitorState.NoChange);
            });

            s.Start();
            Assert.True(s.IsRunning);

            // The interval loop really ran — awaited on an explicit signal, not slept for.
            await loopTickEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));

            s.Stop();
            Volatile.Write(ref stopped, true);
            Assert.False(s.IsRunning);

            // Let the parked in-flight tick finish, then wait for it to actually leave the injected
            // body. Waiting for something that MUST happen (so a slow machine waits longer, it does
            // not fail).
            release.Set();
            await loopTickExited.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // ...and nothing ticks after stop(). The loop re-tests the cancellation token after each
            // tick, so a working Stop() ends it right here; a broken one would keep entering the tick
            // every 20ms. `entriesAfterStop` is incremented BY THE LOOP's own thread, so this half
            // does not depend on this thread being scheduled promptly — it only needs the window to
            // span several intervals. Fail on the FIRST extra tick rather than at the end.
            var window = DateTime.UtcNow + TimeSpan.FromSeconds(0.5);  // 25 intervals
            while (DateTime.UtcNow < window)
            {
                Assert.Equal(0, Volatile.Read(ref entriesAfterStop));
                await Task.Delay(10);
            }
            Assert.Equal(0, Volatile.Read(ref entriesAfterStop));

            // B1: exactly ONE tick is due — Start's immediate first tick. The loop's second tick was
            // provably in flight when Stop() ran, so its result is discarded, not counted: a round the
            // user ended must not go on reporting. The quiet window above is far longer than the
            // microseconds a commit would take, so a scheduler that (wrongly) committed it has already
            // done so by now.
            Assert.Equal(1, s.TickCount);
        }

        /// B256 unit: RunOnce-only path yields exact tick counts without any wall clock.
        /// Failure case: break TickCount increment → assertion red.
        [Fact]
        public async Task InjectedTicks_YieldExactDueCount()
        {
            var s = new MonitorScheduler(intervalSeconds: 999, tick: () => Task.FromResult(MakeTick(MonitorState.NoChange)));
            Assert.Equal(0, s.TickCount);
            await s.RunOnceAsync();
            await s.RunOnceAsync();
            await s.RunOnceAsync();
            Assert.Equal(3, s.TickCount);
        }

        /// B1: a tick that finishes after Stop() must not be counted, latched, or delivered.
        /// The guard existed but tested the `_cts` FIELD after the await, and Stop() nulls that
        /// field — so in the one path it was written for it never fired. The fix captures the token
        /// at entry, which is what the macOS twin's `Task.isCancelled` does.
        /// Failure case (pre-fix): the parked tick commits, so TickCount is 2 and onTick fires twice.
        [Fact]
        public async Task TickFinishingAfterStop_IsNotCounted_NotLatched_NotDelivered()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delivered = 0;
            using var release = new ManualResetEventSlim(false);

            // A long interval: the loop can only ever run its FIRST tick, so every count below is
            // attributable and no wall clock is involved.
            var s = new MonitorScheduler(intervalSeconds: 999, tick: () =>
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(30));
                exited.TrySetResult();
                return Task.FromResult(MakeTick(MonitorState.Changed));
            }, onTick: _ => Interlocked.Increment(ref delivered));

            s.Start();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));   // first tick provably in flight

            s.Stop();                                                 // nulls _cts — the dead guard's path
            release.Set();
            await exited.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // The in-flight claim is released only AFTER the commit decision (B5), so a direct tick
            // that is NOT skipped proves that decision has already been made. That makes the count
            // below deterministic rather than a settling guess.
            MonitorTick? direct = null;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (direct is null && DateTime.UtcNow < deadline)
            {
                direct = await s.RunOnceAsync();
                if (direct is null) await Task.Delay(5);   // never hot-spin a pool thread
            }
            Assert.NotNull(direct);   // monitoring is stopped, so a "Check now" is NOT discarded

            // Exactly one tick committed: the direct one. The post-Stop tick was discarded.
            Assert.Equal(1, s.TickCount);
            Assert.Equal(1, delivered);
        }

        /// C111 (macOS parity): a skipped tick must leave a trace. The C# port dropped the
        /// accounting and returned null silently, so a surface could not tell "checked, nothing
        /// changed" from "did not check" — a missing measurement rendering as a passing one.
        /// Failure case (pre-fix): SkippedTickCount / LastTickAttemptedAt do not exist.
        [Fact]
        public async Task ASkippedTick_IsRecorded_NotSilentlyDropped()
        {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim(false);

            var s = new MonitorScheduler(intervalSeconds: 999, tick: () =>
            {
                entered.TrySetResult();
                release.Wait(TimeSpan.FromSeconds(30));
                return Task.FromResult(MakeTick(MonitorState.NoChange));
            });

            Assert.Null(s.LastTickAttemptedAt);
            Assert.Null(s.LastTickCompletedAt);
            Assert.Equal(0, s.SkippedTickCount);

            var first = Task.Run(() => s.RunOnceAsync());
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

            // Re-entrant while the first is parked: refused, and RECORDED as refused.
            Assert.Null(await s.RunOnceAsync());
            Assert.Equal(1, s.SkippedTickCount);
            Assert.NotNull(s.LastTickAttemptedAt);   // the attempt is visible...
            Assert.Null(s.LastTickCompletedAt);      // ...and nothing has completed yet

            release.Set();
            Assert.NotNull(await first);
            Assert.Equal(1, s.TickCount);
            Assert.Equal(1, s.SkippedTickCount);     // the skip is not retroactively forgiven
            Assert.NotNull(s.LastTickCompletedAt);
        }

        /// B5 + B4 together. B5: the in-flight claim is released only AFTER the state commit, so a
        /// handler is never handed a result the scheduler has not published yet. B4: onTick runs with
        /// no lock held, so it may read this scheduler's own properties without wedging on _runLock.
        /// Failure case (pre-fix): the claim was released in a finally that ran BEFORE the commit, so
        /// the re-entrant call inside onTick ran a whole second tick (TickCount 2) instead of being
        /// refused, and it did so while _runLock was held by the same thread.
        [Fact]
        public async Task OnTick_SeesTheCommit_AndTheInFlightClaimIsStillHeld()
        {
            MonitorScheduler? s = null;
            var observedCount = -1;
            var reentered = 0;
            var reentrantCompletedSynchronously = false;
            MonitorTick? reentrant = null;

            s = new MonitorScheduler(intervalSeconds: 999,
                tick: () => Task.FromResult(MakeTick(MonitorState.Changed)),
                onTick: _ =>
                {
                    // Guarded so a scheduler that (wrongly) lets this through recurses exactly once
                    // instead of overflowing the stack and taking the whole test host with it.
                    if (Interlocked.Exchange(ref reentered, 1) != 0) return;
                    observedCount = s!.TickCount;          // B4: a lock-held onTick could not do this safely
                    var t = s!.RunOnceAsync();
                    reentrantCompletedSynchronously = t.IsCompleted;
                    reentrant = t.GetAwaiter().GetResult();
                });

            Assert.NotNull(await s.RunOnceAsync());

            Assert.Equal(1, observedCount);                 // B5: the commit landed before onTick ran
            Assert.True(reentrantCompletedSynchronously);   // refused without ever awaiting the tick
            Assert.Null(reentrant);                         // B5: the claim is still held during onTick
            Assert.Equal(1, s.SkippedTickCount);
            Assert.Equal(1, s.TickCount);                   // exactly one tick, not two
        }

        /// B3: a Stop() landing between the loop's cancellation check and its `State = Running`
        /// assignment used to be overwritten by the loop thread. The result was a PERMANENT wedge —
        /// State == Running with no token source and no loop task, nothing left alive to write Idle
        /// back, IsRunning reporting true for the rest of the process while no tick would ever run
        /// again. A safety feature silently off is worse than one visibly off.
        ///
        /// The window is two adjacent statements, so this races rather than parks: it cannot
        /// GUARANTEE it would have caught the old code, but it can never fail against the new code,
        /// where the assignment is generation-checked under the lifecycle lock.
        [Fact]
        public async Task StartStopRace_NeverLeavesTheSchedulerWedgedInRunning()
        {
            var schedulers = new List<MonitorScheduler>();
            for (var i = 0; i < 300; i++)
            {
                var s = new MonitorScheduler(intervalSeconds: 999,
                    tick: () => Task.FromResult(MakeTick(MonitorState.NoChange)));
                s.Start();
                s.Stop();          // deliberately concurrent with the loop task's own startup
                schedulers.Add(s);
            }

            // The wedge is written by the LOOP thread after Stop() returns, so the check has to come
            // after the loop threads have had room to run.
            await Task.Delay(250);

            foreach (var s in schedulers)
            {
                Assert.Equal(MonitorScheduler.SchedulerState.Idle, s.State);
                Assert.False(s.IsRunning);
            }
        }

        /// B3, the same invariant on the awaitable path: after StopAsync returns, the loop has ended
        /// and State == Idle is truthful — no later assignment from the loop can contradict it.
        [Fact]
        public async Task StopAsync_LeavesStateIdle_AndTheLoopEnded()
        {
            var entries = 0;
            var s = new MonitorScheduler(intervalSeconds: 0.01, tick: () =>
            {
                Interlocked.Increment(ref entries);
                return Task.FromResult(MakeTick(MonitorState.NoChange));
            });

            s.Start();
            await s.StopAsync();

            Assert.Equal(MonitorScheduler.SchedulerState.Idle, s.State);
            Assert.False(s.IsRunning);

            var seen = Volatile.Read(ref entries);
            await Task.Delay(100);   // 10 intervals
            Assert.Equal(seen, Volatile.Read(ref entries));
        }

        /// B1, the loop path. Capturing the token at RunOnceAsync entry closes the awaited path but
        /// NOT the loop: the loop captured the right token, then called the parameterless overload,
        /// which threw that token away and re-read the `_cts` FIELD. Stop() nulls the field BEFORE it
        /// cancels, so a loop tick entering in that region captured CancellationToken.None — a token
        /// that can never be cancelled — and committed, latched and delivered a toast for a round the
        /// user had switched off. Stop() is called from the UI thread (AppViewModel.cs:163) while the
        /// loop sits in exactly that region.
        ///
        /// The region is a few instructions wide, so this races rather than parks — the loop spins
        /// with no interval so it passes through continuously, and Stop() is fired into the spin. The
        /// assertion itself is unconditional and correct on every attempt whichever token the parked
        /// tick captured, so it can never fail against a scheduler that discards properly.
        [Fact]
        public async Task LoopTickEnteringAroundStop_IsNeverCommittedAfterTheFact()
        {
            // 120 attempts: pre-fix this reproduced inside the first handful every run, so the
            // extra power of a longer loop does not pay for the runtime it adds to the suite.
            for (var attempt = 0; attempt < 120; attempt++)
            {
                // Not disposed on purpose: the loop may still evaluate `gate.IsSet` after this
                // attempt ends, and a disposed handle would fault the loop instead of the test.
                var gate = new ManualResetEventSlim(true);   // open — ticks fly straight through
                var parked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var hot = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var parkOne = 0;
                var entered = 0;

                var s = new MonitorScheduler(intervalSeconds: 0, tick: () =>
                {
                    if (Interlocked.Increment(ref entered) == 2) hot.TrySetResult();
                    if (!gate.IsSet && Interlocked.Exchange(ref parkOne, 1) == 0)
                    {
                        parked.TrySetResult();
                        gate.Wait(TimeSpan.FromSeconds(30));
                        exited.TrySetResult();
                    }
                    return Task.FromResult(MakeTick(MonitorState.NoChange));
                });

                s.Start();
                // Wait for the loop to be spinning on a signal from the tick itself — polling with
                // Task.Delay(1) here costs ~10ms per attempt on a coarse timer and dominates the run.
                await hot.Task.WaitAsync(TimeSpan.FromSeconds(5));

                gate.Reset();     // the next tick to enter the body parks there
                s.Stop();         // ...fired into the spin, racing that tick's token capture

                // A tick that is going to park does so the instant it enters the body, so a short
                // wait costs nothing. Missing one only skips that attempt's assertion — it can
                // never turn into a false failure.
                if (await Task.WhenAny(parked.Task, Task.Delay(5)) == parked.Task)
                {
                    // A tick is parked AND Stop() has returned, so the token is cancelled, the field
                    // is null, and the loop is blocked inside this one tick. Nothing else can move
                    // TickCount, which makes the comparison exact rather than a settling guess.
                    var before = s.TickCount;
                    gate.Set();
                    await exited.Task.WaitAsync(TimeSpan.FromSeconds(30));
                    await Task.Delay(10);
                    Assert.Equal(before, s.TickCount);
                }
                else
                {
                    gate.Set();   // the loop had already exited; nothing parked this attempt
                }
            }
        }
    }
}
