using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// CS2 puts Steam names back without an event, and the scoreboard redraw is a game
// event sent to clients. Neither runs without the engine, so these read the code.
public class PlayerNameWiringTests
{
    private static string MatchManager([CallerFilePath] string testFile = "")
    {
        string path = Path.GetFullPath(
            Path.Combine(
                Path.GetDirectoryName(testFile)!,
                "..",
                "src",
                "FiveStack.Services",
                "MatchManager.cs"
            )
        );

        return Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
    }

    private static string Method(string signature)
    {
        string source = MatchManager();
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    [Fact]
    public void ARenameRedrawsEveryPlayersScoreboardOnceATick()
    {
        string redraw = Method("private void RedrawScoreboards()");

        Assert.Contains("RedrawScoreboards();", Method("public void UpdatePlayerName("));
        Assert.Contains("_core.GameEvent.Fire<EventNextlevelChanged>()", redraw);
        Assert.Contains("if (_scoreboardRedrawQueued)", redraw);
        Assert.Contains("_core.Scheduler.NextTick(", redraw);
        Assert.DoesNotContain("FireToPlayer<EventNextlevelChanged>", MatchManager());
    }

    [Fact]
    public void NamesAreCheckedForAsLongAsTheMatchIsSetUp()
    {
        string setup = Method("public void SetupMatch(MatchData match)");
        int start = setup.IndexOf("StartPlayerNameCheck();", StringComparison.Ordinal);

        Assert.True(start >= 0, "SetupMatch never starts the name check");
        Assert.True(start < setup.IndexOf("ChangeMap(", StringComparison.Ordinal));
        Assert.True(start < setup.IndexOf("_currentMap == null", StringComparison.Ordinal));
        Assert.Contains("StopPlayerNameCheck();", Method("public void Reset()"));
        Assert.Contains("_playerNameCheckTimer = null;", Method("private void StopPlayerNameCheck()"));
        Assert.Contains(
            "TimerUtility.Repeat(PlayerNameCheckSeconds, RestorePlayerNames)",
            Method("private void StartPlayerNameCheck()")
        );
    }

    [Fact]
    public void ATooLongNameIsFittedBeforeItIsWrittenOrCompared()
    {
        Assert.Contains("PlayerNameUtility.Fit(name)", Method("public void UpdatePlayerName("));
        Assert.Contains("PlayerNameUtility.Fit(member.name)", Method("public void RestorePlayerName("));
    }
}
