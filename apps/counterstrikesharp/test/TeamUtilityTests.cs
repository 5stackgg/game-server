using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class TeamUtilityTests
{
    [Theory]
    [InlineData(1, "Spectator")]
    [InlineData(2, "TERRORIST")]
    [InlineData(3, "CT")]
    [InlineData(0, "None")]
    [InlineData(99, "None")]
    public void TeamNumToString_MapsTeamNumbers(int teamNum, string expected)
    {
        Assert.Equal(expected, TeamUtility.TeamNumToString(teamNum));
    }

    [Theory]
    [InlineData(1, CsTeam.Spectator)]
    [InlineData(2, CsTeam.Terrorist)]
    [InlineData(3, CsTeam.CounterTerrorist)]
    [InlineData(0, CsTeam.None)]
    [InlineData(99, CsTeam.None)]
    public void TeamNumToCSTeam_MapsTeamNumbers(int teamNum, CsTeam expected)
    {
        Assert.Equal(expected, TeamUtility.TeamNumToCSTeam(teamNum));
    }

    [Theory]
    [InlineData("Spectator", CsTeam.Spectator)]
    [InlineData("TERRORIST", CsTeam.Terrorist)]
    [InlineData("CT", CsTeam.CounterTerrorist)]
    [InlineData("", CsTeam.None)]
    [InlineData("garbage", CsTeam.None)]
    public void TeamStringToCsTeam_MapsApiStrings(string team, CsTeam expected)
    {
        Assert.Equal(expected, TeamUtility.TeamStringToCsTeam(team));
    }

    [Theory]
    [InlineData(CsTeam.Spectator)]
    [InlineData(CsTeam.Terrorist)]
    [InlineData(CsTeam.CounterTerrorist)]
    public void TeamStringRoundTrips(CsTeam team)
    {
        Assert.Equal(team, TeamUtility.TeamStringToCsTeam(TeamUtility.CSTeamToString(team)));
    }
}

public class GetLineupSideTests
{
    private static readonly Guid Lineup1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Lineup2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static (MatchData match, MatchMap map) BuildMatch(
        int mr = 12,
        string lineup1Side = "CT",
        string lineup2Side = "TERRORIST"
    )
    {
        var match = new MatchData
        {
            lineup_1_id = Lineup1,
            lineup_2_id = Lineup2,
            options = new MatchOptions { mr = mr },
        };
        var map = new MatchMap { lineup_1_side = lineup1Side, lineup_2_side = lineup2Side };
        return (match, map);
    }

    [Theory]
    [InlineData(0, CsTeam.CounterTerrorist)]
    [InlineData(11, CsTeam.CounterTerrorist)]
    [InlineData(12, CsTeam.Terrorist)]
    [InlineData(23, CsTeam.Terrorist)]
    public void RegularTime_Lineup1_SwitchesAtHalf(int round, CsTeam expected)
    {
        var (match, map) = BuildMatch();
        Assert.Equal(expected, TeamUtility.GetLineupSide(match, map, Lineup1, round));
    }

    [Theory]
    [InlineData(0, CsTeam.Terrorist)]
    [InlineData(11, CsTeam.Terrorist)]
    [InlineData(12, CsTeam.CounterTerrorist)]
    [InlineData(23, CsTeam.CounterTerrorist)]
    public void RegularTime_Lineup2_SwitchesAtHalf(int round, CsTeam expected)
    {
        var (match, map) = BuildMatch();
        Assert.Equal(expected, TeamUtility.GetLineupSide(match, map, Lineup2, round));
    }

    [Fact]
    public void ShortMatch_SwitchesAtItsOwnHalf()
    {
        var (match, map) = BuildMatch(mr: 8);
        Assert.Equal(
            CsTeam.CounterTerrorist,
            TeamUtility.GetLineupSide(match, map, Lineup1, 7)
        );
        Assert.Equal(CsTeam.Terrorist, TeamUtility.GetLineupSide(match, map, Lineup1, 8));
    }

    [Fact]
    public void ZeroMr_ReturnsNone()
    {
        var (match, map) = BuildMatch(mr: 0);
        Assert.Equal(CsTeam.None, TeamUtility.GetLineupSide(match, map, Lineup1, 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Spectator")]
    public void InvalidStartingSide_ReturnsNone(string side)
    {
        var (match, map) = BuildMatch(lineup1Side: side);
        Assert.Equal(CsTeam.None, TeamUtility.GetLineupSide(match, map, Lineup1, 0));
    }

    [Fact]
    public void UnknownLineupId_IsTreatedAsLineup2()
    {
        var (match, map) = BuildMatch();
        Guid unknown = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Assert.Equal(
            TeamUtility.GetLineupSide(match, map, Lineup2, 0),
            TeamUtility.GetLineupSide(match, map, unknown, 0)
        );
    }
}
