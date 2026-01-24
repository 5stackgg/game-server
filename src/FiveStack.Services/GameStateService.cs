using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;

namespace FiveStack.Services
{
    public class GameStateService : IGameStateService
    {
        public CCSGameRules? Rules()
        {
            return MatchUtility.Rules();
        }

        public bool IsWarmup()
        {
            return MatchUtility.Rules()?.WarmupPeriod ?? false;
        }

        public bool IsFreezePeriod()
        {
            return MatchUtility.Rules()?.FreezePeriod ?? false;
        }

        public bool IsPaused()
        {
            // Check if the game is paused by checking the game rules
            var rules = MatchUtility.Rules();
            return rules?.IsPaused ?? false;
        }

        public bool IsKnife()
        {
            // This would need to be implemented based on the actual game state tracking
            // For now, we'll check if there's a specific knife round state in the match data
            // or by checking for active knife system
            return false; // Placeholder - implementation depends on how this is tracked
        }

        public bool isOverTime()
        {
            return MatchUtility.Rules()?.OvertimePlaying > 0;
        }

        public bool isSurrendered()
        {
            // Check if the game state indicates surrender
            var rules = MatchUtility.Rules();
            return rules?.Surrendered ?? false;
        }

        public int GetOverTimeNumber()
        {
            return MatchUtility.Rules()?.OvertimePlaying ?? 0;
        }

        public int GetTotalRoundsPlayed()
        {
            return MatchUtility.Rules()?.TotalRoundsPlayed ?? 0;
        }

        public int GetCurrentRound()
        {
            return GetTotalRoundsPlayed() + 1;
        }

        public List<CCSPlayerController> Players()
        {
            return MatchUtility.Players();
        }

        public IEnumerable<CCSTeam> Teams()
        {
            return MatchUtility.Teams();
        }
    }
}