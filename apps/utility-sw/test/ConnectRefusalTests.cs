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
    public void ThePracticeConnectHookRefusesWithThePasswordAndOnlyStripsTheTicketWithoutOne()
    {
        RefusesWithThePasswordBeforeStrippingTheTicket(
            Code("utility-sw", "Events", "PracticeConnect.cs"),
            @"if\s*\(\s*decision\.action == eConnectAction\.Reject\s*&&\s*ConnectRefusal\.ByPassword\(\s*Core\.ConVar\.FindAsString\(""sv_password""\)\?\.ValueAsString\s*\)\s*\)\s*\{\s*return next\(\)\(\s*param1,\s*param2,\s*param3,\s*param4,\s*param5,\s*RefusedPasswordBuffer,\s*param7,\s*param8,\s*param9\s*\);",
            @"if\s*\(decision\.action == eConnectAction\.Reject\)\s*\{\s*return next\(\)\(\s*param1,\s*param2,\s*param3,\s*param4,\s*param5,\s*param6,\s*nint\.Zero,\s*0,\s*param9\s*\);"
        );
    }
}
