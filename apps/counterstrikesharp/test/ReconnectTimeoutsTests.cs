using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

// Off a LAN a missing player holds the match for a technical timeout charged to
// their lineup, and once a lineup has none left the match plays on without them.
public class ReconnectTimeoutsTests
{
    private static readonly Guid MapA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MapB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid Lineup1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Lineup2 = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static MatchData Match(int lineup1 = 5, int lineup2 = 5)
    {
        return new MatchData
        {
            lineup_1_id = Lineup1,
            lineup_2_id = Lineup2,
            lineup_1 = new MatchLineUp { lineup_players = Members(0, lineup1) },
            lineup_2 = new MatchLineUp { lineup_players = Members(5, lineup2) },
        };
    }

    private static List<MatchMember> Members(int from, int count)
    {
        return Enumerable
            .Range(from, count)
            .Select(i => new MatchMember { steam_id = $"7656119800000{i:D4}" })
            .ToList();
    }

    private static IEnumerable<(string, string)> Players(params int[] seats)
    {
        return seats.Select(i => ($"7656119800000{i:D4}", $"player{i}"));
    }

    [Fact]
    public void A_Short_Lineup_Is_Held_For_Until_Its_Timeouts_Are_Spent()
    {
        ReconnectTimeouts timeouts = new();

        Assert.True(timeouts.Charge(MapA, [Lineup1], 2));
        Assert.True(timeouts.Charge(MapA, [Lineup1], 2));
        Assert.False(timeouts.Charge(MapA, [Lineup1], 2));
    }

    [Fact]
    public void One_Lineup_Spending_Its_Timeouts_Leaves_The_Others()
    {
        ReconnectTimeouts timeouts = new();

        Assert.True(timeouts.Charge(MapA, [Lineup1], 1));
        Assert.False(timeouts.Charge(MapA, [Lineup1], 1));
        Assert.True(timeouts.Charge(MapA, [Lineup2], 1));
    }

    [Fact]
    public void Only_One_Lineup_Pays_For_A_Timeout()
    {
        ReconnectTimeouts timeouts = new();

        Assert.True(timeouts.Charge(MapA, [Lineup1, Lineup2], 1));

        Assert.Equal(1, timeouts.Used(Lineup1) + timeouts.Used(Lineup2));
    }

    [Fact]
    public void Two_Short_Lineups_Take_Turns_Until_Both_Are_Spent()
    {
        ReconnectTimeouts timeouts = new();
        List<(int, int)> spent = [];

        while (timeouts.Charge(MapA, [Lineup1, Lineup2], 2))
        {
            spent.Add((timeouts.Used(Lineup1), timeouts.Used(Lineup2)));
        }

        Assert.Equal(new[] { (1, 0), (1, 1), (2, 1), (2, 2) }, spent);
    }

    [Fact]
    public void The_Lineup_That_Has_Spent_Fewer_Pays_Next()
    {
        ReconnectTimeouts timeouts = new();
        timeouts.Charge(MapA, [Lineup2], 2);

        timeouts.Charge(MapA, [Lineup1, Lineup2], 2);

        Assert.Equal((1, 1), (timeouts.Used(Lineup1), timeouts.Used(Lineup2)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_Timeouts_Level_The_Lineup_That_Did_Not_Pay_Last_Goes_First(bool lineup1PaidLast)
    {
        ReconnectTimeouts timeouts = new();
        Guid first = lineup1PaidLast ? Lineup2 : Lineup1;
        Guid last = lineup1PaidLast ? Lineup1 : Lineup2;
        timeouts.Charge(MapA, [first], 2);
        timeouts.Charge(MapA, [last], 2);

        timeouts.Charge(MapA, [Lineup1, Lineup2], 2);

        Assert.Equal(2, timeouts.Used(first));
        Assert.Equal(1, timeouts.Used(last));
    }

    // The lineup is charged at the drop, and its player can be back before the
    // pause lands while the other lineup is still short.
    [Fact]
    public void A_Timeout_Given_Back_Is_Paid_By_Whoever_Is_Short_Now()
    {
        ReconnectTimeouts timeouts = new();
        timeouts.Charge(MapA, [Lineup1], 1);

        timeouts.Refund();
        timeouts.Charge(MapA, [Lineup2], 1);

        Assert.Equal((0, 1), (timeouts.Used(Lineup1), timeouts.Used(Lineup2)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_Timeout_Given_Back_Does_Not_Cost_Its_Lineup_The_Turn(bool lineup1HasTheTurn)
    {
        ReconnectTimeouts timeouts = new();
        Guid turn = lineup1HasTheTurn ? Lineup1 : Lineup2;
        Guid other = lineup1HasTheTurn ? Lineup2 : Lineup1;
        timeouts.Charge(MapA, [turn], 2);
        timeouts.Charge(MapA, [other], 2);
        timeouts.Charge(MapA, [Lineup1, Lineup2], 2);
        timeouts.Refund();

        timeouts.Charge(MapA, [Lineup1, Lineup2], 2);

        Assert.Equal(2, timeouts.Used(turn));
        Assert.Equal(1, timeouts.Used(other));
    }

    // Taking turns alone would hand this one to the lineup that already spent two.
    [Fact]
    public void Spending_Fewer_Counts_Before_Whose_Turn_It_Is()
    {
        ReconnectTimeouts timeouts = new();
        timeouts.Charge(MapA, [Lineup1], 3);
        timeouts.Charge(MapA, [Lineup1], 3);
        timeouts.Charge(MapA, [Lineup2], 3);

        timeouts.Charge(MapA, [Lineup1, Lineup2], 3);

        Assert.Equal((2, 2), (timeouts.Used(Lineup1), timeouts.Used(Lineup2)));
    }

    [Fact]
    public void A_Lineup_With_A_Timeout_Left_Holds_For_One_That_Has_None()
    {
        ReconnectTimeouts timeouts = new();
        timeouts.Charge(MapA, [Lineup1], 1);

        Assert.True(timeouts.Charge(MapA, [Lineup1, Lineup2], 1));
        Assert.False(timeouts.Charge(MapA, [Lineup1, Lineup2], 1));
    }

    [Fact]
    public void Nobody_Short_Or_No_Timeouts_Allowed_Holds_Nothing()
    {
        ReconnectTimeouts timeouts = new();

        Assert.False(timeouts.Charge(MapA, [], 1));
        Assert.False(timeouts.Charge(MapA, [Lineup1], 0));
    }

    [Fact]
    public void A_New_Map_Gives_Every_Lineup_Its_Timeouts_Back()
    {
        ReconnectTimeouts timeouts = new();
        timeouts.Charge(MapA, [Lineup1], 1);

        Assert.True(timeouts.Charge(MapB, [Lineup1], 1));
        Assert.False(timeouts.Charge(MapB, [Lineup1], 1));
    }

    [Fact]
    public void A_Timeout_Whose_Pause_Never_Happened_Is_Given_Back()
    {
        ReconnectTimeouts timeouts = new();
        timeouts.Charge(MapA, [Lineup1, Lineup2], 1);

        timeouts.Refund();

        Assert.Equal(0, timeouts.Used(Lineup1) + timeouts.Used(Lineup2));
        Assert.True(timeouts.Charge(MapA, [Lineup1], 1));
        Assert.True(timeouts.Charge(MapA, [Lineup2], 1));
    }

    [Fact]
    public void Only_The_Last_Timeout_Is_Given_Back_And_Only_Once()
    {
        ReconnectTimeouts timeouts = new();
        timeouts.Charge(MapA, [Lineup1], 2);
        timeouts.Charge(MapA, [Lineup1], 2);

        timeouts.Refund();
        timeouts.Refund();

        Assert.True(timeouts.Charge(MapA, [Lineup1], 2));
        Assert.False(timeouts.Charge(MapA, [Lineup1], 2));
    }

    [Fact]
    public void A_Lineup_That_Paid_Nothing_Is_Given_Nothing_Back()
    {
        ReconnectTimeouts timeouts = new();
        timeouts.Charge(MapA, [Lineup1], 1);
        timeouts.Charge(MapA, [Lineup1, Lineup2], 1);

        timeouts.Refund();

        Assert.False(timeouts.Charge(MapA, [Lineup1], 1));
        Assert.True(timeouts.Charge(MapA, [Lineup2], 1));
    }

    [Fact]
    public void Only_The_Lineup_Missing_A_Player_Is_Short()
    {
        MatchData match = Match();

        Assert.Empty(
            ReconnectPauseUtility.ShortLineups(match, Players(0, 1, 2, 3, 4, 5, 6, 7, 8, 9), 10)
        );
        Assert.Equal(
            new[] { Lineup2 },
            ReconnectPauseUtility.ShortLineups(match, Players(0, 1, 2, 3, 4, 5, 6, 7, 8), 10)
        );
        Assert.Equal(
            new[] { Lineup1, Lineup2 },
            ReconnectPauseUtility.ShortLineups(match, Players(0, 1, 2, 3, 5, 6, 7, 8), 10)
        );
    }

    [Fact]
    public void A_Lineup_Short_By_Design_Is_Not_Short()
    {
        Assert.Empty(
            ReconnectPauseUtility.ShortLineups(Match(lineup2: 4), Players(0, 1, 2, 3, 4, 5, 6, 7, 8), 10)
        );
    }

    [Fact]
    public void A_Lineup_Is_Empty_Only_With_None_Of_Its_Players_In()
    {
        MatchData match = Match();

        Assert.False(ReconnectPauseUtility.IsALineupEmpty(match, Players(0, 5), 10));
        Assert.True(ReconnectPauseUtility.IsALineupEmpty(match, Players(0, 1, 2, 3, 4), 10));
        Assert.True(ReconnectPauseUtility.IsALineupEmpty(match, Players(5, 6), 10));
    }

    [Fact]
    public void A_Lineup_With_No_Seats_To_Fill_Is_Not_Empty()
    {
        Assert.False(ReconnectPauseUtility.IsALineupEmpty(Match(lineup2: 0), Players(0, 1), 10));
    }
}
