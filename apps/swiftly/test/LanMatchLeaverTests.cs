using System.Collections;
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

    // Lineup 1 is the leaver, four more and a substitute. Lineup 2 is five.
    private static MatchData Lineups()
    {
        List<MatchMember> Seats(int from, int count) =>
            Enumerable
                .Range(from, count)
                .Select(seat => new MatchMember { steam_id = $"765611980000000{seat:D2}" })
                .ToList();

        return new MatchData
        {
            lineup_1_id = Guid.NewGuid(),
            lineup_2_id = Guid.NewGuid(),
            lineup_1 = new MatchLineUp { lineup_players = Seats(1, 6) },
            lineup_2 = new MatchLineUp { lineup_players = Seats(11, 5) },
        };
    }

    private static IEnumerable<(string, string)> Connected(params int[] seats)
    {
        return seats.Select(seat => ($"765611980000000{seat:D2}", $"player{seat}"));
    }

    private static HashSet<ulong> TimedOutLeavers(SurrenderSystem surrender)
    {
        return (HashSet<ulong>)
            typeof(SurrenderSystem)
                .GetField("_timedOutLeavers", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(surrender)!;
    }

    private static readonly FieldInfo DisconnectTimers = typeof(SurrenderSystem).GetField(
        "_disconnectTimers",
        BindingFlags.NonPublic | BindingFlags.Instance
    )!;

    // Leavers' timers as the plugin files them while they run. Only the engine
    // can make a timer, so each is filed empty.
    private static void RunTimers(SurrenderSystem surrender, params ulong[] steamIds)
    {
        Type[] filed = DisconnectTimers.FieldType.GetGenericArguments();
        IDictionary timers = (IDictionary)Activator.CreateInstance(filed[1])!;

        foreach (ulong steamId in steamIds)
        {
            timers[steamId] = null;
        }

        IDictionary sides = (IDictionary)DisconnectTimers.GetValue(surrender)!;

        sides[Activator.CreateInstance(filed[0])!] = timers;
    }

    private static bool HasTimer(SurrenderSystem surrender, ulong steamId)
    {
        return ((IDictionary)DisconnectTimers.GetValue(surrender)!)
            .Values.Cast<IDictionary>()
            .Any(timers => timers.Contains(steamId));
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

    // The leaver is kicked for a full lineup when they try to come back, so
    // coming back never takes them off the wait.
    [Fact]
    public void A_Lan_Leaver_A_Substitute_Replaced_Is_No_Longer_Waiting_On_A_Surrender()
    {
        var (surrender, events) = Server(lan: true, tournament: false);
        surrender.PlayerAbandonedMatch(Leaver);

        surrender.ForgetReplacedLeavers(
            Lineups(),
            Connected(2, 3, 4, 5, 6, 11, 12, 13, 14, 15),
            10
        );

        Assert.Empty(TimedOutLeavers(surrender));
        Assert.Equal(0, events.Sent("abandoned"));
    }

    [Fact]
    public void A_Lan_Leaver_Keeps_Waiting_While_Only_The_Other_Lineup_Is_Whole()
    {
        var (surrender, _) = Server(lan: true, tournament: false);
        surrender.PlayerAbandonedMatch(Leaver);

        surrender.ForgetReplacedLeavers(Lineups(), Connected(2, 3, 4, 5, 11, 12, 13, 14, 15), 10);

        Assert.Equal(Leaver, Assert.Single(TimedOutLeavers(surrender)));
    }

    [Fact]
    public void A_Whole_Lineup_Only_Takes_Its_Own_Leavers_Off_The_Wait()
    {
        const ulong otherLineupsLeaver = 76561198000000011;
        var (surrender, _) = Server(lan: true, tournament: false);
        surrender.PlayerAbandonedMatch(Leaver);
        surrender.PlayerAbandonedMatch(otherLineupsLeaver);

        surrender.ForgetReplacedLeavers(Lineups(), Connected(2, 3, 4, 5, 6, 12, 13, 14, 15), 10);

        Assert.Equal(otherLineupsLeaver, Assert.Single(TimedOutLeavers(surrender)));
    }

    // A substitute joins, then someone else drops: the first leaver's three
    // minutes would run out into a lineup that is short over the second.
    [Fact]
    public void Only_A_Whole_Lineups_Lan_Leavers_Stop_Running_Out_Of_Time()
    {
        const ulong otherLineupsLeaver = 76561198000000011;
        var (surrender, _) = Server(lan: true, tournament: false);
        RunTimers(surrender, Leaver, otherLineupsLeaver);

        surrender.ForgetReplacedLeavers(Lineups(), Connected(2, 3, 4, 5, 6, 12, 13, 14, 15), 10);

        Assert.False(HasTimer(surrender, Leaver));
        Assert.True(HasTimer(surrender, otherLineupsLeaver));
    }

    // Online the timer is what reports the leaver, and in a LAN tournament
    // what tells the organizers, whoever has the seat by then.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void A_Replaced_Leaver_Keeps_Running_Out_Of_Time_Where_That_Reports_Them(
        bool lan,
        bool tournament
    )
    {
        var (surrender, _) = Server(lan: lan, tournament: tournament);
        RunTimers(surrender, Leaver);

        surrender.ForgetReplacedLeavers(
            Lineups(),
            Connected(2, 3, 4, 5, 6, 11, 12, 13, 14, 15),
            10
        );

        Assert.True(HasTimer(surrender, Leaver));
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

    // Three minutes away in warmup must not use up the alert for the round
    // their machine dies in.
    [Fact]
    public void A_Lan_Tournament_Player_Who_Came_Back_Is_Reported_Absent_The_Next_Time_Too()
    {
        var (surrender, events) = Server(lan: true, tournament: true);
        surrender.PlayerAbandonedMatch(Leaver);

        surrender.CancelDisconnectTimer(Leaver);
        surrender.PlayerAbandonedMatch(Leaver);
        surrender.PlayerAbandonedMatch(Leaver);

        Assert.Equal(2, events.Sent("player-absent"));
        Assert.Equal(0, events.Sent("abandoned"));
    }

    // The api lengthens the ban on every abandon it is sent.
    [Fact]
    public void An_Online_Leaver_Who_Came_Back_Is_Not_Reported_Again()
    {
        var (surrender, events) = Server(lan: false, tournament: false);
        surrender.PlayerAbandonedMatch(Leaver);

        surrender.CancelDisconnectTimer(Leaver);
        surrender.PlayerAbandonedMatch(Leaver);

        Assert.Equal(1, events.Sent("abandoned"));
    }

    [Fact]
    public void The_Same_Match_Loading_Again_Tells_The_Organizers_Nothing_A_New_One_Does()
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

    private static string Code(string folder, string file)
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), folder, file)),
            @"//[^\n]*",
            ""
        );
    }

    private static string Surrender()
    {
        return Code("FiveStack.Services", "SurrenderSystem.cs");
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

    // Which drops are timed is in LeaverRuleUtilityTests: play for every match,
    // and anything short of a map that is over for a LAN tournament.
    [Fact]
    public void A_Drop_Is_Only_Timed_Where_The_Matchs_Rule_Times_It()
    {
        string source = Surrender();

        Assert.Matches(
            @"^public void SetupDisconnectTimer\([^)]*\)\s*\{\s*MatchManager\? match = _matchService\.GetCurrentMatch\(\);\s*if \(match == null \|\| !TimesLeaver\(match\)\)\s*\{\s*return;\s*\}",
            Method(source, "public void SetupDisconnectTimer(")
        );
        Assert.Matches(
            @"^private bool TimesLeaver\(MatchManager match\)\s*\{\s*return LeaverRuleUtility\.TimesLeaver\(\s*LeaverRule\(\),\s*match\.IsInPlay\(\),\s*match\.gameEnded\s*\|\| MatchPhaseUtility\.PhaseOf\(match\.GetCurrentMapStatus\(\)\) == eMatchPhase\.Over\s*\);\s*$",
            Method(source, "private bool TimesLeaver(MatchManager match)")
        );
    }

    // Gated on play in the handler, a LAN tournament player gone from warmup
    // was never timed. Only a lineup's own player leaving the server gets this
    // far, so a seat nobody sat in never tells the organizers anything.
    [Fact]
    public void Only_A_Lineup_Player_Leaving_Starts_A_Timer_Whatever_The_Phase()
    {
        string handler = Method(
            Code("FiveStack.Events", "PlayerDisconnected.cs"),
            "public HookResult OnPlayerDisconnect("
        );
        int timer = At(handler, "\n        _surrenderSystem.SetupDisconnectTimer(");

        Assert.True(
            At(handler, "if (handling != eDisconnectHandling.LineupMemberLeft)") < timer,
            "the timer starts for someone who is not a lineup's player"
        );
        Assert.True(
            At(handler, "if (match.IsInPlay())") < timer,
            "the pause for a missing player no longer comes first"
        );
    }

    [Fact]
    public void Nothing_But_A_Disconnect_Starts_A_Leavers_Timer()
    {
        string src = SourceDir();
        string[] built = { "bin", "obj", "build" };

        Assert.Equal(
            2,
            Directory
                .EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
                .Where(file =>
                    !Path.GetRelativePath(src, file)
                        .Split(Path.DirectorySeparatorChar)
                        .Any(folder => built.Contains(folder))
                )
                .Sum(file =>
                    Regex.Matches(File.ReadAllText(file), @"\bSetupDisconnectTimer\(").Count
                )
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
    public void Only_A_Short_Surrendering_Lineups_Leavers_Who_Are_Still_Gone_Are_Reported()
    {
        string report = Method(Surrender(), "private void ReportTimedOutLeavers(");

        Assert.Contains("SurrenderingLineup(matchData, winningLineup)", report);
        Assert.Matches(
            @"if \([^{]*ReconnectPauseUtility\.IsLineupWhole\([^;]*surrenderingLineup\.Value\s*\)\s*\)\s*\{\s*return;\s*\}\s*foreach \(ulong steamId in _timedOutLeavers\)",
            report
        );
        Assert.Matches(
            @"foreach \(ulong steamId in _timedOutLeavers\)\s*\{\s*if \(\s*LeaverLineup\(matchData, steamId\.ToString\(\), """"\) == surrenderingLineup\s*&&\s*!MatchUtility\.Players\(\)\.Any\(player => player\.SteamID == steamId\)\s*\)\s*\{\s*ReportAbandon\(steamId\);\s*\}\s*\}\s*$",
            report
        );
    }

    // A vote that fails, or a map that ends, leaves the leaver waiting: a later
    // surrender by their lineup still reports them.
    [Fact]
    public void Only_Coming_Back_A_Whole_Lineup_Or_A_New_Match_Takes_A_Leaver_Off_The_Wait()
    {
        string source = Surrender();
        string cancel = Method(source, "public void CancelDisconnectTimer(");

        Assert.True(
            At(cancel, "_timedOutLeavers.Remove(steamId);")
                < At(cancel, "if (!KillDisconnectTimer(steamId))"),
            "a leaver whose timer already fired stays waiting once they are back"
        );
        Assert.Single(Regex.Matches(source, @"_timedOutLeavers\.Remove\("));
        Assert.Single(Regex.Matches(source, @"_timedOutLeavers\.RemoveWhere\("));
        Assert.Matches(
            @"List<Guid> shortLineups = ReconnectPauseUtility\.ShortLineups\(\s*matchData,\s*connected,\s*expectedPlayers\s*\);\s*_timedOutLeavers\.RemoveWhere\(steamId => IsReplaced\(matchData, shortLineups, steamId\)\);",
            Method(source, "public void ForgetReplacedLeavers(")
        );
        Assert.Matches(
            @"^private static bool IsReplaced\([^)]*\)\s*\{\s*return LeaverLineup\(matchData, steamId\.ToString\(\), """"\) is Guid lineup\s*&& !shortLineups\.Contains\(lineup\);\s*$",
            Method(source, "private static bool IsReplaced(")
        );
        Assert.Single(Regex.Matches(source, @"_timedOutLeavers\.Clear\("));
        Assert.Single(Regex.Matches(source, @"_reportedAbsences\.Clear\("));

        string setup = Method(source, "public void OnMatchSetup(");

        Assert.Contains("_timedOutLeavers.Clear();", setup);
        Assert.Contains("_reportedAbsences.Clear();", setup);
    }

    // Online the timer is what reports a leaver, and in a LAN tournament what
    // tells the organizers, so a substitute only ends it under the one rule
    // that reports a leaver for their lineup's surrender.
    [Fact]
    public void A_Replaced_Leavers_Timer_Is_Only_Killed_Where_A_Surrender_Reports_Them()
    {
        Assert.Matches(
            @"_timedOutLeavers\.RemoveWhere\([^;]*\);\s*if \(LeaverRule\(\) != eLeaverRule\.AbandonOnSurrender\)\s*\{\s*return;\s*\}\s*List<ulong> replaced = _disconnectTimers\s*\.Values\.SelectMany\(timers => timers\.Keys\)\s*\.Where\(steamId => IsReplaced\(matchData, shortLineups, steamId\)\)\s*\.ToList\(\);\s*foreach \(ulong steamId in replaced\)\s*\{\s*KillDisconnectTimer\(steamId\);\s*\}\s*$",
            Method(Surrender(), "public void ForgetReplacedLeavers(")
        );
    }

    // An absent player's timer has fired by the time they are back, so their
    // return cannot wait on there being one to kill. An abandon is never taken
    // back: the api lengthens the ban on every one it is sent.
    [Fact]
    public void Coming_Back_Is_What_Lets_The_Organizers_Hear_Of_The_Next_Absence()
    {
        string source = Surrender();
        string cancel = Method(source, "public void CancelDisconnectTimer(");

        Assert.True(
            At(cancel, "_reportedAbsences.Remove(steamId);")
                < At(cancel, "if (!KillDisconnectTimer(steamId))"),
            "a player whose timer already fired stays reported once they are back"
        );
        Assert.Single(Regex.Matches(source, @"_reportedAbsences\.Remove\("));
        Assert.DoesNotContain("_reportedAbandons.Remove", source);
        Assert.Single(Regex.Matches(source, @"_reportedAbandons\.Clear\("));
    }

    // A connect is what makes a lineup whole. A leaver whose seat was filled
    // before their time ran out is taken off the wait as they are put on it.
    [Fact]
    public void A_Whole_Lineups_Leavers_Are_Forgotten_On_Every_Connect_And_As_They_Time_Out()
    {
        string source = Surrender();
        string timer = Method(source, "public void SetupDisconnectTimer(");
        string resume = Method(source, "public void ResumeIfRosterWhole()");
        int forgotten = At(
            resume,
            "ForgetReplacedLeavers(matchData, connected, match.GetExpectedPlayerCount());"
        );

        Assert.True(
            forgotten < At(resume, "!match.timeoutSystem.RosterOwnsPause()"),
            "a LAN roster never owns its pause, so nothing after that return runs there"
        );
        Assert.Single(Regex.Matches(resume[..forgotten], @"\breturn;"));
        Assert.True(
            At(timer, "PlayerAbandonedMatch(steamId);") < At(timer, "ForgetReplacedLeavers("),
            "a leaver is checked against their lineup before they are waiting on it"
        );
        Assert.Equal(3, Regex.Matches(source, @"\bForgetReplacedLeavers\(").Count);

        string connect = Method(
            Code("FiveStack.Events", "PlayerConnected.cs"),
            "public HookResult OnPlayerConnect("
        );

        Assert.True(
            At(connect, "_overCapacityKicks.Add(player.SteamID);")
                < At(connect, "_surrenderSystem.ResumeIfRosterWhole();"),
            "a player about to be kicked for a full lineup counts towards it"
        );
    }
}
