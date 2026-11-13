using System.Reflection;
using System.Runtime.CompilerServices;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

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

        TimeoutSystem timeout = new TimeoutSystem(
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
        SurrenderSystem surrender = new SurrenderSystem(
            NullLogger<ReadySystem>.Instance,
            events,
            matchService,
            null!,
            null!,
            environment
        );
        CameraSystem camera = new CameraSystem(
            NullLogger<CameraSystem>.Instance,
            null!,
            matchService,
            null!
        );

        MatchManager match = new MatchManager(
            NullLogger<MatchManager>.Instance,
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

    private static void WaitingOn(TimeoutSystem timeout, params CsTeam[] teams)
    {
        Set(timeout, "_requiresTeamResumeForCurrentPause", true);
        ((HashSet<CsTeam>)Get(timeout, "_teamsPendingResume")!).UnionWith(teams);
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
        WaitingOn(server.Timeout, CsTeam.Terrorist);

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
        WaitingOn(server.Timeout, CsTeam.Terrorist);

        server.Match.UpdateMapStatus(eMapStatus.WaitingForTV);

        Assert.Equal(eMapStatus.WaitingForTV, server.Match.GetCurrentMapStatus());
        Assert.Null(server.Timeout.pauseVote);
        Assert.Null(server.Timeout.resumeVote);
        Assert.Null(Get(pause, "_voteSuccessCallback"));
        Assert.Null(Get(resume, "_voteSuccessCallback"));
        Assert.False(server.Timeout.ShouldRequireTeamResume());
    }

    // CounterStrikeSharp hands a killed timer's address to the next timer it
    // makes, so ending a phase twice must not kill anything a second time.
    [Fact]
    public void Ending_A_Phase_Abandons_Both_Votes_And_Kills_No_Timer_It_Does_Not_Own()
    {
        Server server = On(eMapStatus.Live);
        VoteSystem pause = Vote();
        VoteSystem resume = Vote();
        server.Timeout.pauseVote = pause;
        server.Timeout.resumeVote = resume;

        server.Match.EndPhase();
        server.Match.KillPhaseTimer(Bare<Timer>());

        Assert.Null(server.Timeout.pauseVote);
        Assert.Null(server.Timeout.resumeVote);
        Assert.Null(Get(pause, "_voteSuccessCallback"));
        Assert.Null(Get(resume, "_voteSuccessCallback"));
        Assert.Empty((List<Timer>)Get(server.Match, "_phaseTimers")!);
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
        WaitingOn(server.Timeout, CsTeam.Terrorist);

        PassPauseVote(server.Timeout);

        Assert.Equal(
            new[] { CsTeam.Terrorist },
            ((HashSet<CsTeam>)Get(server.Timeout, "_teamsPendingResume")!).ToArray()
        );
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
