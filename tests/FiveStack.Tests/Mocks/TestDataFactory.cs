namespace FiveStack.Tests.Mocks;

using FiveStack.Entities;

/// <summary>
/// Factory methods for creating test entity data.
/// Service classes (MatchManager, GameServer, etc.) depend on
/// CounterStrikeSharp static APIs and cannot be mocked with Moq.
/// Use these factories to construct entity objects for testing
/// pure utility functions and decision logic.
/// </summary>
public static class TestDataFactory
{
    public static MatchData CreateMatchData(
        Guid? id = null,
        Guid? currentMapId = null,
        string techTimeoutSetting = "CoachAndCaptains",
        string timeoutSetting = "CoachAndCaptains",
        int mr = 12,
        bool coaches = true
    )
    {
        var matchId = id ?? Guid.NewGuid();
        var mapId = currentMapId ?? Guid.NewGuid();

        return new MatchData
        {
            id = matchId,
            current_match_map_id = mapId,
            lineup_1_id = Guid.NewGuid(),
            lineup_2_id = Guid.NewGuid(),
            lineup_1 = new MatchLineUp
            {
                lineup_players = Array.Empty<MatchMember>(),
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = Array.Empty<MatchMember>(),
            },
            options = new MatchOptions
            {
                mr = mr,
                type = "Competitive",
                overtime = true,
                best_of = 1,
                coaches = coaches,
                tech_timeout_setting = techTimeoutSetting,
                timeout_setting = timeoutSetting,
            },
            match_maps = new[]
            {
                new MatchMap
                {
                    id = mapId,
                    order = 0,
                    status = "Live",
                    lineup_1_timeouts_available = 3,
                    lineup_2_timeouts_available = 3,
                    rounds = Array.Empty<BackupRound>(),
                    map = new Map { name = "de_dust2" },
                },
            },
        };
    }

    public static MatchMap CreateMatchMap(
        Guid? id = null,
        int lineup1Timeouts = 3,
        int lineup2Timeouts = 3,
        BackupRound[]? rounds = null
    )
    {
        return new MatchMap
        {
            id = id ?? Guid.NewGuid(),
            order = 0,
            status = "Live",
            lineup_1_timeouts_available = lineup1Timeouts,
            lineup_2_timeouts_available = lineup2Timeouts,
            rounds = rounds ?? Array.Empty<BackupRound>(),
            map = new Map { name = "de_dust2" },
        };
    }

    public static BackupRound CreateBackupRound(
        int round,
        string? deletedAt = null,
        string backupFile = "backup-content"
    )
    {
        return new BackupRound
        {
            round = round,
            deleted_at = deletedAt,
            backup_file = backupFile,
        };
    }

    public static MatchMember CreateMatchMember(
        string? steamId = null,
        string role = "user",
        Guid? matchLineupId = null,
        string? placeholderName = null
    )
    {
        return new MatchMember
        {
            steam_id = steamId ?? "76561198000000001",
            role = role,
            match_lineup_id = matchLineupId ?? Guid.NewGuid(),
            placeholder_name = placeholderName ?? "",
        };
    }
}
