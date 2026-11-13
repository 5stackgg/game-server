using FiveStack.Entities;
using FiveStack.Enums;

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

    // Anyone who connected with a signed role, a streamer, caster or staff, and a
    // lineup's coach are not offered to the api for a seat. Staff who play a free
    // seat join with the plain password.
    public static bool CouldHoldASeat(
        MatchData matchData,
        string steamId,
        ePlayerRoles? connectedRole
    )
    {
        return connectedRole == null
            && steamId != matchData.lineup_1.coach_steam_id
            && steamId != matchData.lineup_2.coach_steam_id;
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

    // A name that only fits a seat is anyone's to take, so it gives no lineup:
    // the api binds such a seat to a steam id from the players a server reports.
    // Only a seat's exact name counts here, and only when no other seat has it.
    private static (MatchMember Member, int Lineup)? FindPlaceholder(
        MatchData matchData,
        string playerName
    )
    {
        string name = (playerName ?? "").Trim();

        if (name.Length < 2)
        {
            return null;
        }

        List<(MatchMember Member, int Lineup)> seats = Members(matchData)
            .Where(entry =>
                entry.Member.steam_id == null
                && string.Equals(
                    (entry.Member.placeholder_name ?? "").Trim(),
                    name,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();

        return seats.Count == 1 ? seats[0] : null;
    }

    private static IEnumerable<(MatchMember Member, int Lineup)> Members(MatchData matchData)
    {
        return matchData
            .lineup_1.lineup_players.Select(member => (member, 1))
            .Concat(matchData.lineup_2.lineup_players.Select(member => (member, 2)));
    }
}
