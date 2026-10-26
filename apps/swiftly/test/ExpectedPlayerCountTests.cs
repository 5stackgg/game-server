using FiveStack.Entities;
using Xunit;

public class ExpectedPlayerCountTests
{
    private static MatchData Match(string type, int lineup1, int lineup2)
    {
        return new MatchData
        {
            options = new MatchOptions { type = type },
            lineup_1 = new MatchLineUp { lineup_players = Members(lineup1) },
            lineup_2 = new MatchLineUp { lineup_players = Members(lineup2) },
        };
    }

    private static List<MatchMember> Members(int count)
    {
        return Enumerable.Range(0, count).Select(_ => new MatchMember()).ToList();
    }

    [Theory]
    [InlineData("Competitive", 5, 5, 10)]
    [InlineData("Competitive", 6, 6, 10)]
    [InlineData("Competitive", 5, 3, 10)]
    [InlineData("Wingman", 2, 2, 4)]
    [InlineData("Duel", 1, 1, 2)]
    [InlineData("Rush", 3, 3, 6)]
    public void IsTheTypesCountForAFullRoster(string type, int lineup1, int lineup2, int expected)
    {
        Assert.Equal(expected, Match(type, lineup1, lineup2).ExpectedPlayerCount());
    }

    // Every round starts short of players who don't exist, and pauses.
    [Fact]
    public void IsTheRosterForLineupsSmallerThanTheType()
    {
        Assert.Equal(6, Match("Competitive", 3, 3).ExpectedPlayerCount());
        Assert.Equal(8, Match("Competitive", 4, 2).ExpectedPlayerCount());
    }

    [Fact]
    public void IsTheTypesCountBeforeAnyoneIsInALineup()
    {
        Assert.Equal(10, Match("Competitive", 0, 0).ExpectedPlayerCount());
    }
}
