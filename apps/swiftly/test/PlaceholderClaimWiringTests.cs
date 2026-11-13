using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// Only the api binds a pickup seat to a steam id, claiming it from the players a
// server reports. Those are reported as players join in warmup, and the bound
// lineups are taken back once the api has answered.
public class PlaceholderClaimWiringTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Method(string file, string signature)
    {
        string source = Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), file)),
            @"//[^\n]*",
            ""
        );
        int from = source.IndexOf(signature, StringComparison.Ordinal);
        int to = from < 0 ? -1 : source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, $"{signature} is gone");

        return Regex.Replace(source[from..to], @"\s+", "");
    }

    [Fact]
    public void Connected_Players_Are_Reported_When_Someone_Joins_In_Warmup()
    {
        string connect = Method(
            Path.Combine("FiveStack.Events", "PlayerConnected.cs"),
            "public HookResult OnPlayerConnect("
        );
        int publish = connect.IndexOf(
            "if(match.IsWarmup()&&MatchUtility.HasPlaceholderMembers(matchData)){match.PublishConnectedPlayers(eMapStatus.Warmup);}",
            StringComparison.Ordinal
        );

        Assert.True(publish >= 0, "joining in warmup reports nobody to the api");
        Assert.True(
            publish > connect.LastIndexOf("kickid", StringComparison.Ordinal),
            "a player about to be kicked is reported"
        );
    }

    [Fact]
    public void The_Apis_Answer_Brings_Back_The_Bound_Lineups()
    {
        string monitor = Method(
            Path.Combine("FiveStack.Services", "MatchEvents.cs"),
            "private async Task MonitorConnection()"
        );

        Assert.Contains("==\"players-connected\"", monitor);
        Assert.Contains("_matchService.GetMatchFromApi(lineupsOnly:true);", monitor);
    }

    // The map status here can be ahead of the api's, so a lineup refresh never
    // runs the match setup again.
    [Fact]
    public void A_Lineup_Refresh_Takes_Only_The_Lineups()
    {
        string fetch = Method(
            Path.Combine("FiveStack.Services", "MatchService.cs"),
            "public async void GetMatchFromApi("
        );
        int lineupsOnly = fetch.IndexOf("if(lineupsOnly){", StringComparison.Ordinal);

        Assert.True(lineupsOnly >= 0, "a lineup refresh runs the whole match setup");
        Assert.True(
            lineupsOnly < fetch.IndexOf("if(matchData==null)", StringComparison.Ordinal),
            "a lineup refresh can clear or set up the match"
        );
        Assert.Contains("_currentMatch!.TakeLineups(matchData);}return;}", fetch);

        string take = Method(
            Path.Combine("FiveStack.Services", "MatchManager.cs"),
            "public void TakeLineups(MatchData matchData)"
        );

        Assert.Contains("_matchData.lineup_1=matchData.lineup_1;", take);
        Assert.Contains("_matchData.lineup_2=matchData.lineup_2;", take);
        Assert.DoesNotContain("UpdateMapStatus", take);
    }
}
