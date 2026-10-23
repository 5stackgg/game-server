using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// Staff override the pause, resume, camera and empty-team gates. Only the role
// a player connected with and their own lineup role count, never the clan tag.
public class TimeoutStaffTests
{
    private const ulong LineupPlayer = 76561198000000001;
    private const ulong Staff = 76561198000000002;

    private static readonly Guid MatchA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatchB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly eTimeoutSettings[] Settings = Enum.GetValues<eTimeoutSettings>();

    private static ConnectRoleBook Connected(string role)
    {
        ConnectRoleBook roles = new();
        roles.Offer(Staff, MatchA, role);
        roles.Claim(Staff, MatchA);

        return roles;
    }

    [Fact]
    public void A_Lineup_Player_With_No_Connected_Role_Is_Not_Staff()
    {
        ePlayerRoles? connectedRole = new ConnectRoleBook().RoleIn(LineupPlayer, MatchA);

        Assert.False(TimeoutUtility.IsStaff(connectedRole, ePlayerRoles.User));
        Assert.False(
            TimeoutUtility.CanPause(
                eTimeoutSettings.Admin,
                connectedRole,
                ePlayerRoles.User,
                false,
                false
            )
        );
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public void A_Player_Who_Connected_With_A_Staff_Role_Is_Staff(string role)
    {
        Assert.True(TimeoutUtility.IsStaff(Connected(role).RoleIn(Staff, MatchA), null));
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public void A_Staff_Role_From_An_Earlier_Match_Is_Not_Staff(string role)
    {
        ePlayerRoles? connectedRole = Connected(role).RoleIn(Staff, MatchB);

        Assert.False(TimeoutUtility.IsStaff(connectedRole, null));
        Assert.False(
            TimeoutUtility.CanPause(eTimeoutSettings.Admin, connectedRole, null, false, false)
        );
    }

    [Fact]
    public void A_Streamer_Is_Not_Staff()
    {
        ePlayerRoles? connectedRole = Connected("streamer").RoleIn(Staff, MatchA);

        Assert.False(TimeoutUtility.IsStaff(connectedRole, null));
        Assert.False(
            TimeoutUtility.CanPause(eTimeoutSettings.Admin, connectedRole, null, false, false)
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
        Assert.Equal(
            staff,
            TimeoutUtility.IsStaff(null, PlayerRoleUtility.PlayerRoleStringToEnum(lineupRole))
        );
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("organizer")]
    public void Connected_Staff_Can_Pause_Under_Every_Setting(string role)
    {
        ePlayerRoles? connectedRole = Connected(role).RoleIn(Staff, MatchA);

        foreach (eTimeoutSettings setting in Settings)
        {
            Assert.True(TimeoutUtility.CanPause(setting, connectedRole, null, false, false));
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
        Assert.Equal(
            canPause,
            TimeoutUtility.CanPause(setting, null, ePlayerRoles.User, isCoach, isCaptain)
        );
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
            TimeoutUtility.CanPause(setting, null, ePlayerRoles.Administrator, false, false)
        );
    }
}
