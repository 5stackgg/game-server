using CounterStrikeSharp.API.Core;
using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities
{
    public static class MatchUtility
    {
        public static string GetSafeMatchPrefix(FiveStackMatch match)
        {
            return $"{match.id}_{match.current_match_map_id}".Replace("-", "");
        }

        public static MatchMember? GetMemberFromLineup(
            FiveStackMatch match,
            CCSPlayerController player
        )
        {
            List<MatchMember> players = match
                .lineup_1.lineup_players.Concat(match.lineup_2.lineup_players)
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

        public static Guid? GetPlayerLineup(FiveStackMatch match, CCSPlayerController player)
        {
            MatchMember? member = MatchUtility.GetMemberFromLineup(match, player);

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
                case "TechTimeout":
                    return eMapStatus.TechTimeout;
                case "Warmup":
                    return eMapStatus.Warmup;
                case "Unknown":
                    return eMapStatus.Unknown;
                default:
                    throw new ArgumentException($"Unsupported status string: {state}");
            }
        }
    }
}
