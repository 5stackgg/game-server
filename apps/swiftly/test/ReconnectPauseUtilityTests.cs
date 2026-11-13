using FiveStack.Entities;
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

    [Fact]
    public void A_Placeholder_Member_Counts_Once_Someone_Joins_Under_Its_Name()
    {
        MatchData match = BuildMatch(new MatchMember { steam_id = null, placeholder_name = "Bob" });
        var connected = Roster(9).Append(("76561198999999997", "Bob"));

        Assert.False(ReconnectPauseUtility.ShouldPause(match, connected, 10));
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

    // One player in the server fills one seat: a lineup 1 player whose name starts
    // an unclaimed placeholder on lineup 2 does not stand in for that placeholder.
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

        Assert.True(ReconnectPauseUtility.ShouldPause(match, connected, 10));
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
    public void A_Placeholders_Exact_Name_Wins_Over_A_Longer_One_On_The_Other_Lineup()
    {
        var connected = Players(0, 4)
            .Concat(Players(5, 4))
            .Append(("76561198999999991", "Samuel"))
            .Append(("76561198999999992", "Sam"));

        Assert.Equal(
            (10, 10),
            ReconnectPauseUtility.RosterCount(WithPlaceholders("Samuel", "Sam"), connected, 10)
        );
        Assert.Equal(
            2,
            LineupMembership.LineupNumber(WithPlaceholders("Samuel", "Sam"), "76561198999999992", "Sam")
        );
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

    [Fact]
    public void A_Name_Starting_A_Placeholder_On_One_Lineup_Claims_It()
    {
        Assert.Equal(
            2,
            LineupMembership.LineupNumber(
                WithPlaceholders("Bob", "Maxwell"),
                "76561198999999992",
                "Max"
            )
        );
    }
}
