using SwiftlyS2.Shared.Players;
using FiveStack;
using FiveStack.Entities;
using Xunit;

// A player who times out after halftime has to surrender from the side their
// lineup plays now. Using the side they left from opens the vote to the
// opponents and hands the map to the leaver's own lineup.
public class LeaverSurrenderSideTests
{
    private static readonly Guid Lineup1 = Guid.NewGuid();
    private static readonly Guid Lineup2 = Guid.NewGuid();

    private static (MatchData match, MatchMap map) BuildMatch()
    {
        var match = new MatchData
        {
            lineup_1_id = Lineup1,
            lineup_2_id = Lineup2,
            options = new MatchOptions { mr = 12, type = "Competitive" },
        };
        var map = new MatchMap { lineup_1_side = "CT", lineup_2_side = "TERRORIST" };
        return (match, map);
    }

    [Fact]
    public void A_Leaver_Who_Left_On_CT_Before_Halftime_Surrenders_From_T_After_It()
    {
        var (match, map) = BuildMatch();

        Assert.Equal(
            Team.T,
            SurrenderSystem.LeaverSurrenderSide(match, map, Lineup1, Team.CT, 12)
        );
    }

    [Fact]
    public void A_Leaver_In_The_Same_Half_Surrenders_From_The_Side_They_Left()
    {
        var (match, map) = BuildMatch();

        Assert.Equal(
            Team.CT,
            SurrenderSystem.LeaverSurrenderSide(match, map, Lineup1, Team.CT, 5)
        );
    }

    [Fact]
    public void The_Other_Lineup_Swaps_Too()
    {
        var (match, map) = BuildMatch();

        Assert.Equal(
            Team.CT,
            SurrenderSystem.LeaverSurrenderSide(match, map, Lineup2, Team.T, 13)
        );
    }

    [Fact]
    public void Without_A_Lineup_The_Side_They_Left_From_Is_Kept()
    {
        var (match, map) = BuildMatch();

        Assert.Equal(
            Team.T,
            SurrenderSystem.LeaverSurrenderSide(match, map, null, Team.T, 12)
        );
    }
}
