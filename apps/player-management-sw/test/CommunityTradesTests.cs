using FiveStack.Utilities;
using Xunit;

// Slots 1 and 2 are T, slots 3 and 4 are CT.
public class CommunityTradesTests
{
    private const int T = PlayerRoster.TeamT;
    private const int CT = PlayerRoster.TeamCT;

    [Theory]
    [InlineData(4.9f, 1)]
    [InlineData(5.0f, 1)]
    [InlineData(5.1f, 0)]
    public void AKillTradesATeammatesDeathWithinFiveSeconds(float after, int expected)
    {
        CommunityTrades trades = new();

        Assert.Equal(0, trades.Killed(3, CT, 1, T, 10f));
        Assert.Equal(expected, trades.Killed(2, T, 3, CT, 10f + after));
    }

    [Fact]
    public void KillingSomeoneWhoKilledTwoTeammatesTradesBoth()
    {
        CommunityTrades trades = new();

        trades.Killed(3, CT, 1, T, 10f);
        trades.Killed(3, CT, 2, T, 11f);

        Assert.Equal(2, trades.Killed(5, T, 3, CT, 12f));
    }

    [Fact]
    public void ANewRoundClearsTheKills()
    {
        CommunityTrades trades = new();

        trades.Killed(3, CT, 1, T, 10f);
        trades.Clear();

        Assert.Equal(0, trades.Killed(2, T, 3, CT, 11f));
    }

    // Only the killer of a teammate is traded, and only by a teammate of the
    // one who died; a team kill of the killer trades nothing, and the killer
    // has then died.
    [Fact]
    public void OnlyTheKillerOfATeammateIsTraded()
    {
        CommunityTrades trades = new();

        trades.Killed(3, CT, 1, T, 10f);

        Assert.Equal(0, trades.Killed(2, T, 4, CT, 11f));
        Assert.Equal(1, trades.Killed(2, T, 3, CT, 12f));

        CommunityTrades teamKill = new();

        teamKill.Killed(3, CT, 1, T, 10f);

        Assert.Equal(0, teamKill.Killed(4, CT, 3, CT, 11f));
        Assert.Equal(0, teamKill.Killed(2, T, 3, CT, 12f));
    }

    // A player who respawns and kills their own killer has avenged nobody.
    [Fact]
    public void AVictimDoesNotTradeTheirOwnDeath()
    {
        CommunityTrades trades = new();

        trades.Killed(3, CT, 1, T, 10f);

        Assert.Equal(0, trades.Killed(1, T, 3, CT, 12f));
    }

    // A trading kill is a kill like any other, and can be traded in turn.
    [Fact]
    public void ATradeCanBeTraded()
    {
        CommunityTrades trades = new();

        trades.Killed(3, CT, 1, T, 10f);
        trades.Killed(2, T, 3, CT, 11f);

        Assert.Equal(1, trades.Killed(4, CT, 2, T, 12f));
    }

    // Where players respawn, a killer who dies and comes back has paid for
    // the kills they made before: killing them again is no second trade.
    [Fact]
    public void ADeathIsTradedOnlyOnce()
    {
        CommunityTrades trades = new();

        trades.Killed(3, CT, 1, T, 10f);

        Assert.Equal(1, trades.Killed(2, T, 3, CT, 11f));
        Assert.Equal(0, trades.Killed(5, T, 3, CT, 13f));
    }

    // A killer who falls to their death and respawns took their pending kills
    // with them.
    [Fact]
    public void AKillerWhoDiesAnyOtherWayIsNoLongerTraded()
    {
        CommunityTrades trades = new();

        trades.Killed(3, CT, 1, T, 10f);
        trades.Died(3);

        Assert.Equal(0, trades.Killed(2, T, 3, CT, 12f));
    }

    [Fact]
    public void AnotherPlayersDeathLeavesAKillerTraded()
    {
        CommunityTrades trades = new();

        trades.Killed(3, CT, 1, T, 10f);
        trades.Died(4);

        Assert.Equal(1, trades.Killed(2, T, 3, CT, 12f));
    }
}
