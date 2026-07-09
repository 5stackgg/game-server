using CounterStrikeSharp.API.Modules.Timers;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack.Utilities
{
    public static class TimerUtility
    {
        public static readonly List<Timer> Timers = new List<Timer>();

        public static Timer AddTimer(float interval, Action callback, TimerFlags? flags = null)
        {
            Timer timer = new Timer(interval, callback, flags.GetValueOrDefault());
            Timers.Add(timer);
            return timer;
        }
    }
}
