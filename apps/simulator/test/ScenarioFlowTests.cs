using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Simulator;
using Xunit;

// The plugin's own paths for a surrender vote, a lineup that never turns up,
// and a player who leaves mid-map.
public class ScenarioFlowTests
{
    private static async Task<(RecordingSink Sink, MatchData Match, SimulatedServer Server)> Play(
        string scenario,
        bool waitForEnd = true
    )
    {
        MatchData match = Matches.Build();
        match.lineup_2.name = $"Bravo sim:{scenario}";
        var sink = new RecordingSink();
        SimulatedServer server = Matches.Server(
            new FakeSource()
                .Then(Matches.Live(match))
                .Then(new MatchFetch(eCurrentMatchAction.Clear)),
            sink
        );

        await server.RefreshMatch();
        if (waitForEnd)
        {
            await Matches.Finish(server.LastRun);
        }

        return (sink, match, server);
    }

    private static int IndexOf(List<Published> events, string name, string? status = null)
    {
        return events.FindIndex(e =>
            e.Name == name && (status == null || (string)e.Data["status"] == status)
        );
    }

    private static HashSet<string> SteamIds(MatchLineUp lineup)
    {
        return lineup.lineup_players.Select(member => member.steam_id!).ToHashSet();
    }

    [Fact]
    public async Task ASurrenderHandsTheMapToTheOtherLineupAndTheGameEndsWithoutAnyMoreStats()
    {
        (RecordingSink sink, MatchData match, _) = await Play("surrender");
        List<Published> events = sink.Events;
        int surrendered = IndexOf(events, "mapStatus", "Surrendered");

        Assert.Equal(
            ["Warmup", "Live", "Surrendered", "WaitingForTV", "Finished"],
            sink.Statuses()
        );
        Assert.Equal(match.lineup_1_id.ToString(), events[surrendered].Data["winning_lineup_id"]);

        // the surrendering lineup leaves, and nothing is in play any more
        Assert.Equal(
            SteamIds(match.lineup_2),
            events
                .Skip(surrendered)
                .Where(e => e.Name == "player-disconnected")
                .Select(e => (string)e.Data["steam_id"])
                .ToHashSet()
        );
        Assert.DoesNotContain(
            events.Skip(surrendered),
            e => e.Name is "kill" or "damage" or "utility" or "flash" or "assist" or "objective"
        );

        // the rounds it gave up still count when the game ends
        Published last = events.Last(e => e.Name == "score");
        Assert.True(events.IndexOf(last) > surrendered);
        Assert.Equal(13, Convert.ToInt32(last.Data["lineup_1_score"]));

        // GameEnd.cs moves the map to WaitingForTV before HandleEndOfMap asks
        // whether it was surrendered, so the plugin never sends `surrender`
        Assert.DoesNotContain(events, e => e.Name == "surrender");
    }

    [Fact]
    public async Task ALineupThatNeverTurnsUpLeavesTheMapInWarmup()
    {
        (RecordingSink sink, MatchData match, SimulatedServer server) = await Play(
            "noshow",
            waitForEnd: false
        );

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (
            sink.Events.Count(e => e.Name == "player-connected") < 5 && DateTime.UtcNow < deadline
        )
        {
            await Task.Delay(10);
        }
        await Task.Delay(200);

        Assert.Equal(
            SteamIds(match.lineup_1),
            sink.Events.Where(e => e.Name == "player-connected")
                .Select(e => (string)e.Data["steam_id"])
                .ToHashSet()
        );
        Assert.Equal(["Warmup"], sink.Statuses());
        Assert.True(server.LastRun!.WaitingForReady);
    }

    [Fact]
    public async Task APlayerWhoDropsPausesTheNextRoundUntilTheyAreBack()
    {
        (RecordingSink sink, MatchData match, _) = await Play("disconnect");
        List<Published> events = sink.Events;
        int dropped = IndexOf(events, "player-disconnected");
        string leaver = (string)events[dropped].Data["steam_id"];
        int paused = IndexOf(events, "mapStatus", "Paused");
        int back = events.FindIndex(dropped, e => e.Name == "player-connected");
        int resumed = events.FindIndex(
            paused,
            e => e.Name == "mapStatus" && (string)e.Data["status"] == "Live"
        );

        Assert.Contains(leaver, SteamIds(match.lineup_2));
        Assert.True(dropped < paused && paused < back && back < resumed);
        Assert.Equal(leaver, events[back].Data["steam_id"]);

        // the round they left in has its score sent before the pause
        Assert.Contains(events.Take(paused), e => e.Name == "score");

        // nobody else's stats stop, and theirs carry on after they are back
        Assert.Contains(
            events.Skip(resumed),
            e => e.Name is "damage" or "kill" && (string)e.Data["attacker_steam_id"] == leaver
        );
        Assert.Contains("Finished", sink.Statuses());
    }

    [Fact]
    public async Task APlayerWhoNeverComesBackIsReportedAbandonedAndTheirTeamGivesUp()
    {
        (RecordingSink sink, MatchData match, _) = await Play("abandon");
        List<Published> events = sink.Events;
        int dropped = IndexOf(events, "player-disconnected");
        string leaver = (string)events[dropped].Data["steam_id"];
        int abandoned = IndexOf(events, "abandoned");
        int surrendered = IndexOf(events, "mapStatus", "Surrendered");

        Assert.True(dropped < abandoned && abandoned < surrendered);
        Assert.Equal(leaver, events[abandoned].Data["steam_id"]);
        Assert.Single(events, e => e.Name == "abandoned");
        Assert.DoesNotContain(
            events,
            e =>
                e.Name == "player-connected"
                && (string)e.Data["steam_id"] == leaver
                && events.IndexOf(e) > dropped
        );
        Assert.Equal(match.lineup_1_id.ToString(), events[surrendered].Data["winning_lineup_id"]);

        // still paused for the missing player, so CS2 never reaches the end of the game
        Assert.Equal(["Warmup", "Live", "Paused", "Surrendered"], sink.Statuses());
    }
}
