using FiveStack.Utilities;
using Xunit;

// Roles come from game tokens, which are signed for one player and one match.
public class ConnectRoleBookTests
{
    private const ulong Staff = 76561198000000001;

    private static readonly Guid MatchA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatchB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void A_Role_Offered_For_The_Match_Is_Held_Until_They_Leave()
    {
        ConnectRoleBook roles = new();

        roles.Offer(Staff, MatchA, "organizer");

        Assert.Equal("organizer", roles.Claim(Staff, MatchA));
        Assert.Equal("organizer", roles.RoleIn(Staff, MatchA));

        roles.Leave(Staff);

        Assert.Null(roles.RoleIn(Staff, MatchA));
    }

    [Fact]
    public void A_Role_Is_Not_Held_Into_The_Next_Match()
    {
        ConnectRoleBook roles = new();

        roles.Offer(Staff, MatchA, "admin");
        roles.Claim(Staff, MatchA);

        Assert.Null(roles.RoleIn(Staff, MatchB));
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
    public void A_Pending_Role_Is_Claimed_Once()
    {
        ConnectRoleBook roles = new();

        roles.Offer(Staff, MatchA, "admin");

        Assert.Equal("admin", roles.Claim(Staff, MatchA));
        Assert.Null(roles.Claim(Staff, MatchA));
        Assert.Equal("admin", roles.RoleIn(Staff, MatchA));
    }
}
