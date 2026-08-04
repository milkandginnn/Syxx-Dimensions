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
        public void RunOnce_RecordsTheResult()
        {
            var s = new MonitorScheduler(intervalSeconds: 999, tick: () => MakeTick(MonitorState.BaselineEstablished));
            var r = s.RunOnce();

            Assert.Equal(MonitorState.BaselineEstablished, r?.State);
            Assert.Equal(MonitorState.BaselineEstablished, s.LastTick?.State);
            Assert.Equal(1, s.TickCount);
        }

        [Fact]
        public void AChangeIsLatched_AndNotErasedByALaterNoChange()
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
                tick: () => MakeTick(states.Dequeue()));

            s.RunOnce();  // baseline
            s.RunOnce();  // changed
            Assert.Equal(MonitorState.Changed, s.LastChange?.State);

            s.RunOnce();  // noChange
            Assert.Equal(MonitorState.Changed, s.LastChange?.State);
            // A later NoChange must NOT erase the latched change.

            s.AcknowledgeChange();
            Assert.Null(s.LastChange);
        }

        [Fact]
        public void OnTick_FiresPerTick_ForNotifications()
        {
            // onTick is how a surface raises a notification. It must fire once per tick, with the result.
            var seen = new List<MonitorState>();
            var s = new MonitorScheduler(intervalSeconds: 999,
                tick: () => MakeTick(MonitorState.Changed),
                onTick: r => seen.Add(r.State));

            s.RunOnce();
            s.RunOnce();

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
            // signals from it, and it PARKS the loop's first tick so Stop() is called at a moment
            // when the in-flight tick is known, which removes race (b) by construction.
            var entries = 0;                // every entry into the injected tick
            var entriesAfterStop = 0;       // entries seen after Stop() returned — must stay 0
            var stopped = false;
            var loopTickEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim(false);

            var s = new MonitorScheduler(intervalSeconds: 0.02, tick: () =>
            {
                var n = Interlocked.Increment(ref entries);
                if (Volatile.Read(ref stopped)) { Interlocked.Increment(ref entriesAfterStop); }
                if (n == 2)
                {
                    // The loop's first tick (call 1 is Start()'s immediate, synchronous one — it must
                    // never park here or Start() would deadlock on the caller's thread). Park so the
                    // test can Stop() with this tick provably in flight. RunOnce holds `_runLock`
                    // while calling us and Stop() does not take that lock, so this cannot deadlock.
                    loopTickEntered.SetResult();
                    release.Wait(TimeSpan.FromSeconds(30));
                }
                return MakeTick(MonitorState.NoChange);
            });

            s.Start();
            Assert.Equal(1, s.TickCount);           // start() ticks immediately, on this thread
            Assert.Equal(1, Volatile.Read(ref entries));
            Assert.True(s.IsRunning);

            // The interval loop really ran — awaited on an explicit signal, not slept for.
            await loopTickEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));

            s.Stop();
            Volatile.Write(ref stopped, true);
            Assert.False(s.IsRunning);

            // Let the parked in-flight tick finish and be counted. Waiting for something that MUST
            // happen (so a slow machine waits longer, it does not fail).
            release.Set();
            var counted = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (s.TickCount < 2 && DateTime.UtcNow < counted) { await Task.Delay(5); }

            var afterStop = s.TickCount;
            // B256: exact due-monitor count — Start's immediate tick + the one loop tick that was
            // parked in flight when Stop ran. A broken Stop that lets more interval ticks land
            // would push this above 2 after the quiet window below.
            Assert.Equal(2, afterStop);

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
            Assert.Equal(afterStop, s.TickCount);   // no ticks after stop()
        }

        /// B256 unit: RunOnce-only path yields exact tick counts without any wall clock.
        /// Failure case: break TickCount increment → assertion red.
        [Fact]
        public void InjectedTicks_YieldExactDueCount()
        {
            var s = new MonitorScheduler(intervalSeconds: 999, tick: () => MakeTick(MonitorState.NoChange));
            Assert.Equal(0, s.TickCount);
            s.RunOnce();
            s.RunOnce();
            s.RunOnce();
            Assert.Equal(3, s.TickCount);
        }
    }
}
