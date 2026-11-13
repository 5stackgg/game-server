using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// A pickup seat is taken only by linking Discord on 5stack.gg: the api binds it
// to the player's steam id and sends get_match, which brings the bound lineups
// back and puts the player on their team. A name never takes a seat.
public class PlaceholderClaimWiringTests
{
    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Source(params string[] path)
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(new[] { SourceDir() }.Concat(path).ToArray())),
            @"//[^\n]*",
            ""
        );
    }

    private static string Method(string file, string signature)
    {
        string source = Source(file);
        int from = source.IndexOf(signature, StringComparison.Ordinal);
        int to = from < 0 ? -1 : source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, $"{signature} is gone");

        return Regex.Replace(source[from..to], @"\s+", "");
    }

    private static string Manager(string signature)
    {
        return Method(Path.Combine("FiveStack.Services", "MatchManager.cs"), signature);
    }

    private static string Connect()
    {
        return Method(
            Path.Combine("FiveStack.Events", "PlayerConnected.cs"),
            "public HookResult OnPlayerConnect("
        );
    }

    private static string Fetch()
    {
        return Method(
            Path.Combine("FiveStack.Services", "MatchService.cs"),
            "public async void GetMatchFromApi()"
        );
    }

    [Fact]
    public void Connected_Players_Are_Reported_At_Knife_And_Live_Only()
    {
        Assert.Contains(
            "if((status==eMapStatus.Knife||status==eMapStatus.Live)&&MatchUtility.HasPlaceholderMembers(_matchData)){PublishConnectedPlayers(status);}",
            Manager("public void UpdateMapStatus(")
        );
        Assert.DoesNotContain("PublishConnectedPlayers", Connect());
    }

    [Fact]
    public void The_Apis_Answer_To_An_Event_Refreshes_Nothing()
    {
        string monitor = Method(
            Path.Combine("FiveStack.Services", "MatchEvents.cs"),
            "private async Task MonitorConnection()"
        );

        Assert.Contains("_pendingMessages.TryRemove(messageId,out_);", monitor);
        Assert.DoesNotContain("players-connected", monitor);
        Assert.DoesNotContain("GetLineupsFromApi", Source("FiveStack.Services", "MatchService.cs"));
    }

    [Fact]
    public void A_Get_Match_Puts_A_Newly_Bound_Player_On_Their_Team()
    {
        Assert.Contains(
            "if(previousMatchId==matchData.id){_logger.LogInformation($\"Match{matchData.id}stillassigned—re-runningsetup\");_currentMatch!.TakeLineups(matchData,captainsAsOf);_currentMatch.SetupMatch(matchData);return;}",
            Fetch()
        );
    }

    [Fact]
    public void A_Newly_Bound_Player_Is_Put_On_Their_Team()
    {
        string take = Manager("public void TakeLineups(");
        int swap = take.IndexOf("_matchData.lineup_2=matchData.lineup_2;", StringComparison.Ordinal);

        Assert.True(swap >= 0, "the lineups are not taken");
        Assert.True(take.IndexOf("EnforceMemberTeam(player);", StringComparison.Ordinal) > swap);
        Assert.True(take.IndexOf("readySystem.UpdatePlayerStatus();", StringComparison.Ordinal) > swap);
        Assert.DoesNotContain("UpdateMapStatus", take);
    }

    // The api answers with the captains it had when asked: a captain event still
    // unanswered then, or sent since, is not in its answer.
    [Fact]
    public void A_Captain_Change_The_Api_Has_Not_Taken_Survives_A_Get_Match()
    {
        Assert.Contains(
            "return_matchEvents.IsPending(\"captain\")?null:_matchEvents.Sent(\"captain\");",
            Manager("public int? CaptainsAsOf()")
        );
        Assert.Contains(
            "if(captainsAsOf==null||captainsAsOf!=_matchEvents.Sent(\"captain\")){KeepCaptains(matchData);}",
            Manager("public void TakeLineups(MatchData matchData, int? captainsAsOf)")
        );

        string fetch = Fetch();
        int asked = fetch.IndexOf("int?captainsAsOf=_currentMatch?.CaptainsAsOf();", StringComparison.Ordinal);

        Assert.True(
            asked >= 0 && asked < fetch.IndexOf("HttpClientProvider.Client.SendAsync(", StringComparison.Ordinal),
            "the captains are read when the answer comes, not when it was asked for"
        );
    }

    // Two get_match answers can arrive in either order, and taking the older one
    // last would hand a bound seat back to its placeholder.
    [Fact]
    public void An_Answer_Older_Than_The_Last_Taken_Is_Dropped()
    {
        string fetch = Fetch();
        int numbered = fetch.IndexOf("intfetch=_fetches.Request();", StringComparison.Ordinal);

        Assert.True(numbered >= 0, "the request is not numbered");
        Assert.True(
            numbered < fetch.IndexOf("HttpClientProvider.Client.SendAsync(", StringComparison.Ordinal)
        );
        Assert.Contains("if(!_fetches.TryTake(fetch)){", fetch);
    }

    [Fact]
    public void An_Unlinked_Player_Is_Told_Once_How_To_Take_Their_Seat()
    {
        string connect = Connect();
        int told = connect.IndexOf("LineupMembership.WaitsForADiscordLink(", StringComparison.Ordinal);

        Assert.True(told >= 0, "an unlinked player is never told how to take their seat");
        Assert.True(
            told > connect.LastIndexOf("kickid", StringComparison.Ordinal),
            "a player about to be kicked is told to link Discord"
        );
        Assert.Contains("\"player.join.link_discord\"", connect[told..]);
    }
}
