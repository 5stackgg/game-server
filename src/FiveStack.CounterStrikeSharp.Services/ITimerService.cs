using CounterStrikeSharp.API.Core;

namespace FiveStack.CounterStrikeSharp.Services
{
    public interface ITimerService
    {
        void CreateTimer(float time, Action callback, TimerFlags flags = TimerFlags.NONE);
        void CreateTimer(float time, Action<CCSPlayerController> callback, TimerFlags flags = TimerFlags.NONE);
        void CreateTimer(float time, Action<CCSPlayerController, string[]> callback, TimerFlags flags = TimerFlags.NONE);
        void CreateTimer(float time, Action<CCSPlayerController, string> callback, TimerFlags flags = TimerFlags.NONE);
    }
}