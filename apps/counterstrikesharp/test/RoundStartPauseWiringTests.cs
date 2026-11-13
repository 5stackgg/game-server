using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// Round start is an engine event, so this reads the plugin: the pause has to be
// decided by ReconnectPauseUtility, not by a count of everyone in the server.
public class RoundStartPauseWiringTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    [Fact]
    public void Round_Start_Pauses_On_The_Lineups_Players_Only()
    {
        string source = Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Events", "RoundStart.cs")),
            @"//[^\n]*",
            ""
        );
        int from = source.IndexOf("public HookResult OnRoundStart(", StringComparison.Ordinal);
        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, "OnRoundStart is gone");

        string handler = source[from..to];
        int decision = handler.IndexOf("ReconnectPauseUtility.ShouldPause(", StringComparison.Ordinal);
        int pause = handler.IndexOf("\"Waiting for players to reconnect\"", StringComparison.Ordinal);

        Assert.True(decision >= 0, "the pause is not decided by ReconnectPauseUtility");
        Assert.True(pause > decision, "the pause is not behind that decision");
        Assert.DoesNotContain("Players().Count", handler);
        Assert.DoesNotContain("PlayerCount()", handler);
    }
}
