using FiveStack.Utilities;
using Xunit;

public class SteamRelayUtilityTests
{
    private const string Address = "[A:1:3653557264:51859]";

    [Fact]
    public void AServerOffTheRelaySaysSoAndIsUp()
    {
        SteamRelayReport relay = SteamRelayUtility.Report(false, false, () => Address);

        Assert.False(relay.OnRelay);
        Assert.Null(relay.Address);
        Assert.False(relay.Pending);
    }

    [Fact]
    public void AListeningServerReportsItsAddress()
    {
        SteamRelayReport relay = SteamRelayUtility.Report(true, true, () => Address);

        Assert.True(relay.OnRelay);
        Assert.Equal(Address, relay.Address);
        Assert.False(relay.Pending);
    }

    [Fact]
    public void AListeningServerWithNoAddressYetIsNotUp()
    {
        SteamRelayReport relay = SteamRelayUtility.Report(true, true, () => null);

        Assert.True(relay.OnRelay);
        Assert.True(relay.Pending);
    }

    [Fact]
    public void AServerSetUpByHandIsOnTheRelayWithoutTheLauncher()
    {
        SteamRelayReport relay = SteamRelayUtility.Report(true, false, () => Address);

        Assert.True(relay.OnRelay);
        Assert.Equal(Address, relay.Address);
    }

    // The launcher asked for the relay and the engine is not on it: handing
    // out its Steam ID or its host and port would both reach nobody.
    [Fact]
    public void AServerMeantForTheRelayThatIsNotListeningNeverComesUp()
    {
        bool asked = false;

        SteamRelayReport relay = SteamRelayUtility.Report(
            false,
            true,
            () =>
            {
                asked = true;
                return Address;
            }
        );

        Assert.True(relay.OnRelay);
        Assert.Null(relay.Address);
        Assert.True(relay.Pending);
        Assert.False(asked);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData(" true ", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReadsTheLaunchersWord(string? launcher, bool expected)
    {
        Assert.Equal(expected, SteamRelayUtility.Expected(launcher));
    }
}
