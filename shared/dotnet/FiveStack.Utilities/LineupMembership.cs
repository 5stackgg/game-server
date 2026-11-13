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

    // Pickup seats are named after a Discord name, so a Steam name claims a seat
    // the way the api's PlayersConnected namesMatch does: ignoring case, with
    // either name inside the other. The exact name goes first, and a name that
    // fits more than one seat claims none.
    private static (MatchMember Member, int Lineup)? FindPlaceholder(
        MatchData matchData,
        string playerName
    )
    {
        string name = Normalize(playerName);

        if (name.Length < 2)
        {
            return null;
        }

        return OnlySeat(matchData, seat => seat == name)
            ?? OnlySeat(
                matchData,
                seat =>
                    seat.Length >= 2
                    && (
                        seat.Contains(name, StringComparison.Ordinal)
                        || name.Contains(seat, StringComparison.Ordinal)
                    )
            );
    }

    private static (MatchMember Member, int Lineup)? OnlySeat(
        MatchData matchData,
        Func<string, bool> claims
    )
    {
        List<(MatchMember Member, int Lineup)> claimed = Members(matchData)
            .Where(entry =>
                entry.Member.steam_id == null && claims(Normalize(entry.Member.placeholder_name))
            )
            .ToList();

        return claimed.Count == 1 ? claimed[0] : null;
    }

    private static string Normalize(string? name)
    {
        return (name ?? "").Trim().ToLowerInvariant();
    }

    private static IEnumerable<(MatchMember Member, int Lineup)> Members(MatchData matchData)
    {
        return matchData
            .lineup_1.lineup_players.Select(member => (member, 1))
            .Concat(matchData.lineup_2.lineup_players.Select(member => (member, 2)));
    }
}
