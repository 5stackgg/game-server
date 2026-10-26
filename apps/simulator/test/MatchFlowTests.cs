using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Simulator;
using Xunit;

public class MatchFlowTests
{
    private static async Task<(RecordingSink Sink, MatchData Match)> PlayMap(
        MatchData match,
        int seed = 1,
        bool onGameNode = true,
        Action<SimulatedServer, Published>? onPublish = null
    )
    {
        var sink = new RecordingSink();
        var source = new FakeSource()
            .Then(Matches.Live(match))
            .Then(new MatchFetch(eCurrentMatchAction.Clear));
        SimulatedServer server = Matches.Server(source, sink, seed, onGameNode);

        if (onPublish != null)
        {
            sink.OnPublish = published => onPublish(server, published);
        }

        await server.RefreshMatch();
        await Matches.Finish(server.LastRun);

        return (sink, match);
    }

    private static int Int(Published published, string key) => Convert.ToInt32(published.Data[key]);

    [Fact]
    public async Task A5v5MapGoesFromWarmupToFinishedAsThePluginReportsIt()
    {
        (RecordingSink sink, MatchData match) = await PlayMap(Matches.Build());

        Assert.Equal(
            ["Warmup", "Live", "WaitingForTV", "Finished"],
            sink.Statuses().Where(status => status != "Overtime")
        );

        List<Published> events = sink.Events;
        int live = events.FindIndex(e =>
            e.Name == "mapStatus" && (string)e.Data["status"] == "Live"
        );

        Assert.Equal(10, events.Take(live).Count(e => e.Name == "player-connected"));
        Assert.Equal(2, events.Take(live).Count(e => e.Name == "captain"));
        Assert.Equal("techTimeout", events[live + 1].Name);

        List<Published> scores = events.Where(e => e.Name == "score").ToList();
        Assert.Equal(
            Enumerable.Range(1, scores.Count),
            scores.Select(score => Int(score, "round"))
        );

        Published last = scores.Last();
        int lineup1 = Int(last, "lineup_1_score");
        int lineup2 = Int(last, "lineup_2_score");
        Assert.True(SideRules.IsMapOver(match.options, lineup1, lineup2));
        Assert.All(
            scores.SkipLast(1),
            score =>
                Assert.False(
                    SideRules.IsMapOver(
                        match.options,
                        Int(score, "lineup_1_score"),
                        Int(score, "lineup_2_score")
                    )
                )
        );

        Published waiting = events.Single(e =>
            e.Name == "mapStatus" && (string)e.Data["status"] == "WaitingForTV"
        );
        Assert.Equal(
            (lineup1 > lineup2 ? match.lineup_1_id : match.lineup_2_id).ToString(),
            waiting.Data["winning_lineup_id"]
        );
        Assert.True(events.IndexOf(last) < events.IndexOf(waiting));
    }

    [Fact]
    public async Task EachRoundsStatsComeBeforeItsScore()
    {
        (RecordingSink sink, _) = await PlayMap(Matches.Build(), seed: 4);

        int round = 1;
        foreach (Published published in sink.Events)
        {
            if (published.Name == "score")
            {
                Assert.Equal(round, Int(published, "round"));
                round++;
            }
            else if (
                published.Name
                is "kill"
                    or "damage"
                    or "utility"
                    or "flash"
                    or "assist"
                    or "objective"
            )
            {
                Assert.Equal(round, Int(published, "round"));
            }
        }

        Assert.Contains(sink.Events, e => e.Name == "kill");
        Assert.Contains(sink.Events, e => e.Name == "damage");
        Assert.Contains(sink.Events, e => e.Name == "assist");
        Assert.Contains(sink.Events, e => e.Name == "flash");
        Assert.Contains(sink.Events, e => e.Name == "utility");
        Assert.Contains(sink.Events, e => e.Name == "objective");
    }

    [Fact]
    public async Task NoTwoTimedEventsShareAMillisecond()
    {
        (RecordingSink sink, _) = await PlayMap(Matches.Build(), seed: 2);

        List<DateTimeOffset> times = sink
            .Events.Where(e => e.Data.ContainsKey("time"))
            .Select(e => (DateTimeOffset)e.Data["time"])
            .ToList();

        Assert.NotEmpty(times);
        Assert.All(times.Zip(times.Skip(1)), pair => Assert.True(pair.Second > pair.First));
        Assert.All(times, time => Assert.Equal(0, time.Ticks % TimeSpan.TicksPerMillisecond));
    }

    [Fact]
    public async Task OffAGameNodeTheMapUploadsNothingBeforeItFinishes()
    {
        (RecordingSink sink, _) = await PlayMap(Matches.Build(), onGameNode: false);

        Assert.Equal(
            ["WaitingForTV", "UploadingDemo", "Finished"],
            sink.Statuses().SkipWhile(status => status != "WaitingForTV")
        );
    }

    [Fact]
    public async Task AKnifeWinnerThatSwitchesWaitsForTheApiThenPlaysTheOtherSide()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            bool switched = false;
            (RecordingSink sink, MatchData match) = await PlayMap(
                Matches.Build(knife: true),
                seed,
                onPublish: (server, published) =>
                {
                    if (published.Name == "switch")
                    {
                        switched = true;
                        Task.Run(() => server.Execute("api_knife_switch"));
                    }
                }
            );

            Assert.Equal(["Warmup", "Knife", "Live"], sink.Statuses().Take(3));

            Published firstScore = sink.Events.First(e => e.Name == "score");
            Assert.Equal(switched ? "TERRORIST" : "CT", firstScore.Data["lineup_1_side"]);

            if (switched)
            {
                int switchAt = sink.Events.FindIndex(e => e.Name == "switch");
                int knifeAt = sink.Events.FindIndex(e =>
                    e.Name == "mapStatus" && (string)e.Data["status"] == "Knife"
                );
                int liveAt = sink.Events.FindIndex(e =>
                    e.Name == "mapStatus" && (string)e.Data["status"] == "Live"
                );
                Assert.True(knifeAt < switchAt && switchAt < liveAt);
                return;
            }
        }

        Assert.Fail("no knife winner switched sides in 20 seeds");
    }

    [Fact]
    public async Task PlaceholdersJoinUnderMadeUpSteamIdsThatTheApiCanPin()
    {
        (RecordingSink sink, MatchData match) = await PlayMap(Matches.Build(placeholders: true));

        Published connected = sink.Events.First(e => e.Name == "players-connected");
        var players = (List<Dictionary<string, object>>)connected.Data["players"];

        Assert.Equal("Live", connected.Data["status"]);
        Assert.Equal(10, players.Select(player => player["steam_id"]).Distinct().Count());
        Assert.All(players, player => Assert.StartsWith("ringer-", (string)player["player_name"]));

        var steamIds = players.Select(player => (string)player["steam_id"]).ToHashSet();
        Assert.All(
            sink.Events.Where(e => e.Name == "kill"),
            kill => Assert.Contains((string)kill.Data["attacker_steam_id"], steamIds)
        );
    }

    [Fact]
    public async Task AnAdminStartedMatchWaitsForForceReady()
    {
        var sink = new RecordingSink();
        MatchData match = Matches.Build(ready: "Admin");
        SimulatedServer server = Matches.Server(
            new FakeSource()
                .Then(Matches.Live(match))
                .Then(new MatchFetch(eCurrentMatchAction.Clear)),
            sink
        );

        await server.RefreshMatch();
        MapRun run = server.LastRun!;

        await WaitUntil(() => sink.Events.Count(e => e.Name == "player-connected") == 10);
        await Task.Delay(100);
        Assert.Equal(["Warmup"], sink.Statuses());

        server.Execute("force_ready");
        await Matches.Finish(run);

        Assert.Contains("Live", sink.Statuses());
    }

    [Fact]
    public async Task PlayersOnlyJoinOnceTheApiCallsTheMatchLive()
    {
        var sink = new RecordingSink();
        MatchData match = Matches.Build();
        var source = new FakeSource()
            .Then(new MatchFetch(eCurrentMatchAction.Setup, match, "WaitingForServer"))
            .Then(Matches.Live(match))
            .Then(new MatchFetch(eCurrentMatchAction.Clear));
        SimulatedServer server = Matches.Server(source, sink);

        await server.RefreshMatch();
        await Task.Delay(100);

        Assert.Equal(["Warmup"], sink.Statuses());
        Assert.DoesNotContain(sink.Events, e => e.Name == "player-connected");
        Assert.True(server.Run!.WaitingForLive);

        server.Execute("get_match");
        await Matches.Finish(server.LastRun);

        Assert.Equal(10, sink.Events.Count(e => e.Name == "player-connected"));
    }

    [Fact]
    public async Task AServerOnAnotherMapChangesLevelBeforeSettingUp()
    {
        var sink = new RecordingSink();
        MatchData match = Matches.Build(map: "de_dust2");
        var source = new FakeSource()
            .Then(Matches.Live(match))
            .Then(Matches.Live(match))
            .Then(new MatchFetch(eCurrentMatchAction.Clear));
        SimulatedServer server = Matches.Server(source, sink, startMap: "de_inferno");

        await server.RefreshMatch();

        Assert.Null(server.LastRun);
        await WaitUntil(() => server.LastRun != null);
        Assert.Equal("de_dust2", server.LoadedMap);

        await Matches.Finish(server.LastRun);
        Assert.Contains("Finished", sink.Statuses());
    }

    [Fact]
    public async Task ATiedMapGoesToOvertimeAndIsSaidToOnce()
    {
        for (int seed = 0; seed < 300; seed++)
        {
            (RecordingSink sink, MatchData match) = await PlayMap(Matches.Build(), seed);
            List<Published> scores = sink.Events.Where(e => e.Name == "score").ToList();

            if (scores.Count <= 24)
            {
                continue;
            }

            Assert.Single(sink.Statuses(), status => status == "Overtime");

            int overtimeAt = sink.Events.FindIndex(e =>
                e.Name == "mapStatus" && (string)e.Data["status"] == "Overtime"
            );
            int round24 = sink.Events.FindIndex(e => e.Name == "score" && Int(e, "round") == 24);
            Assert.True(
                overtimeAt < round24,
                "the plugin reports Overtime before round 24's score"
            );
            Assert.Equal(12, Int(scores[23], "lineup_1_score"));
            return;
        }

        Assert.Fail("no map went to overtime in 300 seeds");
    }

    [Fact]
    public async Task PausedBetweenRoundsUntilResumed()
    {
        var sink = new RecordingSink();
        MatchData match = Matches.Build();
        SimulatedServer? server = null;
        var resumed = new TaskCompletionSource();
        sink.OnPublish = published =>
        {
            if (published.Name == "score" && Convert.ToInt32(published.Data["round"]) == 3)
            {
                server!.Execute("css_pause");
                Task.Run(async () =>
                {
                    await Task.Delay(100);
                    server.Execute("css_resume");
                });
            }
        };
        server = Matches.Server(
            new FakeSource()
                .Then(Matches.Live(match))
                .Then(new MatchFetch(eCurrentMatchAction.Clear)),
            sink
        );

        await server.RefreshMatch();
        await Matches.Finish(server.LastRun);

        Assert.Equal(["Warmup", "Live", "Paused", "Live"], sink.Statuses().Take(4));
        int pausedAt = sink.Events.FindIndex(e =>
            e.Name == "mapStatus" && (string)e.Data["status"] == "Paused"
        );
        Assert.DoesNotContain(
            sink.Events.Take(pausedAt),
            e => e.Name == "kill" && Convert.ToInt32(e.Data["round"]) > 3
        );
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SmallerLineupsPlayTheSameWay(int perSide)
    {
        (RecordingSink sink, MatchData match) = await PlayMap(
            Matches.Build(perSide: perSide, ready: "Captains")
        );

        Assert.Equal(perSide * 2, sink.Events.Count(e => e.Name == "player-connected"));
        Assert.Contains("Finished", sink.Statuses());
        Assert.All(
            sink.Events.Where(e => e.Name == "kill"),
            kill => Assert.NotEqual(kill.Data["attacker_team"], kill.Data["attacked_team"])
        );
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException();
            }
            await Task.Delay(10);
        }
    }
}
