using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// A match with the halftime break waits for both teams at halftime and before
// each overtime, never at the side swap inside an overtime.
public class BreakUtilityTests
{
    private const int OvertimeMax = 6;

    private static int? MR(int mr, int roundsPlayed)
    {
        return BreakUtility.BreakBefore(roundsPlayed, mr, OvertimeMax, true, true);
    }

    [Theory]
    [InlineData(8, 8, BreakUtility.Halftime)]
    [InlineData(8, 16, 1)]
    [InlineData(8, 22, 2)]
    [InlineData(8, 28, 3)]
    [InlineData(12, 12, BreakUtility.Halftime)]
    [InlineData(12, 24, 1)]
    [InlineData(12, 30, 2)]
    [InlineData(12, 36, 3)]
    [InlineData(12, 42, 4)]
    public void Halftime_And_Each_Overtime_Start_Are_Breaks(int mr, int roundsPlayed, int expected)
    {
        Assert.Equal(expected, MR(mr, roundsPlayed));
    }

    // Overtime swaps sides after half its rounds.
    [Theory]
    [InlineData(8, 19)]
    [InlineData(8, 25)]
    [InlineData(8, 31)]
    [InlineData(12, 27)]
    [InlineData(12, 33)]
    [InlineData(12, 39)]
    public void The_Side_Swap_Inside_An_Overtime_Is_Not_A_Break(int mr, int roundsPlayed)
    {
        Assert.Null(MR(mr, roundsPlayed));
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(8, 1)]
    [InlineData(8, 7)]
    [InlineData(8, 9)]
    [InlineData(8, 15)]
    [InlineData(8, 17)]
    [InlineData(12, 11)]
    [InlineData(12, 13)]
    [InlineData(12, 23)]
    [InlineData(12, 25)]
    public void Other_Rounds_Are_Not_Breaks(int mr, int roundsPlayed)
    {
        Assert.Null(MR(mr, roundsPlayed));
    }

    [Fact]
    public void A_Match_That_Keeps_Sides_Has_No_Halftime_Break()
    {
        Assert.Null(BreakUtility.BreakBefore(12, 12, OvertimeMax, false, true));
        Assert.Equal(1, BreakUtility.BreakBefore(24, 12, OvertimeMax, false, true));
    }

    [Fact]
    public void Without_Overtime_Only_Halftime_Is_A_Break()
    {
        Assert.Equal(BreakUtility.Halftime, BreakUtility.BreakBefore(12, 12, OvertimeMax, true, false));
        Assert.Null(BreakUtility.BreakBefore(24, 12, OvertimeMax, true, false));
        Assert.Null(BreakUtility.BreakBefore(30, 12, OvertimeMax, true, false));
    }

    [Fact]
    public void Other_Overtime_Lengths_Count_From_Regulation()
    {
        Assert.Equal(1, BreakUtility.BreakBefore(24, 12, 4, true, true));
        Assert.Null(BreakUtility.BreakBefore(26, 12, 4, true, true));
        Assert.Equal(2, BreakUtility.BreakBefore(28, 12, 4, true, true));
    }

    [Theory]
    [InlineData(0, 6)]
    [InlineData(-1, 6)]
    [InlineData(12, 0)]
    public void A_Bad_Setting_Has_No_Overtime_Breaks(int mr, int overtimeMax)
    {
        Assert.Null(BreakUtility.BreakBefore(mr * 2, mr, overtimeMax, true, true));
        Assert.Null(BreakUtility.BreakBefore(mr * 2 + 6, mr, overtimeMax, true, true));
    }

    [Fact]
    public void A_Break_Is_Owed_At_Halftime_Whatever_The_Score()
    {
        Assert.True(BreakUtility.Owed(true, true, false, false, false, BreakUtility.Halftime));
        Assert.True(BreakUtility.Owed(true, true, false, false, true, BreakUtility.Halftime));
    }

    // Overtime only starts from a tie; any other score at that round means the
    // map is over.
    [Fact]
    public void An_Overtime_Break_Is_Owed_Only_On_A_Tie()
    {
        Assert.True(BreakUtility.Owed(true, true, false, false, true, 1));
        Assert.True(BreakUtility.Owed(true, true, false, false, true, 3));
        Assert.False(BreakUtility.Owed(true, true, false, false, false, 1));
    }

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void No_Break_When_The_Option_Is_Off_Out_Of_Play_Restoring_Or_Already_Taken(
        bool optionOn,
        bool inPlay,
        bool restoring,
        bool taken
    )
    {
        Assert.False(BreakUtility.Owed(optionOn, inPlay, restoring, taken, true, BreakUtility.Halftime));
        Assert.False(BreakUtility.Owed(optionOn, inPlay, restoring, taken, true, 1));
    }

    [Fact]
    public void No_Break_Off_The_Break_Rounds()
    {
        Assert.False(BreakUtility.Owed(true, true, false, false, true, null));
    }

    [Fact]
    public void A_Player_Or_Coach_Readies_Their_Team()
    {
        Assert.Equal(eBreakReady.Team, BreakUtility.WhoReadies(false, true, true));
        Assert.Equal(eBreakReady.Team, BreakUtility.WhoReadies(false, true, false));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Someone_Off_The_Lineups_Cannot_Ready_A_Team(bool readyCommand)
    {
        Assert.Equal(eBreakReady.Refused, BreakUtility.WhoReadies(false, false, readyCommand));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Staff_Off_The_Lineups_Force_The_Break(bool readyCommand)
    {
        Assert.Equal(eBreakReady.Force, BreakUtility.WhoReadies(true, false, readyCommand));
    }

    [Fact]
    public void Staff_In_A_Lineup_Ready_Their_Team_With_Ready_And_Force_With_Resume()
    {
        Assert.Equal(eBreakReady.Team, BreakUtility.WhoReadies(true, true, true));
        Assert.Equal(eBreakReady.Force, BreakUtility.WhoReadies(true, true, false));
    }

    private const string Player1 = "76561198000000001";
    private const string Player2 = "76561198000000002";
    private const string Coach1 = "76561198000000101";
    private const string Coach2 = "76561198000000102";
    private static readonly Guid Lineup1 = Guid.NewGuid();
    private static readonly Guid Lineup2 = Guid.NewGuid();

    private static MatchData Match(bool coaches)
    {
        MatchData match = new MatchData
        {
            lineup_1_id = Lineup1,
            lineup_2_id = Lineup2,
            lineup_1 = new MatchLineUp { coach_steam_id = Coach1 },
            lineup_2 = new MatchLineUp { coach_steam_id = Coach2 },
        };
        match.options.coaches = coaches;
        match.lineup_1.lineup_players.Add(new MatchMember { steam_id = Player1 });
        match.lineup_2.lineup_players.Add(new MatchMember { steam_id = Player2 });

        return match;
    }

    [Fact]
    public void A_Player_Belongs_To_Their_Lineup()
    {
        Assert.Equal(Lineup1, BreakUtility.LineupOf(Match(false), Player1));
        Assert.Equal(Lineup2, BreakUtility.LineupOf(Match(true), Player2));
    }

    [Fact]
    public void A_Coach_Belongs_To_Their_Lineup_When_The_Match_Has_Coaches()
    {
        Assert.Equal(Lineup1, BreakUtility.LineupOf(Match(true), Coach1));
        Assert.Equal(Lineup2, BreakUtility.LineupOf(Match(true), Coach2));
        Assert.Null(BreakUtility.LineupOf(Match(false), Coach1));
    }

    [Theory]
    [InlineData("76561198000000009")]
    [InlineData("")]
    public void A_Spectator_Belongs_To_No_Lineup(string steamId)
    {
        Assert.Null(BreakUtility.LineupOf(Match(true), steamId));
    }
}
