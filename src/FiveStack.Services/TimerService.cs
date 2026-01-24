using CounterStrikeSharp.API.Modules.Timers;

namespace FiveStack.Services
{
    public class TimerService : ITimerService
    {
        public Timer AddTimer(float delay, Action action, TimerFlags flags = TimerFlags.NONE)
        {
            return new Timer(delay, action, flags);
        }

        public void KillTimer(Timer timer)
        {
            timer.Kill();
        }
    }
}