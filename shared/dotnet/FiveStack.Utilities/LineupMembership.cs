using FiveStack.Entities;

namespace FiveStack.Utilities;

public static class LineupMembership
{
    public static Guid? LineupOf(MatchData matchData, string steamId, string playerName)
    {
        return LineupNumber(matchData, steamId, playerName) switch
        {
            1 => matchData.lineup_1_id,
            2 => matchData.lineup_2_id,
            _ => null,
        };
    }

    // Exact steam ids first: a placeholder's name matches by prefix, and an empty
    // name would match every unclaimed placeholder.
    public static int? LineupNumber(MatchData matchData, string steamId, string playerName)
    {
        if (!string.IsNullOrEmpty(steamId))
        {
            if (matchData.lineup_1.lineup_players.Any(member => member.steam_id == steamId))
            {
                return 1;
            }

            if (matchData.lineup_2.lineup_players.Any(member => member.steam_id == steamId))
            {
                return 2;
            }
        }

        if (string.IsNullOrEmpty(playerName))
        {
            return null;
        }

        if (matchData.lineup_1.lineup_players.Any(member => IsPlaceholderFor(member, playerName)))
        {
            return 1;
        }

        if (matchData.lineup_2.lineup_players.Any(member => IsPlaceholderFor(member, playerName)))
        {
            return 2;
        }

        return null;
    }

    private static bool IsPlaceholderFor(MatchMember member, string playerName)
    {
        return member.steam_id == null && member.placeholder_name.StartsWith(playerName);
    }
}
