namespace FiveStack.Tests.Utilities;

using FiveStack.Utilities;
using CounterStrikeSharp.API.Modules.Utils;

public class TeamUtilityTests
{
    [Theory]
    [InlineData("Spectator", CsTeam.Spectator)]
    [InlineData("TERRORIST", CsTeam.Terrorist)]
    [InlineData("CT", CsTeam.CounterTerrorist)]
    [InlineData("Unknown", CsTeam.None)]
    public void TeamStringToCsTeam_ConvertsCorrectly(string input, CsTeam expected)
    {
        TeamUtility.TeamStringToCsTeam(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(CsTeam.Spectator, "Spectator")]
    [InlineData(CsTeam.Terrorist, "TERRORIST")]
    [InlineData(CsTeam.CounterTerrorist, "CT")]
    [InlineData(CsTeam.None, "None")]
    public void CSTeamToString_ConvertsCorrectly(CsTeam input, string expected)
    {
        TeamUtility.CSTeamToString(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "Spectator")]
    [InlineData(2, "TERRORIST")]
    [InlineData(3, "CT")]
    [InlineData(0, "None")]
    [InlineData(99, "None")]
    public void TeamNumToString_ConvertsCorrectly(int input, string expected)
    {
        TeamUtility.TeamNumToString(input).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, CsTeam.Spectator)]
    [InlineData(2, CsTeam.Terrorist)]
    [InlineData(3, CsTeam.CounterTerrorist)]
    [InlineData(0, CsTeam.None)]
    [InlineData(99, CsTeam.None)]
    public void TeamNumToCSTeam_ConvertsCorrectly(int input, CsTeam expected)
    {
        TeamUtility.TeamNumToCSTeam(input).Should().Be(expected);
    }

    [Fact]
    public void TeamStringToCsTeam_And_CSTeamToString_AreInverse()
    {
        var teams = new[] { "Spectator", "TERRORIST", "CT" };
        foreach (var team in teams)
        {
            var csTeam = TeamUtility.TeamStringToCsTeam(team);
            TeamUtility.CSTeamToString(csTeam).Should().Be(team);
        }
    }
}
