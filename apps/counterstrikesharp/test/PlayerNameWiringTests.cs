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
        Assert.Contains("new EventNextlevelChanged(false).FireEvent(false)", redraw);
        Assert.Contains("if (_scoreboardRedrawQueued)", redraw);
        Assert.Contains("Server.NextFrame(", redraw);
        Assert.DoesNotContain("FireEventToClient", MatchManager());
    }

    [Fact]
    public void NamesAreCheckedForAsLongAsTheMatchIsSetUp()
    {
        string start = Method("private void StartPlayerNameCheck()");

        Assert.Contains("StartPlayerNameCheck();", Method("public void SetupMatch(MatchData match)"));
        Assert.Contains("StopPlayerNameCheck();", Method("public void Reset()"));
        Assert.Contains("RestorePlayerNames,", start);
        Assert.Contains("TimerFlags.REPEAT", start);
    }
}
