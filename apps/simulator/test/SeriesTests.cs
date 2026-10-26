using FiveStack.Entities;
using FiveStack.Simulator;
using Xunit;

public class SeriesTests
{
    private static async Task<(RecordingSink Sink, SeriesApi Api, MatchData Match)> PlaySeries(
        int bestOf,
        string lineup1,
        string lineup2,
        int seed = 1
    )
    {
        MatchData match = Matches.Series(bestOf, lineup1, lineup2);
        var api = new SeriesApi(match);
        var sink = new RecordingSink { OnPublish = api.Observe };
        SimulatedServer server = Matches.Server(api, sink, seed);

        await server.RefreshMatch();

        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (!(api.Over && server.LastRun?.Task.IsCompleted == true))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"series not over: {api.MapWinners.Count} maps played");
            }
            await Task.Delay(10);
        }

        return (sink, api, match);
    }

    private static List<List<Published>> Maps(RecordingSink sink)
    {
        var maps = new List<List<Published>>();
        foreach (Published published in sink.Events)
        {
            if (published.Name == "mapStatus" && (string)published.Data["status"] == "Warmup")
            {
                maps.Add(new List<Published>());
            }
            maps.LastOrDefault()?.Add(published);
        }
        return maps;
    }

    [Theory]
    [InlineData(5, 3, 0, "WWW")]
    [InlineData(5, 3, 1, "WLWW")]
    [InlineData(5, 3, 2, "WLWLW")]
    [InlineData(5, 2, 3, "WLWLL")]
    [InlineData(3, 2, 0, "WW")]
    [InlineData(3, 2, 1, "WLW")]
    [InlineData(3, 1, 2, "WLL")]
    public async Task EachMapGoesToTheLineupTheResultNeeds(
        int bestOf,
        int wins,
        int losses,
        string plan
    )
    {
        (RecordingSink sink, SeriesApi api, MatchData match) = await PlaySeries(
            bestOf,
            "Alpha",
            $"Bravo sim:{wins}-{losses}"
        );

        Assert.Equal(
            plan.Select(map => map == 'W' ? match.lineup_2_id : match.lineup_1_id).ToList(),
            api.MapWinners
        );

        // a decided series plays no more maps
        Assert.Equal(plan.Length, Maps(sink).Count);

        foreach (List<Published> map in Maps(sink))
        {
            Published last = map.Last(published => published.Name == "score");
            int lineup1 = Convert.ToInt32(last.Data["lineup_1_score"]);
            int lineup2 = Convert.ToInt32(last.Data["lineup_2_score"]);
            Published waiting = map.Single(published =>
                published.Name == "mapStatus" && (string)published.Data["status"] == "WaitingForTV"
            );

            Assert.Equal(13, Math.Max(lineup1, lineup2));
            Assert.Equal(
                (lineup1 > lineup2 ? match.lineup_1_id : match.lineup_2_id).ToString(),
                waiting.Data["winning_lineup_id"]
            );
        }
    }

    [Theory]
    [InlineData(5, "3-2", true)]
    [InlineData(5, "3-1", false)]
    [InlineData(3, "2-1", true)]
    [InlineData(3, "2-0", false)]
    public async Task OnlyTheDeciderHasAKnifeRound(int bestOf, string result, bool deciderPlayed)
    {
        (RecordingSink sink, _, _) = await PlaySeries(bestOf, $"Alpha sim:{result}", "Bravo");
        List<List<Published>> maps = Maps(sink);

        for (int index = 0; index < maps.Count; index++)
        {
            bool decider = index + 1 == bestOf;
            Assert.Equal(
                decider,
                maps[index]
                    .Any(published =>
                        published.Name == "mapStatus" && (string)published.Data["status"] == "Knife"
                    )
            );
        }
        Assert.Equal(deciderPlayed, maps.Count == bestOf);
    }

    [Fact]
    public async Task AnUnmarkedSeriesIsPlayedOutAtRandom()
    {
        (_, SeriesApi api, _) = await PlaySeries(3, "Alpha", "Bravo", seed: 5);

        Assert.InRange(api.MapWinners.Count, 2, 3);
    }

    [Fact]
    public async Task AResultTheSeriesCannotEndOnIsPlayedOutAtRandom()
    {
        (_, SeriesApi api, _) = await PlaySeries(3, "Alpha sim:3-0", "Bravo", seed: 5);

        Assert.InRange(api.MapWinners.Count, 2, 3);
    }
}

public class TieTests
{
    [Fact]
    public async Task ATiedMapEndsLevelAndIsReportedAsTheSecondLineupsTheWayThePluginDefaultsIt()
    {
        MatchData match = Matches.Build(overtime: false);
        match.lineup_1.name = "Alpha sim:tie";
        var sink = new RecordingSink();
        SimulatedServer server = Matches.Server(
            new FakeSource()
                .Then(Matches.Live(match))
                .Then(new MatchFetch(FiveStack.Enums.eCurrentMatchAction.Clear)),
            sink
        );

        await server.RefreshMatch();
        await Matches.Finish(server.LastRun);

        Published last = sink.Events.Last(e => e.Name == "score");
        Assert.Equal(24, Convert.ToInt32(last.Data["round"]));
        Assert.Equal(12, Convert.ToInt32(last.Data["lineup_1_score"]));
        Assert.Equal(12, Convert.ToInt32(last.Data["lineup_2_score"]));
        Assert.DoesNotContain("Overtime", sink.Statuses());

        Assert.All(
            sink.Events.Where(e =>
                e.Name == "mapStatus" && (string)e.Data["status"] is "WaitingForTV" or "Finished"
            ),
            e => Assert.Equal(match.lineup_2_id.ToString(), e.Data["winning_lineup_id"])
        );
    }
}
