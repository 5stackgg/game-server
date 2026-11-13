using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// The api claims pickup seats from the players a server reports, so only
// someone who could be one of the lineups' players is reported.
public class LineupMembershipTests
{
    private const string Coach1 = "76561198000000101";
    private const string Coach2 = "76561198000000102";
    private const string Player = "76561198000000001";

    private static MatchData BuildMatch()
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp { coach_steam_id = Coach1 },
            lineup_2 = new MatchLineUp { coach_steam_id = Coach2 },
        };
    }

    [Fact]
    public void A_Player_Could_Hold_A_Seat()
    {
        Assert.True(LineupMembership.CouldHoldASeat(BuildMatch(), Player, null));
    }

    [Theory]
    [InlineData(ePlayerRoles.Administrator)]
    [InlineData(ePlayerRoles.MatchOrganizer)]
    public void Staff_Could_Hold_A_Seat(ePlayerRoles role)
    {
        Assert.True(LineupMembership.CouldHoldASeat(BuildMatch(), Player, role));
    }

    [Fact]
    public void A_Streamer_Or_Caster_Is_Not_Offered_A_Seat()
    {
        Assert.False(LineupMembership.CouldHoldASeat(BuildMatch(), Player, ePlayerRoles.Streamer));
    }

    [Theory]
    [InlineData(Coach1)]
    [InlineData(Coach2)]
    public void A_Lineups_Coach_Is_Not_Offered_A_Seat(string coach)
    {
        Assert.False(LineupMembership.CouldHoldASeat(BuildMatch(), coach, null));
    }
}
