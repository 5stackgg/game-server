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

    public static int? LineupNumber(MatchData matchData, string steamId, string playerName)
    {
        return Find(matchData, steamId, playerName)?.Lineup;
    }

    public static MatchMember? Member(MatchData matchData, string steamId, string playerName)
    {
        return Find(matchData, steamId, playerName)?.Member;
    }

    public static MatchMember? Placeholder(MatchData matchData, string playerName)
    {
        return FindPlaceholder(matchData, playerName)?.Member;
    }

    // Exact steam ids first, so a placeholder never claims a player who is on a lineup.
    private static (MatchMember Member, int Lineup)? Find(
        MatchData matchData,
        string steamId,
        string playerName
    )
    {
        if (!string.IsNullOrEmpty(steamId))
        {
            foreach ((MatchMember member, int lineup) in Members(matchData))
            {
                if (member.steam_id == steamId)
                {
                    return (member, lineup);
                }
            }
        }

        return FindPlaceholder(matchData, playerName);
    }

    // A name claims the placeholder it equals, or else one it starts. A name that
    // fits placeholders on both lineups says nothing about which one, and an empty
    // name would fit them all.
    private static (MatchMember Member, int Lineup)? FindPlaceholder(
        MatchData matchData,
        string playerName
    )
    {
        if (string.IsNullOrEmpty(playerName))
        {
            return null;
        }

        return OnOneLineup(matchData, name => name == playerName)
            ?? OnOneLineup(matchData, name => name.StartsWith(playerName));
    }

    private static (MatchMember Member, int Lineup)? OnOneLineup(
        MatchData matchData,
        Func<string, bool> claims
    )
    {
        List<(MatchMember Member, int Lineup)> claimed = Members(matchData)
            .Where(entry => entry.Member.steam_id == null && claims(entry.Member.placeholder_name))
            .ToList();

        if (claimed.Count == 0 || claimed.Any(entry => entry.Lineup != claimed[0].Lineup))
        {
            return null;
        }

        return claimed[0];
    }

    private static IEnumerable<(MatchMember Member, int Lineup)> Members(MatchData matchData)
    {
        return matchData
            .lineup_1.lineup_players.Select(member => (member, 1))
            .Concat(matchData.lineup_2.lineup_players.Select(member => (member, 2)));
    }
}
