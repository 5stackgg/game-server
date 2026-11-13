using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// A restarted server that plays its map again sends a reset to round 0 and then
// Warmup, and the api handles each on its own, in whatever order they land. It
// ignores a reset on a map that is already in warmup, so the Warmup carries the
// replay and a newer api sets the rounds aside on it. The payloads are tested in
// MatchEventPayloadsTests; sending them needs the engine, so these read the plugin.
public class ReplayWiringTests
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

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{start} has moved");

        int to = source.IndexOf(end, from + 1, StringComparison.Ordinal);

        Assert.True(to > from, $"{end} has moved");

        return source[from..to];
    }

    private static string Backups()
    {
        return Code("FiveStack.Services", "GameBackUpRounds.cs");
    }

    [Fact]
    public void TheWarmupAfterARestartReplaySaysItIsOne()
    {
        string update = Method(
            Code("FiveStack.Services", "MatchManager.cs"),
            "public void UpdateMapStatus("
        );

        Assert.Matches(
            @"bool replay =\s+_currentMapStatus == eMapStatus\.Unknown\s+&& _backUpManagement\.CheckForBackupRestore\(status\);",
            update
        );
        Assert.Contains("_matchEvents.PublishMapStatus(status, winningLineupId, replay);", update);
    }

    [Fact]
    public void TheStatusCarriesTheReplay()
    {
        string publish = Method(
            Code("FiveStack.Services", "MatchEvents.cs"),
            "public void PublishMapStatus("
        );

        Assert.Contains("bool replay = false", publish);
        Assert.Matches(@"MatchEventPayloads\.MapStatus\([^;]*replay\s*\)", publish);
    }

    [Fact]
    public void ARestartReplayMarksItsReset()
    {
        Assert.Contains(
            "fromWarmup: true",
            Method(Backups(), "private bool ReplayMapAfterRestart(")
        );
        Assert.Contains(
            "SendRestoreRoundToBackend(0, fromWarmup);",
            Method(Backups(), "private void ReplayMap(")
        );
        Assert.Matches(
            @"MatchEventPayloads\.RestoreRound\(\s*round,\s*mapId,\s*replay\s*\)",
            Method(Backups(), "public void SendRestoreRoundToBackend(")
        );
    }

    [Fact]
    public void ADeclinedRecoveryVoteStaysOnTheMapAndSendsAnOrdinaryReset()
    {
        Assert.Contains(
            "fromWarmup: false",
            Between(Backups(), "\"restore round vote failed\"", "ResumeFrom(ePauseReason.Restore)")
        );
    }

    [Fact]
    public void RestartMapOutOfAHoldTakesTheHoldsPauseOff()
    {
        string state = Method(Code("FiveStack.Commands", "Match.cs"), "public void SetMatchState(");

        int held = state.IndexOf(
            "bool held = _gameBackupRounds.IsHoldingForRestore();",
            StringComparison.Ordinal
        );
        int abandon = state.IndexOf("_gameBackupRounds.AbandonRestore();", StringComparison.Ordinal);
        int resume = state.IndexOf("match?.ResumeMatch(null, true);", StringComparison.Ordinal);
        int update = state.IndexOf("match?.UpdateMapStatus(status);", StringComparison.Ordinal);

        Assert.True(held >= 0, state);
        Assert.True(held < abandon, state);
        Assert.True(abandon < resume, state);
        Assert.True(resume < update, state);
        Assert.Contains("if (held)", state[abandon..resume]);
        Assert.DoesNotContain("IsResettingRound", state);
    }
}
