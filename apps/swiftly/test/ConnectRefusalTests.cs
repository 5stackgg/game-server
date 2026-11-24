using System.Text.RegularExpressions;
using FiveStack.Utilities;
using Xunit;

// An empty Steam ticket made the engine challenge a refused client again,
// forever, each try holding a slot until the server was full. With a server
// password the client is handed one that can't match, and refused once;
// without one it is let in and kicked with a reason at connect full.
public class ConnectRefusalTests
{
    private static string Code(string app, params string[] path)
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(
                new[] { directory.FullName, "apps", app, "src" }.Concat(path).ToArray()
            );

            if (File.Exists(candidate))
            {
                return Regex.Replace(File.ReadAllText(candidate), @"//[^\n]*", "");
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{path[^1]} was not found above the test output");
    }

    [Theory]
    [InlineData("k8by6zss", true)]
    [InlineData(" none ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("none", false)]
    [InlineData("NONE", false)]
    [InlineData("None", false)]
    [InlineData(ConnectRefusal.Password, false)]
    public void ARefusedClientIsHandedAPasswordOnlyWhenTheServerHasOne(string? serverPassword, bool refuses)
    {
        Assert.Equal(refuses, ConnectRefusal.ByPassword(serverPassword));
    }

    [Fact]
    public void TheRefusedPasswordIsOneNobodyCouldSet()
    {
        Assert.Contains(ConnectRefusal.Password, character => char.IsControl(character));
    }

    [Fact]
    public void TheMatchConnectHookRefusesWithThePasswordAndNeverStripsTheTicket()
    {
        string hook = Code("swiftly", "FiveStack.Events", "InitConnect.cs");

        Assert.Matches(@"bool refusedByPassword =\s*decision\.action == eConnectAction\.Reject\s*&&\s*ConnectRefusal\.ByPassword\(\s*Core\.ConVar\.FindAsString\(""sv_password""\)\?\.ValueAsString\s*\);", hook);
        Assert.Matches(@"if\s*\(refusedByPassword\)\s*\{\s*return next\(\)\(\s*param1,\s*param2,\s*param3,\s*param4,\s*param5,\s*RefusedPasswordBuffer,", hook);
        Assert.Matches(
            @"RefusedPasswordBuffer = Marshal\.StringToCoTaskMemUTF8\(\s*ConnectRefusal\.Password\s*\)",
            hook
        );
        Assert.DoesNotMatch(@"SetParam\(\s*[67]\s*,|nint\.Zero,\s*0,", hook);

        int authorize = hook.IndexOf("ConnectUtility.Authorize(");
        int record = hook.IndexOf("RefusedConnects.Record(");
        Assert.True(authorize >= 0 && record > authorize, "the hook no longer records its decision");
        // Only a refusal the engine does not enforce itself is kicked later.
        Assert.Matches(
            @"RefusedConnects\.Record\(\s*steamId,\s*decision\.action == eConnectAction\.Reject && !refusedByPassword\s*\)",
            hook
        );
        Assert.Contains("RefusedConnects.Clear();", Code("swiftly", "FiveStack.Events", "MapChange.cs"));
    }

    [Fact]
    public void ARefusedClientIsKickedWithAReasonBeforeAnythingElseAtConnectFull()
    {
        string connected = Code("swiftly", "FiveStack.Events", "PlayerConnected.cs");
        string disconnected = Code("swiftly", "FiveStack.Events", "PlayerDisconnected.cs");

        int take = connected.IndexOf("RefusedConnects.Take(");
        Assert.True(take >= 0, "connect full no longer takes refused clients");
        Assert.Contains("RefusedConnects.Take(ClaimedSteamId(", connected);
        Assert.Contains("IsAuthorized ? player.SteamID : player.UnauthorizedSteamID", connected);
        Assert.True(take < connected.IndexOf("ClearCommunicationAbuseMute("));
        Assert.Contains("\"player.join.refused\"", connected);
        Assert.Contains("_refusedKicks.Add(", connected);
        Assert.Contains("_refusedKicks.Remove(ClaimedSteamId(player))", disconnected);
    }

    [Fact]
    public void ARefusalIsKeptOnlyUntilTheNextConnectTakeOrMap()
    {
        var refused = new RefusedConnects();

        refused.Record(76561198000000001, true);
        refused.Record(0, true);
        Assert.True(refused.Take(76561198000000001));
        Assert.False(refused.Take(76561198000000001));
        Assert.False(refused.Take(0));

        refused.Record(76561198000000002, true);
        refused.Record(76561198000000002, false);
        Assert.False(refused.Take(76561198000000002));

        refused.Record(76561198000000003, true);
        refused.Clear();
        Assert.False(refused.Take(76561198000000003));
    }
}
