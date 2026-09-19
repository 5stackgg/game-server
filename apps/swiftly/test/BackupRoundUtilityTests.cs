using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class BackupRoundUtilityTests
{
    private static string Backup(int round, int team1Players, int team2Players)
    {
        string Players(int count, int offset) =>
            string.Join(
                "\n",
                Enumerable
                    .Range(0, count)
                    .Select(i =>
                        $"\t\t\"{874739096 + offset + i}\"\n\t\t{{\n\t\t\t\"name\"\t\t\"p{{{i}}}\"\n\t\t\t\"cash\"\t\t\"800\"\n\t\t\t\"Items\"\n\t\t\t{{\n\t\t\t\t\"weapon_ak47\"\t\t\"1\"\n\t\t\t}}\n\t\t}}"
                    )
            );

        return "\"SaveFile\"\n{\n"
            + $"\t\"timestamp\"\t\t\"2026-09-19 13:41:02\"\n\t\"map\"\t\t\"de_inferno\"\n\t\"round\"\t\t\"{round}\"\n"
            + "\t\"FirstHalfScore\"\n\t{\n\t\t\"team1\"\t\t\"0\"\n\t\t\"team2\"\t\t\"1\"\n\t}\n"
            + "\t\"Timeouts\"\n\t{\n\t\t\"team1\"\t\t\"3\"\n\t\t\"technical\"\n\t\t{\n\t\t\t\"team1\"\t\t\"0\"\n\t\t}\n\t}\n"
            + $"\t\"PlayersOnTeam1\"\n\t{{\n{Players(team1Players, 0)}\n\t}}\n"
            + $"\t\"PlayersOnTeam2\"\n\t{{\n{Players(team2Players, 100)}\n\t}}\n"
            + "}\n";
    }

    // What CS2 wrote on 2026-09-19 for a round that ended with nobody seated:
    // well-formed, right round number, and no player sections at all.
    private const string PlayerlessBackup =
        "\"SaveFile\"\n{\n\t\"timestamp\"\t\t\"2026-09-19 13:49:42\"\n\t\"map\"\t\t\"de_inferno\"\n\t\"round\"\t\t\"1\"\n"
        + "\t\"FirstHalfScore\"\n\t{\n\t\t\"team1\"\t\t\"1\"\n\t\t\"team2\"\t\t\"0\"\n\t}\n"
        + "\t\"Timeouts\"\n\t{\n\t\t\"team1\"\t\t\"3\"\n\t\t\"team2\"\t\t\"3\"\n\t\t\"technical\"\n\t\t{\n\t\t\t\"team1\"\t\t\"0\"\n\t\t\t\"team2\"\t\t\"0\"\n\t\t}\n\t}\n}\n";

    [Theory]
    [InlineData(10, 3)]
    [InlineData(4, 1)]
    [InlineData(2, 1)]
    public void MinPlayersPerTeam_IsAMajorityOfOneSide(int expectedPlayers, int expected)
    {
        Assert.Equal(expected, BackupRoundUtility.MinPlayersPerTeam(expectedPlayers));
    }

    [Fact]
    public void Validate_AcceptsAFullBackup()
    {
        Assert.Null(BackupRoundUtility.Validate(Backup(7, 5, 5), 7, 3));
    }

    [Fact]
    public void Validate_RejectsABackupWithNoPlayers()
    {
        Assert.NotNull(BackupRoundUtility.Validate(PlayerlessBackup, 1, 3));
    }

    [Fact]
    public void Validate_RejectsAOneSidedBackup()
    {
        Assert.NotNull(BackupRoundUtility.Validate(Backup(1, 5, 1), 1, 3));
    }

    [Fact]
    public void Validate_RejectsTheWrongRound()
    {
        Assert.NotNull(BackupRoundUtility.Validate(Backup(3, 5, 5), 4, 3));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a backup")]
    public void Validate_RejectsGarbage(string? backupFile)
    {
        Assert.NotNull(BackupRoundUtility.Validate(backupFile, 1, 3));
    }

    [Fact]
    public void Validate_DoesNotCountNestedSectionsAsPlayers()
    {
        // Each player carries an Items block; 2 players must not read as 4.
        Assert.NotNull(BackupRoundUtility.Validate(Backup(1, 2, 2), 1, 3));
    }

    [Fact]
    public void FindRestorable_PrefersTheNewestEntryForARound()
    {
        BackupRound stale = new BackupRound { round = 2, backup_file = Backup(2, 5, 5) };
        BackupRound replayed = new BackupRound { round = 2, backup_file = Backup(2, 5, 5) };

        Assert.Same(
            replayed,
            BackupRoundUtility.FindRestorable(new[] { stale, replayed }, 2, 3)
        );
    }

    [Fact]
    public void FindRestorable_SkipsDeletedAndInvalidRounds()
    {
        BackupRound[] rounds =
        {
            new BackupRound
            {
                round = 1,
                backup_file = Backup(1, 5, 5),
                deleted_at = "2026-09-19T13:53:00Z",
            },
            new BackupRound { round = 2, backup_file = PlayerlessBackup },
        };

        Assert.Null(BackupRoundUtility.FindRestorable(rounds, 1, 3));
        Assert.Null(BackupRoundUtility.FindRestorable(rounds, 2, 3));
    }

    [Fact]
    public void HighestRestorableRound_FallsBackPastAnUnusableLatestRound()
    {
        BackupRound[] rounds =
        {
            new BackupRound { round = 1, backup_file = Backup(1, 5, 5) },
            new BackupRound { round = 2, backup_file = Backup(2, 5, 5) },
            new BackupRound { round = 3, backup_file = "" },
        };

        Assert.Equal(3, BackupRoundUtility.HighestRound(rounds));
        Assert.Equal(2, BackupRoundUtility.HighestRestorableRound(rounds, 3));
    }

    [Fact]
    public void HighestRound_IsZeroWithNothingRecorded()
    {
        Assert.Equal(0, BackupRoundUtility.HighestRound(new BackupRound[0]));
        Assert.Equal(0, BackupRoundUtility.HighestRestorableRound(new BackupRound[0], 3));
    }

    [Fact]
    public void Upsert_ReplacesRatherThanDuplicates()
    {
        BackupRound[] rounds =
        {
            new BackupRound { round = 1, backup_file = "old" },
            new BackupRound { round = 2, backup_file = "keep" },
        };

        BackupRound[] updated = BackupRoundUtility.Upsert(
            rounds,
            new BackupRound { round = 1, backup_file = "new" }
        );

        Assert.Equal(2, updated.Length);
        Assert.Equal("new", updated.Single(backupRound => backupRound.round == 1).backup_file);
    }

    [Fact]
    public void DropAbove_VoidsRoundsPastTheRestorePoint()
    {
        BackupRound[] rounds = Enumerable
            .Range(1, 6)
            .Select(round => new BackupRound { round = round })
            .ToArray();

        Assert.Equal(new[] { 1, 2, 3 }, BackupRoundUtility.DropAbove(rounds, 3).Select(r => r.round));
    }
}
