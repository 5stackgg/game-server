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

    [Fact]
    public void Restores_Count_The_Lineups_Players_Only()
    {
        string source = Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", "GameBackUpRounds.cs")),
            @"//[^\n]*",
            ""
        );

        Assert.Contains("ReconnectPauseUtility.RosterCount(", source);
        Assert.DoesNotContain("Players().Count", source);
        Assert.DoesNotContain("MatchUtility.PlayerCount()", source);
    }
}
