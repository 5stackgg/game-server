using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities
{
    public static class ReadyUtility
    {
        public static eReadySettings ReadySettingStringToEnum(string state)
        {
            switch (state)
            {
                case "Captains":
                    return eReadySettings.Captains;
                case "Coach":
                    return eReadySettings.Coach;
                case "Admin":
                    return eReadySettings.Admin;
                case "Players":
                    return eReadySettings.Players;
                default:
                    throw new ArgumentException($"Unsupported ready setting: {state}");
            }
        }

        // A coach, caster or spectator readying would take a lineup player's place
        // in the count and start the match a player short.
        public static bool CanReady(
            eReadySettings setting,
            MatchData matchData,
            string steamId,
            string playerName,
            bool isCoach,
            bool isCaptain
        )
        {
            bool onLineup = LineupMembership.LineupNumber(matchData, steamId, playerName) != null;

            switch (setting)
            {
                case eReadySettings.Coach:
                    return isCoach;
                case eReadySettings.Captains:
                    return isCaptain && onLineup;
                case eReadySettings.Admin:
                    return false;
            }

            return onLineup;
        }

        // Each lineup readies on its own: up to its own size, counted as RosterCount
        // counts it, or through its captain or its coach. A 5v4 is ready with nine,
        // and a spare ready player on one lineup never makes up for the other.
        public static (int Ready, int Target) ReadyCount(
            eReadySettings setting,
            MatchData matchData,
            IEnumerable<(string SteamId, string Name)> ready,
            int expectedPlayers
        )
        {
            switch (setting)
            {
                case eReadySettings.Players:
                    return ReconnectPauseUtility.RosterCount(matchData, ready, expectedPlayers);
                case eReadySettings.Captains:
                    return (
                        ready
                            .Select(player =>
                                LineupMembership.LineupNumber(matchData, player.SteamId, player.Name)
                            )
                            .OfType<int>()
                            .Distinct()
                            .Count(),
                        2
                    );
                case eReadySettings.Coach:
                    return (
                        ready
                            .Select(player => CoachedLineup(matchData, player.SteamId))
                            .OfType<int>()
                            .Distinct()
                            .Count(),
                        2
                    );
            }

            return (0, 1);
        }

        private static int? CoachedLineup(MatchData matchData, string steamId)
        {
            if (string.IsNullOrEmpty(steamId))
            {
                return null;
            }

            if (matchData.lineup_1.coach_steam_id == steamId)
            {
                return 1;
            }

            return matchData.lineup_2.coach_steam_id == steamId ? 2 : null;
        }

        // The seats nobody in the server holds, on each lineup still short of its size.
        public static List<string> EmptySeats(
            MatchData matchData,
            IEnumerable<(string SteamId, string Name)> connected,
            int expectedPlayers
        )
        {
            HashSet<MatchMember> held = connected
                .Select(player => LineupMembership.Member(matchData, player.SteamId, player.Name))
                .OfType<MatchMember>()
                .ToHashSet();
            List<string> empty = [];

            foreach (MatchLineUp lineup in new[] { matchData.lineup_1, matchData.lineup_2 })
            {
                List<MatchMember> open = lineup
                    .lineup_players.Where(member => !held.Contains(member))
                    .ToList();

                if (
                    lineup.lineup_players.Count - open.Count
                    < ReconnectPauseUtility.Seats(lineup, expectedPlayers)
                )
                {
                    empty.AddRange(open.Select(SeatName));
                }
            }

            return empty;
        }

        private static string SeatName(MatchMember member)
        {
            if (member.steam_id == null)
            {
                return member.placeholder_name;
            }

            return string.IsNullOrEmpty(member.name) ? member.steam_id : member.name;
        }
    }
}
