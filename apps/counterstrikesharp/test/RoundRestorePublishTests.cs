using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using FiveStack;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Runs the plugin's own MatchEvents. With no socket a published event stops in
// _pendingMessages, and the publish queue keeps its order, so once the marker
// has arrived everything sent before it has been dealt with.
public class RoundRestorePublishTests : IAsyncLifetime
{
    private const string Marker = "restoreRound";

    private readonly RoundRestoreState _restore = new();
    private readonly MatchEvents _events;

    public RoundRestorePublishTests()
    {
        MatchManager match = Bare<MatchManager>();
        Set(match, "_matchData", new MatchData { id = Guid.NewGuid() });
        Set(match, "_activeMapId", (Guid?)Guid.NewGuid());

        MatchService matchService = Bare<MatchService>();
        Set(matchService, "_currentMatch", match);

        _events = new MatchEvents(
            NullLogger<MatchEvents>.Instance,
            Bare<EnvironmentService>(),
            matchService,
            null!,
            _restore
        );
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return _events.Disconnect();
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

    private List<string> Published()
    {
        var pending = (ConcurrentDictionary<
            Guid,
            (MatchEvents.EventData<Dictionary<string, object>> Event, DateTime Timestamp)
        >)
            typeof(MatchEvents)
                .GetField("_pendingMessages", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(_events)!;

        return pending.Values.Select(entry => (string)entry.Event.data!["event"]).ToList();
    }

    private async Task<List<string>> PublishThenSettle(string gameEvent)
    {
        _events.PublishGameEvent(gameEvent, new Dictionary<string, object>());
        _events.PublishGameEvent(Marker, new Dictionary<string, object>());

        DateTime giveUp = DateTime.UtcNow.AddSeconds(5);

        while (!Published().Contains(Marker))
        {
            Assert.True(DateTime.UtcNow < giveUp, "the marker never reached the publish step");
            await Task.Delay(5);
        }

        return Published();
    }

    [Theory]
    [InlineData("kill")]
    [InlineData("assist")]
    [InlineData("damage")]
    [InlineData("utility")]
    [InlineData("flash")]
    [InlineData("objective")]
    public async Task AStatEventIsNotPublishedWhileABackupLoads(string gameEvent)
    {
        _restore.StartLoad();

        Assert.DoesNotContain(gameEvent, await PublishThenSettle(gameEvent));
    }

    [Theory]
    [InlineData("kill")]
    [InlineData("damage")]
    [InlineData("objective")]
    public async Task ARestoreVoteKeepsPublishingTheRoundBeingPlayed(string gameEvent)
    {
        _restore.StartVote();

        Assert.Contains(gameEvent, await PublishThenSettle(gameEvent));
    }

    [Fact]
    public async Task ARequestThatTimedOutStopsBeingResent()
    {
        Assert.Contains("kill", await PublishThenSettle("kill"));

        _events.WithdrawRestoreRequests();

        Assert.DoesNotContain(Marker, Published());
        Assert.Contains("kill", Published());
    }

    [Fact]
    public async Task AKillIsPublishedOnceTheLoadHasEnded()
    {
        _restore.StartLoad();
        _restore.End();

        Assert.Contains("kill", await PublishThenSettle("kill"));
    }

    [Fact]
    public async Task ALoadAfterAPassedVoteStopsPublishing()
    {
        _restore.StartVote();
        _restore.End();
        _restore.StartLoad();

        Assert.DoesNotContain("kill", await PublishThenSettle("kill"));
    }

    [Theory]
    [InlineData("score")]
    [InlineData("mapStatus")]
    public async Task ALoadLeavesEventsThatAreNotRoundStatsAlone(string gameEvent)
    {
        _restore.StartLoad();

        Assert.Contains(gameEvent, await PublishThenSettle(gameEvent));
    }
}
