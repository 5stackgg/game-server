using FiveStack.Utilities;
using Xunit;

public class GamedataUtilityTests
{
    // The file the plugin ships, copied beside the tests by the project reference.
    private static string Shipped()
    {
        return File.ReadAllText(GamedataUtility.PathIn(AppContext.BaseDirectory));
    }

    [Theory]
    [InlineData("FiveStack_ConnectClient", "engine2", true)]
    [InlineData("FiveStack_ConnectClient", "engine2", false)]
    [InlineData("FiveStack_CCSPlayerController_SetClan", "server", true)]
    [InlineData("FiveStack_CCSPlayerController_SetClan", "server", false)]
    public void EverySignatureAPluginAsksForShipsWithIt(string name, string library, bool linux)
    {
        GamedataSignature? signature = GamedataUtility.Parse(Shipped(), name, linux);

        Assert.NotNull(signature);
        Assert.Equal(library, signature.Library);
        Assert.Matches("^[0-9A-F?]{1,2}( [0-9A-F?]{1,2})+$", signature.Pattern);
    }

    [Fact]
    public void LinuxAndWindowsGetTheirOwnPattern()
    {
        string json = """
            { "Thing": { "signatures": { "library": "server", "linux": "55 48", "windows": "48 89" } } }
            """;

        Assert.Equal("55 48", GamedataUtility.Parse(json, "Thing", linux: true)?.Pattern);
        Assert.Equal("48 89", GamedataUtility.Parse(json, "Thing", linux: false)?.Pattern);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "Thing": { "offsets": { "linux": 24 } } }""")]
    [InlineData("""{ "Thing": { "signatures": { "library": "server", "windows": "48 89" } } }""")]
    [InlineData("""{ "Thing": { "signatures": { "library": "server", "linux": " " } } }""")]
    public void AnEntryWithNoPatternForThisPlatformIsNotFound(string json)
    {
        Assert.Null(GamedataUtility.Parse(json, "Thing", linux: true));
    }
}
