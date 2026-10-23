using FiveStack.Utilities;
using Xunit;

// The api is told about everyone it was told connected, not only the lineups:
// admins, organizers and streamers, and anyone at all on a match with open spots.
public class PlayerPresenceUtilityTests
{
    private const ulong Organizer = 76561198000000001;
    private const ulong LineupPlayer = 76561198000000002;
    private const ulong Kicked = 76561198000000003;

    [Fact]
    public void A_Player_Outside_The_Lineups_Reported_Connected_Is_Reported_Leaving()
    {
        HashSet<ulong> reported = new() { Organizer };

        Assert.True(PlayerPresenceUtility.ShouldReportDisconnect(reported, Organizer, false));
    }

    [Fact]
    public void A_Player_Outside_The_Lineups_Is_Reported_Leaving_Once()
    {
        HashSet<ulong> reported = new() { Organizer };

        Assert.True(PlayerPresenceUtility.ShouldReportDisconnect(reported, Organizer, false));
        Assert.False(PlayerPresenceUtility.ShouldReportDisconnect(reported, Organizer, false));
    }

    [Fact]
    public void A_Player_Kicked_Before_Being_Reported_Is_Not_Reported_Leaving()
    {
        HashSet<ulong> reported = new() { Organizer };

        Assert.False(PlayerPresenceUtility.ShouldReportDisconnect(reported, Kicked, false));
        Assert.Contains(Organizer, reported);
    }

    [Fact]
    public void A_Lineup_Member_Is_Reported_Leaving_Even_If_Their_Connect_Was_Not()
    {
        HashSet<ulong> reported = new();

        Assert.True(PlayerPresenceUtility.ShouldReportDisconnect(reported, LineupPlayer, true));
    }
}
