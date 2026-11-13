using System.Reflection;
using System.Runtime.CompilerServices;
using SwiftlyS2.Shared.Players;
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
            camera,
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
            .Invoke(timeout, ["paused"]);
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
