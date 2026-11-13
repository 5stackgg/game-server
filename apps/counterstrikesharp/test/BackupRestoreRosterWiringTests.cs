using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// Restores wait on the lineups' own players, as the round-start pause does: a
// 5v4 is whole with nine, and a coach or caster never stands in for a player.
public class BackupRestoreRosterWiringTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Source()
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", "GameBackUpRounds.cs")),
            @"//[^\n]*",
            ""
        );
    }

    private static (int From, int To) Method(string source, string signature)
    {
        int from = source.IndexOf(signature, StringComparison.Ordinal);
        int to = from < 0 ? -1 : source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, $"{signature} is gone");

        return (from, to);
    }

    [Theory]
    [InlineData("public void OfferPendingRestore()")]
    [InlineData("public string? RestoreRefusal(int round)")]
    [InlineData("private bool CanRestoreRound(int round)")]
    public void Each_Restore_Gate_Counts_Through_The_Roster_Helper(string signature)
    {
        string source = Source();
        (int from, int to) = Method(source, signature);

        Assert.Contains("= RosterCount();", source[from..to]);
    }

    [Fact]
    public void Only_The_Roster_Helper_Reads_Who_Is_In_The_Server()
    {
        string source = Source();
        (int from, int to) = Method(source, "private (int Present, int Required) RosterCount()");
        string rest = source[..from] + source[to..];

        Assert.Contains("ReconnectPauseUtility.RosterCount(", source[from..to]);
        Assert.DoesNotContain("Players()", rest);
        Assert.DoesNotContain("GetExpectedPlayerCount", rest);
    }
}
