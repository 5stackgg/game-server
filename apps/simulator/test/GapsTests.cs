using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Simulator;
using Xunit;

// Timeouts, chat, coaches, cameras, demos, a 3v3 Competitive and Rush, played
// the way the plugin plays them.
public class GapsTests
{
    private sealed class RecordingDemos : IDemoUploader
    {
        public List<(Guid Match, Guid Map)> Uploads { get; } = new();

        public Task Upload(Guid matchId, Guid mapId, CancellationToken token)
        {
            Uploads.Add((matchId, mapId));
            return Task.CompletedTask;
        }
    }

    private static async Task<(List<Published> Events, SimulatedServer Server)> Play(
        MatchData match,
        Action<SimulatedServer, Published>? during = null,
        bool onGameNode = true,
        IDemoUploader? demos = null
    )
    {
        var sink = new RecordingSink();
        SimulatedServer server = Matches.Server(
            new FakeSource()
                .Then(Matches.Live(match))
                .Then(new MatchFetch(eCurrentMatchAction.Clear)),
            sink,
            onGameNode: onGameNode
        );
        server.Demos = demos;
        sink.OnPublish = published => during?.Invoke(server, published);

        await server.RefreshMatch();
        await Matches.Finish(server.LastRun);

        return (sink.Events, server);
    }

    private static List<string> Statuses(List<Published> events) =>
        events.Where(e => e.Name == "mapStatus").Select(e => (string)e.Data["status"]).ToList();

    [Fact]
    public async Task ATacticalTimeoutSpendsOneOfTheTeamsTimeoutsAndATechnicalPauseIsResumed()
    {
        MatchData match = Matches.Build();
        match.lineup_2.name = "Bravo sim:timeouts";

        (List<Published> events, _) = await Play(match);

        List<Published> timeouts = events.Where(e => e.Name == "techTimeout").ToList();
        Assert.Equal(2, timeouts.Count);
        Assert.Equal(3, timeouts[1].Data["lineup_1_timeouts_available"]);
        Assert.Equal(2, timeouts[1].Data["lineup_2_timeouts_available"]);

        Published last = events.Last(e => e.Name == "score");
        Assert.Equal("3", last.Data["lineup_1_timeouts_available"]);
        Assert.Equal("2", last.Data["lineup_2_timeouts_available"]);

        Assert.Equal(1, Statuses(events).Count(status => status == "Paused"));
        Assert.Equal("Live", Statuses(events)[Statuses(events).IndexOf("Paused") + 1]);
    }

    [Fact]
    public async Task PlayersChatToTheMatchAndToTheirOwnLineup()
    {
        MatchData match = Matches.Build();
        match.lineup_2.name = "Bravo sim:chat";
        match.relay_team_chat = true;

        (List<Published> events, _) = await Play(match);

        string subject = match.lineup_2.lineup_players[0].steam_id!;
        Published hello = Assert.Single(
            events,
            e => e.Name == "chat" && (string)e.Data["message"] == "glhf"
        );
        Assert.Equal(subject, hello.Data["player"]);

        Published team = Assert.Single(events, e => e.Name == "teamChat");
        Assert.Equal(subject, team.Data["player"]);
        Assert.Equal(match.lineup_2_id.ToString(), team.Data["lineupId"]);

        Assert.Contains(events, e => e.Name == "chat" && (string)e.Data["message"] == "gg");
    }

    [Fact]
    public async Task TeamChatStaysOnTheServerWhenTheApiDoesNotTakeIt()
    {
        MatchData match = Matches.Build();
        match.lineup_2.name = "Bravo sim:chat";
        match.relay_team_chat = false;

        (List<Published> events, _) = await Play(match);

        Assert.DoesNotContain(events, e => e.Name == "teamChat");
    }

    [Fact]
    public async Task ACoachConnectsButNeitherReadiesNorPlays()
    {
        const string coach = "76561198000999999";
        MatchData match = Matches.Build();
        match.options.coaches = true;
        match.lineup_2.coach_steam_id = coach;

        (List<Published> events, SimulatedServer server) = await Play(match);

        Assert.Single(
            events,
            e => e.Name == "player-connected" && (string)e.Data["steam_id"] == coach
        );
        Assert.Equal(10, server.LastRun!.ConnectedPlayers);
        Assert.DoesNotContain(
            events,
            e =>
                e.Data.Values.OfType<string>().Contains(coach)
                && e.Name is "kill" or "damage" or "assist" or "flash" or "utility"
        );
    }

    [Fact]
    public async Task APlayerWithoutACameraHoldsTheReadyUpAndPausesPlayUntilItIsBack()
    {
        MatchData match = Matches.Build();
        match.options.camera_required = true;
        string player = match.lineup_1.lineup_players[0].steam_id!;
        int scores = 0;

        (List<Published> events, _) = await Play(
            match,
            (server, published) =>
            {
                if (
                    published.Name == "player-connected"
                    && (string)published.Data["steam_id"] == player
                )
                {
                    server.Execute($"camera_state {player}");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        server.Execute("camera_state");
                    });
                }

                if (published.Name == "score" && ++scores == 3)
                {
                    server.Execute($"camera_state {player}");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        server.Execute("camera_state");
                    });
                }
            }
        );

        Assert.Equal(1, Statuses(events).Count(status => status == "Paused"));
        Assert.Equal("Finished", Statuses(events)[^1]);
    }

    [Fact]
    public async Task AServerOffAGameNodeUploadsADemoForEveryMapItPlays()
    {
        MatchData match = Matches.Build();
        var demos = new RecordingDemos();

        await Play(match, onGameNode: false, demos: demos);

        Assert.Equal([(match.id, match.match_maps[0].id)], demos.Uploads);
    }

    [Fact]
    public async Task AGameNodeServerUploadsNothing()
    {
        var demos = new RecordingDemos();

        await Play(Matches.Build(), onGameNode: true, demos: demos);

        Assert.Empty(demos.Uploads);
    }

    [Theory]
    [InlineData("Players")]
    [InlineData("Captains")]
    public async Task A3v3CompetitiveReadiesItselfAndPlaysWithoutPausing(string ready)
    {
        (List<Published> events, SimulatedServer server) = await Play(
            Matches.Build(perSide: 3, ready: ready)
        );

        Assert.False(server.LastRun!.WaitingForReady);
        Assert.DoesNotContain("Paused", Statuses(events));
        Assert.Equal("Finished", Statuses(events)[^1]);
    }

    [Fact]
    public async Task A3v3CompetitiveTheAdminStartsWaitsForForceReady()
    {
        MatchData match = Matches.Build(perSide: 3, ready: "Admin");
        var sink = new RecordingSink();
        SimulatedServer server = Matches.Server(
            new FakeSource()
                .Then(Matches.Live(match))
                .Then(new MatchFetch(eCurrentMatchAction.Clear)),
            sink
        );

        await server.RefreshMatch();
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (server.LastRun?.WaitingForReady != true && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }
        Assert.True(server.LastRun!.WaitingForReady);

        server.Execute("force_ready");
        await Matches.Finish(server.LastRun);

        Assert.Equal("Finished", Statuses(sink.Events)[^1]);
    }

    [Fact]
    public async Task ARushMapARestartedServerComesBackToIsPlayedAgainFromWarmup()
    {
        MatchData match = Matches.Build(perSide: 3);
        match.options.type = "Rush";
        match.match_maps[0].status = "Live";
        match.match_maps[0].rounds =
        [
            new BackupRound
            {
                round = 4,
                backup_file = SimulatedBackup.Write(4, 3, 1, Roster.From(match, new Random(1))),
            },
        ];
        string? refusal = null;

        (List<Published> events, _) = await Play(
            match,
            (server, published) =>
            {
                if (published.Name == "restoreRound")
                {
                    refusal = server.Execute("restore_round 4");
                }
            }
        );

        Published restore = Assert.Single(events, e => e.Name == "restoreRound");
        Assert.Equal(0, restore.Data["round"]);
        Assert.Equal("Warmup", Statuses(events)[0]);
        Assert.Equal(1, events.First(e => e.Name == "score").Data["round"]);
        Assert.Equal(
            $"{SimulatedServer.RestoreRefused} round restores are disabled in Rush",
            refusal
        );
    }

    [Fact]
    public async Task ThePanelsBarePauseAndResumeReachTheMatch()
    {
        int scores = 0;

        (List<Published> events, _) = await Play(
            Matches.Build(),
            (server, published) =>
            {
                if (published.Name == "score" && ++scores == 2)
                {
                    server.Execute("pause");
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(50);
                        server.Execute("resume");
                    });
                }
            }
        );

        Assert.Contains("Paused", Statuses(events));
        Assert.Equal("Finished", Statuses(events)[^1]);
    }
}
