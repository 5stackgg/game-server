using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class PracticeConnectUtilityTests
{
    private static readonly Guid MatchId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private const string Password = "practice-password";
    private const ulong Member = 76561198000000001UL;
    private const ulong Stranger = 76561198000000002UL;

    private static PracticeSessionData Session()
    {
        return new PracticeSessionData
        {
            id = Guid.NewGuid(),
            match_id = MatchId,
            password = Password,
            allowed_steam_ids = new List<string> { Member.ToString() },
        };
    }

    private static string Token(string type, string role, ulong steamId)
    {
        return $"{type}:{role}:{ConnectAuth.ComputeExpectedToken(Password, type, role, steamId, MatchId)}";
    }

    // An unloaded roster must not read as "everyone is welcome".
    [Fact]
    public void WithoutASessionTheEnginesPasswordCheckStays()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(null, Member, "anything");

        Assert.Equal(ePracticeConnect.PasswordCheck, decision.action);
        Assert.Null(decision.pending_role);
    }

    [Fact]
    public void AConnectWithNoTokenIsRejected()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(Session(), Member, null);

        Assert.Equal(ePracticeConnect.Reject, decision.action);
    }

    [Fact]
    public void TheSessionPasswordItselfAuthorizes()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Stranger,
            Password
        );

        Assert.Equal(ePracticeConnect.Authorized, decision.action);
    }

    // The roster is checked before the token, so a player the panel invited
    // gets in whatever their client sent.
    [Fact]
    public void ARosterMemberIsAuthorizedWithoutAValidToken()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Member,
            "garbage"
        );

        Assert.Equal(ePracticeConnect.Authorized, decision.action);
    }

    [Fact]
    public void RosterMatchingIgnoresSurroundingWhitespace()
    {
        var session = Session();
        session.allowed_steam_ids = new List<string> { $"  {Stranger}  " };

        Assert.True(PracticeConnectUtility.IsOnRoster(session, Stranger));
        Assert.False(PracticeConnectUtility.IsOnRoster(session, Member));
    }

    [Fact]
    public void ATokenThatIsNotThreePartsIsRejected()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Stranger,
            "game:administrator"
        );

        Assert.Equal(ePracticeConnect.Reject, decision.action);
    }

    [Theory]
    [InlineData("administrator", "admin")]
    [InlineData("streamer", "streamer")]
    [InlineData("match_organizer", "organizer")]
    [InlineData("tournament_organizer", "organizer")]
    public void APrivilegedGameTokenCarriesItsRole(string role, string expected)
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Stranger,
            Token("game", role, Stranger)
        );

        Assert.Equal(ePracticeConnect.Authorized, decision.action);
        Assert.Equal(expected, decision.pending_role);
    }

    [Fact]
    public void AnOrdinaryGameTokenAuthorizesWithNoRole()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Stranger,
            Token("game", "verified_user", Stranger)
        );

        Assert.Equal(ePracticeConnect.Authorized, decision.action);
        Assert.Null(decision.pending_role);
    }

    // Only "game" tokens hand out roles: a tv connection is still just a
    // spectator.
    [Fact]
    public void ATvTokenNeverCarriesARole()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Stranger,
            Token("tv", "administrator", Stranger)
        );

        Assert.Equal(ePracticeConnect.Authorized, decision.action);
        Assert.Null(decision.pending_role);
    }

    [Fact]
    public void TheUrlSafeAlphabetIsAccepted()
    {
        string token = Token("game", "administrator", Stranger);
        string urlSafe = token.Replace("+", "-").Replace("/", "_");

        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Stranger,
            urlSafe
        );

        Assert.Equal(ePracticeConnect.Authorized, decision.action);
    }

    // A token signed for somebody else is not proof of anything, but neither is
    // it grounds to refuse: the password may still be right.
    [Fact]
    public void ATokenSignedForAnotherPlayerFallsBackToThePasswordCheck()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Stranger,
            Token("game", "administrator", Member)
        );

        Assert.Equal(ePracticeConnect.PasswordCheck, decision.action);
    }

    // A bad tv token is different: nothing but the token can authorise a tv
    // connection, so the auth ticket is stripped instead.
    [Fact]
    public void ABadTvTokenIsRejected()
    {
        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            Session(),
            Stranger,
            Token("tv", "streamer", Member)
        );

        Assert.Equal(ePracticeConnect.Reject, decision.action);
    }

    [Fact]
    public void ATokenSignedWithAnotherSessionsPasswordDoesNotAuthorize()
    {
        var session = Session();
        session.password = "a-different-password";

        PracticeConnectDecision decision = PracticeConnectUtility.Authorize(
            session,
            Stranger,
            Token("game", "administrator", Stranger)
        );

        Assert.Equal(ePracticeConnect.PasswordCheck, decision.action);
    }
}
