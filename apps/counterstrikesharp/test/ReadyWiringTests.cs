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

    // Each lineup readies up to its own size, so a 5v4 is ready with nine, and a
    // spare ready player on one lineup never makes up for one missing on the other.
    [Theory]
    [InlineData("public void ToggleReady(")]
    [InlineData("public void SetupReadyMessage(")]
    public void The_Ready_Count_Is_Held_To_Each_Lineup(string signature)
    {
        string method = Method(signature);

        Assert.Contains("= ReadyCount();", method);
        Assert.DoesNotContain("TotalReady()", method);
        Assert.DoesNotContain("GetExpectedPlayerCount", method);
        Assert.Contains(
            "ReadyUtility.ReadyCount(",
            Method("private (int Ready, int Target) ReadyCount()")
        );
    }

    // A captain who released the spot keeps their ready flag, but it no longer counts.
    [Fact]
    public void Only_Players_Who_May_Ready_Now_Are_Counted()
    {
        Assert.Contains("CanVote(player)", Method("private (int Ready, int Target) ReadyCount()"));
    }

    // Lineup players who left are "not connected"; a seat nobody has taken by
    // linking Discord is "not linked".
    [Fact]
    public void The_Not_Ready_List_Names_The_Seats_Nobody_Holds()
    {
        string seats = Method("private (List<string> Absent, List<string> Unlinked) GetEmptySeats()");

        Assert.Contains("GetEmptySeats()", Method("public void SendNotReadyMessage()"));
        Assert.Contains("ReadyUtility.EmptySeats(", seats);
        Assert.Contains("ReadyUtility.UnlinkedSeats(", seats);
    }
}
