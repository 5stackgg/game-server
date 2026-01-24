using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;

namespace FiveStack.CounterStrikeSharp.Services
{
    public class TimerService : ITimerService
    {
        public void CreateTimer(float time, Action callback, TimerFlags flags = TimerFlags.NONE)
        {
            new Timer(time, callback, flags);
        }

        public void CreateTimer(float time, Action<CCSPlayerController> callback, TimerFlags flags = TimerFlags.NONE)
        {
            new Timer(time, callback, flags);
        }

        public void CreateTimer(float time, Action<CCSPlayerController, string[]> callback, TimerFlags flags = TimerFlags.NONE)
        {
            new Timer(time, callback, flags);
        }

        public void CreateTimer(float time, Action<CCSPlayerController, string> callback, TimerFlags flags = TimerFlags.NONE)
        {
            new Timer(time, callback, flags);
        }
    }
}