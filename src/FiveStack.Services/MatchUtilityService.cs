using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;

namespace FiveStack.Services
{
    public class MatchUtilityService : IMatchUtilityService
    {
        public CCSGameRules? Rules()
        {
            return MatchUtility.Rules();
        }

        public List<CCSPlayerController> Players()
        {
            return MatchUtility.Players();
        }

        public IEnumerable<CCSTeam> Teams()
        {
            return MatchUtility.Teams();
        }

        public string GetSafeMatchPrefix(MatchData matchData)
        {
            return MatchUtility.GetSafeMatchPrefix(matchData);
        }

        public MatchMember? GetMemberFromLineup(MatchData matchData, string steamId, string playerName)
        {
            return MatchUtility.GetMemberFromLineup(matchData, steamId, playerName);
        }

        public Guid? GetPlayerLineup(MatchData matchData, CCSPlayerController player)
        {
            return MatchUtility.GetPlayerLineup(matchData, player);
        }

        public string? GetPlayerLineupTag(MatchData matchData, CCSPlayerController player)
        {
            return MatchUtility.GetPlayerLineupTag(matchData, player);
        }

        public eMapStatus MapStatusStringToEnum(string state)
        {
            return MatchUtility.MapStatusStringToEnum(state);
        }
    }
}