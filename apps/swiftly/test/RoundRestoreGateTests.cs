using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class RoundRestoreGateTests
{
    [Fact]
    public void LetsARestoreThroughWithEveryoneIn()
    {
        Assert.Null(
            RoundRestoreGateUtility.Refusal(
                false,
                false,
                eMapStatus.Live,
                4,
                true,
                10,
                10,
                "Competitive"
            )
        );
    }

    [Fact]
    public void RefusesWhilePlayersAreMissing()
    {
        Assert.Equal(
            "waiting for all players to reconnect (3/10)",
            RoundRestoreGateUtility.Refusal(
                false,
                false,
                eMapStatus.Live,
                4,
                true,
                3,
                10,
                "Competitive"
            )
        );
    }

    [Fact]
    public void RefusesWhileAnotherRestoreIsRunning()
    {
        Assert.Equal(
            "a round restore is already in progress",
            RoundRestoreGateUtility.Refusal(
                false,
                true,
                eMapStatus.Live,
                4,
                true,
                10,
                10,
                "Competitive"
            )
        );
    }

    [Fact]
    public void RefusesWhereRestoresAreDisabled()
    {
        Assert.Equal(
            "round restores are disabled in Rush",
            RoundRestoreGateUtility.Refusal(true, false, eMapStatus.Live, 4, true, 6, 6, "Rush")
        );
    }

    [Theory]
    [InlineData(eMapStatus.Warmup)]
    [InlineData(eMapStatus.Knife)]
    public void RefusesUntilTheMapIsLive(eMapStatus status)
    {
        Assert.Equal(
            "the map isn't live yet",
            RoundRestoreGateUtility.Refusal(false, false, status, 4, true, 10, 10, "Competitive")
        );
    }

    [Theory]
    [InlineData(eMapStatus.WaitingForTV)]
    [InlineData(eMapStatus.UploadingDemo)]
    [InlineData(eMapStatus.Finished)]
    [InlineData(eMapStatus.Surrendered)]
    public void RefusesOnAMapThatIsOver(eMapStatus status)
    {
        Assert.Equal(
            "the map is already over",
            RoundRestoreGateUtility.Refusal(false, false, status, 4, true, 10, 10, "Competitive")
        );
    }

    // A server that reloads mid-map offers its restore before it has a status,
    // and the vote passes once it is back on the api's Live or Paused.
    [Theory]
    [InlineData(eMapStatus.Unknown)]
    [InlineData(eMapStatus.Live)]
    [InlineData(eMapStatus.Paused)]
    [InlineData(eMapStatus.Overtime)]
    public void LetsARestoreThroughOnALiveMap(eMapStatus status)
    {
        Assert.Null(
            RoundRestoreGateUtility.Refusal(false, false, status, 4, true, 10, 10, "Competitive")
        );
    }

    [Fact]
    public void RefusesARoundTheServerHasNoBackupOf()
    {
        Assert.Equal(
            "the server has no backup of round 4",
            RoundRestoreGateUtility.Refusal(
                false,
                false,
                eMapStatus.Live,
                4,
                false,
                10,
                10,
                "Competitive"
            )
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
    public void StillLoadsARoundARestoreUndid()
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

        Assert.Equal("undone", RoundRestoreGateUtility.LatestBackup(rounds, 6)?.backup_file);
    }

    [Fact]
    public void SkipsABackupWithNothingInIt()
    {
        var rounds = new[]
        {
            new BackupRound { round = 6, backup_file = "kept" },
            new BackupRound { round = 6, backup_file = "" },
        };

        Assert.Equal("kept", RoundRestoreGateUtility.LatestBackup(rounds, 6)?.backup_file);
    }

    [Fact]
    public void TellsAnUndoneRoundFromOnePlayedAgain()
    {
        var rounds = new[]
        {
            new BackupRound
            {
                round = 6,
                backup_file = "undone",
                deleted_at = "2026-10-19T09:00:00Z",
            },
            new BackupRound
            {
                round = 7,
                backup_file = "undone",
                deleted_at = "2026-10-19T09:00:00Z",
            },
            new BackupRound { round = 7, backup_file = "replayed" },
        };

        Assert.True(RoundRestoreGateUtility.IsUndone(rounds, 6));
        Assert.False(RoundRestoreGateUtility.IsUndone(rounds, 7));
        Assert.False(RoundRestoreGateUtility.IsUndone(rounds, 8));
    }

    [Fact]
    public void RefusesWarmupWithoutAMatch()
    {
        Assert.Equal(
            "no match is loaded",
            RoundRestoreGateUtility.WarmupRefusal(false, eMapStatus.Unknown)
        );
    }

    [Theory]
    [InlineData(eMapStatus.WaitingForTV)]
    [InlineData(eMapStatus.UploadingDemo)]
    [InlineData(eMapStatus.Finished)]
    [InlineData(eMapStatus.Surrendered)]
    public void RefusesWarmupOnAMapThatIsOver(eMapStatus current)
    {
        Assert.Equal(
            "the map is already over",
            RoundRestoreGateUtility.WarmupRefusal(true, current)
        );
    }

    [Theory]
    [InlineData(eMapStatus.Warmup)]
    [InlineData(eMapStatus.Knife)]
    [InlineData(eMapStatus.Live)]
    [InlineData(eMapStatus.Overtime)]
    [InlineData(eMapStatus.Paused)]
    public void SendsAMapBeingPlayedBackToWarmup(eMapStatus current)
    {
        Assert.Null(RoundRestoreGateUtility.WarmupRefusal(true, current));
    }
}
