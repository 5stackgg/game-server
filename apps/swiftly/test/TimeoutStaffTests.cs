using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// Staff override the pause, resume, camera and empty-team gates. A lineup
// player's clan tag is their team's short name, which the team owner picks, so
// a team called "admin" is just a lineup player with no connected role.
public class TimeoutStaffTests
{
    private const ulong LineupPlayer = 76561198000000001;
    private const ulong Staff = 76561198000000002;

    private static readonly Guid MatchA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatchB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly eTimeoutSettings[] Settings = Enum.GetValues<eTimeoutSettings>();

    [Fact]
    public void A_Lineup_Player_On_A_Team_Tagged_Admin_Is_Not_Staff()
    {
        ConnectRoleBook roles = new();

        string? connectedRole = roles.RoleIn(LineupPlayer, MatchA);

        Assert.False(TimeoutUtility.IsStaff(connectedRole, "user"));
        Assert.False(
            TimeoutUtility.CanPause(eTimeoutSettings.Admin, connectedRole, "user", false, false)
        );
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public void A_Player_Who_Connected_With_A_Staff_Role_Is_Staff(string role)
    {
        ConnectRoleBook roles = new();
        roles.Offer(Staff, MatchA, role);
        roles.Claim(Staff, MatchA);

        Assert.True(TimeoutUtility.IsStaff(roles.RoleIn(Staff, MatchA), null));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public void A_Staff_Role_From_An_Earlier_Match_Is_Not_Staff(string role)
    {
        ConnectRoleBook roles = new();
        roles.Offer(Staff, MatchA, role);
        roles.Claim(Staff, MatchA);

        string? connectedRole = roles.RoleIn(Staff, MatchB);

        Assert.False(TimeoutUtility.IsStaff(connectedRole, null));
        Assert.False(
            TimeoutUtility.CanPause(eTimeoutSettings.Admin, connectedRole, null, false, false)
        );
    }

    [Fact]
    public void A_Streamer_Is_Not_Staff()
    {
        Assert.False(TimeoutUtility.IsStaff("streamer", null));
        Assert.False(
            TimeoutUtility.CanPause(eTimeoutSettings.Admin, "streamer", null, false, false)
        );
    }

    [Theory]
    [InlineData("administrator", true)]
    [InlineData("match_organizer", true)]
    [InlineData("tournament_organizer", true)]
    [InlineData("streamer", false)]
    [InlineData("verified_user", false)]
    [InlineData("user", false)]
    public void A_Lineup_Players_Own_Role_Decides_Whether_They_Are_Staff(
        string lineupRole,
        bool staff
    )
    {
        Assert.Equal(staff, TimeoutUtility.IsStaff(null, lineupRole));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public void Connected_Staff_Can_Pause_Under_Every_Setting(string role)
    {
        foreach (eTimeoutSettings setting in Settings)
        {
            Assert.True(TimeoutUtility.CanPause(setting, role, null, false, false));
        }
    }

    [Theory]
    [InlineData(eTimeoutSettings.Coach, true, false, true)]
    [InlineData(eTimeoutSettings.Coach, false, true, false)]
    [InlineData(eTimeoutSettings.Coach, false, false, false)]
    [InlineData(eTimeoutSettings.CoachAndCaptains, true, false, true)]
    [InlineData(eTimeoutSettings.CoachAndCaptains, false, true, true)]
    [InlineData(eTimeoutSettings.CoachAndCaptains, false, false, false)]
    [InlineData(eTimeoutSettings.CoachAndPlayers, false, false, true)]
    [InlineData(eTimeoutSettings.Admin, true, true, false)]
    public void Players_Pause_By_Setting(
        eTimeoutSettings setting,
        bool isCoach,
        bool isCaptain,
        bool canPause
    )
    {
        Assert.Equal(canPause, TimeoutUtility.CanPause(setting, null, "user", isCoach, isCaptain));
    }

    [Theory]
    [InlineData(eTimeoutSettings.Admin, true)]
    [InlineData(eTimeoutSettings.Coach, false)]
    [InlineData(eTimeoutSettings.CoachAndCaptains, false)]
    public void A_Lineup_Players_Staff_Role_Only_Pauses_Under_The_Admin_Setting(
        eTimeoutSettings setting,
        bool canPause
    )
    {
        Assert.Equal(
            canPause,
            TimeoutUtility.CanPause(setting, null, "administrator", false, false)
        );
    }
}
