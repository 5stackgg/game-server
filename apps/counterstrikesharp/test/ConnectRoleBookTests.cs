using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// Roles come from game tokens, which are signed for one player and one match.
public class ConnectRoleBookTests
{
    private const ulong Staff = 76561198000000001;
    private const string Password = "server-password";

    private static readonly Guid MatchA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatchB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ConnectRoleBook Connected(string role, Guid matchId)
    {
        ConnectRoleBook roles = new();
        roles.Offer(Staff, matchId, role);
        roles.Claim(Staff, matchId);

        return roles;
    }

    [Theory]
    [InlineData("administrator", ePlayerRoles.Administrator)]
    [InlineData("match_organizer", ePlayerRoles.MatchOrganizer)]
    [InlineData("tournament_organizer", ePlayerRoles.MatchOrganizer)]
    [InlineData("streamer", ePlayerRoles.Streamer)]
    public void Every_Role_A_Game_Token_Grants_Is_Held(string tokenRole, ePlayerRoles held)
    {
        ConnectRules rules = new() { match_id = MatchA, password = Password };
        string signature = ConnectAuth.ComputeExpectedToken(
            Password,
            "game",
            tokenRole,
            Staff,
            MatchA
        );
        ConnectDecision decision = ConnectUtility.Authorize(
            rules,
            Staff,
            $"game:{tokenRole}:{signature}"
        );
        ConnectRoleBook roles = new();

        roles.Offer(Staff, rules.match_id, decision.pending_role);

        Assert.Equal(decision.pending_role, roles.Claim(Staff, MatchA));
        Assert.Equal(held, roles.RoleIn(Staff, MatchA));
    }

    [Fact]
    public void A_Role_Offered_For_The_Match_Is_Held_Until_They_Leave()
    {
        ConnectRoleBook roles = Connected("organizer", MatchA);

        Assert.Equal(ePlayerRoles.MatchOrganizer, roles.RoleIn(Staff, MatchA));

        roles.Leave(Staff);

        Assert.Null(roles.RoleIn(Staff, MatchA));
    }

    [Fact]
    public void A_Role_Is_Not_Held_Into_The_Next_Match()
    {
        ConnectRoleBook roles = Connected("admin", MatchA);

        Assert.Null(roles.RoleIn(Staff, MatchB));
    }

    [Fact]
    public void No_Role_Is_Held_While_No_Match_Is_Loaded()
    {
        ConnectRoleBook roles = Connected("admin", MatchA);

        Assert.Null(roles.RoleIn(Staff, null));
    }

    [Fact]
    public void A_Role_Offered_For_Another_Match_Is_Not_Claimed()
    {
        ConnectRoleBook roles = new();

        roles.Offer(Staff, MatchA, "admin");

        Assert.Null(roles.Claim(Staff, MatchB));
        Assert.Null(roles.RoleIn(Staff, MatchB));
        Assert.Null(roles.Claim(Staff, MatchA));
    }

    [Fact]
    public void A_Later_Connect_Without_A_Role_Drops_The_Pending_One()
    {
        ConnectRoleBook roles = new();

        roles.Offer(Staff, MatchA, "admin");
        roles.Offer(Staff, MatchA, null);

        Assert.Null(roles.Claim(Staff, MatchA));
    }

    [Fact]
    public void A_Connect_With_No_Match_Loaded_Drops_The_Pending_Role()
    {
        ConnectRoleBook roles = new();

        roles.Offer(Staff, MatchA, "admin");
        roles.Offer(Staff, null, null);

        Assert.Null(roles.Claim(Staff, MatchA));
    }

    [Fact]
    public void A_Connect_Without_A_Role_Leaves_The_Connected_One_Alone()
    {
        ConnectRoleBook roles = Connected("admin", MatchA);

        roles.Offer(Staff, MatchA, null);

        Assert.Equal(ePlayerRoles.Administrator, roles.RoleIn(Staff, MatchA));
    }

    // Reconnecting from the same address fires the old connection's disconnect
    // after the new connect has already offered its role.
    [Fact]
    public void Leaving_Keeps_The_Pending_Role_Of_A_Reconnect()
    {
        ConnectRoleBook roles = new();

        roles.Offer(Staff, MatchA, "admin");
        roles.Leave(Staff);

        Assert.Equal("admin", roles.Claim(Staff, MatchA));
        Assert.Equal(ePlayerRoles.Administrator, roles.RoleIn(Staff, MatchA));
    }

    [Fact]
    public void A_Pending_Role_Is_Claimed_Once()
    {
        ConnectRoleBook roles = new();

        roles.Offer(Staff, MatchA, "admin");

        Assert.Equal("admin", roles.Claim(Staff, MatchA));
        Assert.Null(roles.Claim(Staff, MatchA));
        Assert.Equal(ePlayerRoles.Administrator, roles.RoleIn(Staff, MatchA));
    }
}
