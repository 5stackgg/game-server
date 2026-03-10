using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.CounterStrikeSharp.Services
{
    public interface IMatchUtilityService
    {
        CCSGameRules? Rules();
        List<CCSPlayerController> Players();
        IEnumerable<CCSTeam> Teams();
        string GetSafeMatchPrefix(MatchData matchData);
        MatchMember? GetMemberFromLineup(MatchData matchData, string steamId, string playerName);
        Guid? GetPlayerLineup(MatchData matchData, CCSPlayerController player);
        string? GetPlayerLineupTag(MatchData matchData, CCSPlayerController player);
        eMapStatus MapStatusStringToEnum(string state);
    }
}