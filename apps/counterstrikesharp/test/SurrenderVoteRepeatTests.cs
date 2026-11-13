using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// A surrender vote that has been answered must not come back on its own.
// Running it needs the engine, so this reads the plugin.
public class SurrenderVoteRepeatTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Surrender()
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", "SurrenderSystem.cs")),
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

    // Dropping a running vote without ending it left it prompting every second,
    // while .y and .n no longer reached it.
    [Fact]
    public void Reset_Ends_A_Running_Vote_Before_Letting_Go_Of_It()
    {
        string end = Method(Surrender(), "public void EndVote()");
        int abandon = end.IndexOf("surrenderingVote?.Abandon();", StringComparison.Ordinal);
        int drop = end.IndexOf("surrenderingVote = null;", StringComparison.Ordinal);

        Assert.True(abandon >= 0, "the running vote is never ended");
        Assert.True(abandon < drop, "the vote is dropped before it is ended");
        Assert.Contains("EndVote();", Method(Surrender(), "public void Reset()"));
    }
}
