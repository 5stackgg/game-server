using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// The api is told when anyone leaves, or whoever it was told connected
// outside the lineups stays in game in the match lobby.
public class PlayerDisconnectUtilityTests
{
    [Fact]
    public void A_Player_Outside_The_Lineups_Is_Reported()
    {
        Assert.Equal(
            eDisconnectHandling.Report,
            PlayerDisconnectUtility.Handle(
                isBot: false,
                overCapacityKick: false,
                isLineupMember: false
            )
        );
    }

    [Fact]
    public void An_Over_Capacity_Kick_Is_Reported_But_Not_As_Leaving_The_Match()
    {
        Assert.Equal(
            eDisconnectHandling.Report,
            PlayerDisconnectUtility.Handle(
                isBot: false,
                overCapacityKick: true,
                isLineupMember: true
            )
        );
    }

    [Fact]
    public void A_Lineup_Member_Is_Reported_Leaving_The_Match()
    {
        Assert.Equal(
            eDisconnectHandling.LineupMemberLeft,
            PlayerDisconnectUtility.Handle(
                isBot: false,
                overCapacityKick: false,
                isLineupMember: true
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Bot_Is_Ignored(bool isLineupMember)
    {
        Assert.Equal(
            eDisconnectHandling.Ignore,
            PlayerDisconnectUtility.Handle(
                isBot: true,
                overCapacityKick: false,
                isLineupMember: isLineupMember
            )
        );
    }
}
