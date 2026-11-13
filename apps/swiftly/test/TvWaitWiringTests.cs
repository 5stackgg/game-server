using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// What counts as starting into the wait is tested in MatchPhaseUtilityTests.
// Acting on it needs the engine, so these read the plugin.
public class TvWaitWiringTests
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

    private static string GameEnd()
    {
        return Code("FiveStack.Events", "GameEnd.cs");
    }

    [Fact]
    public void AServerThatStartsIntoTheTvWaitReportsIt()
    {
        string setup = Method(
            Code("FiveStack.Services", "MatchManager.cs"),
            "public void SetupMatch("
        );

        int decided = At(setup, "MatchPhaseUtility.StartsIntoTvWait(");
        int adopted = At(setup, "UpdateMapStatus(reported);");
        int told = At(setup, "_matchService.ReportMapLeftWaitingForTV();");

        Assert.True(decided < adopted, "the status is taken before the start is judged");
        Assert.True(adopted < told, "the wait is reported before the status is taken");
        Assert.Contains(
            "startsIntoTvWait && _currentMapStatus == eMapStatus.WaitingForTV",
            setup[adopted..told]
        );
    }

    [Fact]
    public void ThePluginHearsTheReport()
    {
        string service = Code("FiveStack.Services", "MatchService.cs");

        Assert.Contains(
            "MapLeftWaitingForTV?.Invoke();",
            Method(service, "public void ReportMapLeftWaitingForTV()")
        );
        Assert.Contains(
            "_matchService.MapLeftWaitingForTV += FinishMapLeftWaitingForTV;",
            Code("FiveStack.cs")
        );
    }

    [Fact]
    public void TheMapIsFinishedWithoutWaitingAgain()
    {
        string finish = Method(GameEnd(), "private void FinishMapLeftWaitingForTV()");

        Assert.Contains("HandleEndOfMap(null, false, 0);", finish);
        Assert.Contains(
            "int recordingDelay = recordingDelayOverride ?? (usePlaycast ? 5 : tvDelay);",
            Method(GameEnd(), "private void HandleEndOfMap(")
        );
    }

    [Fact]
    public void AWaitThisServerIsStillTimingIsLeftToItsTimer()
    {
        string conclude = Method(GameEnd(), "private void ConcludeMap()");
        string finish = Method(GameEnd(), "private void FinishMapLeftWaitingForTV()");

        Assert.True(
            At(conclude, "_concludingMapId = match.GetCurrentMap()?.id;")
                < At(conclude, "match.UpdateMapStatus(eMapStatus.WaitingForTV")
        );
        Assert.True(
            At(finish, "_concludingMapId == currentMap.id") < At(finish, "HandleEndOfMap(")
        );
        Assert.True(
            At(finish, "match.GetCurrentMapStatus() != eMapStatus.WaitingForTV")
                < At(finish, "HandleEndOfMap(")
        );
    }
}
