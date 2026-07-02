using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class MatchUtilityTests
{
    private static MatchData BuildMatch()
    {
        return new MatchData
        {
            id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            current_match_map_id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
            lineup_1 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = "76561198000000001", name = "Real" },
                },
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = null, placeholder_name = "AceBot" },
                },
            },
        };
    }

    [Fact]
    public void GetMemberFromLineup_MatchesBySteamId()
    {
        MatchMember? member = MatchUtility.GetMemberFromLineup(
            BuildMatch(),
            "76561198000000001",
            "ignored"
        );
        Assert.NotNull(member);
        Assert.Equal("Real", member!.name);
    }

    [Fact]
    public void GetMemberFromLineup_MatchesPlaceholderByNamePrefix()
    {
        MatchMember? member = MatchUtility.GetMemberFromLineup(BuildMatch(), "9999", "Ace");
        Assert.NotNull(member);
        Assert.Equal("AceBot", member!.placeholder_name);
    }

    [Fact]
    public void GetMemberFromLineup_ReturnsNullWhenNoMatch()
    {
        Assert.Null(MatchUtility.GetMemberFromLineup(BuildMatch(), "9999", "Nobody"));
    }

    [Fact]
    public void HasPlaceholderMembers_TrueWhenAnyNullSteamId()
    {
        Assert.True(MatchUtility.HasPlaceholderMembers(BuildMatch()));
    }

    [Fact]
    public void HasPlaceholderMembers_FalseWhenAllHaveSteamId()
    {
        MatchData match = BuildMatch();
        match.lineup_2.lineup_players[0].steam_id = "76561198000000002";
        Assert.False(MatchUtility.HasPlaceholderMembers(match));
    }

    [Fact]
    public void GetSafeMatchPrefix_StripsDashesAndJoinsIds()
    {
        string prefix = MatchUtility.GetSafeMatchPrefix(BuildMatch());
        Assert.DoesNotContain("-", prefix);
        Assert.Equal("11111111111111111111111111111111_22222222222222222222222222222222", prefix);
    }
}
