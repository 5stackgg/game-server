using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// A player who drops out of a LAN match has lost their machine or the network
// far more often than walked out, so a LAN match never calls a surrender vote
// and never reports anyone as having abandoned it. Calling these needs the
// engine, so the tests read the plugin.
public class LanMatchLeaverTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Surrender()
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", "SurrenderSystem.cs")),
            @"//[^\n]*",
            ""
        );
    }

    private static string Method(string source, string signature)
    {
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    // The LAN check has to return on its own: a check that only logs, or a return
    // somewhere later in the method, lets the action run. Offline matches are
    // all LAN, and are left as they were.
    private static readonly Regex LanGuard = new Regex(
        @"if\s*\((?<cond>[^{};]*is_lan[^{};]*)\)\s*\{(?:[^{}]|\{[^{}]*\})*?\breturn;\s*\}"
    );

    private static void ReturnsOnLanBefore(string method, string action)
    {
        Match guard = LanGuard.Match(method);
        int act = method.IndexOf(action, StringComparison.Ordinal);

        Assert.True(guard.Success, "no LAN check returns on its own");
        Assert.True(act >= 0, $"{action} is gone");
        Assert.True(act > guard.Index + guard.Length, $"{action} runs before the LAN check returns");

        string condition = guard.Groups["cond"].Value;

        Assert.DoesNotMatch(@"![\w.?()]*is_lan|is_lan\s*(==\s*false|!=\s*true)", condition);
        Assert.Contains("!_environmentService.IsOfflineMode()", condition);
    }

    [Fact]
    public void A_Lan_Match_Never_Starts_A_Surrender_Vote()
    {
        ReturnsOnLanBefore(
            Method(Surrender(), "public void SetupSurrender("),
            "surrenderingVote.StartVote("
        );
    }

    [Fact]
    public void A_Lan_Match_Never_Times_Out_A_Disconnected_Player()
    {
        ReturnsOnLanBefore(
            Method(Surrender(), "public void SetupDisconnectTimer("),
            "TimerUtility.AddTimer("
        );
    }

    [Fact]
    public void A_Lan_Match_Never_Reports_An_Abandon()
    {
        ReturnsOnLanBefore(
            Method(Surrender(), "public void PlayerAbandonedMatch("),
            "\"abandoned\""
        );
    }
}
