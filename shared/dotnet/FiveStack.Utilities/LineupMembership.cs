using FiveStack.Entities;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// A pickup seat is taken by linking Discord on 5stack.gg, which binds it to the
// player's steam id. Until then it holds nobody, and a name never takes one.
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
        return Find(matchData, steamId)?.Lineup;
    }

    public static MatchMember? Member(MatchData matchData, string steamId, string playerName)
    {
        return Find(matchData, steamId)?.Member;
    }

    // Anyone who connected with a signed role, a streamer, caster or staff, and a
    // lineup's coach never hold a seat. Staff who play a free seat join with the
    // plain password.
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

    public static bool WaitsForADiscordLink(
        MatchData matchData,
        string steamId,
        ePlayerRoles? connectedRole
    )
    {
        return Find(matchData, steamId) == null
            && Members(matchData).Any(entry => entry.Member.steam_id == null)
            && CouldHoldASeat(matchData, steamId, connectedRole);
    }

    private static (MatchMember Member, int Lineup)? Find(MatchData matchData, string steamId)
    {
        if (string.IsNullOrEmpty(steamId))
        {
            return null;
        }

        foreach ((MatchMember member, int lineup) in Members(matchData))
        {
            if (member.steam_id == steamId)
            {
                return (member, lineup);
            }
        }

        return null;
    }

    private static IEnumerable<(MatchMember Member, int Lineup)> Members(MatchData matchData)
    {
        return matchData
            .lineup_1.lineup_players.Select(member => (member, 1))
            .Concat(matchData.lineup_2.lineup_players.Select(member => (member, 2)));
    }
}
