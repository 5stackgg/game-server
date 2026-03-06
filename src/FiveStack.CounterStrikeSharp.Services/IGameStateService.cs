using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack.CounterStrikeSharp.Services
{
    public interface IGameStateService
    {
        CCSGameRules? Rules();
        List<CCSPlayerController> Players();
        IEnumerable<CCSTeam> Teams();
    }
}