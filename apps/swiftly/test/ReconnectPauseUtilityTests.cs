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
}
