using System.Text.RegularExpressions;
using FiveStack.Utilities;
using Xunit;

// An empty Steam ticket made the engine challenge a refused client again,
// forever, each try holding a slot until the server was full. With a server
// password the client is handed one that can't match, and refused once.
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

    // Without a password the Swiftly gate lets the client in and kicks it
    // with the reason, so it never strips the ticket.
    [Fact]
    public void TheSwiftlyAccessGateRefusesWithThePasswordAndOtherwiseMarksTheKick()
    {
        string hook = Code("player-management-sw", "ConnectGate.cs");

        Assert.Matches(
            @"if\s*\(\s*access == eServerAccess\.Denied\s*&&\s*ConnectRefusal\.ByPassword\(\s*Core\.ConVar\.Find<string>\(""sv_password""\)\?\.Value\s*\)\s*\)\s*\{\s*return next\(\)\(\s*server,\s*name,\s*address,\s*netInfo,\s*connectMsg,\s*RefusedPasswordBuffer,\s*authTicket,\s*authTicketLength,\s*isLowViolence\s*\);",
            hook
        );
        Assert.DoesNotMatch(@"nint\.Zero,\s*0,\s*isLowViolence", hook);
        Assert.Matches(
            @"_access\.Connecting\(\s*steamId\.ToString\(\),\s*access == eServerAccess\.Denied,",
            hook
        );
    }

    [Fact]
    public void TheCounterStrikeSharpAccessGateRefusesWithThePasswordAndOtherwiseMarksTheKick()
    {
        string hook = Code("player-management-css", "ConnectGate.cs");

        Assert.Matches(
            @"if\s*\(\s*access == eServerAccess\.Denied\s*&&\s*ConnectRefusal\.ByPassword\(ConVar\.Find\(""sv_password""\)\?\.StringValue\)\s*\)\s*\{\s*hook\.SetParam\(5, RefusedPasswordBuffer\);\s*\}",
            hook
        );
        Assert.Matches(
            @"RefusedPasswordBuffer = Marshal\.StringToCoTaskMemUTF8\(\s*ConnectRefusal\.Password\s*\)",
            hook
        );
        Assert.DoesNotMatch(@"hook\.SetParam\(\s*[67]\s*,", hook);
        Assert.Matches(
            @"_access\.Connecting\(\s*steamId\.ToString\(\),\s*access == eServerAccess\.Denied,\s*Humans\(\)\.Any\(",
            hook
        );
        // A banned player coming back is refused the same way, only on a password server.
        Assert.Matches(
            @"_rejoin\.Refuse\(steamId, _book, DateTimeOffset\.UtcNow\)\s*&&\s*ConnectRefusal\.ByPassword\(ConVar\.Find\(""sv_password""\)\?\.StringValue\)\s*\)\s*\{[\s\S]*?hook\.SetParam\(5, RefusedPasswordBuffer\);",
            hook
        );
    }

    // A refused player is kicked even while the sanctions answer is out, and
    // says nothing on the way.
    [Theory]
    [InlineData("player-management-sw")]
    [InlineData("player-management-css")]
    public void ARefusedPlayerIsKickedBeforeTheSanctionsAnswerAndCannotChat(string app)
    {
        string plugin = Code(app, "PlayerManagementPlugin.cs");

        int enforce = plugin.IndexOf("private void Enforce(", StringComparison.Ordinal);
        Assert.True(enforce >= 0, "Enforce is gone or renamed");

        int denied = plugin.IndexOf("_access.IsDenied(steamId.ToString())", enforce, StringComparison.Ordinal);
        int awaiting = plugin.IndexOf("_book.IsAwaiting(steamId.ToString())", enforce, StringComparison.Ordinal);
        Assert.True(denied >= 0 && awaiting >= 0);
        Assert.True(denied < awaiting, "the access kick must come before the awaiting skip");

        Assert.Matches(
            @"if\s*\(_access\.IsDenied\(SteamIdOf\(player\)\.ToString\(\)\)\)\s*\{\s*return HookResult\.Stop;\s*\}\s*SanctionState state = _book\.StateFor\(",
            plugin
        );
    }
}
