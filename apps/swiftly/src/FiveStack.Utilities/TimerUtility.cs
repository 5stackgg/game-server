using System.Threading;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace FiveStack.Utilities
{
    public static class TimerUtility
    {
        private static ISwiftlyCore? _core;

        public static readonly List<CancellationTokenSource> Timers =
            new List<CancellationTokenSource>();

        public static void Initialize(ISwiftlyCore core)
        {
            _core = core;
        }

        // SwiftlyS2 drops a delay of under a millisecond without running it or
        // saying so.
        public static bool TooShortToDelay(float interval)
        {
            return (long)(interval * 1000) <= 0;
        }

        public static CancellationTokenSource AddTimer(float interval, Action callback)
        {
            if (TooShortToDelay(interval))
            {
                CancellationTokenSource immediate = new CancellationTokenSource();
                Timers.Add(immediate);
                _core!.Scheduler.NextTick(() => RunOnce(immediate, callback));
                return immediate;
            }

            CancellationTokenSource? timer = null;
            timer = _core!.Scheduler.DelayBySeconds(interval, () => RunOnce(timer!, callback));
            Timers.Add(timer);
            return timer;
        }

        private static void RunOnce(CancellationTokenSource timer, Action callback)
        {
            try
            {
                if (!timer.IsCancellationRequested)
                {
                    callback();
                }
            }
            finally
            {
                // Scheduler tokens are owned by SwiftlyS2; only forget our reference.
                Timers.Remove(timer);
            }
        }

        public static CancellationTokenSource Repeat(float interval, Action callback)
        {
            CancellationTokenSource? timer = null;

            // SwiftlyS2 never reschedules a repeating timer whose callback throws,
            // and still runs a timer's first pass if it was cancelled before then
            timer = _core!.Scheduler.RepeatBySeconds(
                interval,
                () =>
                {
                    if (timer?.IsCancellationRequested == true)
                    {
                        return;
                    }

                    try
                    {
                        callback();
                    }
                    catch (Exception ex)
                    {
                        _core!.Logger.LogError(ex, "repeating timer callback failed");
                    }
                }
            );
            Timers.Add(timer);
            return timer;
        }

        public static void Kill(CancellationTokenSource? timer)
        {
            if (timer == null)
            {
                return;
            }

            timer.Cancel();
            Timers.Remove(timer);
        }

        public static void ClearAll()
        {
            foreach (CancellationTokenSource timer in Timers)
            {
                timer.Cancel();
            }

            Timers.Clear();
        }
    }
}
