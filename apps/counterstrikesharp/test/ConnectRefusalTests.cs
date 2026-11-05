using System.Text.RegularExpressions;
using FiveStack.Utilities;
using Xunit;

// An empty Steam ticket made the engine challenge a refused client again,
// forever, each try holding a slot until the server was full. With a server
// password the client is handed one that can't match, and refused once.
public class ConnectRefusalTests
{
    private static string Code(params string[] path)
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            string candidate = Path.Combine(
                new[] { directory.FullName, "apps", "counterstrikesharp", "src" }.Concat(path).ToArray()
            );

            if (File.Exists(candidate))
            {
                return Regex.Replace(File.ReadAllText(candidate), @"//[^\n]*", "");
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"{path[^1]} was not found above the test output");
    }

    private static string Between(string source, string from, string to)
    {
        int start = source.IndexOf(from, StringComparison.Ordinal);
        int end = source.IndexOf(to, start + 1, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, $"{from} is gone");

        return source[start..end];
    }

    [Theory]
    [InlineData("k8by6zss", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData(ConnectUtility.RefusedPassword, false)]
    public void ARefusedClientIsHandedAPasswordOnlyWhenTheServerHasOne(string? serverPassword, bool refuses)
    {
        Assert.Equal(refuses, ConnectUtility.RefusesByPassword(serverPassword));
    }

    [Fact]
    public void TheRefusedPasswordIsOneNobodyCouldSet()
    {
        Assert.Contains(ConnectUtility.RefusedPassword, character => char.IsControl(character));
    }

    [Fact]
    public void TheConnectHookRefusesWithThePasswordAndOnlyStripsTheTicketWithoutOne()
    {
        string reject = Between(
            Code("FiveStack.Events", "InitConnect.cs"),
            "case eConnectAction.Reject:",
            "break;"
        );
        int byPassword = reject.IndexOf("hook.SetParam(5, RefusedPasswordBuffer);", StringComparison.Ordinal);
        int stripped = reject.IndexOf("hook.SetParam(6, 0);", StringComparison.Ordinal);

        Assert.Contains("ConnectUtility.RefusesByPassword(ConVar.Find(\"sv_password\")?.StringValue)", reject);
        Assert.True(byPassword >= 0 && stripped > byPassword);
        Assert.Contains("else", reject[byPassword..stripped]);
    }

    [Fact]
    public void APlayerPlacedDuringTheFreezeIsSpawned()
    {
        string placed = Between(
            Code("FiveStack.Services", "MatchManager.cs"),
            "player.ChangeTeam(applyPlacementTeam);",
            "[team] ChangeTeam applied"
        );

        Assert.Contains("IsFreezePeriod()", placed);
        Assert.Contains("!player.PawnIsAlive", placed);
        Assert.Contains("player.Respawn();", placed);
    }
}
