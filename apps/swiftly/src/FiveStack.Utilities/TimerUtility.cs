using System.Threading;
using SwiftlyS2.Shared;

namespace FiveStack.Utilities
{
    public static class TimerUtility
    {
        private static ISwiftlyCore _core = null!;

        public static readonly List<CancellationTokenSource> Timers =
            new List<CancellationTokenSource>();

        public static void Initialize(ISwiftlyCore core)
        {
            _core = core;
        }

        public static CancellationTokenSource AddTimer(float interval, Action callback)
        {
            CancellationTokenSource timer = _core.Scheduler.DelayBySeconds(interval, callback);
            Timers.Add(timer);
            return timer;
        }

        public static CancellationTokenSource Repeat(float interval, Action callback)
        {
            CancellationTokenSource timer = _core.Scheduler.RepeatBySeconds(interval, callback);
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
