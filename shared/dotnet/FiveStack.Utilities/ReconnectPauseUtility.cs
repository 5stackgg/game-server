using FiveStack.Entities;

namespace FiveStack.Utilities;

public static class ReconnectPauseUtility
{
    // Coaches, casters and admins are in the server too, so only the lineups'
    // own players say whether anyone is missing. A placeholder member counts
    // once someone joins under its name, as GetMemberFromLineup matches them.
    public static bool ShouldPause(
        MatchData matchData,
        IEnumerable<(string SteamId, string Name)> connected,
        int expectedPlayers
    )
    {
        List<MatchMember> roster = matchData
            .lineup_1.lineup_players.Concat(matchData.lineup_2.lineup_players)
            .ToList();

        int present = connected.Count(player =>
            roster.Any(member =>
                member.steam_id == null
                    ? member.placeholder_name.StartsWith(player.Name)
                    : member.steam_id == player.SteamId
            )
        );

        return present < expectedPlayers;
    }
}
