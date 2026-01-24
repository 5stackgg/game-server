using CounterStrikeSharp.API.Modules.Timers;

namespace FiveStack.Services
{
    public interface ITimerService
    {
        Timer AddTimer(float delay, Action action, TimerFlags flags = TimerFlags.NONE);
        void KillTimer(Timer timer);
    }
}