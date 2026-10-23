using FiveStack;
using FiveStack.Entities;
using Xunit;

// Staff override the pause, resume, camera and empty-team gates. A lineup
// player's clan tag is their team's short name, which the team owner picks.
public class TimeoutStaffTests
{
    private const ulong LineupPlayer = 76561198000000001;
    private const ulong Outsider = 76561198000000002;

    private static readonly Guid Lineup1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Lineup2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static MatchData BuildMatch(string teamTag, string role = "user")
    {
        return new MatchData
        {
            lineup_1_id = Lineup1,
            lineup_2_id = Lineup2,
            lineup_1 = new MatchLineUp
            {
                id = Lineup1,
                tag = teamTag,
                lineup_players = new List<MatchMember>
                {
                    new MatchMember
                    {
                        steam_id = LineupPlayer.ToString(),
                        name = "Player",
                        role = role,
                        match_lineup_id = Lineup1,
                    },
                },
            },
            lineup_2 = new MatchLineUp { id = Lineup2, tag = "OTHER" },
        };
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public void A_Lineup_Player_Whose_Team_Is_Tagged_As_Staff_Is_Not_Staff(string teamTag)
    {
        Assert.False(TimeoutSystem.IsStaff(BuildMatch(teamTag), LineupPlayer, "Player", null));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public void A_Player_Who_Connected_With_A_Staff_Role_Is_Staff(string connectedRole)
    {
        Assert.True(TimeoutSystem.IsStaff(BuildMatch("TEAM"), Outsider, "Staff", connectedRole));
    }

    [Fact]
    public void A_Player_Who_Connected_As_A_Streamer_Is_Not_Staff()
    {
        Assert.False(TimeoutSystem.IsStaff(BuildMatch("TEAM"), Outsider, "Caster", "streamer"));
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("match_organizer")]
    [InlineData("tournament_organizer")]
    public void A_Lineup_Player_With_A_Staff_Role_Is_Staff(string role)
    {
        Assert.True(TimeoutSystem.IsStaff(BuildMatch("TEAM", role), LineupPlayer, "Player", null));
    }
}
