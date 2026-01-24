using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack.Services
{
    public interface IGameStateService
    {
        CCSGameRules? Rules();
        bool IsWarmup();
        bool IsFreezePeriod();
        bool IsPaused();
        bool IsKnife();
        bool isOverTime();
        bool isSurrendered();
        int GetOverTimeNumber();
        int GetTotalRoundsPlayed();
        int GetCurrentRound();
        List<CCSPlayerController> Players();
        IEnumerable<CCSTeam> Teams();
    }
}