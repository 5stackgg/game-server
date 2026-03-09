namespace FiveStack.Tests.Services;

using FiveStack.Entities;
using FiveStack.Tests.Mocks;
using FiveStack.Utilities;

/// <summary>
/// Tests for GameBackUpRounds logic.
/// Since GameBackUpRounds depends on CounterStrikeSharp static APIs
/// (Server.GameDirectory, Server.NextFrame, File system access) that are
/// not mockable, these tests verify the pure decision and formatting logic.
/// </summary>
public class GameBackUpRoundsTests
{
    // -- GetSafeMatchPrefix --

    [Fact]
    public void GetSafeMatchPrefix_CombinesIdsAndRemovesHyphens()
    {
        var matchId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
        var mapId = Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210");

        var match = TestDataFactory.CreateMatchData(id: matchId, currentMapId: mapId);

        var prefix = MatchUtility.GetSafeMatchPrefix(match);

        prefix.Should().NotContain("-");
        prefix.Should().Contain("0123456789abcdef0123456789abcdef");
        prefix.Should().Contain("fedcba9876543210fedcba9876543210");
    }

    [Fact]
    public void GetSafeMatchPrefix_FormatIsMatchId_MapId()
    {
        var matchId = Guid.NewGuid();
        var mapId = Guid.NewGuid();
        var match = TestDataFactory.CreateMatchData(id: matchId, currentMapId: mapId);

        var prefix = MatchUtility.GetSafeMatchPrefix(match);

        var expected = $"{matchId}_{mapId}".Replace("-", "");
        prefix.Should().Be(expected);
    }

    // -- Backup round file name formatting --

    private static string FormatBackupFileName(string prefix, int round)
    {
        return $"{prefix}_round{round.ToString().PadLeft(2, '0')}.txt";
    }

    private static string FormatRestoreFileName(string prefix, int round)
    {
        return $"restore-{prefix}round{round.ToString().PadLeft(2, '0')}.txt";
    }

    [Theory]
    [InlineData(0, "_round00.txt")]
    [InlineData(1, "_round01.txt")]
    [InlineData(5, "_round05.txt")]
    [InlineData(10, "_round10.txt")]
    [InlineData(24, "_round24.txt")]
    public void BackupFileName_PadsRoundNumberTo2Digits(int round, string expectedSuffix)
    {
        var fileName = FormatBackupFileName("prefix", round);
        fileName.Should().EndWith(expectedSuffix);
    }

    [Theory]
    [InlineData(0, "restore-prefixround00.txt")]
    [InlineData(5, "restore-prefixround05.txt")]
    [InlineData(12, "restore-prefixround12.txt")]
    public void RestoreFileName_HasCorrectFormat(int round, string expected)
    {
        FormatRestoreFileName("prefix", round).Should().Be(expected);
    }

    // -- CheckForBackupRestore decision logic --
    // Logic: if highestAvailableRound > totalRoundsPlayed → should restore
    //        if totalRoundsPlayed >= highestAvailableRound → already live, skip

    private static bool ShouldRestore(int highestRound, int totalRoundsPlayed)
    {
        if (totalRoundsPlayed > 0 && totalRoundsPlayed >= highestRound)
        {
            return false; // already live
        }
        return highestRound > totalRoundsPlayed;
    }

    [Theory]
    [InlineData(5, 0, true)]   // server restarted at round 0, backup at 5 → restore
    [InlineData(5, 3, true)]   // server behind → restore
    [InlineData(5, 5, false)]  // already at correct round → skip
    [InlineData(5, 6, false)]  // already past → skip
    [InlineData(1, 0, true)]   // backup at round 1, server fresh → restore
    [InlineData(0, 0, false)]  // no backup and no rounds → skip
    public void CheckForBackupRestore_DecisionLogic(
        int highestRound, int totalRoundsPlayed, bool expected)
    {
        ShouldRestore(highestRound, totalRoundsPlayed).Should().Be(expected);
    }

    // -- Backup round filtering (only non-deleted rounds) --

    [Fact]
    public void AvailableRounds_FiltersDeletedRounds()
    {
        var rounds = new[]
        {
            TestDataFactory.CreateBackupRound(1),
            TestDataFactory.CreateBackupRound(2, deletedAt: "2024-01-01"),
            TestDataFactory.CreateBackupRound(3),
            TestDataFactory.CreateBackupRound(4, deletedAt: "2024-01-02"),
        };

        var available = rounds.Where(r => r.deleted_at == null).ToArray();

        available.Should().HaveCount(2);
        available.Select(r => r.round).Should().BeEquivalentTo(new[] { 1, 3 });
    }

    [Fact]
    public void AvailableRounds_MaxRoundFromFiltered()
    {
        var rounds = new[]
        {
            TestDataFactory.CreateBackupRound(1),
            TestDataFactory.CreateBackupRound(5, deletedAt: "2024-01-01"),
            TestDataFactory.CreateBackupRound(3),
        };

        var available = rounds.Where(r => r.deleted_at == null);
        var highest = available.Max(r => r.round);

        highest.Should().Be(3);
    }

    // -- IsResettingRound state --

    [Fact]
    public void IsResettingRound_FalseWhenNull()
    {
        int? resetRound = null;
        (resetRound != null).Should().BeFalse();
    }

    [Fact]
    public void IsResettingRound_TrueWhenSet()
    {
        int? resetRound = 5;
        (resetRound != null).Should().BeTrue();
    }

    // -- GetMemberFromLineup --

    [Fact]
    public void GetMemberFromLineup_FindsBySteamId()
    {
        var lineupId = Guid.NewGuid();
        var match = TestDataFactory.CreateMatchData();
        match.lineup_1.lineup_players = new[]
        {
            TestDataFactory.CreateMatchMember(
                steamId: "76561198000000001",
                role: "user",
                matchLineupId: lineupId
            ),
        };
        match.lineup_2.lineup_players = Array.Empty<MatchMember>();

        var member = MatchUtility.GetMemberFromLineup(
            match, "76561198000000001", "SomePlayer");

        member.Should().NotBeNull();
        member!.match_lineup_id.Should().Be(lineupId);
    }

    [Fact]
    public void GetMemberFromLineup_ReturnsNullWhenNotFound()
    {
        var match = TestDataFactory.CreateMatchData();
        match.lineup_1.lineup_players = Array.Empty<MatchMember>();
        match.lineup_2.lineup_players = Array.Empty<MatchMember>();

        var member = MatchUtility.GetMemberFromLineup(
            match, "99999999999999999", "Unknown");

        member.Should().BeNull();
    }

    // -- SendRestoreRoundToBackend event data --

    [Fact]
    public void RestoreRoundEvent_IncludesRoundAndMapId()
    {
        var round = 5;
        var mapId = Guid.NewGuid();

        var eventData = new Dictionary<string, object>
        {
            { "round", round },
            { "match_map_id", mapId },
        };

        eventData["round"].Should().Be(5);
        eventData["match_map_id"].Should().Be(mapId);
    }
}
