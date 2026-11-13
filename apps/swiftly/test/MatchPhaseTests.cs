using System.Reflection;
using System.Runtime.CompilerServices;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Translation;
using FiveStack;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// The plugin's own MatchManager and the systems a phase owns, with no engine
// behind them. Asking whether warmup is on needs the engine, so every status but
// Warmup is only used where the plugin reads nothing but the status.
public class MatchPhaseTests
{
    private sealed record Server(MatchManager Match, TimeoutSystem Timeout, CameraSystem Camera);

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

    private static Server On(eMapStatus status)
    {
        EnvironmentService environment = Bare<EnvironmentService>();
        MatchService matchService = Bare<MatchService>();

        MatchEvents events = Bare<MatchEvents>();
        Set(events, "_logger", NullLogger<MatchEvents>.Instance);
        Set(events, "_environmentService", environment);
        Set(events, "_matchService", matchService);
        Set(events, "_mapStatusClock", new MapStatusClock());
        Set(events, "_restore", new RoundRestoreState());

        CameraSystem camera = new CameraSystem(
            NullLogger<CameraSystem>.Instance,
            null!,
            matchService,
            null!
        );
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
            camera
        );
        ReadySystem ready = new ReadySystem(
            NullLogger<ReadySystem>.Instance,
            null!,
            matchService,
            null!,
            null!,
            null!
        );
        SurrenderSystem surrender = new SurrenderSystem(
            NullLogger<ReadySystem>.Instance,
            events,
            matchService,
            null!,
            null!,
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
        match.Init(new MatchData { id = Guid.NewGuid() });
        Set(match, "_currentMapStatus", status);
        Set(matchService, "_currentMatch", match);

        return new Server(match, timeout, camera);
    }

    private static VoteSystem Vote()
    {
        VoteSystem vote = Bare<VoteSystem>();
        Set(vote, "_voteSuccessCallback", (Action)(() => { }));

        return vote;
    }

    private static void WaitingOn(TimeoutSystem timeout, params Team[] teams)
    {
        Set(timeout, "_requiresTeamResumeForCurrentPause", true);
        ((HashSet<Team>)Get(timeout, "_teamsPendingResume")!).UnionWith(teams);
    }

    private static void PassPauseVote(TimeoutSystem timeout)
    {
        typeof(TimeoutSystem)
            .GetMethod("PauseTechMatch", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(timeout, [(Func<ILocalizer, string>)(_ => "paused")]);
    }

    [Fact]
    public void A_Status_In_The_Same_Phase_Keeps_Its_Votes()
    {
        Server server = On(eMapStatus.Live);
        VoteSystem pause = Vote();
        server.Timeout.pauseVote = pause;
        WaitingOn(server.Timeout, Team.T);

        server.Match.UpdateMapStatus(eMapStatus.Overtime);

        Assert.Equal(eMapStatus.Overtime, server.Match.GetCurrentMapStatus());
        Assert.Same(pause, server.Timeout.pauseVote);
        Assert.NotNull(Get(pause, "_voteSuccessCallback"));
        Assert.True(server.Timeout.ShouldRequireTeamResume());
    }

    [Fact]
    public void A_Status_In_Another_Phase_Ends_Its_Votes()
    {
        Server server = On(eMapStatus.Live);
        VoteSystem pause = Vote();
        VoteSystem resume = Vote();
        server.Timeout.pauseVote = pause;
        server.Timeout.resumeVote = resume;
        WaitingOn(server.Timeout, Team.T);

        server.Match.UpdateMapStatus(eMapStatus.WaitingForTV);

        Assert.Equal(eMapStatus.WaitingForTV, server.Match.GetCurrentMapStatus());
        Assert.Null(server.Timeout.pauseVote);
        Assert.Null(server.Timeout.resumeVote);
        Assert.Null(Get(pause, "_voteSuccessCallback"));
        Assert.Null(Get(resume, "_voteSuccessCallback"));
        Assert.False(server.Timeout.ShouldRequireTeamResume());
    }

    [Fact]
    public void Ending_A_Phase_Kills_Its_Timers_And_Abandons_Both_Votes()
    {
        Server server = On(eMapStatus.Live);
        VoteSystem pause = Vote();
        VoteSystem resume = Vote();
        CancellationTokenSource timer = new CancellationTokenSource();
        server.Timeout.pauseVote = pause;
        server.Timeout.resumeVote = resume;
        ((List<CancellationTokenSource>)Get(server.Match, "_phaseTimers")!).Add(timer);

        server.Match.EndPhase();

        Assert.True(timer.IsCancellationRequested);
        Assert.Empty((List<CancellationTokenSource>)Get(server.Match, "_phaseTimers")!);
        Assert.Null(server.Timeout.pauseVote);
        Assert.Null(server.Timeout.resumeVote);
        Assert.Null(Get(pause, "_voteSuccessCallback"));
        Assert.Null(Get(resume, "_voteSuccessCallback"));
    }

    [Fact]
    public void A_Passed_Pause_Vote_Outside_Play_Pauses_Nothing()
    {
        Server server = On(eMapStatus.Warmup);

        PassPauseVote(server.Timeout);

        Assert.Equal(eMapStatus.Warmup, server.Match.GetCurrentMapStatus());
        Assert.False(server.Timeout.ShouldRequireTeamResume());
    }

    [Fact]
    public void A_Passed_Pause_Vote_Keeps_The_Teams_A_Tech_Pause_Still_Waits_On()
    {
        Server server = On(eMapStatus.Paused);
        WaitingOn(server.Timeout, Team.T);

        PassPauseVote(server.Timeout);

        Assert.Equal(
            new[] { Team.T },
            ((HashSet<Team>)Get(server.Timeout, "_teamsPendingResume")!).ToArray()
        );
        Assert.Equal(eMapStatus.Paused, server.Match.GetCurrentMapStatus());
    }

    private static void CamerasOffline(Server server, params ulong[] steamIds)
    {
        MatchData matchData = server.Match.GetMatchData()!;
        matchData.options.camera_required = true;
        Set(server.Camera, "_matchId", matchData.id);
        ((HashSet<ulong>)Get(server.Camera, "_offline")!).UnionWith(steamIds);
    }

    private static void LineupCamera(Server server, bool cameraOk)
    {
        MatchData matchData = server.Match.GetMatchData()!;
        matchData.options.camera_required = true;
        matchData.lineup_1.lineup_players.Add(
            new MatchMember { steam_id = "76561198000000001", camera_ok = cameraOk }
        );
    }

    private static object? Call(object target, string method, params object[] args)
    {
        return target
            .GetType()
            .GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(target, args);
    }

    private static object Reason(string name)
    {
        return Enum.Parse(typeof(eMapStatus).Assembly.GetType("FiveStack.Enums.ePauseReason")!, name);
    }

    private static Server PausedFor(string reason)
    {
        Server server = On(eMapStatus.Paused);
        Set(server.Match, "_pauseReason", Reason(reason));

        return server;
    }

    private static bool OwesPause(Server server)
    {
        return (bool)Call(server.Camera, "OwesPause")!;
    }

    private static bool RosterOwnsPause(Server server)
    {
        return (bool)Call(server.Timeout, "RosterOwnsPause")!;
    }

    [Fact]
    public void A_Camera_Lost_Before_Play_Is_Owed_A_Pause()
    {
        Server server = On(eMapStatus.Knife);
        CamerasOffline(server, 76561198000000001);

        typeof(CameraSystem)
            .GetMethod("OnCamerasLost", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(server.Camera, []);

        Assert.Equal(eMapStatus.Knife, server.Match.GetCurrentMapStatus());
        Assert.True(OwesPause(server));
    }

    // camera_state is only sent on a change, so a plugin that restarts mid-map
    // learns of a camera that is still down from the match data alone.
    [Fact]
    public void A_Camera_Down_When_The_Plugin_Loads_Is_Owed_A_Pause()
    {
        Server server = On(eMapStatus.Live);
        LineupCamera(server, cameraOk: false);

        Assert.True(OwesPause(server));
    }

    [Fact]
    public void Every_Camera_Up_Owes_No_Pause()
    {
        Server server = On(eMapStatus.Live);
        LineupCamera(server, cameraOk: true);

        Assert.False(OwesPause(server));
    }

    [Fact]
    public void Staff_Resuming_Over_A_Breach_Uses_Up_The_Owed_Pause_And_Its_Reminder()
    {
        Server server = On(eMapStatus.Live);
        LineupCamera(server, cameraOk: false);

        Assert.True(OwesPause(server));

        CancellationTokenSource reminder = new CancellationTokenSource();
        Set(server.Camera, "_reminderTimer", reminder);

        Call(server.Camera, "Override");

        Assert.False(OwesPause(server));
        Assert.True(reminder.IsCancellationRequested);
        Assert.Null(Get(server.Camera, "_reminderTimer"));
        Assert.True(server.Camera.IsBlocking());
    }

    // An override lasts the map it was made on.
    [Fact]
    public void A_New_Map_Owes_A_Pause_For_A_Camera_Still_Down_After_An_Override()
    {
        Server server = On(eMapStatus.Live);
        LineupCamera(server, cameraOk: false);

        Assert.True(OwesPause(server));

        Call(server.Camera, "Override");
        Call(server.Camera, "NewMap");

        Assert.True(OwesPause(server));
    }

    [Fact]
    public void A_New_Map_With_Every_Camera_Up_Owes_No_Pause()
    {
        Server server = On(eMapStatus.Live);
        LineupCamera(server, cameraOk: true);

        Assert.False(OwesPause(server));

        Call(server.Camera, "NewMap");

        Assert.False(OwesPause(server));
    }

    // "type .resume to continue" only makes sense with the map paused.
    [Theory]
    [InlineData(eMapStatus.Live)]
    [InlineData(eMapStatus.Overtime)]
    [InlineData(eMapStatus.Warmup)]
    [InlineData(eMapStatus.Knife)]
    public void Cameras_Coming_Back_Outside_A_Pause_Say_Nothing(eMapStatus status)
    {
        Server server = On(status);
        CancellationTokenSource reminder = new CancellationTokenSource();
        Set(server.Camera, "_reminderTimer", reminder);

        Call(server.Camera, "OnCamerasRestored");

        Assert.Null(Get(server.Camera, "_reminderTimer"));
        Assert.True(reminder.IsCancellationRequested);
    }

    // Round end in overtime used to write Overtime over a pause called mid-round,
    // which dropped the pause and its reason.
    [Fact]
    public void A_Pause_In_Overtime_Keeps_Its_Reason_Through_Round_End_And_A_Reconnect_Still_Ends_It()
    {
        Server server = PausedFor("Roster");

        Call(server.Match, "MarkOvertime");

        Assert.Equal(eMapStatus.Paused, server.Match.GetCurrentMapStatus());
        Assert.True(RosterOwnsPause(server));
    }

    [Fact]
    public void An_Overtime_Round_Ending_In_Play_Is_Overtime()
    {
        Server server = On(eMapStatus.Live);

        Call(server.Match, "MarkOvertime");

        Assert.Equal(eMapStatus.Overtime, server.Match.GetCurrentMapStatus());
    }

    [Fact]
    public void A_Whole_Roster_Ends_The_Pause_It_Made()
    {
        Assert.True(RosterOwnsPause(PausedFor("Roster")));
    }

    // A caster, coach or spectator joining makes the roster whole as much as the
    // player who left coming back does.
    [Theory]
    [InlineData("Camera")]
    [InlineData("Tech")]
    [InlineData("Admin")]
    [InlineData("Restore")]
    public void A_Whole_Roster_Leaves_A_Pause_It_Did_Not_Make(string reason)
    {
        Assert.False(RosterOwnsPause(PausedFor(reason)));
    }

    [Fact]
    public void A_Whole_Roster_Never_Resumes_Over_A_Camera_Still_Down()
    {
        Server server = PausedFor("Roster");
        CamerasOffline(server, 76561198000000001);

        Assert.False(RosterOwnsPause(server));
    }

    [Fact]
    public void A_Whole_Roster_Leaves_A_Pause_The_Teams_Still_Resume()
    {
        Server server = PausedFor("Roster");
        WaitingOn(server.Timeout, Team.T);

        Assert.False(RosterOwnsPause(server));
    }

    [Fact]
    public void A_Whole_Roster_Has_Nothing_To_Resume_In_Play()
    {
        Assert.False(RosterOwnsPause(On(eMapStatus.Live)));
    }

    // On a LAN someone at the table says the player is back.
    [Fact]
    public void A_Whole_Roster_Never_Ends_A_Lan_Pause()
    {
        Server server = PausedFor("Roster");
        server.Match.GetMatchData()!.is_lan = true;

        Assert.False(RosterOwnsPause(server));
    }

    [Theory]
    [InlineData(eMapStatus.Live)]
    [InlineData(eMapStatus.Paused)]
    public void A_Lan_Match_Holds_For_Its_Roster_On_No_Timeout(eMapStatus status)
    {
        Server server = On(status);
        server.Match.GetMatchData()!.is_lan = true;

        Assert.True(server.Timeout.HoldForRoster([]));
        Assert.True(server.Timeout.HoldsForRoster());
        Assert.Null(Get(server.Timeout, "_reconnectTimer"));
        Assert.Null(server.Timeout.ReconnectTimeoutSecondsLeft());
    }

    [Theory]
    [InlineData("Camera")]
    [InlineData("Tech")]
    [InlineData("Admin")]
    [InlineData("Restore")]
    public void A_Drop_Under_A_Pause_Someone_Else_Made_Costs_No_Timeout(string reason)
    {
        Server server = PausedFor(reason);

        Assert.True(server.Timeout.HoldForRoster([]));
        Assert.Null(Get(server.Timeout, "_reconnectTimer"));
        Assert.False(server.Timeout.HoldsForRoster());
    }

    private static readonly Guid ReconnectMap = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ReconnectLineup = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static Server WithLineup(Server server, int players)
    {
        MatchData matchData = server.Match.GetMatchData()!;
        matchData.lineup_1_id = ReconnectLineup;
        matchData.lineup_1.lineup_players.AddRange(
            Enumerable
                .Range(0, players)
                .Select(i => new MatchMember { steam_id = $"7656119800000{i:D4}" })
        );

        return server;
    }

    private static ReconnectTimeouts Timeouts(Server server)
    {
        return (ReconnectTimeouts)Get(server.Timeout, "_reconnectTimeouts")!;
    }

    private static int TimeoutsUsed(Server server)
    {
        return ((Dictionary<Guid, int>)Get(Timeouts(server), "_used")!).Values.Sum();
    }

    [Fact]
    public void Resuming_Ends_The_Timeout_A_Missing_Player_Was_Given()
    {
        Server server = PausedFor("Roster");
        CancellationTokenSource timer = new CancellationTokenSource();
        ((List<CancellationTokenSource>)Get(server.Match, "_phaseTimers")!).Add(timer);
        Set(server.Timeout, "_reconnectTimer", timer);
        Set(server.Timeout, "_reconnectTimeoutEnds", DateTime.UtcNow.AddSeconds(90));

        Assert.InRange(server.Timeout.ReconnectTimeoutSecondsLeft() ?? 0, 1, 90);
        Assert.True(server.Timeout.HoldsForRoster());

        server.Timeout.Reset();

        Assert.True(timer.IsCancellationRequested);
        Assert.Null(Get(server.Timeout, "_reconnectTimer"));
        Assert.Null(server.Timeout.ReconnectTimeoutSecondsLeft());
        Assert.False(server.Timeout.HoldsForRoster());
    }

    [Theory]
    [InlineData("_reconnectTimeoutOwed", true)]
    [InlineData("_reconnectTimer", null)]
    public void A_Timeout_Already_Given_Holds_For_A_Second_Drop_At_No_Cost(
        string field,
        object? value
    )
    {
        Server server = WithLineup(On(eMapStatus.Live), 2);
        CancellationTokenSource timer = new CancellationTokenSource();
        Set(server.Timeout, field, value ?? timer);

        Assert.True(server.Timeout.HoldForRoster([("76561198000000000", "player0")]));
        Assert.Equal(0, TimeoutsUsed(server));
    }

    // Playing on would give the other side rounds against no one.
    [Fact]
    public void A_Lineup_With_Nobody_Left_Is_Waited_On_At_No_Cost()
    {
        Server server = WithLineup(On(eMapStatus.Live), 2);

        Assert.True(server.Timeout.HoldForRoster([]));
        Assert.True(server.Timeout.HoldsForRoster());
        Assert.Equal(0, TimeoutsUsed(server));
        Assert.Null(Get(server.Timeout, "_reconnectTimer"));
    }

    // Behind a tactical timeout the pause is retried, and the player can be back
    // before it lands.
    [Fact]
    public void A_Player_Back_Before_The_Pause_Lands_Gets_Their_Lineups_Timeout_Back()
    {
        Server server = WithLineup(On(eMapStatus.Live), 1);
        Timeouts(server).Charge(ReconnectMap, [ReconnectLineup], 1);
        Set(server.Timeout, "_reconnectTimeoutOwed", true);

        server.Timeout.RosterChanged([("76561198000000000", "player0")]);

        Assert.False(server.Timeout.HoldsForRoster());
        Assert.Equal(0, TimeoutsUsed(server));
    }

    [Fact]
    public void A_Timeout_Stays_Owed_While_The_Lineup_Is_Still_Short()
    {
        Server server = WithLineup(On(eMapStatus.Live), 2);
        Timeouts(server).Charge(ReconnectMap, [ReconnectLineup], 1);
        Set(server.Timeout, "_reconnectTimeoutOwed", true);

        server.Timeout.RosterChanged([("76561198000000000", "player0")]);

        Assert.True(server.Timeout.HoldsForRoster());
        Assert.Equal(1, TimeoutsUsed(server));
    }

    [Fact]
    public void Resuming_Gives_Back_A_Timeout_Whose_Pause_Never_Landed()
    {
        Server server = WithLineup(On(eMapStatus.Live), 2);
        Timeouts(server).Charge(ReconnectMap, [ReconnectLineup], 1);
        Set(server.Timeout, "_reconnectTimeoutOwed", true);

        server.Timeout.Reset();

        Assert.False(server.Timeout.HoldsForRoster());
        Assert.Equal(0, TimeoutsUsed(server));
    }

    [Fact]
    public void A_Whole_Roster_Stops_Waiting_On_A_Lineup_That_Was_Empty()
    {
        Server server = WithLineup(On(eMapStatus.Live), 1);
        Set(server.Timeout, "_waitsOnEmptyLineup", true);

        server.Timeout.RosterChanged([("76561198000000000", "player0")]);

        Assert.False(server.Timeout.HoldsForRoster());
    }

    [Fact]
    public void The_Countdown_Is_Only_Shown_Over_A_Pause_The_Roster_Made()
    {
        Server server = PausedFor("Tech");
        Set(server.Timeout, "_reconnectTimeoutEnds", DateTime.UtcNow.AddSeconds(90));

        Assert.Null(server.Timeout.ReconnectTimeoutSecondsLeft());
    }

    [Theory]
    [InlineData("Roster", "Restore", "Restore")]
    [InlineData("Roster", "Camera", "Camera")]
    [InlineData("Restore", "Camera", "Camera")]
    [InlineData("Restore", "Tech", "Tech")]
    [InlineData("Restore", "Roster", "Restore")]
    [InlineData("Camera", "Roster", "Camera")]
    [InlineData("Tech", "Restore", "Tech")]
    [InlineData("Admin", "Roster", "Admin")]
    public void A_Second_Pause_Only_Takes_Over_One_That_Ends_On_Its_Own(
        string held,
        string next,
        string kept
    )
    {
        Server server = PausedFor(held);

        Call(server.Match, "HoldPauseFor", Reason(next));

        Assert.Equal(Reason(kept), Call(server.Match, "GetPauseReason"));
    }

    [Theory]
    [InlineData("Roster")]
    [InlineData("Camera")]
    [InlineData("Tech")]
    [InlineData("Admin")]
    public void A_Declined_Restore_Leaves_A_Pause_It_Did_Not_Make(string reason)
    {
        Server server = PausedFor(reason);

        Call(server.Match, "ResumeFrom", Reason("Restore"));

        Assert.Equal(eMapStatus.Paused, server.Match.GetCurrentMapStatus());
    }

    [Theory]
    [InlineData(eMapStatus.Warmup)]
    [InlineData(eMapStatus.Knife)]
    [InlineData(eMapStatus.WaitingForTV)]
    [InlineData(eMapStatus.Finished)]
    public void A_Lost_Camera_Outside_Play_Pauses_Nothing(eMapStatus status)
    {
        Server server = On(status);

        typeof(CameraSystem)
            .GetMethod("OnCamerasLost", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(server.Camera, []);

        Assert.Equal(status, server.Match.GetCurrentMapStatus());
        Assert.Null(Get(server.Camera, "_reminderTimer"));
    }
}
