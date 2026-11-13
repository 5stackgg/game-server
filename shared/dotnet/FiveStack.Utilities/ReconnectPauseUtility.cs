using FiveStack.Entities;

namespace FiveStack.Utilities;

public static class ReconnectPauseUtility
{
    // Coaches, casters and admins are in the server too, so only the lineups'
    // own players say whether anyone is missing. Each lineup is held to its own
    // size: a lineup short by design (a 5v4 started by its captains or an admin)
    // is not missing anyone. A placeholder member counts once someone joins
    // under its name, as GetMemberFromLineup matches them.
    public static bool ShouldPause(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        List<(string SteamId, string Name)> players = connected.ToList();
        int perLineup = expectedPlayers / 2;

        return IsShort(matchData.lineup_1, players, perLineup)
            || IsShort(matchData.lineup_2, players, perLineup);
    }

    public static (int Present, int Required) RosterCount(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        return (connected.Count(), expectedPlayers);
    }

    private static bool IsShort(
        MatchLineUp lineup,
        List<(string SteamId, string Name)> connected,
        int perLineup
    )
    {
        int required = Math.Min(perLineup, lineup.lineup_players.Count);

        int present = connected.Count(player =>
            lineup.lineup_players.Any(member =>
                member.steam_id == null
                    ? member.placeholder_name.StartsWith(player.Name)
                    : member.steam_id == player.SteamId
            )
        );

        return present < required;
    }
}
