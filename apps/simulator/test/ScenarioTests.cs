using FiveStack.Entities;
using FiveStack.Simulator;
using Xunit;

public class ScenarioTests
{
    private static MatchData Named(string lineup1, string lineup2, int bestOf = 5)
    {
        MatchData match = Matches.Build();
        match.lineup_1.name = lineup1;
        match.lineup_2.name = lineup2;
        match.options.best_of = bestOf;
        return match;
    }

    [Fact]
    public void AnUnmarkedMatchHasNoScenario()
    {
        Assert.True(Scenario.From(Named("Alpha", "Bravo")).IsEmpty);
    }

    [Fact]
    public void TheMarkedLineupIsTheOneTheScenarioIsAbout()
    {
        MatchData match = Named("Alpha", "Bravo SIM:3-1");
        Scenario scenario = Scenario.From(match);

        Assert.Equal(match.lineup_2_id, scenario.Subject);
        Assert.Equal(3, scenario.Wins);
        Assert.Equal(1, scenario.Losses);
    }

    [Fact]
    public void ATeamTagCarriesItToo()
    {
        MatchData match = Named("Alpha", "Bravo");
        match.lineup_1.tag = "sim:tie";

        Scenario scenario = Scenario.From(match);

        Assert.Equal(match.lineup_1_id, scenario.Subject);
        Assert.True(scenario.Tie);
    }

    [Theory]
    [InlineData(5, 3, 0, "WWW")]
    [InlineData(5, 3, 1, "WLWW")]
    [InlineData(5, 3, 2, "WLWLW")]
    [InlineData(5, 2, 3, "WLWLL")]
    [InlineData(5, 0, 3, "LLL")]
    [InlineData(3, 2, 0, "WW")]
    [InlineData(3, 2, 1, "WLW")]
    [InlineData(3, 1, 2, "WLL")]
    [InlineData(1, 1, 0, "W")]
    // the series is decided on its last map, and never before it
    public void ASeriesGoesExactlyAsFarAsItsResult(int bestOf, int wins, int losses, string maps)
    {
        Scenario scenario = Scenario.From(Named($"Alpha sim:{wins}-{losses}", "Bravo", bestOf));

        Assert.Equal(maps.Select(map => map == 'W').ToList(), scenario.SeriesPlan(bestOf));
    }

    [Theory]
    [InlineData(5, 2, 1)]
    [InlineData(3, 3, 0)]
    [InlineData(3, 2, 2)]
    [InlineData(1, 1, 1)]
    public void AResultTheSeriesCannotEndOnIsIgnored(int bestOf, int wins, int losses)
    {
        Scenario scenario = Scenario.From(Named($"Alpha sim:{wins}-{losses}", "Bravo", bestOf));

        Assert.Null(scenario.SeriesPlan(bestOf));
    }

    [Fact]
    public void AScriptedMapIsWonInRegulationAndDecidedOnlyOnItsLastRound()
    {
        var options = new MatchOptions { mr = 12, type = "Competitive" };
        Guid winner = Guid.NewGuid();
        Guid loser = Guid.NewGuid();

        for (int seed = 0; seed < 200; seed++)
        {
            List<Guid> rounds = MapScript.Won(options, winner, loser, new Random(seed));
            int won = 0;
            int lost = 0;

            for (int round = 0; round < rounds.Count; round++)
            {
                if (rounds[round] == winner)
                {
                    won++;
                }
                else
                {
                    lost++;
                }

                Assert.Equal(round == rounds.Count - 1, SideRules.IsMapOver(options, won, lost));
            }

            Assert.Equal(13, won);
            Assert.InRange(lost, 2, 11);
        }
    }

    [Fact]
    public void ATiedMapEndsLevelWhenThereIsNoOvertime()
    {
        var options = new MatchOptions
        {
            mr = 12,
            type = "Competitive",
            overtime = false,
        };
        Guid lineup1 = Guid.NewGuid();
        Guid lineup2 = Guid.NewGuid();

        List<Guid> rounds = MapScript.Tied(options, lineup1, lineup2, new Random(3));

        Assert.Equal(24, rounds.Count);
        Assert.Equal(12, rounds.Count(round => round == lineup1));
        Assert.True(SideRules.IsMapOver(options, 12, 12));
        for (int played = 1; played < rounds.Count; played++)
        {
            int one = rounds.Take(played).Count(round => round == lineup1);
            Assert.False(SideRules.IsMapOver(options, one, played - one));
        }
    }
}
