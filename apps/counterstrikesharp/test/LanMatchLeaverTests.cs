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

    private static void ReturnsOnLanBefore(string method, string action)
    {
        int lan = method.IndexOf("is_lan", StringComparison.Ordinal);
        int act = method.IndexOf(action, StringComparison.Ordinal);

        Assert.True(lan >= 0, "nothing checks for a LAN match");
        Assert.True(act > lan, $"{action} runs before the LAN check");
        Assert.Contains("return;", method[lan..act]);
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
