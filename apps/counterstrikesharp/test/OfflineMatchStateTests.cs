using System.Text.Json;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// Offline there is no api: the server keeps the match's progress itself, in
// memory and in /opt/demos/<match>/, which outlives the container.
public class OfflineMatchStateTests : IDisposable
{
    private static readonly Guid MatchId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Lineup1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Lineup2 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Map1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid Map2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid Map3 = Guid.Parse("00000000-0000-0000-0000-000000000003");

    private const string MatchJson = "{\"id\":\"11111111-1111-1111-1111-111111111111\"}";

    private readonly string _demos = Directory.CreateTempSubdirectory("5stack-offline-").FullName;
    private readonly string _game = Directory.CreateTempSubdirectory("5stack-csgo-").FullName;

    public void Dispose()
    {
        Directory.Delete(_demos, true);
        Directory.Delete(_game, true);
    }

    private string Folder => OfflineMatchState.Folder(_demos, MatchId);

    private static MatchData BestOf(int bestOf, string type = "Competitive")
    {
        Guid[] ids = { Map1, Map2, Map3 };

        return new MatchData
        {
            id = MatchId,
            lineup_1_id = Lineup1,
            lineup_2_id = Lineup2,
            current_match_map_id = Map1,
            options = new MatchOptions { best_of = bestOf, mr = 12, type = type },
            match_maps = ids.Take(bestOf)
                .Select(
                    (id, index) =>
                        new MatchMap
                        {
                            id = id,
                            order = index + 1,
                            map = new Map { name = $"de_map{index + 1}" },
                            status = index == 0 ? "Warmup" : "Scheduled",
                            lineup_1_side = "CT",
                            lineup_2_side = "TERRORIST",
                        }
                )
                .ToArray(),
        };
    }

    private static MatchMap MapOf(MatchData match, Guid id)
    {
        return match.match_maps.Single(map => map.id == id);
    }

    private static void Finish(MatchData match, Guid map, Guid? winner)
    {
        OfflineMatchState.Record(MapOf(match, map), eMapStatus.WaitingForTV, winner);
        OfflineMatchState.Record(MapOf(match, map), eMapStatus.Finished, winner);
    }

    private static BackupRound Round(int round, string deletedAt = "")
    {
        return new BackupRound
        {
            round = round,
            backup_file = $"backup {round}",
            deleted_at = deletedAt,
        };
    }

    [Fact]
    public void TheMatchBeingPlayedIsKeptWhenTheServerAsksForItAgain()
    {
        string hash = OfflineMatchState.Hash(MatchJson);

        Assert.True(OfflineMatchState.KeepsLoaded(true, hash, hash));
        Assert.False(OfflineMatchState.KeepsLoaded(false, null, hash));
        Assert.False(OfflineMatchState.KeepsLoaded(true, OfflineMatchState.Hash("{}"), hash));
    }

    [Fact]
    public void TheSameMatchJsonAlwaysHashesTheSame()
    {
        Assert.Equal(OfflineMatchState.Hash(MatchJson), OfflineMatchState.Hash(MatchJson));
        Assert.NotEqual(OfflineMatchState.Hash(MatchJson), OfflineMatchState.Hash(MatchJson + " "));
    }

    [Fact]
    public void AMapKeepsItsStatusAndItsWinnerOnceItIsOver()
    {
        MatchMap map = BestOf(1).match_maps[0];

        OfflineMatchState.Record(map, eMapStatus.Live, Lineup1);

        Assert.Equal("Live", map.status);
        Assert.Null(map.winning_lineup_id);

        OfflineMatchState.Record(map, eMapStatus.WaitingForTV, Lineup1);
        OfflineMatchState.Record(map, eMapStatus.Finished, null);

        Assert.Equal("Finished", map.status);
        Assert.Equal(Lineup1, map.winning_lineup_id);
    }

    [Fact]
    public void ASurrenderStandsWhileTheMapWindsDown()
    {
        MatchMap map = BestOf(1).match_maps[0];

        OfflineMatchState.Record(map, eMapStatus.Surrendered, Lineup2);
        OfflineMatchState.Record(map, eMapStatus.WaitingForTV, Lineup1);
        OfflineMatchState.Record(map, eMapStatus.Finished, Lineup1);

        Assert.Equal("Surrendered", map.status);
        Assert.Equal(Lineup2, map.winning_lineup_id);
    }

    [Fact]
    public void ALevelScoreWinsNobodyTheMap()
    {
        MatchData match = BestOf(1);

        Assert.Equal(Lineup1, OfflineMatchState.MapWinner(match, 13, 7));
        Assert.Equal(Lineup2, OfflineMatchState.MapWinner(match, 11, 13));
        Assert.Null(OfflineMatchState.MapWinner(match, 12, 12));
    }

    [Fact]
    public void ABestOfThreeMovesOnToItsSecondMap()
    {
        MatchData match = BestOf(3);
        Finish(match, Map1, Lineup1);

        MatchMap? next = OfflineMatchState.Advance(match, MapOf(match, Map1));

        Assert.Equal(Map2, next?.id);
        Assert.Equal(Map2, match.current_match_map_id);
        Assert.Equal("Warmup", MapOf(match, Map2).status);
        Assert.Null(OfflineMatchState.Result(match));
    }

    [Fact]
    public void ABestOfThreeIsOverOnceALineupHasWonTwoMaps()
    {
        MatchData match = BestOf(3);
        Finish(match, Map1, Lineup2);
        match.current_match_map_id = Map2;
        Finish(match, Map2, Lineup2);

        Assert.Null(OfflineMatchState.Advance(match, MapOf(match, Map2)));
        Assert.Equal(Map2, match.current_match_map_id);
        Assert.Equal("Scheduled", MapOf(match, Map3).status);
        Assert.Equal(
            new OfflineMatchResult(eMapStatus.Finished, Lineup2),
            OfflineMatchState.Result(match)
        );
    }

    [Fact]
    public void ABestOfThreeAtOneMapEachPlaysTheDecider()
    {
        MatchData match = BestOf(3);
        Finish(match, Map1, Lineup1);
        Finish(match, Map2, Lineup2);

        Assert.Equal(Map3, OfflineMatchState.NextMap(match, MapOf(match, Map2))?.id);
    }

    [Fact]
    public void ASurrenderEndsTheWholeSeriesAsTheApiDoes()
    {
        MatchData match = BestOf(3);
        OfflineMatchState.Record(MapOf(match, Map1), eMapStatus.Surrendered, Lineup2);

        Assert.Null(OfflineMatchState.NextMap(match, MapOf(match, Map1)));
        Assert.Equal(
            new OfflineMatchResult(eMapStatus.Surrendered, Lineup2),
            OfflineMatchState.Result(match)
        );
        Assert.Equal("Surrendered", OfflineMatchState.Capture(match).match_status);
    }

    [Fact]
    public void AMapStillBeingPlayedDecidesNothing()
    {
        MatchData match = BestOf(1);
        OfflineMatchState.Record(MapOf(match, Map1), eMapStatus.Live, null);

        Assert.False(OfflineMatchState.IsMatchOver(match));
    }

    [Fact]
    public void LevelMapsWithNothingLeftToPlayAreATie()
    {
        MatchData match = BestOf(1);
        Finish(match, Map1, null);

        Assert.Equal(new OfflineMatchResult(eMapStatus.Finished, null), OfflineMatchState.Result(match));
        Assert.Equal("Tie", OfflineMatchState.Capture(match).match_status);
    }

    [Fact]
    public void WhatWasPlayedSurvivesARestart()
    {
        MatchData played = BestOf(3);
        Finish(played, Map1, Lineup1);
        OfflineMatchState.Advance(played, MapOf(played, Map1));
        MapOf(played, Map2).lineup_1_side = "TERRORIST";
        MapOf(played, Map2).lineup_2_side = "CT";
        OfflineMatchState.Record(MapOf(played, Map2), eMapStatus.Live, null);

        OfflineMatchState.Save(Folder, OfflineMatchState.Capture(played));

        MatchData restarted = BestOf(3);
        OfflineMatchProgress? progress = OfflineMatchState.Load(Folder);

        Assert.NotNull(progress);
        Assert.True(OfflineMatchState.Restore(restarted, progress!));
        Assert.Equal(Map2, restarted.current_match_map_id);
        Assert.Equal("Live", MapOf(restarted, Map2).status);
        Assert.Equal("TERRORIST", MapOf(restarted, Map2).lineup_1_side);
        Assert.Equal("CT", MapOf(restarted, Map2).lineup_2_side);
        Assert.Equal("Finished", MapOf(restarted, Map1).status);
        Assert.Equal(Lineup1, MapOf(restarted, Map1).winning_lineup_id);
    }

    [Fact]
    public void AnEditThatKeepsTheMapsKeepsTheSeries()
    {
        MatchData played = BestOf(3);
        Finish(played, Map1, Lineup1);
        OfflineMatchState.Advance(played, MapOf(played, Map1));

        MatchData edited = BestOf(3);
        edited.lineup_1 = new MatchLineUp { name = "Renamed" };
        MapOf(edited, Map1).lineup_1_side = "TERRORIST";
        MapOf(edited, Map1).lineup_2_side = "CT";
        MapOf(edited, Map3).lineup_1_side = "TERRORIST";
        MapOf(edited, Map3).lineup_2_side = "CT";

        Assert.True(OfflineMatchState.Restore(edited, OfflineMatchState.Capture(played)));
        Assert.Equal(Map2, edited.current_match_map_id);
        Assert.Equal("Finished", MapOf(edited, Map1).status);
        Assert.Equal(Lineup1, MapOf(edited, Map1).winning_lineup_id);
        Assert.Equal("CT", MapOf(edited, Map1).lineup_1_side);
        Assert.Equal("TERRORIST", MapOf(edited, Map3).lineup_1_side);
        Assert.Equal("CT", MapOf(edited, Map3).lineup_2_side);
    }

    [Fact]
    public void ChangingTheMapsStartsTheSeriesOver()
    {
        MatchData played = BestOf(3);
        Finish(played, Map1, Lineup1);
        OfflineMatchState.Advance(played, MapOf(played, Map1));
        OfflineMatchProgress progress = OfflineMatchState.Capture(played);

        MatchData renamed = BestOf(3);
        MapOf(renamed, Map2).map = new Map { name = "de_other" };
        MatchData shorter = BestOf(1);
        MatchData swapped = BestOf(3);
        swapped.lineup_1_id = Lineup2;
        swapped.lineup_2_id = Lineup1;

        Assert.False(OfflineMatchState.Restore(renamed, progress));
        Assert.Equal(Map1, renamed.current_match_map_id);
        Assert.Equal("Warmup", MapOf(renamed, Map1).status);
        Assert.False(OfflineMatchState.Restore(shorter, progress));
        Assert.False(OfflineMatchState.Restore(swapped, progress));
        Assert.Null(MapOf(swapped, Map1).winning_lineup_id);
    }

    [Fact]
    public void AMapCutShortAtItsEndIsFinishedAndTheSeriesMovesOn()
    {
        MatchData played = BestOf(3);
        OfflineMatchState.Record(MapOf(played, Map1), eMapStatus.WaitingForTV, Lineup1);

        MatchData restarted = BestOf(3);

        Assert.True(
            OfflineMatchState.Restore(restarted, OfflineMatchState.Capture(played))
        );
        Assert.Equal("Finished", MapOf(restarted, Map1).status);
        Assert.Equal(Map2, restarted.current_match_map_id);
        Assert.Equal("Warmup", MapOf(restarted, Map2).status);
    }

    [Fact]
    public void AMatchThatWasOverStaysOverOnItsLastMap()
    {
        MatchData played = BestOf(3);
        Finish(played, Map1, Lineup1);
        OfflineMatchState.Advance(played, MapOf(played, Map1));
        Finish(played, Map2, Lineup1);

        MatchData restarted = BestOf(3);
        OfflineMatchState.Restore(restarted, OfflineMatchState.Capture(played));

        Assert.Equal(Map2, restarted.current_match_map_id);
        Assert.True(OfflineMatchState.IsMatchOver(restarted));
        Assert.Equal("Scheduled", MapOf(restarted, Map3).status);
    }

    [Fact]
    public void ASaveFromASurrenderedMapStillRestoresAsOver()
    {
        MatchData played = BestOf(3);
        OfflineMatchState.Record(MapOf(played, Map1), eMapStatus.Surrendered, Lineup2);

        MatchData restarted = BestOf(3);

        Assert.True(
            OfflineMatchState.Restore(restarted, OfflineMatchState.Capture(played))
        );
        Assert.Equal(Map1, restarted.current_match_map_id);
        Assert.Equal("Surrendered", MapOf(restarted, Map1).status);
        Assert.Equal(
            new OfflineMatchResult(eMapStatus.Surrendered, Lineup2),
            OfflineMatchState.Result(restarted)
        );
        Assert.True(OfflineMatchState.IsMatchOver(restarted));
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"maps\":null}")]
    [InlineData("{\"maps\":[{\"rounds\":null}]}")]
    [InlineData("{\"maps\":[{\"rounds\":[null]}]}")]
    [InlineData("null")]
    public void AnUnreadableStateFileIsSetAsideWithItsBackups(string contents)
    {
        Directory.CreateDirectory(Folder);
        string backup = OfflineMatchState.BackupFileName(MatchId, Map1, 4);
        File.WriteAllText(Path.Join(Folder, OfflineMatchState.FileName), contents);
        File.WriteAllText(Path.Join(Folder, backup), "backup 4");

        Assert.Throws<JsonException>(() => OfflineMatchState.Load(Folder));
        Assert.Null(OfflineMatchState.Load(Path.Join(_demos, "missing")));

        string aside = OfflineMatchState.SetAside(Folder, MatchId);

        Assert.Equal(Path.Join(Folder, OfflineMatchState.Unreadable), Path.GetDirectoryName(aside));
        Assert.Empty(Directory.GetFiles(Folder));
        Assert.Equal(contents, File.ReadAllText(Path.Join(aside, OfflineMatchState.FileName)));
        Assert.Equal("backup 4", File.ReadAllText(Path.Join(aside, backup)));
    }

    [Fact]
    public void TheBackupFileNamesAreTheOnesTheGameWrites()
    {
        MatchData match = BestOf(3);
        match.current_match_map_id = Map2;

        Assert.Equal(
            $"{MatchUtility.GetSafeMatchPrefix(match)}_round07.txt",
            OfflineMatchState.BackupFileName(MatchId, Map2, 7)
        );
    }

    [Fact]
    public void RoundBackupsAreKeptBesideTheDemos()
    {
        string prefix = OfflineMatchState.BackupPrefix(MatchId, Map1);
        File.WriteAllText(Path.Join(_game, OfflineMatchState.BackupFileName(MatchId, Map1, 3)), "3");
        File.WriteAllText(Path.Join(_game, OfflineMatchState.BackupFileName(MatchId, Map2, 1)), "x");
        File.WriteAllText(Path.Join(_game, $"restore-{prefix}round03.txt"), "load");

        Assert.Equal(1, OfflineMatchState.SaveBackups(_game, Folder, prefix));
        Assert.Equal(0, OfflineMatchState.SaveBackups(_game, Folder, prefix));
        Assert.Equal(
            new[] { OfflineMatchState.BackupFileName(MatchId, Map1, 3) },
            Directory.GetFiles(Folder).Select(Path.GetFileName).ToArray()
        );
    }

    [Fact]
    public void ARestartedServerGetsItsBackupsBackWithoutLosingNewerOnes()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Join(Folder, OfflineMatchState.BackupFileName(MatchId, Map1, 4)), "kept 4");
        File.WriteAllText(Path.Join(Folder, OfflineMatchState.BackupFileName(MatchId, Map1, 5)), "kept 5");
        File.WriteAllText(Path.Join(_game, OfflineMatchState.BackupFileName(MatchId, Map1, 5)), "newer 5");

        Assert.Equal(1, OfflineMatchState.RestoreBackups(Folder, _game, MatchId));
        Assert.Equal(
            "kept 4",
            File.ReadAllText(Path.Join(_game, OfflineMatchState.BackupFileName(MatchId, Map1, 4)))
        );
        Assert.Equal(
            "newer 5",
            File.ReadAllText(Path.Join(_game, OfflineMatchState.BackupFileName(MatchId, Map1, 5)))
        );
    }

    [Fact]
    public void ARestartedServerOffersItsNewestKeptRound()
    {
        MatchData played = BestOf(1);
        OfflineMatchState.Record(MapOf(played, Map1), eMapStatus.Live, null);
        MapOf(played, Map1).rounds = new[] { Round(1), Round(2), Round(3, "2026-11-05T10:00:00Z") };

        foreach (BackupRound backup in MapOf(played, Map1).rounds)
        {
            File.WriteAllText(
                Path.Join(_game, OfflineMatchState.BackupFileName(MatchId, Map1, backup.round)),
                backup.backup_file
            );
        }

        OfflineMatchState.Save(Folder, OfflineMatchState.Capture(played));
        OfflineMatchState.SaveBackups(_game, Folder, OfflineMatchState.BackupPrefix(MatchId, Map1));

        MatchData restarted = BestOf(1);
        OfflineMatchProgress progress = OfflineMatchState.Load(Folder)!;
        OfflineMatchState.Restore(restarted, progress);
        OfflineMatchState.LoadBackups(restarted, progress, Folder);

        BackupRound[] rounds = MapOf(restarted, Map1).rounds;

        Assert.Equal(new[] { 1, 2, 3 }, rounds.Select(backup => backup.round).ToArray());
        Assert.Equal("backup 2", RoundRestoreGateUtility.LatestBackup(rounds, 2)?.backup_file);
        Assert.Equal(2, RoundRestoreGateUtility.HighestRestorableRound(rounds, 0));
        Assert.False(RoundRestoreGateUtility.IsUsableOnDisk(rounds, 3, true));
    }

    [Fact]
    public void AMatchWithNewMapsForgetsItsOldBackupsButNotItsDemos()
    {
        string demos = Path.Join(Folder, Map1.ToString());
        Directory.CreateDirectory(demos);
        File.WriteAllText(Path.Join(demos, "map1.dem"), "demo");
        File.WriteAllText(Path.Join(Folder, OfflineMatchState.BackupFileName(MatchId, Map1, 9)), "9");
        OfflineMatchState.Save(Folder, OfflineMatchState.Capture(BestOf(1)));

        OfflineMatchState.Forget(Folder, MatchId);

        Assert.Empty(Directory.GetFiles(Folder));
        Assert.True(File.Exists(Path.Join(demos, "map1.dem")));
    }

    [Fact]
    public void AMapThatIsOverNeverGoesBackIntoPlay()
    {
        MatchData match = BestOf(1);
        Finish(match, Map1, Lineup1);

        OfflineMatchState.Record(MapOf(match, Map1), eMapStatus.Warmup, null);
        OfflineMatchState.Record(MapOf(match, Map1), eMapStatus.Live, Lineup2);

        Assert.Equal("Finished", MapOf(match, Map1).status);
        Assert.Equal(Lineup1, MapOf(match, Map1).winning_lineup_id);
        Assert.True(OfflineMatchState.IsMatchOver(match));

        MatchMap waiting = BestOf(1).match_maps[0];
        OfflineMatchState.Record(waiting, eMapStatus.WaitingForTV, Lineup1);
        OfflineMatchState.Record(waiting, eMapStatus.Paused, null);

        Assert.Equal("WaitingForTV", waiting.status);
    }

    [Fact]
    public void ARestoreUndoesTheRoundsAfterItAsTheApiDoes()
    {
        MatchMap map = BestOf(1).match_maps[0];
        map.status = "Live";
        map.rounds = new[] { Round(1), Round(2, "earlier"), Round(2), Round(3), Round(4) };

        Assert.Null(OfflineMatchState.ResetToRound(map, false, 2, "now"));
        Assert.Equal(
            new[] { "", "", "", "now", "now" },
            map.rounds.Select(backup => backup.deleted_at).ToArray()
        );
        Assert.Equal("backup 3", map.rounds[3].backup_file);
    }

    [Fact]
    public void ARoundUndoneByARestoreCanOnlyBeRestoredOncePlayedAgain()
    {
        MatchMap map = BestOf(1).match_maps[0];
        map.status = "Live";
        map.rounds = new[] { Round(1), Round(2), Round(3), Round(4) };

        Assert.Null(OfflineMatchState.ResetToRound(map, false, 1, "now"));
        Assert.Equal(
            "round 4 was undone by an earlier restore",
            OfflineMatchState.ResetToRound(map, false, 4, "later")
        );

        map.rounds = map.rounds.Append(Round(2)).ToArray();

        Assert.Null(OfflineMatchState.ResetToRound(map, false, 2, "later"));
    }

    [Fact]
    public void ARestoreIsRefusedWhereTheApiWouldRefuseIt()
    {
        MatchMap map = BestOf(1).match_maps[0];
        map.rounds = new[] { Round(1), Round(2) };

        map.status = "Warmup";
        Assert.Equal("the map is Warmup, not in play", OfflineMatchState.ResetToRound(map, false, 1, "now"));

        map.status = "Paused";
        Assert.Equal("Rush matches only reset to round 0", OfflineMatchState.ResetToRound(map, true, 1, "now"));
        Assert.Equal(
            "there is no round 5 on this map to go back to",
            OfflineMatchState.ResetToRound(map, false, 5, "now")
        );
        Assert.All(map.rounds, backup => Assert.Equal("", backup.deleted_at));

        Assert.Null(OfflineMatchState.ResetToRound(map, true, 0, "now"));
        Assert.All(map.rounds, backup => Assert.Equal("now", backup.deleted_at));
    }
}
