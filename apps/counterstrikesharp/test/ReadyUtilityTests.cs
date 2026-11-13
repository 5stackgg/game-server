using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// Only the lineups' own players ready up and are listed as not ready; a coach,
// caster or spectator readying would start the match a player short.
public class ReadyUtilityTests
{
    private const string LineupPlayer = "76561198000000001";
    private const string Outsider = "76561198000000009";

    private static bool CanReady(
        eReadySettings setting,
        string steamId,
        string name,
        bool isCoach = false,
        bool isCaptain = false
    )
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                lineup_players = new List<MatchMember> { new MatchMember { steam_id = LineupPlayer } },
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = new List<MatchMember>
                {
                    new MatchMember { steam_id = null, placeholder_name = "Bob" },
                },
            },
        };

        return ReadyUtility.CanReady(setting, match, steamId, name, isCoach, isCaptain);
    }

    [Fact]
    public void A_Lineup_Player_Can_Ready()
    {
        Assert.True(CanReady(eReadySettings.Players, LineupPlayer, "Alice"));
    }

    [Fact]
    public void A_Player_Who_Only_Shares_A_Seats_Name_Cannot_Ready()
    {
        Assert.False(CanReady(eReadySettings.Players, Outsider, "Bob"));
    }

    [Fact]
    public void A_Player_Whose_Name_Only_Fits_A_Seat_Cannot_Ready()
    {
        Assert.False(CanReady(eReadySettings.Players, Outsider, "[TAG] bob"));
    }

    [Theory]
    [InlineData("coach", true)]
    [InlineData("caster", false)]
    [InlineData("spectator", false)]
    public void Someone_Off_The_Lineups_Cannot_Ready(string name, bool isCoach)
    {
        Assert.False(CanReady(eReadySettings.Players, Outsider, name, isCoach));
    }

    [Fact]
    public void A_Captain_Has_To_Be_On_A_Lineup()
    {
        Assert.True(CanReady(eReadySettings.Captains, LineupPlayer, "Alice", isCaptain: true));
        Assert.False(CanReady(eReadySettings.Captains, Outsider, "coach", true, true));
        Assert.False(CanReady(eReadySettings.Captains, LineupPlayer, "Alice"));
    }

    [Fact]
    public void Only_The_Coach_Readies_In_Coach_Mode()
    {
        Assert.True(CanReady(eReadySettings.Coach, Outsider, "coach", isCoach: true));
        Assert.False(CanReady(eReadySettings.Coach, LineupPlayer, "Alice", isCaptain: true));
    }

    [Fact]
    public void Nobody_Readies_In_Admin_Mode()
    {
        Assert.False(CanReady(eReadySettings.Admin, LineupPlayer, "Alice", true, true));
    }

    private static List<MatchMember> Members(int from, int count)
    {
        return Enumerable
            .Range(from, count)
            .Select(i => new MatchMember { steam_id = $"7656119800000{i:D4}", name = $"player{i}" })
            .ToList();
    }

    private static IEnumerable<(string, string)> Connected(int from, int count)
    {
        return Enumerable.Range(from, count).Select(i => ($"7656119800000{i:D4}", $"player{i}"));
    }

    private static MatchData FiveVersusFour()
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 5) },
            lineup_2 = new MatchLineUp
            {
                lineup_players = Members(5, 3)
                    .Append(new MatchMember { steam_id = null, placeholder_name = "Luke" })
                    .ToList(),
            },
        };
    }

    [Fact]
    public void A_Five_Versus_Four_Is_Ready_With_Nine()
    {
        MatchData match = FiveVersusFour();
        match.lineup_2.lineup_players[3].steam_id = "76561198999999999";
        var ready = Connected(0, 8).Append(("76561198999999999", "lukepolo"));

        Assert.Equal(
            (9, 9),
            ReadyUtility.ReadyCount(eReadySettings.Players, match, ready, match.ExpectedPlayerCount())
        );
    }

    [Fact]
    public void A_Spare_Ready_Player_Never_Makes_Up_For_One_Missing_On_The_Other_Lineup()
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 6) },
            lineup_2 = new MatchLineUp { lineup_players = Members(6, 5) },
        };
        var ready = Connected(0, 6).Concat(Connected(6, 4));

        Assert.Equal((9, 10), ReadyUtility.ReadyCount(eReadySettings.Players, match, ready, 10));
    }

    // A captain who readied, released the spot and was replaced by a teammate who
    // readied too is still one lineup: the other captain has not readied.
    [Fact]
    public void Two_Ready_Captains_Of_One_Lineup_Count_Once()
    {
        Assert.Equal(
            (1, 2),
            ReadyUtility.ReadyCount(eReadySettings.Captains, FiveVersusFour(), Connected(0, 2), 10)
        );
        Assert.Equal(
            (2, 2),
            ReadyUtility.ReadyCount(
                eReadySettings.Captains,
                FiveVersusFour(),
                Connected(0, 1).Concat(Connected(5, 1)),
                10
            )
        );
    }

    [Fact]
    public void Each_Lineups_Coach_Has_To_Ready()
    {
        MatchData match = FiveVersusFour();
        match.lineup_1.coach_steam_id = "76561198000000101";
        match.lineup_2.coach_steam_id = "76561198000000102";

        Assert.Equal(
            (1, 2),
            ReadyUtility.ReadyCount(
                eReadySettings.Coach,
                match,
                new[] { ("76561198000000101", "coach"), ("76561198000000000", "player0") },
                10
            )
        );
        Assert.Equal(
            (2, 2),
            ReadyUtility.ReadyCount(
                eReadySettings.Coach,
                match,
                new[] { ("76561198000000101", "coach"), ("76561198000000102", "coach") },
                10
            )
        );
    }

    [Fact]
    public void A_Coach_On_Only_One_Lineup_Readies_Alone()
    {
        MatchData match = FiveVersusFour();
        match.lineup_1.coach_steam_id = "76561198000000101";

        Assert.Equal(
            (1, 1),
            ReadyUtility.ReadyCount(
                eReadySettings.Coach,
                match,
                new[] { ("76561198000000101", "coach") },
                10
            )
        );
    }

    [Fact]
    public void Coach_Mode_Without_Coaches_Waits_On_One()
    {
        Assert.Equal(
            (0, 1),
            ReadyUtility.ReadyCount(eReadySettings.Coach, FiveVersusFour(), Connected(0, 1), 10)
        );
    }

    [Fact]
    public void Nobody_Counts_As_Ready_In_Admin_Mode()
    {
        Assert.Equal(
            (0, 1),
            ReadyUtility.ReadyCount(eReadySettings.Admin, FiveVersusFour(), Connected(0, 1), 10)
        );
    }

    [Fact]
    public void Lineup_Players_Not_In_The_Server_Are_Named_While_Their_Lineup_Is_Short()
    {
        Assert.Equal(
            new[] { "player7" },
            ReadyUtility.EmptySeats(FiveVersusFour(), Connected(0, 7), 10)
        );
    }

    // An unbound seat is waiting on a Discord link, not on someone who left.
    [Fact]
    public void An_Unbound_Seat_Is_Not_Named_As_A_Player_Who_Left()
    {
        var connected = Connected(0, 8).Append(("76561198999999999", "Luke"));

        Assert.Empty(ReadyUtility.EmptySeats(FiveVersusFour(), connected, 10));
    }

    // Players mode waits on an unbound seat until its player links Discord, or an
    // admin forces the start.
    [Fact]
    public void An_Unbound_Seat_Holds_Up_Players_Mode()
    {
        var ready = Connected(0, 8).Append(("76561198999999999", "Luke"));

        Assert.Equal(
            (8, 9),
            ReadyUtility.ReadyCount(eReadySettings.Players, FiveVersusFour(), ready, 10)
        );
    }

    [Fact]
    public void An_Absent_Substitute_On_A_Whole_Lineup_Is_Not_Named()
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 6) },
            lineup_2 = new MatchLineUp { lineup_players = Members(6, 5) },
        };

        Assert.Empty(ReadyUtility.EmptySeats(match, Connected(0, 5).Concat(Connected(6, 5)), 10));
    }
}
