using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Simulator;
using Xunit;

// A map that comes back to a server still in play, as after a crash or a
// resumed match, and the backups the rounds carry for it.
public class ResumeTests
{
    private static MatchData LiveMap(params BackupRound[] rounds)
    {
        MatchData match = Matches.Build();
        match.lineup_2.name = "Bravo sim:1-0";
        match.match_maps[0].status = "Live";
        match.match_maps[0].rounds = rounds;
        return match;
    }

    private static BackupRound Backup(int round, int lineup1, int lineup2, bool undone = false)
    {
        return new BackupRound
        {
            round = round,
            backup_file = SimulatedBackup.Write(
                round,
                lineup1,
                lineup2,
                Roster.From(Matches.Build(), new Random(1))
            ),
            deleted_at = undone ? "2026-10-26T10:00:00Z" : "",
        };
    }

    private static async Task<(List<Published> Events, MatchData Match)> Play(
        MatchData match,
        int? restoreTo = null
    )
    {
        var sink = new RecordingSink();
        SimulatedServer server = Matches.Server(
            new FakeSource()
                .Then(Matches.Live(match))
                .Then(new MatchFetch(eCurrentMatchAction.Clear)),
            sink
        );
        sink.OnPublish = published =>
        {
            if (published.Name == "restoreRound" && restoreTo is int round)
            {
                _ = Task.Run(() => server.Execute($"api_restore_round {round}"));
            }
        };

        await server.RefreshMatch();
        await Matches.Finish(server.LastRun);

        return (sink.Events, match);
    }

    private static List<Published> Scores(List<Published> events)
    {
        return events.Where(e => e.Name == "score").ToList();
    }

    [Fact]
    public async Task EveryRoundButTheLastCarriesABackupOfItsScore()
    {
        (List<Published> events, _) = await Play(Matches.Build());
        List<Published> scores = Scores(events);

        foreach (Published score in scores.SkipLast(1))
        {
            string backup = (string)score.Data["backup_file"];
            Assert.Contains($"\"round\"\t\t\"{score.Data["round"]}\"", backup);
            Assert.Equal(
                (
                    Convert.ToInt32(score.Data["lineup_1_score"]),
                    Convert.ToInt32(score.Data["lineup_2_score"])
                ),
                SimulatedBackup.Scores(backup)
            );
        }
        Assert.Equal("", scores[^1].Data["backup_file"]);
    }

    [Fact]
    public async Task AMapStillInPlayIsRestoredToItsNewestKeptBackupOnceEveryoneIsBack()
    {
        (List<Published> events, MatchData match) = await Play(
            LiveMap(Backup(1, 1, 0), Backup(4, 3, 1), Backup(5, 4, 1, undone: true)),
            restoreTo: 4
        );

        List<string> statuses = events
            .Where(e => e.Name == "mapStatus")
            .Select(e => (string)e.Data["status"])
            .ToList();
        Assert.Equal(["Live", "Paused", "Live"], statuses.Take(3));
        Assert.DoesNotContain("Warmup", statuses);

        int vote = events.FindIndex(e => e.Name == "restoreRound");
        Assert.Equal(4, events[vote].Data["round"]);
        Assert.Equal(10, events.Take(vote).Count(e => e.Name == "player-connected"));

        List<Published> scores = Scores(events);
        Assert.Equal(5, scores[0].Data["round"]);
        Assert.Contains(
            (
                Convert.ToInt32(scores[0].Data["lineup_1_score"]),
                Convert.ToInt32(scores[0].Data["lineup_2_score"])
            ),
            new[] { (4, 1), (3, 2) }
        );
        Assert.Equal(13, Convert.ToInt32(scores[^1].Data["lineup_2_score"]));
        Assert.Equal(
            match.lineup_2_id.ToString(),
            events.Last(e => e.Name == "mapStatus").Data["winning_lineup_id"]
        );
    }

    [Fact]
    public async Task OnlyKeptBackupsAreOfferedBack()
    {
        (List<Published> events, _) = await Play(LiveMap(Backup(3, 2, 1, undone: true)));

        Assert.DoesNotContain(events, e => e.Name == "restoreRound");
        Assert.Equal("Warmup", events.First(e => e.Name == "mapStatus").Data["status"]);
    }
}
