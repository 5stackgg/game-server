using FiveStack.Entities;

namespace FiveStack.Utilities;

public static class ReconnectPauseUtility
{
    // Coaches, casters and admins are in the server too, so only the lineups'
    // own players say whether anyone is missing. Each lineup is held to its own
    // size: a lineup short by design (a 5v4 started by its captains or an admin)
    // is not missing anyone. A placeholder member counts once someone joins
    // under its name, and a player fills a seat on one lineup only.
    public static bool ShouldPause(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        (int present, int required) = RosterCount(matchData, connected, expectedPlayers);

        return present < required;
    }

    // Each lineup's count is capped at what it needs, so a lineup with a spare
    // player in the server never makes up for one that is short.
    public static (int Present, int Required) RosterCount(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        int required1 = Seats(matchData.lineup_1, expectedPlayers);
        int required2 = Seats(matchData.lineup_2, expectedPlayers);
        int present1 = 0;
        int present2 = 0;

        foreach ((string steamId, string name) in connected)
        {
            int? lineup = LineupMembership.LineupNumber(matchData, steamId, name);

            if (lineup == 1)
            {
                present1++;
            }
            else if (lineup == 2)
            {
                present2++;
            }
        }

        return (
            Math.Min(present1, required1) + Math.Min(present2, required2),
            required1 + required2
        );
    }

    public static int Seats(MatchLineUp lineup, int expectedPlayers)
    {
        return Math.Min(expectedPlayers / 2, lineup.lineup_players.Count);
    }
}
