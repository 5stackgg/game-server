using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack.CounterStrikeSharp.Services
{
    public class GameStateService : IGameStateService
    {
        public CCSGameRules? Rules()
        {
            return Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()?.GameRules;
        }

        public List<CCSPlayerController> Players()
        {
            return Utilities.GetPlayers();
        }

        public IEnumerable<CCSTeam> Teams()
        {
            return Utilities.GetTeams();
        }
    }
}