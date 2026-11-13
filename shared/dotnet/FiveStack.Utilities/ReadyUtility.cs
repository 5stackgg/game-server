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

        // Each lineup readies up to its own size, as RosterCount counts it, so a
        // 5v4 is ready with nine.
        public static int ReadyTarget(eReadySettings setting, MatchData matchData, int expectedPlayers)
        {
            switch (setting)
            {
                case eReadySettings.Admin:
                    return 1;
                case eReadySettings.Captains:
                case eReadySettings.Coach:
                    return 2;
            }

            return ReconnectPauseUtility.Seats(matchData.lineup_1, expectedPlayers)
                + ReconnectPauseUtility.Seats(matchData.lineup_2, expectedPlayers);
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
