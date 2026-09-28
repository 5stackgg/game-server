using FiveStack.Entities;
using Xunit;

public class MatchOptionsTests
{
    [Theory]
    [InlineData("Competitive", 1, 10)]
    [InlineData("Wingman", 2, 4)]
    [InlineData("Duel", 2, 2)]
    [InlineData("Rush", 6, 6)]
    public void GameModeAndPlayerCount_FollowType(string type, int gameMode, int players)
    {
        var options = new MatchOptions { type = type };
        Assert.Equal(gameMode, options.GameMode());
        Assert.Equal(players, options.ExpectedPlayerCount());
    }

    [Theory]
    [InlineData(8)]
    [InlineData(12)]
    public void Rush_IsFirstToEightWhateverTheOptionsSay(int mr)
    {
        var options = new MatchOptions
        {
            type = "Rush",
            mr = mr,
            overtime = true,
            knife_round = true,
        };
        Assert.Equal(15, options.MaxRounds());
        Assert.False(options.OvertimeEnabled());
        Assert.False(options.KnifeRoundEnabled());
        Assert.False(options.SwapsSides());
        Assert.False(options.CanRestoreRounds());
    }

    [Fact]
    public void Competitive_PlaysBothHalves()
    {
        var options = new MatchOptions
        {
            type = "Competitive",
            mr = 12,
            overtime = true,
            knife_round = true,
        };
        Assert.Equal(24, options.MaxRounds());
        Assert.True(options.OvertimeEnabled());
        Assert.True(options.KnifeRoundEnabled());
        Assert.True(options.SwapsSides());
        Assert.True(options.CanRestoreRounds());
    }
}
