using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// A pickup seat is taken by linking Discord on 5stack.gg, so someone in the
// server who could hold one is told how while a seat is still unbound.
public class LineupMembershipTests
{
    private const string Coach1 = "76561198000000101";
    private const string Coach2 = "76561198000000102";
    private const string Player = "76561198000000001";

    private static MatchData BuildMatch()
    {
        return new MatchData
        {
            lineup_1 = new MatchLineUp { coach_steam_id = Coach1 },
            lineup_2 = new MatchLineUp { coach_steam_id = Coach2 },
        };
    }

    [Fact]
    public void A_Player_Could_Hold_A_Seat()
    {
        Assert.True(LineupMembership.CouldHoldASeat(BuildMatch(), Player, null));
    }

    // A staff member who plays a free seat joins with the plain password.
    [Theory]
    [InlineData(ePlayerRoles.Streamer)]
    [InlineData(ePlayerRoles.Administrator)]
    [InlineData(ePlayerRoles.MatchOrganizer)]
    [InlineData(ePlayerRoles.TournamentOrganizer)]
    public void Anyone_Who_Joined_With_A_Role_Is_Not_Offered_A_Seat(ePlayerRoles role)
    {
        Assert.False(LineupMembership.CouldHoldASeat(BuildMatch(), Player, role));
    }

    [Theory]
    [InlineData(Coach1)]
    [InlineData(Coach2)]
    public void A_Lineups_Coach_Is_Not_Offered_A_Seat(string coach)
    {
        Assert.False(LineupMembership.CouldHoldASeat(BuildMatch(), coach, null));
    }

    private static MatchData WithAnUnboundSeat()
    {
        MatchData match = BuildMatch();
        match.lineup_1.lineup_players.Add(new MatchMember { steam_id = Player });
        match.lineup_2.lineup_players.Add(new MatchMember { steam_id = null, placeholder_name = "Luke" });

        return match;
    }

    [Fact]
    public void Someone_Off_The_Lineups_Waits_For_A_Discord_Link_While_A_Seat_Is_Unbound()
    {
        Assert.True(
            LineupMembership.WaitsForADiscordLink(WithAnUnboundSeat(), "76561198000000009", null)
        );
    }

    [Fact]
    public void A_Lineup_Player_Does_Not_Wait()
    {
        Assert.False(LineupMembership.WaitsForADiscordLink(WithAnUnboundSeat(), Player, null));
    }

    [Fact]
    public void Nobody_Waits_Once_Every_Seat_Is_Bound()
    {
        MatchData match = WithAnUnboundSeat();
        match.lineup_2.lineup_players[0].steam_id = "76561198000000002";

        Assert.False(LineupMembership.WaitsForADiscordLink(match, "76561198000000009", null));
    }

    [Fact]
    public void A_Coach_Or_Someone_With_A_Role_Does_Not_Wait()
    {
        Assert.False(LineupMembership.WaitsForADiscordLink(WithAnUnboundSeat(), Coach1, null));
        Assert.False(
            LineupMembership.WaitsForADiscordLink(
                WithAnUnboundSeat(),
                "76561198000000009",
                ePlayerRoles.Streamer
            )
        );
    }
}
