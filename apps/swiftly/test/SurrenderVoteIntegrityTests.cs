using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SwiftlyS2.Shared.Players;
using FiveStack;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public class SurrenderVoteIntegrityTests
{
    // A spectator calling surrender opened a vote for spectators that handed
    // the map to CT.
    [Fact]
    public void Nobody_Off_The_Two_Sides_Can_Call_A_Surrender()
    {
        Assert.False(SurrenderSystem.CanCallSurrender(Team.Spectator, false));
        Assert.False(SurrenderSystem.CanCallSurrender(Team.Spectator, true));
        Assert.False(SurrenderSystem.CanCallSurrender(Team.None, true));
    }

    [Fact]
    public void Only_A_Lineup_Player_On_A_Side_Can_Call_One()
    {
        Assert.True(SurrenderSystem.CanCallSurrender(Team.T, true));
        Assert.True(SurrenderSystem.CanCallSurrender(Team.CT, true));
        Assert.False(SurrenderSystem.CanCallSurrender(Team.CT, false));
    }

    private static readonly Guid Lineup1 = Guid.NewGuid();
    private static readonly Guid Lineup2 = Guid.NewGuid();

    [Fact]
    public void The_Lineup_That_Gives_Up_Is_The_One_That_Did_Not_Win()
    {
        var match = new MatchData { lineup_1_id = Lineup1, lineup_2_id = Lineup2 };

        Assert.Equal(Lineup1, SurrenderSystem.SurrenderingLineup(match, Lineup2));
        Assert.Equal(Lineup2, SurrenderSystem.SurrenderingLineup(match, Lineup1));
        Assert.Null(SurrenderSystem.SurrenderingLineup(match, Guid.NewGuid()));
    }

    private static (MatchData match, MatchMap map) Halftime()
    {
        var match = new MatchData
        {
            lineup_1_id = Lineup1,
            lineup_2_id = Lineup2,
            options = new MatchOptions { mr = 12, type = "Competitive" },
        };
        var map = new MatchMap { lineup_1_side = "CT", lineup_2_side = "TERRORIST" };
        return (match, map);
    }

    // Twelve rounds played puts lineup 1 on T by the count, but until the swap its
    // players still stand on CT. One of them calling surrender from CT gives up for
    // lineup 1, not for whoever the count says is on CT.
    [Fact]
    public void A_Surrender_Called_At_Halftime_Before_The_Swap_Is_The_Callers_Lineups()
    {
        var (match, map) = Halftime();

        Assert.Equal(
            Lineup2,
            SurrenderSystem.WinningLineup(match, map, Lineup1, Team.CT, 12)
        );
    }

    [Fact]
    public void A_Timed_Out_Leaver_With_No_Caller_Goes_By_Side()
    {
        var (match, map) = Halftime();

        Assert.Equal(
            Lineup2,
            SurrenderSystem.WinningLineup(match, map, null, Team.T, 12)
        );
    }

    [Fact]
    public void Only_The_Surrendering_Lineups_Players_Vote()
    {
        Assert.True(SurrenderSystem.CanVoteInSurrender(Lineup1, Lineup1));
        Assert.False(SurrenderSystem.CanVoteInSurrender(Lineup2, Lineup1));
    }

    // Coaches, casters and spectators are on no lineup.
    [Fact]
    public void Nobody_Off_The_Lineups_Votes()
    {
        Assert.False(SurrenderSystem.CanVoteInSurrender(null, Lineup1));
    }

    private static string Code(string folder, string file)
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), folder, file)),
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

    // Running the vote needs the engine (timers and player objects), so these two
    // read the plugin: the vote has to be started with the lineup's filter, and
    // the vote has to turn away anyone that filter rejects.
    [Fact]
    public void The_Surrender_Vote_Is_Started_With_The_Lineup_Filter()
    {
        string setup = Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "public void SetupSurrender(");
        int start = setup.IndexOf("surrenderingVote.StartVote(", StringComparison.Ordinal);

        Assert.True(start >= 0, "the surrender vote is never started");
        Assert.Matches(
            @"voterFilter:\s*voter\s*=>\s*CanVoteInSurrender\(\s*VoterLineup\(voter\),\s*surrenderingLineup\.Value\s*\)",
            setup[start..]
        );
    }

    [Fact]
    public void The_Command_Hands_The_Callers_Lineup_To_The_Vote()
    {
        string command = Code("FiveStack.Commands", "Surrender.cs");
        string setup = Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "public void SetupSurrender(");

        Assert.Matches(@"SetupSurrender\([^;]*,\s*callerLineup\)", command);
        Assert.Matches(@"WinningLineup\([^;]*callerLineup,", setup);
    }

    [Fact]
    public void A_Voter_The_Filter_Rejects_Never_Counts()
    {
        string canVote = Method(Code("FiveStack.Services", "VoteSystem.cs"), "private bool CanVote(");

        Assert.Matches(
            @"^private bool CanVote\([^)]*\)\s*\{\s*if \(_voterFilter != null && !_voterFilter\(player\)\)\s*\{\s*return false;\s*\}",
            canVote
        );
    }

    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    // A failed vote reset the whole surrender system, which killed every other
    // leaver's disconnect timer: no abandon, and no vote when it ran out. The
    // timers need the engine, so this reads the plugin.
    [Fact]
    public void A_Failed_Vote_Leaves_Other_Leavers_Timers_Alone()
    {
        string source = Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", "SurrenderSystem.cs")),
            @"//[^\n]*",
            ""
        );
        int failed = source.IndexOf("\"surrender vote failed\"", StringComparison.Ordinal);

        Assert.True(failed >= 0, "the failure callback is gone");

        int end = source.IndexOf("},", failed, StringComparison.Ordinal);
        string callback = source[failed..end];

        Assert.DoesNotContain("Reset()", callback);
        Assert.Contains("surrenderingVote = null;", callback);
    }

    // A caller's own lineup is only taken once the match can say where it plays:
    // with no map, no rounds set or no sides yet, nobody can start a surrender.
    [Fact]
    public void A_Caller_Cannot_Surrender_Before_The_Map_And_Sides_Are_Known()
    {
        var (match, map) = Halftime();

        Assert.Null(SurrenderSystem.WinningLineup(match, null, Lineup1, Team.CT, 0));
        Assert.Null(
            SurrenderSystem.WinningLineup(
                match,
                new MatchMap { lineup_1_side = "", lineup_2_side = "" },
                Lineup1,
                Team.CT,
                0
            )
        );

        match.options.mr = 0;
        Assert.Null(SurrenderSystem.WinningLineup(match, map, Lineup1, Team.CT, 0));
    }

    private static T Bare<T>()
    {
        return (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    }

    private static void Set(object target, string field, object? value)
    {
        target
            .GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);
    }

    private static object? Get(object target, string field)
    {
        return target
            .GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(target);
    }

    private sealed class VoteRequests : IServiceProvider
    {
        public int Asked;

        public object? GetService(Type serviceType)
        {
            Asked++;
            return Bare<VoteSystem>();
        }
    }

    // The plugin's own SurrenderSystem and MatchManager on a map in the given
    // status. The engine is never asked whether warmup is on, so every status
    // but Warmup is only used where the plugin reads nothing but the status.
    private static (SurrenderSystem surrender, MatchManager match, VoteRequests votes) Server(
        eMapStatus status
    )
    {
        EnvironmentService environment = Bare<EnvironmentService>();
        MatchService matchService = Bare<MatchService>();

        MatchEvents events = Bare<MatchEvents>();
        Set(events, "_logger", NullLogger<MatchEvents>.Instance);
        Set(events, "_environmentService", environment);
        Set(events, "_matchService", matchService);
        Set(events, "_mapStatusClock", new MapStatusClock());
        Set(events, "_restore", new RoundRestoreState());

        TimeoutSystem timeout = new TimeoutSystem(
            null!,
            NullLogger<TimeoutSystem>.Instance,
            events,
            null!,
            matchService,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!
        );
        ReadySystem ready = new ReadySystem(
            NullLogger<ReadySystem>.Instance,
            null!,
            matchService,
            null!,
            null!,
            null!,
            null!
        );
        VoteRequests votes = new VoteRequests();
        SurrenderSystem surrender = new SurrenderSystem(
            NullLogger<ReadySystem>.Instance,
            events,
            matchService,
            null!,
            votes,
            environment
        );

        MatchManager match = new MatchManager(
            NullLogger<MatchManager>.Instance,
            null!,
            events,
            null!,
            null!,
            null!,
            null!,
            ready,
            null!,
            environment,
            timeout,
            surrender,
            matchService,
            null!,
            null!,
            null!
        );
        match.Init(
            new MatchData
            {
                id = Guid.NewGuid(),
                lineup_1_id = Lineup1,
                lineup_2_id = Lineup2,
            }
        );
        Set(match, "_currentMapStatus", status);
        Set(matchService, "_currentMatch", match);

        return (surrender, match, votes);
    }

    // Restart Map puts a map being played back in warmup.
    [Fact]
    public void No_Surrender_Vote_Starts_Outside_Play()
    {
        var (surrender, _, votes) = Server(eMapStatus.Warmup);

        Exception? error = Record.Exception(() => surrender.SetupSurrender(Team.T));

        Assert.Equal(0, votes.Asked);
        Assert.Null(error);
        Assert.False(surrender.IsSurrendering());
    }

    [Fact]
    public void A_Surrender_Vote_That_Passes_Outside_Play_Records_Nothing()
    {
        var (surrender, match, _) = Server(eMapStatus.Warmup);

        surrender.Surrender(Lineup2);

        Assert.Equal(eMapStatus.Warmup, match.GetCurrentMapStatus());
        Assert.Null(surrender.GetWinningLineupId());
    }

    // A vote finishing on the end screen handed the map, and in a series the
    // match, to the lineup that had just lost it.
    [Theory]
    [InlineData(eMapStatus.WaitingForTV)]
    [InlineData(eMapStatus.UploadingDemo)]
    [InlineData(eMapStatus.Finished)]
    public void A_Map_That_Is_Over_Is_Never_Surrendered(eMapStatus over)
    {
        var (_, match, _) = Server(over);

        match.UpdateMapStatus(eMapStatus.Surrendered, Lineup2);

        Assert.Equal(over, match.GetCurrentMapStatus());
    }

    [Fact]
    public void A_Map_Being_Played_Can_Still_Be_Surrendered()
    {
        var (_, match, _) = Server(eMapStatus.Live);

        match.UpdateMapStatus(eMapStatus.Surrendered, Lineup2);

        Assert.Equal(eMapStatus.Surrendered, match.GetCurrentMapStatus());
    }

    private const ulong Leaver = 76561198000000001;

    private static Dictionary<Team, Dictionary<ulong, CancellationTokenSource>> LeaverTimers(
        SurrenderSystem surrender
    )
    {
        return (Dictionary<Team, Dictionary<ulong, CancellationTokenSource>>)
            Get(surrender, "_disconnectTimers")!;
    }

    private static Dictionary<Guid, Team> WaitingLineups(SurrenderSystem surrender)
    {
        return (Dictionary<Guid, Team>)Get(surrender, "_waitingLineups")!;
    }

    // A phase change ends the surrender vote, not a leaver's timer: a player who
    // quits just before the map ends is still reported once it runs out.
    [Theory]
    [InlineData(eMapStatus.Live, eMapStatus.WaitingForTV)]
    [InlineData(eMapStatus.WaitingForTV, eMapStatus.UploadingDemo)]
    [InlineData(eMapStatus.UploadingDemo, eMapStatus.Finished)]
    public void The_End_Of_A_Map_Ends_The_Vote_But_Not_The_Leavers_Timers(
        eMapStatus from,
        eMapStatus to
    )
    {
        var (surrender, match, _) = Server(from);
        CancellationTokenSource timer = new CancellationTokenSource();
        LeaverTimers(surrender)[Team.T] = new Dictionary<ulong, CancellationTokenSource>
        {
            [Leaver] = timer,
        };
        WaitingLineups(surrender)[Lineup2] = Team.CT;

        match.UpdateMapStatus(to, Lineup1);

        Assert.Equal(to, match.GetCurrentMapStatus());
        Assert.False(timer.IsCancellationRequested, "the leaver's timer was cancelled");
        Assert.Empty(WaitingLineups(surrender));
    }

    private static string UpdateMapStatusCase(string status)
    {
        string update = Method(Code("FiveStack.Services", "MatchManager.cs"), "public void UpdateMapStatus(");
        int from = update.IndexOf($"case eMapStatus.{status}:", StringComparison.Ordinal);

        Assert.True(from >= 0, $"UpdateMapStatus has no {status} case");

        return update[from..update.IndexOf("break;", from, StringComparison.Ordinal)];
    }

    // Restart Map sends the map back to warmup. Going there needs the engine,
    // so this reads the plugin.
    [Fact]
    public void A_Restarted_Map_Ends_The_Vote_But_Not_The_Leavers_Timers()
    {
        string warmup = UpdateMapStatusCase("Warmup");

        Assert.Contains("_surrenderSystem.EndVote();", warmup);
        Assert.DoesNotContain("_surrenderSystem.Reset();", warmup);
    }

    // A map change goes through MatchManager.Reset. Within the match it ends the
    // vote and keeps the leavers' timers, or a short end flow loses the report.
    [Fact]
    public void A_Map_Change_In_The_Same_Match_Keeps_The_Leavers_Timers()
    {
        string manager = Code("FiveStack.Services", "MatchManager.cs");
        string reset = Method(manager, "public void Reset()");

        Assert.Contains("_surrenderSystem.EndVote();", reset);
        Assert.DoesNotContain("_surrenderSystem.Reset();", reset);
        Assert.DoesNotContain("_surrenderSystem.Reset();", Method(manager, "public void SetupMatch("));
    }

    [Fact]
    public void Only_A_New_Match_Cancels_The_Leavers_Timers()
    {
        var (surrender, _, _) = Server(eMapStatus.Live);
        Guid matchId = Guid.NewGuid();
        surrender.OnMatchSetup(new MatchData { id = matchId });
        CancellationTokenSource timer = new CancellationTokenSource();
        LeaverTimers(surrender)[Team.T] = new Dictionary<ulong, CancellationTokenSource>
        {
            [Leaver] = timer,
        };

        surrender.OnMatchSetup(new MatchData { id = matchId });

        Assert.True(
            LeaverTimers(surrender).GetValueOrDefault(Team.T)?.ContainsKey(Leaver) == true,
            "the next map of the same match cancelled the leaver's timer"
        );

        surrender.OnMatchSetup(new MatchData { id = Guid.NewGuid() });

        Assert.True(timer.IsCancellationRequested, "a new match left the leaver's timer running");
        Assert.Empty(LeaverTimers(surrender));
    }

    // The leaver's timer lives on the surrender system, not on a map.
    [Fact]
    public void A_Reconnect_On_A_Later_Map_Still_Cancels_The_Leavers_Timer()
    {
        Assert.Contains(
            "_surrenderSystem.CancelDisconnectTimer(player.SteamID);",
            Code("FiveStack.Events", "PlayerConnected.cs")
        );
        Assert.Contains(
            "_disconnectTimers.Values",
            Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "private bool KillDisconnectTimer(")
        );
    }

    // Whether the map is in play needs the engine, so the rest read the plugin.
    [Fact]
    public void A_Surrender_Called_Outside_Play_Tells_The_Caller_And_Starts_No_Vote()
    {
        string setup = Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "public void SetupSurrender(");
        var guard = Regex.Match(
            setup,
            @"if \(!IsMapInPlay\(\)\)\s*\{\s*if \(player != null\)\s*\{\s*player\.SendConsole\(_gameServer\.LocalizerFor\(player\)\[""surrender\.not_in_play""\]\);\s*\}\s*return;\s*\}"
        );

        Assert.True(guard.Success, "a surrender is called whether or not the map is in play");
        Assert.True(
            guard.Index < setup.IndexOf("surrenderingVote.StartVote(", StringComparison.Ordinal),
            "the vote starts before the map is checked"
        );
    }

    [Fact]
    public void A_Passed_Vote_Only_Surrenders_A_Map_In_Play()
    {
        string surrender = Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "public void Surrender(Guid lineupId)");

        Assert.Matches(
            @"^public void Surrender\(Guid lineupId\)\s*\{\s*MatchManager\? match = _matchService\.GetCurrentMatch\(\);\s*if \(match == null \|\| !match\.IsInPlay\(\)\)\s*\{[^}]*return;\s*\}",
            surrender
        );
    }

    // A player who joined under a placeholder name has no steam id on the lineup
    // yet, so only their name finds them, and an empty name finds nobody.
    [Fact]
    public void A_Leaver_Is_Found_On_Their_Lineup_By_Their_Name_Too()
    {
        Assert.Contains(
            "MatchUtility.GetMemberFromLineup(matchData, steamId.ToString(), playerName)",
            Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "public void SetupDisconnectTimer(")
        );
    }

    // Outside play the timer still reports a player who is still gone from the
    // same match, but starts no vote.
    [Fact]
    public void A_Leavers_Timer_Reports_Them_Outside_Play_But_Starts_No_Vote()
    {
        string timer = Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "public void SetupDisconnectTimer(");

        Assert.Contains("Guid matchId = matchData.id;", timer);
        Assert.Matches(
            @"TimerUtility\.AddTimer\(\s*60 \* 3,\s*\(\)\s*=>\s*\{\s*ForgetFiredTimer\(team, steamId, timer!\);\s*MatchManager\? current = _matchService\.GetCurrentMatch\(\);\s*if \(\s*current == null\s*\|\|\s*current\.GetMatchData\(\)\?\.id != matchId\s*\|\|\s*MatchUtility\.Players\(\)\.Any\(player => player\.SteamID == steamId\)\s*\)\s*\{\s*return;\s*\}\s*if \(current\.IsInPlay\(\)\)\s*\{\s*OfferLeaverSurrender\(lineupId, team\);\s*\}\s*PlayerAbandonedMatch\(steamId\);\s*\}",
            timer
        );
    }

    // CounterStrikeSharp hands a fired timer's address to the next timer it makes,
    // so killing a fired leaver's timer on a reconnect or reset can kill another.
    [Fact]
    public void A_Fired_Leaver_Timer_Forgets_Itself()
    {
        Assert.Matches(
            @"if \(\s*_disconnectTimers\.TryGetValue\(team, out [^)]*\)\s*&&\s*timers\.GetValueOrDefault\(steamId\) == timer\s*\)\s*\{\s*timers\.Remove\(steamId\);\s*\}",
            Method(Code("FiveStack.Services", "SurrenderSystem.cs"), "private void ForgetFiredTimer(")
        );
    }

    // A passed surrender ended every other leaver's timer, so a player who had
    // quit before it was never reported.
    [Fact]
    public void A_Passed_Surrender_Ends_The_Vote_But_Not_Other_Leavers_Timers()
    {
        string source = Code("FiveStack.Services", "SurrenderSystem.cs");
        int passed = source.IndexOf("\"surrender vote passed\"", StringComparison.Ordinal);

        Assert.True(passed >= 0, "the success callback is gone");

        string callback = source[passed..source.IndexOf("},", passed, StringComparison.Ordinal)];

        Assert.Contains("EndVote();", callback);
        Assert.DoesNotContain("Reset();", callback);
    }

    // The vote already open turned the other lineup's leaver away, so that
    // lineup was never asked.
    [Fact]
    public void A_Leaver_Who_Times_Out_During_Another_Vote_Waits_For_It()
    {
        string source = Code("FiveStack.Services", "SurrenderSystem.cs");

        Assert.Matches(
            @"^private void OfferLeaverSurrender\(Guid\? lineupId, Team sideAtDisconnect\)\s*\{\s*if \(lineupId != null && IsSurrendering\(\)\)\s*\{\s*_waitingLineups\.TryAdd\(lineupId\.Value, sideAtDisconnect\);\s*return;\s*\}",
            Method(source, "private void OfferLeaverSurrender(")
        );
        Assert.Contains("_waitingLineups.Clear();", Method(source, "public void EndVote()"));
    }

    // Setting up the next vote before letting go of the failed one would let go
    // of the new one while it still prompts (#316).
    [Fact]
    public void A_Failed_Vote_Hands_The_Other_Lineup_Its_Waiting_Vote()
    {
        string source = Code("FiveStack.Services", "SurrenderSystem.cs");
        int failed = source.IndexOf("\"surrender vote failed\"", StringComparison.Ordinal);

        Assert.True(failed >= 0, "the failure callback is gone");

        string callback = source[failed..source.IndexOf("},", failed, StringComparison.Ordinal)];
        int dropped = callback.IndexOf("surrenderingVote = null;", StringComparison.Ordinal);
        int offered = callback.IndexOf("OfferWaitingLineup(surrenderingLineup.Value);", StringComparison.Ordinal);

        Assert.True(offered >= 0, "the waiting lineup is never offered its vote");
        Assert.True(dropped >= 0 && dropped < offered, "the failed vote is let go of after the next one is set up");

        string offer = Method(source, "private void OfferWaitingLineup(Guid votedLineup)");
        int forget = offer.IndexOf("_waitingLineups.Remove(votedLineup);", StringComparison.Ordinal);

        Assert.True(forget >= 0, "the lineup that just voted is asked again");
        Assert.True(
            forget < offer.IndexOf("PhaseTimer(", StringComparison.Ordinal),
            "the next vote is offered before the lineup that just voted is forgotten"
        );
    }

    // Offered in the same tick, the next vote's prompt covered the failed one's
    // result before the lineup that had just voted could read it.
    [Fact]
    public void The_Waiting_Lineups_Vote_Comes_A_Few_Seconds_After_The_Failed_One()
    {
        string source = Code("FiveStack.Services", "SurrenderSystem.cs");
        string offer = Method(source, "private void OfferWaitingLineup(Guid votedLineup)");
        var delay = Regex.Match(source, @"const float WaitingVoteDelaySeconds = (\d+);");

        Assert.Matches(
            @"_matchService\.GetCurrentMatch\(\)\?\.PhaseTimer\(\s*WaitingVoteDelaySeconds,\s*OfferNextWaitingLineup\s*\)",
            offer
        );
        Assert.DoesNotContain("OfferLeaverSurrender(", offer);
        Assert.True(delay.Success, "the waiting vote has no delay");
        Assert.InRange(int.Parse(delay.Groups[1].Value), 2, 10);
    }

    // A vote opened in the meantime goes first.
    [Fact]
    public void A_Waiting_Lineup_Waits_For_An_Open_Vote()
    {
        string offer = Method(
            Code("FiveStack.Services", "SurrenderSystem.cs"),
            "private void OfferNextWaitingLineup()"
        );
        var busy = Regex.Match(offer, @"if \([^{]*IsSurrendering\(\)\s*\)\s*\{\s*return;\s*\}");

        Assert.True(busy.Success, "a waiting lineup is offered its vote while another is open");
        Assert.True(
            busy.Index < offer.IndexOf("_waitingLineups.Remove(", StringComparison.Ordinal),
            "the waiting lineup is dropped before an open vote is checked"
        );
        Assert.Contains("OfferLeaverSurrender(next.Key, next.Value);", offer);
    }

    // A substitute in the leaver's seat, or the leaver back while their lineup
    // waited on another vote, makes the lineup whole, and a whole lineup is never
    // asked to give up the map. The leaver's timer still reports them.
    [Fact]
    public void A_Whole_Lineup_Is_Never_Asked_To_Surrender_For_A_Leaver()
    {
        string offer = Method(
            Code("FiveStack.Services", "SurrenderSystem.cs"),
            "private void OfferLeaverSurrender("
        );
        var whole = Regex.Match(
            offer,
            @"if \([^{]*ReconnectPauseUtility\.IsLineupWhole\([^;]*\)\s*\)\s*\{\s*return;\s*\}"
        );

        Assert.True(whole.Success, "a leaver's lineup is asked to surrender however many of its players are in");
        Assert.True(
            whole.Index > offer.IndexOf("_waitingLineups.TryAdd(", StringComparison.Ordinal),
            "a lineup is counted before it waits for an open vote"
        );
        Assert.True(
            whole.Index < offer.IndexOf("SetupSurrender(", StringComparison.Ordinal),
            "the vote is offered before the lineup is counted"
        );
    }

    private static MatchData Roster(int lineup1Players)
    {
        List<MatchMember> Players(int from, int count) =>
            Enumerable
                .Range(from, count)
                .Select(i => new MatchMember { steam_id = $"7656119800000{i:D4}" })
                .ToList();

        return new MatchData
        {
            lineup_1_id = Lineup1,
            lineup_2_id = Lineup2,
            lineup_1 = new MatchLineUp { lineup_players = Players(0, lineup1Players) },
            lineup_2 = new MatchLineUp { lineup_players = Players(5, 5) },
        };
    }

    private static IEnumerable<(string, string)> Connected(params int[] players)
    {
        return players.Select(i => ($"7656119800000{i:D4}", $"player{i}"));
    }

    [Fact]
    public void A_Lineup_With_Its_Players_Back_Is_Whole()
    {
        Assert.True(
            ReconnectPauseUtility.IsLineupWhole(Roster(5), Connected(0, 1, 2, 3, 4), 10, Lineup1)
        );
    }

    [Fact]
    public void A_Lineup_Still_Missing_A_Player_Is_Not_Whole_Whoever_Else_Is_In()
    {
        Assert.False(
            ReconnectPauseUtility.IsLineupWhole(
                Roster(5),
                Connected(0, 1, 2, 3, 5, 6, 7, 8, 9),
                10,
                Lineup1
            )
        );
    }

    [Fact]
    public void A_Lineup_Short_By_Design_Is_Whole_With_All_Its_Players_In()
    {
        Assert.True(ReconnectPauseUtility.IsLineupWhole(Roster(4), Connected(0, 1, 2, 3), 10, Lineup1));
    }
}
