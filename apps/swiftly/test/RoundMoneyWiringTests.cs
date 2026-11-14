using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// Round start and end are engine events, so this reads the plugin: every round
// start takes the baseline, before anything can return, and the score sends it.
public class RoundMoneyWiringTests
{
    private static string Method(
        string file,
        string signature,
        [CallerFilePath] string testFile = ""
    )
    {
        string source = Regex.Replace(
            File.ReadAllText(
                Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src", "FiveStack.Events", file)
            ),
            @"//[^\n]*",
            ""
        );
        int from = source.IndexOf(signature, StringComparison.Ordinal);
        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, $"{signature} is gone");

        return source[from..to];
    }

    [Fact]
    public void EveryRoundStartTakesTheBaselineFirst()
    {
        string handler = Method("RoundStart.cs", "public HookResult OnRoundStart(");
        int baseline = handler.IndexOf(
            "RoundMoney.Baseline(_cashEarnedAtRoundStart, TeamUtility.PlayingMoney());",
            StringComparison.Ordinal
        );

        Assert.True(baseline >= 0, "round start never takes the baseline");
        Assert.True(
            baseline < handler.IndexOf("return ", StringComparison.Ordinal),
            "a round start can return before taking the baseline"
        );
    }

    [Fact]
    public void TheScoreSendsWhatTheRoundEndCaptured()
    {
        string capture = Method("RoundEnd.cs", "private void CaptureRoundResult(");

        Assert.Contains(
            "RoundMoney.Payload(_cashEarnedAtRoundStart, TeamUtility.PlayingMoney())",
            capture
        );
        Assert.Contains("Players = players,", capture);
        Assert.Contains(
            "totalRoundsPlayed >= 1 && !match.IsWarmup() && !match.IsKnife()",
            capture
        );
        Assert.Contains(
            "{ \"players\", snap.Players },",
            Method("RoundEnd.cs", "public void PublishPendingRound(")
        );
    }
}
