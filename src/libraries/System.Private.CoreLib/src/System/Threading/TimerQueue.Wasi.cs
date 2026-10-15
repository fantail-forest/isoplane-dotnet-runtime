// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using WasiPollWorld.wit.imports.wasi.clocks.v0_2_0;

namespace System.Threading
{
#if FEATURE_WASM_MANAGED_THREADS
#error when compiled with FEATURE_WASM_MANAGED_THREADS, we will use TimerQueue.Portable.cs
#endif
    //
    // Wasi implementation of Timer, single-threaded, on top of pollable and wasi:clocks
    // Based on TimerQueue.Portable.cs
    // Not thread safe
    //
    internal sealed partial class TimerQueue
    {
        private static long TickCount64 => Environment.TickCount64;
        private static List<TimerQueue>? s_scheduledTimers;
        private static List<TimerQueue>? s_scheduledTimersToFire;
        private static long s_shortestDueTimeMs = long.MaxValue;
        // The registration of the pollable armed for s_shortestDueTimeMs. A
        // shorter deadline supersedes it and an emptied queue releases it, so
        // the host never holds a timer nothing waits on.
        private static CancellationTokenSource? s_armed;

        // this means that it's in the s_scheduledTimers collection, not that it's the one which would run on the next TimeoutCallback
        private bool _isScheduled;
        private long _scheduledDueTimeMs;
        private TimerQueue(int _)
        {
        }

        private static void TimerHandler(object _)
        {
            try
            {
                s_shortestDueTimeMs = long.MaxValue;
                s_armed = null;

                long currentTimeMs = TickCount64;
                PumpTimerQueue(currentTimeMs);
                // A queue that re-armed itself while firing is scheduled again
                // by now; the earliest deadline is read after the pump, so the
                // registration it made is kept rather than released.
                SetNextTimer(ShortestDueTime(), currentTimeMs);
            }
            catch (Exception e)
            {
                Environment.FailFast("TimerQueue.TimerHandler failed", e);
            }
        }

        // this is called with shortest of timers scheduled on the particular TimerQueue
        private bool SetTimer(uint actualDuration)
        {
            Debug.Assert((int)actualDuration >= 0);
            long currentTimeMs = TickCount64;
            if (!_isScheduled)
            {
                s_scheduledTimers ??= new List<TimerQueue>(Instances.Length);
                s_scheduledTimersToFire ??= new List<TimerQueue>(Instances.Length);
                s_scheduledTimers.Add(this);
                _isScheduled = true;
            }

            _scheduledDueTimeMs = currentTimeMs + (int)actualDuration;

            SetNextTimer(ShortestDueTime(), currentTimeMs);

            return true;
        }

        // shortest time of all TimerQueues
        private static unsafe void SetNextTimer(long shortestDueTimeMs, long currentTimeMs)
        {
            if (shortestDueTimeMs == long.MaxValue)
            {
                ReleaseArmedTimer();
                return;
            }

            // this also covers s_shortestDueTimeMs = long.MaxValue when none is scheduled
            if (s_shortestDueTimeMs > shortestDueTimeMs)
            {
                ReleaseArmedTimer();
                s_shortestDueTimeMs = shortestDueTimeMs;
                ulong shortestWaitMs = (ulong)Math.Max((long)(shortestDueTimeMs - currentTimeMs), 0);

                // `SubscribeDuration` expects nanoseconds:
                var pollable = MonotonicClockInterop.SubscribeDuration(shortestWaitMs * 1000 * 1000);
                var armed = new CancellationTokenSource();
                s_armed = armed;
                Task task = WasiEventLoop.RegisterWasiPollable(pollable, true, armed.Token);
                // A superseded registration is cancelled, not fired.
                task.ContinueWith(TimerHandler, CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.Default);
            }
        }

        partial void TimerQueueEmptied()
        {
            if (!_isScheduled)
            {
                return;
            }
            _isScheduled = false;
            _isTimerScheduled = false;
            s_scheduledTimers?.Remove(this);
            // The armed registration was for this queue's deadline; the host
            // timer is re-armed for whatever remains, or released.
            s_shortestDueTimeMs = long.MaxValue;
            SetNextTimer(ShortestDueTime(), TickCount64);
        }

        // An instance activated from a snapshot taken elapsedMs earlier sees its
        // clock that much later. Every absolute time the queues hold moves with
        // it, so a timer set before the snapshot counts its interval from
        // activation rather than coming due at once.
        internal static void ShiftScheduledTimers(long elapsedMs)
        {
            foreach (TimerQueue queue in Instances)
            {
                lock (queue)
                {
                    for (TimerQueueTimer? timer = queue._shortTimers; timer != null; timer = timer._next)
                    {
                        timer._startTicks += elapsedMs;
                    }
                    for (TimerQueueTimer? timer = queue._longTimers; timer != null; timer = timer._next)
                    {
                        timer._startTicks += elapsedMs;
                    }
                    queue._currentAbsoluteThreshold += elapsedMs;
                    queue._currentTimerStartTicks += elapsedMs;
                    queue._scheduledDueTimeMs += elapsedMs;
                }
            }

            // The armed host timer was set for the old earliest deadline.
            s_shortestDueTimeMs = long.MaxValue;
            SetNextTimer(ShortestDueTime(), TickCount64);
        }

        private static void ReleaseArmedTimer()
        {
            CancellationTokenSource? armed = s_armed;
            s_armed = null;
            armed?.Cancel();
            armed?.Dispose();
        }

        private static long ShortestDueTime()
        {
            if (s_scheduledTimers == null)
            {
                return long.MaxValue;
            }

            long shortestDueTimeMs = long.MaxValue;
            var timers = s_scheduledTimers!;
            for (int i = timers.Count - 1; i >= 0; --i)
            {
                TimerQueue timer = timers[i];
                if (timer._scheduledDueTimeMs < shortestDueTimeMs)
                {
                    shortestDueTimeMs = timer._scheduledDueTimeMs;
                }
            }

            return shortestDueTimeMs;
        }

        private static long PumpTimerQueue(long currentTimeMs)
        {
            if (s_scheduledTimersToFire == null)
            {
                return ShortestDueTime();
            }

            List<TimerQueue> timersToFire = s_scheduledTimersToFire!;
            List<TimerQueue> timers;
            timers = s_scheduledTimers!;
            long shortestDueTimeMs = long.MaxValue;
            for (int i = timers.Count - 1; i >= 0; --i)
            {
                TimerQueue timer = timers[i];
                long waitDurationMs = timer._scheduledDueTimeMs - currentTimeMs;
                if (waitDurationMs <= 0)
                {
                    timer._isScheduled = false;
                    timersToFire.Add(timer);

                    int lastIndex = timers.Count - 1;
                    if (i != lastIndex)
                    {
                        timers[i] = timers[lastIndex];
                    }
                    timers.RemoveAt(lastIndex);
                    continue;
                }

                if (timer._scheduledDueTimeMs < shortestDueTimeMs)
                {
                    shortestDueTimeMs = timer._scheduledDueTimeMs;
                }
            }

            if (timersToFire.Count > 0)
            {
                foreach (TimerQueue timerToFire in timersToFire)
                {
                    timerToFire.FireNextTimers();
                }
                timersToFire.Clear();
            }

            return shortestDueTimeMs;
        }
    }
}
