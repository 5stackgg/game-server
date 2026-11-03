using System.Text.RegularExpressions;
using Xunit;

// UtilityApiClientRetryTests proves what the client does once it is told to. The
// telling is in plugin code that needs the engine, so these read the source, and
// go red when a line that makes all of that run is lost.
public class PracticeSaveWiringTests
{
    private static string Code(params string[] path)
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(
                new[] { directory.FullName, "apps", "utility-sw", "src" }.Concat(path).ToArray()
            );

            if (File.Exists(candidate))
            {
                return Regex.Replace(File.ReadAllText(candidate), @"//[^\n]*", "");
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{path[^1]} was not found above the test output");
    }

    private static string Method(string signature, params string[] file)
    {
        string source = Code(file.Length == 0 ? new[] { "UtilityPracticePlugin.cs" } : file);
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    [Fact]
    public void TheClientKnowsTheMapBeforeTheOldMapsSavesSettle()
    {
        string load = Method("private void OnMapLoad(string mapName)");
        int known = load.IndexOf("_api.Map = mapName;", StringComparison.Ordinal);
        int settled = load.IndexOf("_voxels.Reset();", StringComparison.Ordinal);

        Assert.True(known >= 0, "OnMapLoad does not tell the client the map");
        Assert.True(known < settled, "the old map's saves settle before the client knows the map");
    }

    [Fact]
    public void AHotReloadTellsTheClientTheMap()
    {
        Assert.Contains("_api.Map = current;", Method("public override void Load(bool hotReload)"));
    }

    [Fact]
    public void HeldSavesGetTheirSecond()
    {
        Assert.Contains("_api.Second()", Method("private void OnSecond()"));
    }

    [Fact]
    public void ARetriedSaveIsListenedForUntilTearDown()
    {
        string load = Method("public override void Load(bool hotReload)");
        string tearDown = Method("private void TearDown()");

        Assert.Contains("_api.SaveRetried += OnSaveRetried;", load);
        Assert.Contains("_api.SaveRetried -= OnSaveRetried;", tearDown);
    }

    [Fact]
    public void AFailedDeleteIsListenedForUntilTearDown()
    {
        string load = Method("public override void Load(bool hotReload)");
        string tearDown = Method("private void TearDown()");

        Assert.Contains("_api.WithdrawnDeleteFailed += OnWithdrawnDeleteFailed;", load);
        Assert.Contains("_api.WithdrawnDeleteFailed -= OnWithdrawnDeleteFailed;", tearDown);
    }

    [Fact]
    public void AMapChangeForgetsTheThrowsOfTheOldMap()
    {
        Assert.Contains(
            "_history.Clear();",
            Method("public void Reset()", "Services", "PracticeRecorder.cs")
        );
    }

    [Fact]
    public void ADeleteLetsGoOfTheSavesAndMovesTheDrillOn()
    {
        string delete = Method("private void DeleteLoaded(IPlayer player)", "Commands", "Practice.cs");

        Assert.Contains("PracticeSaveUtility.ForgetSaves(", delete);
        Assert.Contains("_drill.LineupsRemoved(player.SteamID, isLoaded);", delete);
    }

    [Fact]
    public void ADeleteMatchesTheLineupEverywhereBeforeItsIdIsGivenUp()
    {
        string delete = Method("private void DeleteLoaded(IPlayer player)", "Commands", "Practice.cs");
        int matched = delete.IndexOf("isLoaded = PracticeSaveUtility.SameLineup(loaded);", StringComparison.Ordinal);
        int library = delete.IndexOf("_library.Remove(player.SteamID, isLoaded);", StringComparison.Ordinal);
        int forgotten = delete.IndexOf("PracticeSaveUtility.ForgetSaves(", StringComparison.Ordinal);

        Assert.True(matched >= 0 && matched < forgotten, "the panel id is gone before it's read");
        Assert.True(library > matched, "the library keeps the row a push swapped in");
        Assert.Matches(@"PruneResults\(\s*state\.Results,\s*state\.Index,\s*isLoaded\s*\)", delete);
    }

    [Fact]
    public void TheWalkLabelFindsARowAPushSwappedIn()
    {
        Assert.Contains(
            "PracticeSaveUtility.SameLineup(lineup)(state.Results[state.Index])",
            Method("private string Title(IPlayer player, LineupRecord lineup)")
        );
    }

    [Fact]
    public void ADrillWithNothingLeftStopsAndADeletedStepMovesOn()
    {
        string deleted = Method("public void LineupDeleted(", "Services", "PracticeDrill.cs");
        int removed = deleted.IndexOf("run.Remove(clientId)", StringComparison.Ordinal);
        int stopped = deleted.IndexOf("Finish(steamId, run);", StringComparison.Ordinal);
        int moved = deleted.IndexOf("Advance(steamId, run);", StringComparison.Ordinal);

        Assert.True(removed >= 0 && stopped > removed && moved > stopped, "the run is left stalled");
    }

    [Fact]
    public void APushPrunesAWalkRatherThanReplacingIt()
    {
        string landed = Method("private void LibraryLanded(");

        Assert.Contains("if (state.Results.Count == 0)", landed);
        Assert.DoesNotContain("pushed || state.Results.Count == 0", landed);
        Assert.Contains(
            "PracticeSaveUtility.PruneResults(",
            Method("private void DropRemoved(ulong steamId")
        );
    }

    [Fact]
    public void AMapLoadForgetsHowFarBackThePlayerWalked()
    {
        Assert.Contains("_earlier.Clear();", Method("private void OnMapLoad(string mapName)"));
    }

    [Fact]
    public void ASaveAsksTheEngineForItsWorkshopIdBeforeLeavingTheGameThread()
    {
        string save = Method("private void IngestSave(ulong steamId", "Commands", "Practice.cs");
        int asked = save.IndexOf("Core.Engine.WorkshopId", StringComparison.Ordinal);
        int left = save.IndexOf("Task.Run(", StringComparison.Ordinal);

        Assert.True(asked >= 0, "a save does not name its workshop map");
        Assert.True(asked < left, "the engine is asked from a worker thread");
        Assert.Contains("_api.Ingest(thrown, workshopId)", save);
    }

    [Fact]
    public void ADeleteKeepsThePlayersPlaceInTheWalk()
    {
        Assert.Contains(
            "PracticeSaveUtility.PruneResults(",
            Method("private void DeleteLoaded(IPlayer player)", "Commands", "Practice.cs")
        );
    }

    [Fact]
    public void APushRefreshesTheWalkAndClearsItsRowsFromTheDrill()
    {
        string removed = Method("private void DropRemoved(ulong steamId");
        int refreshed = removed.IndexOf("PracticeSaveUtility.RefreshResults(state.Results, library);", StringComparison.Ordinal);
        int unloaded = removed.IndexOf("state.Loaded = null;", StringComparison.Ordinal);
        int drill = removed.IndexOf("_drill.LineupsRemoved(steamId, Gone);", StringComparison.Ordinal);

        Assert.True(refreshed >= 0, "an edited lineup stays stale in the walk");
        Assert.True(drill > unloaded, "the drill's next step can be unloaded behind it");
        Assert.DoesNotContain("return;", removed[..drill]);
    }

    [Fact]
    public void ASaveThatLandedGoesBackInTheLibraryIfARefreshDroppedIt()
    {
        string save = Method("private void IngestSave(ulong steamId", "Commands", "Practice.cs");

        Assert.Contains("PracticeSaveUtility.BelongsBackInLibrary(", save);
        Assert.Contains("_library.Add(steamId, thrown);", save);
    }

    [Fact]
    public void TheSessionKnowsTheWorkshopIdBeforeItIsFetched()
    {
        string load = Method("private void OnMapLoad(string mapName)");
        int known = load.IndexOf("_session.WorkshopId = Core.Engine.WorkshopId;", StringComparison.Ordinal);
        int fetched = load.IndexOf("RefreshEverything();", StringComparison.Ordinal);

        Assert.True(known >= 0 && known < fetched, "the session is fetched without its workshop id");
        Assert.Contains(
            "_session.WorkshopId = Core.Engine.WorkshopId;",
            Method("public override void Load(bool hotReload)")
        );
    }

    [Theory]
    [InlineData("private void OnMapLoad(string mapName)", "_session.Map = mapName;")]
    [InlineData("public override void Load(bool hotReload)", "_session.Map = current;")]
    public void TheSessionLearnsItsWorkshopIdBeforeItsMap(string signature, string map)
    {
        string load = Method(signature);
        int workshop = load.IndexOf("_session.WorkshopId = Core.Engine.WorkshopId;", StringComparison.Ordinal);

        Assert.True(
            workshop >= 0 && workshop < load.IndexOf(map, StringComparison.Ordinal),
            "a fetch can pair the new map with the old workshop id"
        );
    }

    [Fact]
    public void ASaveKeepsTheWorkshopIdItWasMadeOn()
    {
        Assert.Contains(
            "thrown.workshop_map_id = Core.Engine.WorkshopId;",
            Method("private void SaveThrow(ulong steamId", "Commands", "Practice.cs")
        );
        Assert.Contains(
            "thrown.workshop_map_id ?? Core.Engine.WorkshopId",
            Method("private void IngestSave(ulong steamId", "Commands", "Practice.cs")
        );
    }

    [Fact]
    public void ADeleteNamesThePlayerAsking()
    {
        Assert.Contains("_api.Delete(id, steamId.ToString())", Code("Commands", "Practice.cs"));
    }

    [Fact]
    public void ARetriedSaveAsksBeforeGoingBackInTheLibrary()
    {
        string retried = Method(
            "private void OnSaveRetried(LineupRecord saved",
            "Commands",
            "Practice.cs"
        );

        Assert.Contains("PracticeSaveUtility.GivePanelId(saved, result.Id);", retried);
        Assert.Contains("PracticeSaveUtility.BelongsBackInLibrary(", retried);
    }
}
