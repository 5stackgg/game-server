using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// The round-start pause has to notice a missing player even while a coach or
// caster is in the server, and never pause a match whose lineups are whole.
public class ReconnectPauseUtilityTests
{
    private static MatchData BuildMatch(params MatchMember[] extraLineup2)
    {
        List<MatchMember> Lineup(int from, int count) =>
            Enumerable
                .Range(from, count)
                .Select(i => new MatchMember { steam_id = $"7656119800000{i:D4}" })
                .ToList();

        return new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Lineup(0, 5) },
            lineup_2 = new MatchLineUp
            {
                lineup_players = Lineup(5, 5 - extraLineup2.Length).Concat(extraLineup2).ToList(),
            },
        };
    }

    private static IEnumerable<(string, string)> Roster(int count)
    {
        return Enumerable.Range(0, count).Select(i => ($"7656119800000{i:D4}", $"player{i}"));
    }

    [Fact]
    public void A_Missing_Player_Pauses_Even_With_A_Caster_In_The_Server()
    {
        var connected = Roster(9).Append(("76561198999999999", "caster"));

        Assert.True(ReconnectPauseUtility.ShouldPause(BuildMatch(), connected, 10));
    }

    [Fact]
    public void A_Whole_Roster_Does_Not_Pause_With_A_Coach_Watching()
    {
        var connected = Roster(10).Append(("76561198999999998", "coach"));

        Assert.False(ReconnectPauseUtility.ShouldPause(BuildMatch(), connected, 10));
    }

    // A pickup seat is taken by linking Discord on 5stack.gg, which binds it to the
    // player's steam id. A seat nobody holds cannot reconnect, so pausing,
    // restoring and resuming wait only on bound seats, and someone who only shares
    // a seat's name never fills it.
    [Fact]
    public void An_Unbound_Seat_Never_Pauses_The_Match()
    {
        MatchData match = BuildMatch(new MatchMember { steam_id = null, placeholder_name = "Bob" });
        var connected = Roster(9).Append(("76561198999999997", "Bob"));

        Assert.False(ReconnectPauseUtility.ShouldPause(match, connected, 10));
    }

    [Fact]
    public void A_Restore_And_A_Resume_Wait_Only_On_Bound_Seats()
    {
        MatchData match = BuildMatch(new MatchMember { steam_id = null, placeholder_name = "Bob" });

        Assert.Equal((9, 9), ReconnectPauseUtility.RosterCount(match, Roster(9), 10));
        Assert.Equal((8, 9), ReconnectPauseUtility.RosterCount(match, Roster(8), 10));
        Assert.True(ReconnectPauseUtility.ShouldPause(match, Roster(8), 10));
    }

    [Fact]
    public void A_Lineup_With_An_Unbound_Seat_Is_Whole_With_Its_Bound_Players()
    {
        MatchData match = BuildMatch(new MatchMember { steam_id = null, placeholder_name = "Bob" });
        match.lineup_1_id = Guid.NewGuid();
        match.lineup_2_id = Guid.NewGuid();

        Assert.True(ReconnectPauseUtility.IsLineupWhole(match, Roster(9), 10, match.lineup_2_id));
        Assert.False(ReconnectPauseUtility.IsLineupWhole(match, Roster(8), 10, match.lineup_2_id));
    }

    private static MatchData FiveVersusFour()
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                lineup_players = Enumerable
                    .Range(0, 5)
                    .Select(i => new MatchMember { steam_id = $"7656119800000{i:D4}" })
                    .ToList(),
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = Enumerable
                    .Range(5, 4)
                    .Select(i => new MatchMember { steam_id = $"7656119800000{i:D4}" })
                    .ToList(),
            },
        };
    }

    // A 5v4 that went live through its captains or an admin is whole with nine.
    [Fact]
    public void A_Five_Versus_Four_With_Everyone_In_Does_Not_Pause()
    {
        MatchData match = FiveVersusFour();

        Assert.False(ReconnectPauseUtility.ShouldPause(match, Roster(9), match.ExpectedPlayerCount()));
    }

    [Fact]
    public void A_Five_Versus_Four_Missing_One_Of_The_Four_Pauses()
    {
        MatchData match = FiveVersusFour();
        var connected = Roster(9).Where(player => player.Item1 != "76561198000000008");

        Assert.True(ReconnectPauseUtility.ShouldPause(match, connected, match.ExpectedPlayerCount()));
    }

    private static List<MatchMember> Members(int from, int count)
    {
        return Enumerable
            .Range(from, count)
            .Select(i => new MatchMember { steam_id = $"7656119800000{i:D4}" })
            .ToList();
    }

    private static IEnumerable<(string, string)> Players(int from, int count)
    {
        return Enumerable.Range(from, count).Select(i => ($"7656119800000{i:D4}", $"player{i}"));
    }

    // A substitute on the roster does not make a lineup of five need six.
    [Fact]
    public void A_Listed_Substitute_Does_Not_Make_A_Whole_Lineup_Look_Short()
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 6) },
            lineup_2 = new MatchLineUp { lineup_players = Members(6, 5) },
        };

        Assert.False(
            ReconnectPauseUtility.ShouldPause(match, Players(0, 5).Concat(Players(6, 5)), 10)
        );
    }

    // One player in the server fills one seat on one lineup: a lineup 1 player
    // whose name starts an unbound seat on lineup 2 does not stand in for it.
    [Fact]
    public void A_Player_Counts_Toward_One_Lineup_Only()
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 5) },
            lineup_2 = new MatchLineUp
            {
                lineup_players = Members(5, 4)
                    .Append(new MatchMember { steam_id = null, placeholder_name = "Maxwell" })
                    .ToList(),
            },
        };
        var connected = Players(0, 9).Select(player =>
            player.Item1 == "76561198000000000" ? (player.Item1, "Max") : player
        );

        Assert.Equal((9, 9), ReconnectPauseUtility.RosterCount(match, connected, 10));
        Assert.Equal(1, LineupMembership.LineupNumber(match, "76561198000000000", "Max"));
    }

    [Fact]
    public void A_Five_Versus_Four_Reads_As_Nine_Of_Nine()
    {
        MatchData match = FiveVersusFour();

        Assert.Equal(
            (9, 9),
            ReconnectPauseUtility.RosterCount(match, Roster(9), match.ExpectedPlayerCount())
        );
    }

    [Fact]
    public void A_Caster_Does_Not_Fill_A_Missing_Players_Seat()
    {
        var connected = Roster(9).Append(("76561198999999999", "caster"));

        Assert.Equal((9, 10), ReconnectPauseUtility.RosterCount(BuildMatch(), connected, 10));
    }

    private static MatchData WithPlaceholders(string lineup1Name, string lineup2Name)
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp
            {
                lineup_players = Members(0, 4)
                    .Append(new MatchMember { steam_id = null, placeholder_name = lineup1Name })
                    .ToList(),
            },
            lineup_2 = new MatchLineUp
            {
                lineup_players = Members(5, 4)
                    .Append(new MatchMember { steam_id = null, placeholder_name = lineup2Name })
                    .ToList(),
            },
        };
    }

    [Fact]
    public void A_Seat_Bound_To_A_Steam_Id_Counts_That_Player()
    {
        MatchData match = WithPlaceholders("Samuel", "Sam");
        match.lineup_2.lineup_players[4].steam_id = "76561198999999992";
        var connected = Players(0, 4)
            .Concat(Players(5, 4))
            .Append(("76561198999999991", "Samuel"))
            .Append(("76561198999999992", "lukepolo"));

        Assert.Equal((9, 9), ReconnectPauseUtility.RosterCount(match, connected, 10));
        Assert.Equal(2, LineupMembership.LineupNumber(match, "76561198999999992", "lukepolo"));
    }

    [Fact]
    public void A_Name_Starting_Placeholders_On_Both_Lineups_Is_On_Neither()
    {
        Assert.Null(
            LineupMembership.LineupNumber(
                WithPlaceholders("Samuel", "Samantha"),
                "76561198999999992",
                "Sam"
            )
        );
    }

    // Only a steam id the api bound to a seat, or the seat's exact name, puts a
    // player on a lineup: a name that merely fits a seat is anyone's to take.
    [Theory]
    [InlineData("Max")]
    [InlineData("lukepolo")]
    [InlineData("[TAG] Luke")]
    [InlineData("Big Luke")]
    [InlineData("luke123")]
    public void A_Name_That_Only_Fits_A_Seat_Puts_Nobody_On_A_Lineup(string steamName)
    {
        Assert.Null(
            LineupMembership.LineupNumber(
                WithPlaceholders("Maxwell", "Luke"),
                "76561198999999992",
                steamName
            )
        );
    }

    [Theory]
    [InlineData("Luke")]
    [InlineData("LUKE")]
    [InlineData(" luke ")]
    public void A_Seats_Exact_Name_Puts_Nobody_On_A_Lineup(string steamName)
    {
        Assert.Null(
            LineupMembership.LineupNumber(
                WithPlaceholders("Bob", "Luke"),
                "76561198999999992",
                steamName
            )
        );
    }

    [Fact]
    public void A_Player_Whose_Name_Only_Fits_A_Seat_Is_Not_Counted()
    {
        var connected = Players(0, 4)
            .Concat(Players(5, 4))
            .Append(("76561198999999991", "Bob"))
            .Append(("76561198999999992", "[TAG] Luke"));

        Assert.Equal(
            (8, 8),
            ReconnectPauseUtility.RosterCount(WithPlaceholders("Bob", "Luke"), connected, 10)
        );
    }

    [Fact]
    public void A_One_Character_Name_Claims_No_Seat()
    {
        Assert.Null(
            LineupMembership.LineupNumber(WithPlaceholders("Bob", "Luke"), "76561198999999992", "L")
        );
    }

    [Fact]
    public void A_Name_Fitting_Seats_On_Both_Lineups_Claims_Neither()
    {
        Assert.Null(
            LineupMembership.LineupNumber(
                WithPlaceholders("Lukewarm", "Big Luke"),
                "76561198999999992",
                "Luke"
            )
        );
    }

    [Fact]
    public void The_Same_Seat_Name_On_Both_Lineups_Claims_Neither()
    {
        Assert.Null(
            LineupMembership.LineupNumber(WithPlaceholders("Sam", "Sam"), "76561198999999992", "Sam")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Check_With_Nothing_Paused_Only_Says_So(bool shortHanded)
    {
        Assert.Equal(
            eRosterCheck.NotPaused,
            ReconnectPauseUtility.Check(null, false, shortHanded, false, true)
        );
    }

    [Theory]
    [InlineData(ePauseReason.Roster, false)]
    [InlineData(ePauseReason.Roster, true)]
    [InlineData(ePauseReason.Restore, false)]
    [InlineData(ePauseReason.Camera, true)]
    [InlineData(ePauseReason.Tech, true)]
    [InlineData(ePauseReason.Admin, false)]
    public void A_Check_With_A_Player_Still_Out_Leaves_Any_Pause_In_Place(
        ePauseReason pause,
        bool held
    )
    {
        Assert.Equal(
            eRosterCheck.Missing,
            ReconnectPauseUtility.Check(pause, true, true, held, true)
        );
    }

    [Fact]
    public void A_Check_That_Finds_Everyone_Back_Ends_The_Pause_The_Roster_Made()
    {
        Assert.Equal(
            eRosterCheck.Resume,
            ReconnectPauseUtility.Check(ePauseReason.Roster, true, false, false, true)
        );
    }

    // A timeout or a pause someone called is theirs to end, whoever is back.
    [Theory]
    [InlineData(ePauseReason.Restore)]
    [InlineData(ePauseReason.Camera)]
    [InlineData(ePauseReason.Tech)]
    [InlineData(ePauseReason.Admin)]
    public void A_Check_Never_Ends_A_Pause_The_Roster_Did_Not_Make(ePauseReason pause)
    {
        Assert.Equal(
            eRosterCheck.Whole,
            ReconnectPauseUtility.Check(pause, true, false, false, true)
        );
    }

    [Fact]
    public void A_Check_Never_Ends_A_Roster_Pause_Something_Else_Still_Holds()
    {
        Assert.Equal(
            eRosterCheck.Whole,
            ReconnectPauseUtility.Check(ePauseReason.Roster, true, false, true, true)
        );
    }

    // A spectator, a caster or a coach is told what a player is told, and the
    // pause is left for the lineups to end.
    [Theory]
    [InlineData(false, eRosterCheck.Whole)]
    [InlineData(true, eRosterCheck.Missing)]
    public void A_Check_By_Someone_In_Neither_Lineup_Never_Ends_The_Rosters_Pause(
        bool shortHanded,
        eRosterCheck told
    )
    {
        Assert.Equal(
            told,
            ReconnectPauseUtility.Check(ePauseReason.Roster, true, shortHanded, false, false)
        );
    }

    // Halftime holds the round for a resume with the map still Live, so no
    // pause reason is on it.
    [Theory]
    [InlineData(false, eRosterCheck.Whole)]
    [InlineData(true, eRosterCheck.Missing)]
    public void A_Check_In_A_Round_Held_With_No_Pause_On_The_Map_Reports_And_Never_Resumes(
        bool shortHanded,
        eRosterCheck told
    )
    {
        Assert.Equal(
            told,
            ReconnectPauseUtility.Check(
                null,
                TimeoutUtility.HasPauseToResume(eMapStatus.Live, true),
                shortHanded,
                false,
                true
            )
        );
    }

    [Fact]
    public void A_Check_In_A_Live_Round_Nothing_Holds_Says_Nothing_Is_Paused()
    {
        Assert.Equal(
            eRosterCheck.NotPaused,
            ReconnectPauseUtility.Check(
                null,
                TimeoutUtility.HasPauseToResume(eMapStatus.Live, false),
                false,
                false,
                true
            )
        );
    }

    // A check decides on the roster count and names players from the seats the
    // not-ready list shows, so the two have to agree on when anyone is out.
    [Fact]
    public void A_Check_Has_A_Player_To_Name_Exactly_When_The_Roster_Is_Short()
    {
        MatchData unbound = BuildMatch(
            new MatchMember { steam_id = null, placeholder_name = "Bob" }
        );
        MatchData substitute = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 6) },
            lineup_2 = new MatchLineUp { lineup_players = Members(6, 5) },
        };

        (MatchData Match, IEnumerable<(string, string)> Connected)[] rosters =
        [
            (BuildMatch(), Roster(10)),
            (BuildMatch(), Roster(9)),
            (BuildMatch(), Roster(9).Append(("76561198999999999", "caster"))),
            (unbound, Roster(9)),
            (unbound, Roster(8)),
            (FiveVersusFour(), Roster(9)),
            (FiveVersusFour(), Roster(8)),
            (substitute, Players(0, 5).Concat(Players(6, 5))),
            (substitute, Players(0, 4).Concat(Players(6, 5))),
            (substitute, Players(1, 5).Concat(Players(6, 4))),
        ];

        foreach ((MatchData match, IEnumerable<(string, string)> connected) in rosters)
        {
            Assert.Equal(
                ReconnectPauseUtility.ShouldPause(match, connected, 10),
                ReadyUtility.EmptySeats(match, connected, 10).Count > 0
            );
        }
    }

    private static ulong SteamId(int seat)
    {
        return ulong.Parse($"7656119800000{seat:D4}");
    }

    private static List<ulong> AbsentToTime(
        MatchData match,
        IEnumerable<(string, string)> connected,
        HashSet<ulong>? leftUntimed = null,
        HashSet<ulong>? timed = null
    )
    {
        return ReconnectPauseUtility.AbsentToTime(
            match,
            connected,
            10,
            leftUntimed ?? new HashSet<ulong>(),
            timed ?? new HashSet<ulong>()
        );
    }

    // A map starting play times whoever a short lineup is missing, however they
    // came to be missing: gone outside play, or never there.
    [Fact]
    public void The_Players_A_Short_Lineup_Is_Missing_Are_Timed()
    {
        Assert.Equal(
            new[] { SteamId(3), SteamId(9) },
            AbsentToTime(BuildMatch(), Players(0, 3).Concat(Players(4, 5)))
        );
    }

    [Fact]
    public void A_Whole_Lineup_Times_Nobody_Its_Substitute_Replaced()
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 6) },
            lineup_2 = new MatchLineUp { lineup_players = Members(6, 5) },
        };

        Assert.Empty(
            AbsentToTime(
                match,
                Players(1, 5).Concat(Players(6, 5)),
                leftUntimed: new HashSet<ulong> { SteamId(0) }
            )
        );
    }

    // With a bench, a seat that is missing its player and a substitute who never
    // came for one look the same, so only those who left are timed.
    [Fact]
    public void A_Short_Lineup_With_A_Bench_Times_Only_Those_Who_Left()
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 6) },
            lineup_2 = new MatchLineUp { lineup_players = Members(6, 5) },
        };
        var connected = Players(1, 4).Concat(Players(6, 5));

        Assert.Empty(AbsentToTime(match, connected));
        Assert.Equal(
            new[] { SteamId(5) },
            AbsentToTime(match, connected, leftUntimed: new HashSet<ulong> { SteamId(5) })
        );
    }

    [Fact]
    public void An_Unbound_Seat_Or_A_Coach_Is_Never_Timed()
    {
        MatchData match = BuildMatch(new MatchMember { steam_id = null, placeholder_name = "Bob" });

        Assert.Equal(new[] { SteamId(8) }, AbsentToTime(match, Roster(8)));

        match.lineup_2.coach_steam_id = $"7656119800000{8:D4}";

        Assert.Empty(AbsentToTime(match, Roster(8)));
    }

    [Fact]
    public void A_Lineup_Nobody_Is_Bound_To_Times_Nobody()
    {
        var match = new MatchData
        {
            lineup_1 = new MatchLineUp { lineup_players = Members(0, 5) },
            lineup_2 = new MatchLineUp
            {
                lineup_players = Enumerable
                    .Range(0, 5)
                    .Select(seat => new MatchMember
                    {
                        steam_id = null,
                        placeholder_name = $"seat{seat}",
                    })
                    .ToList(),
            },
        };

        Assert.Empty(AbsentToTime(match, Roster(5)));
    }

    [Fact]
    public void A_Lineup_Short_By_Design_Times_Nobody()
    {
        Assert.Empty(AbsentToTime(FiveVersusFour(), Roster(9)));
    }

    // A running timer keeps its three minutes, and an abandon or an absence
    // already reported is never reported again.
    [Fact]
    public void A_Player_Already_Timed_Is_Not_Timed_Again()
    {
        Assert.Equal(
            new[] { SteamId(8) },
            AbsentToTime(BuildMatch(), Roster(8), timed: new HashSet<ulong> { SteamId(9) })
        );
    }
}
