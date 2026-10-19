using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class RoundRestoreGateTests
{
    [Fact]
    public void LetsARestoreThroughWithEveryoneIn()
    {
        Assert.Null(RoundRestoreGateUtility.Refusal(false, false, 10, 10, "Competitive"));
    }

    [Fact]
    public void RefusesWhilePlayersAreMissing()
    {
        Assert.Equal(
            "waiting for all players to reconnect (3/10)",
            RoundRestoreGateUtility.Refusal(false, false, 3, 10, "Competitive")
        );
    }

    [Fact]
    public void RefusesWhileAnotherRestoreIsRunning()
    {
        Assert.Equal(
            "a round restore is already in progress",
            RoundRestoreGateUtility.Refusal(false, true, 10, 10, "Competitive")
        );
    }

    [Fact]
    public void RefusesWhereRestoresAreDisabled()
    {
        Assert.Equal(
            "round restores are disabled in Rush",
            RoundRestoreGateUtility.Refusal(true, false, 6, 6, "Rush")
        );
    }

    [Fact]
    public void LoadsTheNewerBackupOfARoundPlayedAgain()
    {
        var rounds = new[]
        {
            new BackupRound
            {
                round = 6,
                backup_file = "first",
                deleted_at = null!,
            },
            new BackupRound { round = 7, backup_file = "seven" },
            new BackupRound { round = 6, backup_file = "replayed" },
        };

        Assert.Equal("replayed", RoundRestoreGateUtility.LatestBackup(rounds, 6)?.backup_file);
    }

    [Fact]
    public void SkipsABackupTheApiMarkedDeleted()
    {
        var rounds = new[]
        {
            new BackupRound
            {
                round = 6,
                backup_file = "undone",
                deleted_at = "2026-10-19T09:00:00Z",
            },
        };

        Assert.Null(RoundRestoreGateUtility.LatestBackup(rounds, 6));
    }
}
