using FiveStack.Entities;

namespace FiveStack.Utilities;

public static class LineupMembership
{
    // Exact steam ids first: a placeholder's name matches by prefix, and an empty
    // name would match every unclaimed placeholder.
    public static Guid? LineupOf(MatchData matchData, string steamId, string playerName)
    {
        if (!string.IsNullOrEmpty(steamId))
        {
            if (matchData.lineup_1.lineup_players.Any(member => member.steam_id == steamId))
            {
                return matchData.lineup_1_id;
            }

            if (matchData.lineup_2.lineup_players.Any(member => member.steam_id == steamId))
            {
                return matchData.lineup_2_id;
            }
        }

        if (string.IsNullOrEmpty(playerName))
        {
            return null;
        }

        if (matchData.lineup_1.lineup_players.Any(member => IsPlaceholderFor(member, playerName)))
        {
            return matchData.lineup_1_id;
        }

        if (matchData.lineup_2.lineup_players.Any(member => IsPlaceholderFor(member, playerName)))
        {
            return matchData.lineup_2_id;
        }

        return null;
    }

    private static bool IsPlaceholderFor(MatchMember member, string playerName)
    {
        return member.steam_id == null && member.placeholder_name.StartsWith(playerName);
    }
}
