using CounterStrikeSharp.API.Core;
using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities
{
    public static class MatchUtility
    {
        public static string GetSafeMatchPrefix(MatchData matchData)
        {
            return $"{matchData.id}_{matchData.current_match_map_id}".Replace("-", "");
        }

        public static MatchMember? GetMemberFromLineup(
            MatchData matchData,
            CCSPlayerController player
        )
        {
            List<MatchMember> players = matchData
                .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
                .ToList();

            return players.Find(member =>
            {
                if (member.steam_id == null)
                {
                    return member.name.StartsWith(player.PlayerName);
                }

                return member.steam_id == player.SteamID.ToString();
            });
        }

        public static Guid? GetPlayerLineup(MatchData matchData, CCSPlayerController player)
        {
            MatchMember? member = MatchUtility.GetMemberFromLineup(matchData, player);

            if (member == null)
            {
                return null;
            }

            return member.match_lineup_id;
        }

        public static eMapStatus MapStatusStringToEnum(string state)
        {
            switch (state)
            {
                case "Scheduled":
                    return eMapStatus.Scheduled;
                case "Finished":
                    return eMapStatus.Finished;
                case "Knife":
                    return eMapStatus.Knife;
                case "Live":
                    return eMapStatus.Live;
                case "Overtime":
                    return eMapStatus.Overtime;
                case "Paused":
                    return eMapStatus.Paused;
                case "Warmup":
                    return eMapStatus.Warmup;
                case "Unknown":
                    return eMapStatus.Unknown;
                default:
                    throw new ArgumentException($"Unsupported status string: {state}");
            }
        }

        public static CCSGameRules? Rules()
        {
            return CounterStrikeSharp
                .API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                ?.First()
                ?.GameRules;
        }

        public static List<CCSPlayerController> Players()
        {
            return CounterStrikeSharp.API.Utilities.GetPlayers();
        }

        public static IEnumerable<CCSTeam> Teams()
        {
            return CounterStrikeSharp.API.Utilities.FindAllEntitiesByDesignerName<CCSTeam>(
                "cs_team_manager"
            );
        }
    }
}
