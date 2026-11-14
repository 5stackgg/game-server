using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using FiveStack;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// A player who drops out of a LAN or offline match has lost their machine or
// the network far more often than walked out. Their team can still give the map
// up over it, and only then are they reported as having abandoned it. A LAN
// tournament match is never given up: its organizers are told the player is
// absent instead. Which of these a match is, is tested in LeaverRuleUtilityTests.
public class LanMatchLeaverTests
{
    private const ulong Leaver = 76561198000000001;

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

    // The plugin's own SurrenderSystem and MatchEvents on a map that can send
    // events, with no engine behind them.
    private static (SurrenderSystem surrender, MatchEvents events) Server(bool lan, bool tournament)
    {
        EnvironmentService environment = Bare<EnvironmentService>();
        MatchService matchService = Bare<MatchService>();

        MatchManager match = Bare<MatchManager>();
        Set(
            match,
            "_matchData",
            new MatchData
            {
                id = Guid.NewGuid(),
                is_lan = lan,
                is_tournament_match = tournament,
            }
        );
        Set(match, "_activeMapId", Guid.NewGuid());
        Set(matchService, "_currentMatch", match);

        MatchEvents events = Bare<MatchEvents>();
        Set(events, "_logger", NullLogger<MatchEvents>.Instance);
        Set(events, "_environmentService", environment);
        Set(events, "_matchService", matchService);
        Set(events, "_restore", new RoundRestoreState());
        Set(events, "_sent", new ConcurrentDictionary<string, int>());
        Set(
            events,
            "_publishQueue",
            Channel.CreateUnbounded<(
                Guid,
                Guid,
                MatchEvents.EventData<Dictionary<string, object>>
            )>()
        );

        SurrenderSystem surrender = new SurrenderSystem(
            NullLogger<ReadySystem>.Instance,
            events,
            matchService,
            null!,
            null!,
            environment
        );

        return (surrender, events);
    }

    private static HashSet<ulong> TimedOutLeavers(SurrenderSystem surrender)
    {
        return (HashSet<ulong>)
            typeof(SurrenderSystem)
                .GetField("_timedOutLeavers", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(surrender)!;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void An_Online_Leaver_Is_Reported_Once_When_Their_Time_Runs_Out(bool tournament)
    {
        var (surrender, events) = Server(lan: false, tournament: tournament);

        surrender.PlayerAbandonedMatch(Leaver);
        surrender.PlayerAbandonedMatch(Leaver);

        Assert.Equal(1, events.Sent("abandoned"));
        Assert.Equal(0, events.Sent("player-absent"));
        Assert.Empty(TimedOutLeavers(surrender));
    }

    [Fact]
    public void A_Lan_Leaver_Is_Not_Reported_When_Their_Time_Runs_Out()
    {
        var (surrender, events) = Server(lan: true, tournament: false);

        surrender.PlayerAbandonedMatch(Leaver);

        Assert.Equal(0, events.Sent("abandoned"));
        Assert.Equal(0, events.Sent("player-absent"));
        Assert.Equal(Leaver, Assert.Single(TimedOutLeavers(surrender)));
    }

    [Fact]
    public void A_Lan_Leaver_Who_Comes_Back_Is_No_Longer_Waiting_On_A_Surrender()
    {
        var (surrender, events) = Server(lan: true, tournament: false);
        surrender.PlayerAbandonedMatch(Leaver);

        surrender.CancelDisconnectTimer(Leaver);

        Assert.Empty(TimedOutLeavers(surrender));
        Assert.Equal(0, events.Sent("abandoned"));
    }

    [Fact]
    public void A_Lan_Tournament_Leaver_Is_Reported_Absent_Once_And_Never_Abandoned()
    {
        var (surrender, events) = Server(lan: true, tournament: true);

        surrender.PlayerAbandonedMatch(Leaver);
        surrender.PlayerAbandonedMatch(Leaver);

        Assert.Equal(1, events.Sent("player-absent"));
        Assert.Equal(0, events.Sent("abandoned"));
        Assert.Empty(TimedOutLeavers(surrender));
    }

    [Fact]
    public void Only_A_New_Match_Tells_The_Organizers_About_The_Same_Player_Again()
    {
        var (surrender, events) = Server(lan: true, tournament: true);
        Guid matchId = Guid.NewGuid();
        surrender.OnMatchSetup(new MatchData { id = matchId });
        surrender.PlayerAbandonedMatch(Leaver);

        surrender.OnMatchSetup(new MatchData { id = matchId });
        surrender.PlayerAbandonedMatch(Leaver);

        Assert.Equal(1, events.Sent("player-absent"));

        surrender.OnMatchSetup(new MatchData { id = Guid.NewGuid() });
        surrender.PlayerAbandonedMatch(Leaver);

        Assert.Equal(2, events.Sent("player-absent"));
    }

    [Fact]
    public void Only_A_New_Match_Forgets_A_Lan_Leaver_Waiting_On_A_Surrender()
    {
        var (surrender, _) = Server(lan: true, tournament: false);
        Guid matchId = Guid.NewGuid();
        surrender.OnMatchSetup(new MatchData { id = matchId });
        surrender.PlayerAbandonedMatch(Leaver);

        surrender.OnMatchSetup(new MatchData { id = matchId });

        Assert.Equal(Leaver, Assert.Single(TimedOutLeavers(surrender)));

        surrender.OnMatchSetup(new MatchData { id = Guid.NewGuid() });

        Assert.Empty(TimedOutLeavers(surrender));
    }

    // The rest needs the engine, so it reads the plugin.
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

    private static int At(string source, string text)
    {
        int index = source.IndexOf(text, StringComparison.Ordinal);

        Assert.True(index >= 0, $"{text} is gone");

        return index;
    }

    // The timer, the vote and the report read the same rule, so they cannot
    // disagree about the match they are in.
    [Fact]
    public void What_A_Leaver_Costs_Is_Decided_In_One_Place()
    {
        string source = Surrender();

        Assert.Matches(
            @"^private eLeaverRule LeaverRule\(\)\s*\{\s*return LeaverRuleUtility\.For\(\s*_matchService\.GetCurrentMatch\(\)\?\.GetMatchData\(\),\s*_environmentService\.IsOfflineMode\(\)\s*\);\s*$",
            Method(source, "private eLeaverRule LeaverRule()")
        );
        Assert.DoesNotContain("is_lan", source);
        Assert.DoesNotContain("is_tournament_match", source);
        Assert.Equal(2, Regex.Matches(source, @"IsOfflineMode\(\)").Count);
        Assert.Contains(
            "if (_environmentService.IsOfflineMode())",
            Method(source, "public void Surrender(Guid lineupId)")
        );
    }

    // The check has to return on its own: a check that only logs, or a return
    // somewhere later in the method, lets the vote start.
    [Fact]
    public void Only_A_Lan_Tournament_Match_Refuses_A_Surrender_Vote()
    {
        string setup = Method(Surrender(), "public void SetupSurrender(");
        var guard = Regex.Match(
            setup,
            @"if \(LeaverRule\(\) == eLeaverRule\.NoSurrender\)\s*\{(?:[^{}]|\{[^{}]*\})*?\breturn;\s*\}"
        );

        Assert.True(guard.Success, "no LAN tournament check returns on its own");
        Assert.True(
            guard.Index + guard.Length < At(setup, "surrenderingVote.StartVote("),
            "the vote starts before the LAN tournament check returns"
        );
        Assert.Contains("\"surrender.lan\"", guard.Value);
        Assert.Single(Regex.Matches(setup, "eLeaverRule"));
    }

    [Fact]
    public void Every_Match_Times_Out_A_Disconnected_Player()
    {
        string timer = Method(Surrender(), "public void SetupDisconnectTimer(");

        Assert.DoesNotContain("LeaverRule", timer);
        Assert.DoesNotContain("IsOfflineMode", timer);
        Assert.True(
            At(timer, "TimerUtility.AddTimer(") < At(timer, "PlayerAbandonedMatch(steamId);"),
            "a leaver whose time ran out is not handed to the match's rule"
        );
    }

    [Fact]
    public void A_Leaver_Out_Of_Time_Is_Dealt_With_By_The_Matchs_Rule()
    {
        Assert.Matches(
            @"^public void PlayerAbandonedMatch\(ulong steamId\)\s*\{\s*switch \(LeaverRule\(\)\)\s*\{\s*case eLeaverRule\.NoSurrender:\s*ReportAbsent\(steamId\);\s*return;\s*case eLeaverRule\.AbandonOnSurrender:\s*_timedOutLeavers\.Add\(steamId\);\s*return;\s*default:\s*ReportAbandon\(steamId\);\s*return;\s*\}\s*$",
            Method(Surrender(), "public void PlayerAbandonedMatch(")
        );
    }

    [Fact]
    public void An_Abandon_Is_Only_Sent_Once_Per_Match_However_It_Is_Reached()
    {
        string source = Surrender();

        Assert.Single(Regex.Matches(source, "\"abandoned\""));
        Assert.Matches(
            @"^private void ReportAbandon\(ulong steamId\)\s*\{\s*if \(!_reportedAbandons\.Add\(steamId\)\)\s*\{\s*_logger\.LogInformation\([^;]*\);\s*return;\s*\}\s*_matchEvents\.PublishGameEvent\(\s*""abandoned"",",
            Method(source, "private void ReportAbandon(")
        );
        Assert.Equal(2, Regex.Matches(source, @"\bReportAbandon\(steamId\);").Count);
    }

    [Fact]
    public void The_Organizers_Are_Sent_One_Player_Absent_Event()
    {
        string source = Surrender();

        Assert.Single(Regex.Matches(source, "\"player-absent\""));
        Assert.Matches(
            @"^private void ReportAbsent\(ulong steamId\)\s*\{\s*if \(!_reportedAbsences\.Add\(steamId\)\)\s*\{\s*return;\s*\}\s*_matchEvents\.PublishGameEvent\(\s*""player-absent"",\s*new Dictionary<string, object>\s*\{\s*\{ ""time"", DateTime\.Now \},\s*\{ ""steam_id"", steamId\.ToString\(\) \},\s*\}\s*\);\s*$",
            Method(source, "private void ReportAbsent(")
        );
        Assert.Single(Regex.Matches(source, @"\bReportAbsent\(steamId\);"));
    }

    // The api takes no abandon for a match that is over, and the Surrendered
    // status ends it, so the leavers are reported first.
    [Fact]
    public void A_Lan_Or_Offline_Abandon_Is_Reported_As_The_Surrender_Goes_Through()
    {
        string source = Surrender();
        string surrender = Method(source, "public void Surrender(Guid lineupId)");
        int reported = At(surrender, "ReportTimedOutLeavers(match.GetMatchData(), lineupId);");

        Assert.True(
            At(surrender, "return;") < reported,
            "leavers are reported for a surrender that is not carried out"
        );
        Assert.True(
            reported < At(surrender, "match.UpdateMapStatus(eMapStatus.Surrendered, lineupId);"),
            "leavers are reported after the surrender has ended the match"
        );
        Assert.Single(Regex.Matches(source, @"\bReportTimedOutLeavers\(match"));

        string failed = source[At(source, "\"surrender vote failed\"")..];

        Assert.DoesNotContain("Report", failed[..At(failed, "},")]);
    }

    [Fact]
    public void Only_The_Surrendering_Lineups_Leavers_Who_Are_Still_Gone_Are_Reported()
    {
        string report = Method(Surrender(), "private void ReportTimedOutLeavers(");

        Assert.Contains("SurrenderingLineup(matchData, winningLineup)", report);
        Assert.Matches(
            @"if \(matchData == null \|\| surrenderingLineup == null\)\s*\{\s*return;\s*\}\s*foreach \(ulong steamId in _timedOutLeavers\)\s*\{\s*if \(\s*LeaverLineup\(matchData, steamId\.ToString\(\), """"\) == surrenderingLineup\s*&&\s*!MatchUtility\.Players\(\)\.Any\(player => player\.SteamID == steamId\)\s*\)\s*\{\s*ReportAbandon\(steamId\);\s*\}\s*\}\s*$",
            report
        );
    }

    // A vote that fails, or a map that ends, leaves the leaver waiting: a later
    // surrender by their lineup still reports them.
    [Fact]
    public void Only_Coming_Back_Or_A_New_Match_Takes_A_Leaver_Off_The_Wait()
    {
        string source = Surrender();
        string cancel = Method(source, "public void CancelDisconnectTimer(");

        Assert.True(
            At(cancel, "_timedOutLeavers.Remove(steamId);")
                < At(cancel, "if (!KillDisconnectTimer(steamId))"),
            "a leaver whose timer already fired stays waiting once they are back"
        );
        Assert.Single(Regex.Matches(source, @"_timedOutLeavers\.Remove\("));
        Assert.Single(Regex.Matches(source, @"_timedOutLeavers\.Clear\("));
        Assert.Single(Regex.Matches(source, @"_reportedAbsences\.Clear\("));

        string setup = Method(source, "public void OnMatchSetup(");

        Assert.Contains("_timedOutLeavers.Clear();", setup);
        Assert.Contains("_reportedAbsences.Clear();", setup);
    }
}
