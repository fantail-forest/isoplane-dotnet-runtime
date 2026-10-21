// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Diagnostics;
using WasiPollWorld.wit.imports.wasi.clocks.v0_2_0;

namespace System.Threading
{
#if FEATURE_WASM_MANAGED_THREADS
#error when compiled with FEATURE_WASM_MANAGED_THREADS, we will use TimerQueue.Portable.cs
#endif
    // A request's timer driver uses its own execution partition and host wake.
    // Otherwise an overdue background timer can stall unrelated request timers.
    internal sealed partial class TimerQueue : IThreadPoolWorkItem
    {
        private static long TickCount64 => Environment.TickCount64;
        private static readonly Dictionary<long, TimerQueue> s_queues = new();
        public static IEnumerable<TimerQueue> Instances => s_queues.Values;

        internal readonly long IsoplaneOwner;
        internal bool IsClosed { get; private set; }
        private bool _workQueued;
        private long _scheduledDueTimeMs = long.MaxValue;
        private CancellationTokenSource? _armed;

        private TimerQueue(long owner) => IsoplaneOwner = owner;

        internal static TimerQueue ForOwner(long owner)
        {
            owner = ThreadPoolWorkQueue.IsoplaneResolveOwner(owner);
            if (!s_queues.TryGetValue(owner, out TimerQueue? queue))
            {
                queue = new TimerQueue(owner);
                s_queues.Add(owner, queue);
            }
            return queue;
        }

        internal static void CloseOwner(long owner)
        {
            if (owner == 0 || !s_queues.Remove(owner, out TimerQueue? queue))
                return;

            lock (queue.SharedLock)
            {
                queue.IsClosed = true;
                queue.ReleaseArmedTimer();
                long now = TickCount64;
                while ((queue._shortTimers ?? queue._longTimers) is TimerQueueTimer timer)
                {
                    long start = timer._startTicks;
                    uint due = timer._dueTime;
                    uint remaining = (uint)Math.Max(0, start + due - now);
                    uint period = timer._period;
                    queue.DeleteTimer(timer);
                    TimerQueue background = ForOwner(0);
                    lock (background.SharedLock)
                    {
                        timer._associatedTimerQueue = background;
                        background.UpdateTimer(timer, remaining, period);
                        // Preserve the periodic timer's phase even when handover happens late.
                        timer._startTicks = start;
                        timer._dueTime = due;
                    }
                }
            }
        }

        internal void QueueDriver()
        {
            if (IsClosed || _workQueued || _scheduledDueTimeMs == long.MaxValue)
                return;
            _workQueued = true;
            ThreadPool.UnsafeQueueUserWorkItemInternal(this, preferLocal: false);
        }

        void IThreadPoolWorkItem.Execute()
        {
            _workQueued = false;
            if (IsClosed || _scheduledDueTimeMs == long.MaxValue)
                return;

            try
            {
                ReleaseArmedTimer();
                long now = TickCount64;
                if (_scheduledDueTimeMs <= now)
                {
                    _scheduledDueTimeMs = long.MaxValue;
                    FireNextTimers();
                }
                else
                {
                    var pollable = MonotonicClockInterop.SubscribeDuration((ulong)(_scheduledDueTimeMs - now) * 1_000_000);
                    _armed = new CancellationTokenSource();
                    WasiEventLoop.RegisterWasiTimer(pollable, this, _armed.Token);
                }
            }
            catch (Exception e)
            {
                Environment.FailFast("TimerQueue driver failed", e);
            }
        }

        private bool SetTimer(uint actualDuration)
        {
            Debug.Assert((int)actualDuration >= 0);
            _scheduledDueTimeMs = TickCount64 + actualDuration;
            ReleaseArmedTimer();
            QueueDriver();
            return true;
        }

        partial void TimerQueueEmptied()
        {
            _isTimerScheduled = false;
            _scheduledDueTimeMs = long.MaxValue;
            ReleaseArmedTimer();
        }

        // Prepared timers measure their intervals from activation, not snapshot creation.
        internal static void ShiftScheduledTimers(long elapsedMs)
        {
            foreach (TimerQueue queue in Instances)
            {
                for (TimerQueueTimer? timer = queue._shortTimers; timer != null; timer = timer._next)
                    timer._startTicks += elapsedMs;
                for (TimerQueueTimer? timer = queue._longTimers; timer != null; timer = timer._next)
                    timer._startTicks += elapsedMs;
                queue._currentAbsoluteThreshold += elapsedMs;
                queue._currentTimerStartTicks += elapsedMs;
                if (queue._scheduledDueTimeMs != long.MaxValue)
                    queue._scheduledDueTimeMs += elapsedMs;
                queue.ReleaseArmedTimer();
                queue.QueueDriver();
            }
        }

        private void ReleaseArmedTimer()
        {
            CancellationTokenSource? armed = _armed;
            _armed = null;
            armed?.Cancel();
            armed?.Dispose();
        }
    }
}
