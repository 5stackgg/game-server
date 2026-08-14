using FiveStack;
using Xunit;

public class CameraSystemTests
{
    [Fact]
    public void ParsesACommaSeparatedList()
    {
        var parsed = CameraSystem.ParseSteamIds("76561198000000001,76561198000000002");

        Assert.Equal(
            new HashSet<ulong> { 76561198000000001, 76561198000000002 },
            parsed
        );
    }

    // An empty payload is the all-clear, not a parse failure.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",,")]
    public void TreatsAnEmptyPayloadAsNobodyOffline(string payload)
    {
        Assert.Empty(CameraSystem.ParseSteamIds(payload));
    }

    [Fact]
    public void IgnoresSurroundingWhitespace()
    {
        var parsed = CameraSystem.ParseSteamIds(" 76561198000000001 , 76561198000000002 ");

        Assert.Equal(2, parsed.Count);
    }

    // RCON strips nothing for us, so garbage has to be survivable rather than
    // fatal — a bad message must not wedge the camera system mid-match.
    [Theory]
    [InlineData("not-a-steamid")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("99999999999999999999999999")]
    public void DropsAnythingThatIsNotASteamId(string payload)
    {
        Assert.Empty(CameraSystem.ParseSteamIds(payload));
    }

    [Fact]
    public void KeepsTheValidEntriesAlongsideGarbage()
    {
        var parsed = CameraSystem.ParseSteamIds("76561198000000001,nope,0");

        Assert.Equal(new HashSet<ulong> { 76561198000000001 }, parsed);
    }

    [Fact]
    public void DeduplicatesRepeatedIds()
    {
        var parsed = CameraSystem.ParseSteamIds(
            "76561198000000001,76561198000000001"
        );

        Assert.Single(parsed);
    }
}
