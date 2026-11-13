using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FiveStack.Utilities;
using Xunit;

// A surrender vote that has been answered must not come back on its own.
public class SurrenderVoteRepeatTests
{
    private static readonly Guid Map = Guid.NewGuid();
    private static readonly Guid Lineup = Guid.NewGuid();

    [Fact]
    public void A_Lineup_That_Voted_No_Is_Not_Asked_Again_That_Round()
    {
        SurrenderVoteCooldown cooldown = new SurrenderVoteCooldown();

        cooldown.Failed(Map, Lineup, 7);

        Assert.True(cooldown.Blocks(Map, Lineup, 7));
    }

    [Fact]
    public void It_Can_Be_Asked_Again_Once_Another_Round_Is_Played()
    {
        SurrenderVoteCooldown cooldown = new SurrenderVoteCooldown();

        cooldown.Failed(Map, Lineup, 7);

        Assert.False(cooldown.Blocks(Map, Lineup, 8));
    }

    [Fact]
    public void The_Other_Lineup_And_Other_Maps_Are_Not_Held_Back()
    {
        SurrenderVoteCooldown cooldown = new SurrenderVoteCooldown();

        cooldown.Failed(Map, Lineup, 7);

        Assert.False(cooldown.Blocks(Map, Guid.NewGuid(), 7));
        Assert.False(cooldown.Blocks(Guid.NewGuid(), Lineup, 7));
    }

    [Fact]
    public void Nothing_Is_Held_Back_Before_A_Vote_Fails()
    {
        Assert.False(new SurrenderVoteCooldown().Blocks(Map, Lineup, 0));
    }

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
        string reset = Method(Surrender(), "public void Reset()");
        int abandon = reset.IndexOf("surrenderingVote?.Abandon();", StringComparison.Ordinal);
        int drop = reset.IndexOf("surrenderingVote = null;", StringComparison.Ordinal);

        Assert.True(abandon >= 0, "the running vote is never ended");
        Assert.True(abandon < drop, "the vote is dropped before it is ended");
    }

    [Fact]
    public void A_Failed_Vote_Holds_Its_Lineup_Back()
    {
        string setup = Method(Surrender(), "public void SetupSurrender(");
        int blocks = setup.IndexOf("_voteCooldown.Blocks(", StringComparison.Ordinal);
        int start = setup.IndexOf("surrenderingVote.StartVote(", StringComparison.Ordinal);
        int failed = setup.IndexOf("\"surrender vote failed\"", StringComparison.Ordinal);

        Assert.True(blocks >= 0 && blocks < start, "a vote starts without checking the cooldown");
        Assert.Contains("return;", setup[blocks..start]);
        Assert.True(failed > start, "the failure callback is gone");
        Assert.Contains("_voteCooldown.Failed(", setup[start..]);
    }
}
