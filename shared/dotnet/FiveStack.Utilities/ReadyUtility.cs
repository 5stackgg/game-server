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

        // Each lineup readies on its own: up to its own size, every seat counted
        // whether bound or not, or through its captain or its coach. A 5v4 is ready
        // with nine, a spare ready player on one lineup never makes up for the
        // other, and only the lineups that have a coach wait on one.
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
                    return ReconnectPauseUtility.SeatCount(matchData, ready, expectedPlayers);
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
                        Math.Max(
                            1,
                            new[] { matchData.lineup_1, matchData.lineup_2 }.Count(lineup =>
                                !string.IsNullOrEmpty(lineup.coach_steam_id)
                            )
                        )
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

        // Lineup players not in the server, on each lineup still short of its size.
        public static List<string> EmptySeats(
            MatchData matchData,
            IEnumerable<(string SteamId, string Name)> connected,
            int expectedPlayers
        )
        {
            return OpenSeats(matchData, connected, expectedPlayers)
                .Where(member => member.steam_id != null)
                .Select(member => string.IsNullOrEmpty(member.name) ? member.steam_id! : member.name)
                .ToList();
        }

        // Seats nobody has taken by linking Discord, on each lineup still short of its size.
        public static List<string> UnlinkedSeats(
            MatchData matchData,
            IEnumerable<(string SteamId, string Name)> connected,
            int expectedPlayers
        )
        {
            return OpenSeats(matchData, connected, expectedPlayers)
                .Where(member => member.steam_id == null)
                .Select(member => member.placeholder_name)
                .ToList();
        }

        private static List<MatchMember> OpenSeats(
            MatchData matchData,
            IEnumerable<(string SteamId, string Name)> connected,
            int expectedPlayers
        )
        {
            HashSet<MatchMember> held = connected
                .Select(player => LineupMembership.Member(matchData, player.SteamId, player.Name))
                .OfType<MatchMember>()
                .ToHashSet();
            List<MatchMember> open = [];

            foreach (MatchLineUp lineup in new[] { matchData.lineup_1, matchData.lineup_2 })
            {
                List<MatchMember> missing = lineup
                    .lineup_players.Where(member => !held.Contains(member))
                    .ToList();

                if (
                    lineup.lineup_players.Count - missing.Count
                    < ReconnectPauseUtility.Seats(lineup, expectedPlayers)
                )
                {
                    open.AddRange(missing);
                }
            }

            return open;
        }
    }
}
