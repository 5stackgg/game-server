using FiveStack.Utilities;
using Xunit;

public class SteamIdUtilityTests
{
    [Fact]
    public void ConvertsGameServerSteamId()
    {
        ulong sid64 = (1UL << 56) | (3UL << 52) | (0UL << 32) | 12345UL;
        Assert.Equal("[G:1:12345:0]", SteamIdUtility.ConvertSteamID64ToSteamID(sid64));
    }

    [Fact]
    public void ConvertsIndividualSteamId()
    {
        ulong sid64 = (1UL << 56) | (1UL << 52) | (1UL << 32) | 42UL;
        Assert.Equal("[U:1:42:1]", SteamIdUtility.ConvertSteamID64ToSteamID(sid64));
    }

    [Fact]
    public void ZeroAccountIdReturnsNull()
    {
        ulong sid64 = (1UL << 56) | (3UL << 52);
        Assert.Null(SteamIdUtility.ConvertSteamID64ToSteamID(sid64));
    }

    [Fact]
    public void UnknownAccountTypeFallsBackToI()
    {
        ulong sid64 = (1UL << 56) | (15UL << 52) | 7UL;
        Assert.Equal("[I:1:7:0]", SteamIdUtility.ConvertSteamID64ToSteamID(sid64));
    }
}
