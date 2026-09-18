using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class ConnectUtilityTests
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

    private static ConnectDecision Authorize(
        PracticeSessionData? session,
        ulong steamId,
        string? token
    )
    {
        return ConnectUtility.Authorize(
            PracticeConnectUtility.Rules(session, steamId),
            steamId,
            token
        );
    }

    private static string Token(string type, string role, ulong steamId)
    {
        return $"{type}:{role}:{ConnectAuth.ComputeExpectedToken(Password, type, role, steamId, MatchId)}";
    }

    // An unloaded roster must not read as "everyone is welcome".
    [Fact]
    public void WithoutASessionTheEnginesPasswordCheckStays()
    {
        ConnectDecision decision = Authorize(null, Member, "anything");

        Assert.Equal(eConnectAction.PasswordCheck, decision.action);
        Assert.Null(decision.pending_role);
    }

    [Fact]
    public void AConnectWithNoTokenIsRejected()
    {
        ConnectDecision decision = Authorize(Session(), Member, null);

        Assert.Equal(eConnectAction.Reject, decision.action);
    }

    [Fact]
    public void TheSessionPasswordItselfAuthorizes()
    {
        ConnectDecision decision = Authorize(Session(), Stranger, Password);

        Assert.Equal(eConnectAction.Authorized, decision.action);
        Assert.Null(decision.pending_role);
    }

    // The match plugin admits a raw-password connect as a streamer.
    [Fact]
    public void TheServerPasswordCarriesThePasswordRole()
    {
        ConnectRules rules = PracticeConnectUtility.Rules(Session(), Stranger)!;
        rules.password_role = "streamer";

        ConnectDecision decision = ConnectUtility.Authorize(rules, Stranger, Password);

        Assert.Equal(eConnectAction.Authorized, decision.action);
        Assert.Equal("streamer", decision.pending_role);
    }

    // An empty server password must not let an empty token through.
    [Fact]
    public void AnEmptyPasswordNeverMatchesAnEmptyToken()
    {
        var session = Session();
        session.password = "";

        ConnectDecision decision = Authorize(session, Stranger, "");

        Assert.Equal(eConnectAction.Reject, decision.action);
    }

    // The roster is checked before the token, so a player the panel invited
    // gets in whatever their client sent.
    [Fact]
    public void ARosterMemberIsAuthorizedWithoutAValidToken()
    {
        ConnectDecision decision = Authorize(Session(), Member, "garbage");

        Assert.Equal(eConnectAction.Authorized, decision.action);
    }

    [Fact]
    public void ARosterMemberIsAuthorizedWithNoPassword()
    {
        ConnectDecision decision = Authorize(Session(), Member, "");

        Assert.Equal(eConnectAction.Authorized, decision.action);
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
        ConnectDecision decision = Authorize(Session(), Stranger, "game:administrator");

        Assert.Equal(eConnectAction.Reject, decision.action);
    }

    // A plain connect link sends no password at all, so somebody the roster
    // does not know yet is turned away -- and the reason has to say which.
    [Fact]
    public void AStrangerWithNoPasswordIsRejectedForNotBeingOnTheRoster()
    {
        ConnectDecision decision = Authorize(Session(), Stranger, "");

        Assert.Equal(eConnectAction.Reject, decision.action);
        Assert.Contains("not on the roster", decision.reason);
        Assert.Contains("no password", decision.reason);
    }

    [Theory]
    [InlineData("administrator", "admin")]
    [InlineData("streamer", "streamer")]
    [InlineData("match_organizer", "organizer")]
    [InlineData("tournament_organizer", "organizer")]
    public void APrivilegedGameTokenCarriesItsRole(string role, string expected)
    {
        ConnectDecision decision = Authorize(Session(), Stranger, Token("game", role, Stranger));

        Assert.Equal(eConnectAction.Authorized, decision.action);
        Assert.Equal(expected, decision.pending_role);
    }

    [Fact]
    public void AnOrdinaryGameTokenAuthorizesWithNoRole()
    {
        ConnectDecision decision = Authorize(
            Session(),
            Stranger,
            Token("game", "verified_user", Stranger)
        );

        Assert.Equal(eConnectAction.Authorized, decision.action);
        Assert.Null(decision.pending_role);
    }

    // Only "game" tokens hand out roles: a tv connection is still just a
    // spectator.
    [Fact]
    public void ATvTokenNeverCarriesARole()
    {
        ConnectDecision decision = Authorize(
            Session(),
            Stranger,
            Token("tv", "administrator", Stranger)
        );

        Assert.Equal(eConnectAction.Authorized, decision.action);
        Assert.Null(decision.pending_role);
    }

    [Fact]
    public void TheUrlSafeAlphabetIsAccepted()
    {
        string token = Token("game", "administrator", Stranger);
        string urlSafe = token.Replace("+", "-").Replace("/", "_");

        ConnectDecision decision = Authorize(Session(), Stranger, urlSafe);

        Assert.Equal(eConnectAction.Authorized, decision.action);
    }

    // A token signed for somebody else is not proof of anything, but neither is
    // it grounds to refuse: the password may still be right.
    [Fact]
    public void ATokenSignedForAnotherPlayerFallsBackToThePasswordCheck()
    {
        ConnectDecision decision = Authorize(
            Session(),
            Stranger,
            Token("game", "administrator", Member)
        );

        Assert.Equal(eConnectAction.PasswordCheck, decision.action);
    }

    // A bad tv token is different: nothing but the token can authorise a tv
    // connection, so the auth ticket is stripped instead.
    [Fact]
    public void ABadTvTokenIsRejected()
    {
        ConnectDecision decision = Authorize(Session(), Stranger, Token("tv", "streamer", Member));

        Assert.Equal(eConnectAction.Reject, decision.action);
    }

    [Fact]
    public void ATokenSignedWithAnotherSessionsPasswordDoesNotAuthorize()
    {
        var session = Session();
        session.password = "a-different-password";

        ConnectDecision decision = Authorize(
            session,
            Stranger,
            Token("game", "administrator", Stranger)
        );

        Assert.Equal(eConnectAction.PasswordCheck, decision.action);
    }

    [Theory]
    [InlineData(Password)]
    [InlineData("some-other-password")]
    public void TheLogNeverCarriesThePassword(string token)
    {
        string line = Describe(Stranger, token);

        Assert.DoesNotContain(token, line);
        Assert.DoesNotContain(Password, line);
    }

    [Fact]
    public void TheLogNeverCarriesATokenSignature()
    {
        string token = Token("game", "administrator", Member);
        string signature = token.Split(':')[2];

        string line = Describe(Stranger, token);

        Assert.DoesNotContain(signature, line);
        Assert.Contains("game:administrator:", line);
    }

    [Fact]
    public void ARejectedConnectLogsWhoTheRosterExpected()
    {
        string line = Describe(Stranger, "");

        Assert.Contains("Reject", line);
        Assert.Contains("token: empty", line);
        Assert.Contains(Member.ToString(), line);
    }

    private static string Describe(ulong steamId, string? token)
    {
        ConnectRules? rules = PracticeConnectUtility.Rules(Session(), steamId, DateTime.UtcNow);

        return ConnectUtility.Describe(
            rules,
            ConnectUtility.Authorize(rules, steamId, token),
            steamId,
            "player",
            token,
            0,
            true
        );
    }
}
