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

    private static void RefusesWithThePasswordBeforeStrippingTheTicket(string hook, string byPassword, string stripped)
    {
        Match password = Regex.Match(hook, byPassword);
        Match ticket = Regex.Match(hook, stripped);

        Assert.True(password.Success, "the refused password path is gone or changed");
        Assert.True(ticket.Success, "the empty ticket path is gone or changed");
        Assert.True(password.Index < ticket.Index);
        Assert.Matches(
            @"RefusedPasswordBuffer = Marshal\.StringToCoTaskMemUTF8\(\s*ConnectRefusal\.Password\s*\)",
            hook
        );
    }

    [Fact]
    public void TheMatchConnectHookRefusesWithThePasswordAndOnlyStripsTheTicketWithoutOne()
    {
        RefusesWithThePasswordBeforeStrippingTheTicket(
            Code("counterstrikesharp", "FiveStack.Events", "InitConnect.cs"),
            @"case eConnectAction\.Reject:\s*if\s*\(ConnectRefusal\.ByPassword\(ConVar\.Find\(""sv_password""\)\?\.StringValue\)\)\s*\{\s*hook\.SetParam\(5, RefusedPasswordBuffer\);\s*\}",
            @"else\s*\{\s*hook\.SetParam\(6, 0\);\s*hook\.SetParam\(7, 0\);\s*\}\s*break;"
        );
    }
}
