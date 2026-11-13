using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// What offline mode keeps and decides is tested in OfflineMatchStateTests.
// Calling it needs the engine, so these read the plugin. Every one of these
// paths has to stay behind IsOfflineMode: online matches pull the same image.
public class OfflineWiringTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Code(params string[] path)
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(new[] { SourceDir() }.Concat(path).ToArray())),
            @"//[^\n]*",
            ""
        );
    }

    private static string Service(string name)
    {
        return Code("FiveStack.Services", $"{name}.cs");
    }

    private static string Events(string name)
    {
        return Code("FiveStack.Events", $"{name}.cs");
    }

    private static string Method(string source, string signature)
    {
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    private static int At(string source, string text)
    {
        int index = source.IndexOf(text, StringComparison.Ordinal);

        Assert.True(index >= 0, $"{text} is gone");

        return index;
    }

    [Fact]
    public void AnOfflineServerTellsTheOfflineToolItKeepsItsProgress()
    {
        string report = Method(Code("FiveStack.Commands", "Status.cs"), "private string BuildStatusReport()");
        int line = At(report, "lines.Add(\"Offline Progress: kept\");");

        Assert.Matches(@"if \(_environmentService\.IsOfflineMode\(\)\)\s*\{\s*$", report[..line]);
    }

    [Fact]
    public void AnOfflineServerKeepsTheMatchItIsPlayingInsteadOfReadingItAgain()
    {
        string offline = Method(Service("MatchService"), "public void GetMatchFromOffline()");
        int kept = At(offline, "OfflineMatchState.KeepsLoaded(loaded != null, _offlineSourceHash, sourceHash)");
        string keep = offline[kept..At(offline, "JsonSerializer.Deserialize<MatchData>")];

        Assert.Contains("_currentMatch!.SetupMatch(loaded);", keep);
        Assert.Contains("return;", keep);
    }

    [Fact]
    public void AnOfflineServerPicksUpWhereItLeftOffBeforeSettingTheMatchUp()
    {
        string offline = Method(Service("MatchService"), "public void GetMatchFromOffline()");
        int restored = At(offline, "RestoreOfflineProgress(matchData);");

        Assert.True(restored < At(offline, ".SetupMatch(matchData)"));
        Assert.True(restored < At(offline, "_offlineSourceHash = sourceHash;"));

        string restore = Method(Service("MatchService"), "private void RestoreOfflineProgress(");

        Assert.Contains("OfflineMatchState.Restore(matchData, progress)", restore);
        Assert.Contains("OfflineMatchState.Forget(folder, matchData.id);", restore);

        string unreadable = restore[
            At(restore, "catch (JsonException ex)")..At(restore, "OfflineMatchState.Restore(")
        ];

        Assert.Contains("OfflineMatchState.SetAside(folder, matchData.id);", unreadable);
        Assert.DoesNotContain("Forget", unreadable);
        Assert.Contains("OfflineMatchState.LoadBackups(matchData, progress, folder);", restore);
        Assert.Contains("OfflineMatchState.RestoreBackups(folder, BackupDirectory, matchData.id)", restore);
    }

    [Fact]
    public void SavingTheMatchDoesNothingOnline()
    {
        string save = Method(Service("MatchService"), "public void SaveOfflineProgress()");
        string guard = save[..At(save, "return;")];

        Assert.Contains("!_environmentService.IsOfflineMode()", guard);
    }

    [Fact]
    public void EveryStatusChangeIsKeptOffline()
    {
        string update = Method(Service("MatchManager"), "public void UpdateMapStatus(");
        string after = update[At(update, "_currentMapStatus = status;")..];

        Assert.Matches(
            @"if \(_environmentService\.IsOfflineMode\(\) && currentMap != null\)\s*\{\s*OfflineMatchState\.Record\(currentMap, status, winningLineupId\);\s*_matchService\.SaveOfflineProgress\(\);\s*\}",
            after
        );
    }

    [Fact]
    public void EveryRoundIsKeptOnceItsBackupHasBeenRead()
    {
        string start = Method(Events("RoundStart"), "public HookResult OnRoundStart(");

        Assert.True(
            At(start, "PublishPendingRound(SendBackupRound: true);")
                < At(start, "_matchService.SaveOfflineProgress();")
        );
    }

    [Fact]
    public void AnOfflineRestoreLoadsHereInsteadOfWaitingOnTheApi()
    {
        string send = Method(Service("GameBackUpRounds"), "public void SendRestoreRoundToBackend(");
        int offline = At(send, "if (_environmentService.IsOfflineMode())");

        Assert.True(offline < At(send, "RestoreRoundOffline(round);"));
        Assert.True(At(send, "RestoreRoundOffline(round);") < At(send, "return;"));
        Assert.True(At(send, "return;") < At(send, "_matchEvents.PublishGameEvent("));

        string local = Method(Service("GameBackUpRounds"), "private void RestoreRoundOffline(int round)");

        Assert.True(At(local, "OfflineMatchState.ResetToRound(") < At(local, "RestoreRound(round);"));
        Assert.Contains("match.options.IsRush()", local);
        Assert.Contains("_matchService.SaveOfflineProgress();", local);
    }

    [Fact]
    public void AnOfflineMatchHasNoSurrenderToEnd()
    {
        string surrenders = Service("SurrenderSystem");

        Assert.DoesNotMatch(@"EndGame\w*\(", surrenders);
        Assert.DoesNotContain("TerminateRound(", surrenders);
        Assert.DoesNotContain("RoundEndReason", surrenders);
        Assert.DoesNotContain(
            "isSurrendered()",
            Method(Events("RoundStart"), "public HookResult OnRoundStart(")
        );
    }

    [Fact]
    public void AnOfflineMapIsOnlyConcludedOnce()
    {
        string events = Events("GameEnd");

        Assert.Contains("ConcludeMap();", Method(events, "public HookResult OnGameEnd("));

        string conclude = Method(events, "private void ConcludeMap()");

        Assert.Matches(
            @"if \(_environmentService\.IsOfflineMode\(\) && match\.gameEnded\)\s*\{[^}]*return;",
            conclude
        );
        Assert.True(
            At(conclude, "_environmentService.IsOfflineMode() && match.gameEnded")
                < At(conclude, "match.gameEnded = true;")
        );
    }

    [Fact]
    public void OfflineAMapIsWonOnItsScore()
    {
        string events = Events("GameEnd");

        Assert.Matches(
            @"Guid\? winningLineupId = _environmentService\.IsOfflineMode\(\)\s*\? OfflineMapWinner\(matchData\)\s*: _matchEvents\.GetWinningLineupId\(\);",
            Method(events, "private void ConcludeMap()")
        );

        string winner = Method(events, "private Guid? OfflineMapWinner(");

        Assert.DoesNotContain("_surrenderSystem", winner);
        Assert.Contains("OfflineMatchState.MapWinner(matchData, lineup1Score, lineup2Score)", winner);
    }

    [Fact]
    public void OfflineTheSeriesStopsOnceItIsDecided()
    {
        string events = Events("GameEnd");
        string progression = Method(events, "private void HandleOfflineMapProgression(");

        Assert.Contains("OfflineMatchState.Advance(matchData, currentMap)", progression);
        Assert.DoesNotContain("currentMap.order + 1", progression);
        Assert.True(
            At(progression, "_matchService.SaveOfflineProgress();")
                < At(progression, "match.ChangeMap(nextMap.map);")
        );
        Assert.Contains("match.AnnounceOfflineMatchOver();", progression);

        string announce = Method(events, "private void AnnounceMapConclusion(");

        Assert.True(
            At(announce, "if (_environmentService.IsOfflineMode())")
                < At(announce, "int mapsToWin")
        );
        Assert.Contains("OfflineMatchState.NextMap(matchData, currentMap)", announce);
    }

    [Fact]
    public void AnOfflineMatchThatIsOverLeavesTheServerIdle()
    {
        string setup = Method(Service("MatchManager"), "public void SetupMatch(MatchData match)");
        int over = At(setup, "OfflineMatchState.IsMatchOver(_matchData)");

        Assert.Contains("_environmentService.IsOfflineMode()", setup[..over]);
        Assert.True(over < At(setup, "ChangeMap(_currentMap.map);"));

        string idle = setup[over..At(setup, "ChangeMap(_currentMap.map);")];

        Assert.Contains("AnnounceOfflineMatchOver();", idle);
        Assert.Matches(@"_currentMapStatus =\s*OfflineMatchState\.StatusOf\(_currentMap\)", idle);
        Assert.Contains("eMapStatus.Finished;", idle);
        string restored = idle[
            At(idle, "if (_currentMapStatus == eMapStatus.Unknown)")..At(idle, "AnnounceOfflineMatchOver();")
        ];

        Assert.Contains("gameEnded = true;", restored);
        Assert.DoesNotContain("gameEnded", idle[At(idle, "AnnounceOfflineMatchOver();")..]);
    }

    [Fact]
    public void ReadyingUpOnAnOverMatchCannotTakeItLiveAgain()
    {
        string update = Method(Service("MatchManager"), "public void UpdateMapStatus(");
        string terminal = update[..At(update, "CheckForBackupRestore(status)")];

        Assert.Contains("MatchPhaseUtility.Reopens(_currentMapStatus, status)", terminal);
        Assert.True(MatchPhaseUtility.Reopens(eMapStatus.Finished, eMapStatus.Live));
        Assert.True(MatchPhaseUtility.Reopens(eMapStatus.Finished, eMapStatus.Knife));
    }

    [Fact]
    public void PlayersAreToldWhenAnOfflineRestoreIsRefused()
    {
        string local = Method(Service("GameBackUpRounds"), "private void RestoreRoundOffline(int round)");
        string refused = local[At(local, "if (refusal != null)")..At(local, "RestoreRound(round);")];

        Assert.Contains("_gameServer.Message(", refused);
        Assert.Contains("Restore round blocked: {refusal}.", refused);
    }

    [Fact]
    public void AnEventDroppedOfflineIsNotAWarning()
    {
        string publish = Method(Service("MatchEvents"), "public void PublishGameEvent(");

        Assert.Contains("_logger.LogDebug($\"Game event '{Event}' dropped: offline mode\");", publish);
    }
}
