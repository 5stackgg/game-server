using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FiveStack.Utilities;
using Xunit;

// Overtime is never capped by the match type's mp_maxmoney (Duel 8000, Wingman 8000).
public class OvertimeMoneyTests
{
    [Theory]
    [InlineData(23, 12, true, false)]
    [InlineData(24, 12, true, true)]
    [InlineData(31, 12, true, true)]
    [InlineData(16, 8, true, true)]
    [InlineData(15, 8, true, false)]
    [InlineData(24, 12, false, false)]
    [InlineData(5, 0, true, false)]
    public void Overtime_Starts_After_Both_Regulation_Halves(
        int roundsPlayed,
        int mr,
        bool overtime,
        bool expected
    )
    {
        Assert.Equal(expected, OvertimeMoney.InOvertime(roundsPlayed, mr, overtime));
    }

    private static string Code(string folder, string file, [CallerFilePath] string testFile = "")
    {
        string source = File.ReadAllText(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src", folder, file))
        );
        return Regex.Replace(source, @"//[^\n]*", "");
    }

    [Fact]
    public void The_Cap_Is_Raised_At_Round_Prestart_And_Rechecked_At_Round_Start()
    {
        string events = Code("FiveStack.Events", "RoundStart.cs");

        Assert.Contains("OnRoundPrestart(EventRoundPrestart", events);
        Assert.Contains(
            "GetCurrentMatch()?.ApplyOvertimeMoney(_gameServer.GetTotalRoundsPlayed())",
            events
        );
        Assert.True(
            events.IndexOf("matchManager.ApplyOvertimeMoney(totalRoundsPlayed);", StringComparison.Ordinal)
                < events.IndexOf("if (!isInPlay)", StringComparison.Ordinal)
        );

        string manager = Code("FiveStack.Services", "MatchManager.cs");

        Assert.Contains("public void ApplyOvertimeMoney(int roundsPlayed)", manager);
        Assert.Contains("_matchData.options.OvertimeEnabled()", manager);
        Assert.Contains("OvertimeMoney.MaxMoney", manager);
    }

    [Fact]
    public void A_Cfg_Exec_Or_A_Restore_In_Overtime_Keeps_The_Overtime_Cap()
    {
        string manager = Code("FiveStack.Services", "MatchManager.cs");
        int exec = manager.IndexOf("public string[] MatchConfigExecCommands()", StringComparison.Ordinal);
        string execBody = manager[exec..manager.IndexOf("\n    }\n", exec, StringComparison.Ordinal)];

        Assert.Contains("InOvertime(_gameServer.GetTotalRoundsPlayed())", execBody);
        Assert.Contains("[.. commands, OvertimeMoney.Command]", execBody);

        string backups = Code("FiveStack.Services", "GameBackUpRounds.cs");

        Assert.True(
            backups.IndexOf("InOvertime(round)", StringComparison.Ordinal)
                < backups.IndexOf("mp_backup_restore_load_file {backupRoundFileName}", StringComparison.Ordinal)
        );
        Assert.Contains("[.. overtimeMoney, $\"mp_backup_restore_load_file", backups);
    }
}
