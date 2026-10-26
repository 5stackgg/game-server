using FiveStack.Entities;
using FiveStack.Simulator;
using Xunit;

public class SideRulesTests
{
    private static MatchOptions Competitive(bool overtime = true)
    {
        return new MatchOptions
        {
            mr = 12,
            type = "Competitive",
            overtime = overtime,
        };
    }

    [Theory]
    [InlineData(0, Side.CT)]
    [InlineData(11, Side.CT)]
    [InlineData(12, Side.T)]
    [InlineData(23, Side.T)]
    // first overtime: three rounds on the side opposite the start, then back
    [InlineData(24, Side.T)]
    [InlineData(26, Side.T)]
    [InlineData(27, Side.CT)]
    [InlineData(29, Side.CT)]
    // second overtime starts where the first ended
    [InlineData(30, Side.CT)]
    [InlineData(33, Side.T)]
    public void LineupSideFollowsHalvesAndOvertime(int roundsPlayed, Side expected)
    {
        Assert.Equal(expected, SideRules.LineupSide(Competitive(), Side.CT, roundsPlayed));
    }

    [Fact]
    public void RushNeverSwapsSides()
    {
        var rush = new MatchOptions { mr = 12, type = "Rush" };

        Assert.Equal(Side.T, SideRules.LineupSide(rush, Side.T, 14));
    }

    [Theory]
    [InlineData(13, 0, true)]
    [InlineData(12, 11, false)]
    [InlineData(13, 11, true)]
    [InlineData(12, 12, false)]
    [InlineData(15, 13, false)]
    [InlineData(16, 13, true)]
    [InlineData(15, 15, false)]
    [InlineData(19, 17, true)]
    [InlineData(18, 17, false)]
    public void MapEndsOnClinchOrOvertimeWin(int lineup1, int lineup2, bool over)
    {
        Assert.Equal(over, SideRules.IsMapOver(Competitive(), lineup1, lineup2));
    }

    [Fact]
    public void WithoutOvertimeAMapCanEndTied()
    {
        Assert.True(SideRules.IsMapOver(Competitive(overtime: false), 12, 12));
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    [InlineData(24, false)]
    [InlineData(27, true)]
    [InlineData(30, false)]
    [InlineData(33, true)]
    public void SidesSwitchAtHalftimeAndWithinEachOvertime(int roundsPlayed, bool switches)
    {
        Assert.Equal(switches, SideRules.SidesSwitch(Competitive(), roundsPlayed));
    }

    [Theory]
    [InlineData(12, true)]
    [InlineData(13, false)]
    [InlineData(24, true)]
    [InlineData(27, true)]
    [InlineData(28, false)]
    [InlineData(30, true)]
    public void MoneyResetsAtHalftimeAndEveryOvertimeHalf(int roundsPlayed, bool resets)
    {
        Assert.Equal(resets, SideRules.ResetsMoney(Competitive(), roundsPlayed));
    }
}
