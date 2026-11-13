using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// Readying is a chat command from a live player, so this reads the plugin: who
// may ready, and who is listed as not ready, goes by the lineups, not by who is
// in the server.
public class ReadyWiringTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Method(string signature)
    {
        string source = Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", "ReadySystem.cs")),
            @"//[^\n]*",
            ""
        );
        int from = source.IndexOf(signature, StringComparison.Ordinal);
        int to = from < 0 ? -1 : source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, $"{signature} is gone");

        return source[from..to];
    }

    [Fact]
    public void Only_Lineup_Players_Can_Ready()
    {
        string canVote = Method("private bool CanVote(");

        Assert.Contains("ReadyUtility.CanReady(", canVote);
        Assert.DoesNotContain("return true;", canVote);
    }

    [Fact]
    public void Readying_And_The_Not_Ready_List_Go_By_The_Same_Rule()
    {
        Assert.Contains("CanVote(player)", Method("public void ToggleReady("));
        Assert.Contains("CanVote(player)", Method("private string[] GetNotReadyPlayers()"));
    }
}
