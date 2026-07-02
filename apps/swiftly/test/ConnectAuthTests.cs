using FiveStack.Utilities;
using Xunit;

public class ConnectAuthTests
{
    [Fact]
    public void ComputeExpectedToken_MatchesKnownVector()
    {
        string token = ConnectAuth.ComputeExpectedToken(
            "secret",
            "game",
            "administrator",
            76561198000000001UL,
            Guid.Parse("11111111-1111-1111-1111-111111111111")
        );

        Assert.Equal("S/pFKsULtLNuk90gZa3jiRxsS7aJ4Htx89SzGDkbnNM=", token);
    }

    [Fact]
    public void ComputeExpectedToken_IsDeterministic()
    {
        Guid matchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        string a = ConnectAuth.ComputeExpectedToken("pw", "tv", "streamer", 5UL, matchId);
        string b = ConnectAuth.ComputeExpectedToken("pw", "tv", "streamer", 5UL, matchId);
        Assert.Equal(a, b);
    }

    [Fact]
    public void ComputeExpectedToken_ChangesWithAnyField()
    {
        Guid matchId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        string baseline = ConnectAuth.ComputeExpectedToken("pw", "game", "admin", 5UL, matchId);

        Assert.NotEqual(baseline, ConnectAuth.ComputeExpectedToken("pw2", "game", "admin", 5UL, matchId));
        Assert.NotEqual(baseline, ConnectAuth.ComputeExpectedToken("pw", "tv", "admin", 5UL, matchId));
        Assert.NotEqual(baseline, ConnectAuth.ComputeExpectedToken("pw", "game", "user", 5UL, matchId));
        Assert.NotEqual(baseline, ConnectAuth.ComputeExpectedToken("pw", "game", "admin", 6UL, matchId));
        Assert.NotEqual(
            baseline,
            ConnectAuth.ComputeExpectedToken("pw", "game", "admin", 5UL, Guid.Empty)
        );
    }

    [Fact]
    public void NormalizeClientToken_ConvertsUrlSafeChars()
    {
        Assert.Equal("a+b/c+d/", ConnectAuth.NormalizeClientToken("a-b_c-d_"));
    }

    [Fact]
    public void NormalizeClientToken_LeavesStandardBase64Untouched()
    {
        Assert.Equal("AbC123==", ConnectAuth.NormalizeClientToken("AbC123=="));
    }
}
