using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// Only the api binds a pickup seat to a steam id, claiming it from the players a
// server reports. Those are reported in warmup, and the bound lineups are taken
// back once the api has answered.
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

    private static string Manager(string signature)
    {
        return Method(Path.Combine("FiveStack.Services", "MatchManager.cs"), signature);
    }

    private static string Service(string signature)
    {
        return Method(Path.Combine("FiveStack.Services", "MatchService.cs"), signature);
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
    public void Connected_Players_Are_Reported_As_Warmup_Starts()
    {
        Assert.Contains(
            "if((status==eMapStatus.Warmup||status==eMapStatus.Knife||status==eMapStatus.Live)&&MatchUtility.HasPlaceholderMembers(_matchData)){PublishConnectedPlayers(status);}",
            Manager("public void UpdateMapStatus(")
        );
    }

    [Fact]
    public void Streamers_Casters_And_Coaches_Are_Not_Offered_A_Seat()
    {
        string publish = Manager("public void PublishConnectedPlayers(");
        int offered = publish.IndexOf("LineupMembership.CouldHoldASeat(", StringComparison.Ordinal);

        Assert.True(offered >= 0, "everyone in the server is offered a seat");
        Assert.Contains("FiveStackPlugin.ConnectRoles.RoleIn(", publish);
        Assert.True(offered < publish.IndexOf("players.Add(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_Apis_Answer_Brings_Back_The_Bound_Lineups()
    {
        string monitor = Method(
            Path.Combine("FiveStack.Services", "MatchEvents.cs"),
            "private async Task MonitorConnection()"
        );

        Assert.Contains("==\"players-connected\"", monitor);
        Assert.Contains("_matchService.GetLineupsFromApi();", monitor);
    }

    // The map status here can be ahead of the api's, so a lineup refresh never
    // runs the match setup again. A failed one is tried once more in the phase.
    [Fact]
    public void A_Lineup_Refresh_Takes_Only_The_Lineups_And_Is_Tried_Once_More()
    {
        string refresh = Service("public async void GetLineupsFromApi(bool retry = true)");

        Assert.Contains(".TakeLineups(", refresh);
        Assert.DoesNotContain("SetupMatch(", refresh);
        Assert.DoesNotContain("Reset()", refresh);
        Assert.Contains("if(retry){", refresh);
        Assert.Contains(".PhaseTimer(", refresh);
        Assert.Contains("GetLineupsFromApi(false)", refresh);
        Assert.DoesNotContain("GetLineupsFromApi(true)", refresh);
        Assert.DoesNotContain("lineupsOnly", Service("public async void GetMatchFromApi()"));
    }

    [Fact]
    public void A_Newly_Bound_Player_Is_Put_On_Their_Team()
    {
        string take = Manager("public void TakeLineups(MatchData matchData)");
        int swap = take.IndexOf("_matchData.lineup_2=matchData.lineup_2;", StringComparison.Ordinal);

        Assert.True(swap >= 0, "the lineups are not taken");
        Assert.True(take.IndexOf("EnforceMemberTeam(player);", StringComparison.Ordinal) > swap);
        Assert.True(take.IndexOf("readySystem.UpdatePlayerStatus();", StringComparison.Ordinal) > swap);
        Assert.DoesNotContain("UpdateMapStatus", take);
    }

    [Fact]
    public void A_Captain_Change_The_Api_Has_Not_Taken_Survives_A_Lineup_Refresh()
    {
        Assert.Contains(
            "_matchEvents.IsPending(\"captain\")",
            Manager("public void TakeLineups(MatchData matchData)")
        );
    }
}
