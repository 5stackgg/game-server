using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class EloRankTests
{
    [Fact]
    public void PredictedRatingsAreTheCurrentEloPlusEachDelta()
    {
        EloRank? rank = EloRank.From(
            new MatchMember
            {
                elo = 5000,
                elo_win = 212,
                elo_loss = -198,
                elo_tie = 7,
            }
        );

        Assert.Equal(new EloRank(5000, 5212, 4802, 5007), rank);
    }

    // An older api sends no deltas; a predicted rating of 0 shows a -5000 stake.
    [Fact]
    public void AMissingDeltaStakesNothing()
    {
        EloRank? rank = EloRank.From(new MatchMember { elo = 5000, elo_win = 25 });

        Assert.Equal(new EloRank(5000, 5025, 5000, 5000), rank);
    }

    [Fact]
    public void NoEloMeansNoRank()
    {
        Assert.Null(EloRank.From(new MatchMember { elo = null, elo_win = 25 }));
    }
}
