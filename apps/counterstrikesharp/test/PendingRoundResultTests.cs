using System.Runtime.CompilerServices;
using FiveStack;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// The real state and the real clear, joined the way GameBackUpRounds' constructor
// joins them: that constructor can reach native code, so the line itself is read
// in DeclinedVoteRoundTests. Only here: the snapshot holds a team enum, and the
// other plugin's enum cannot load without its native package.
public class PendingRoundResultTests : IAsyncLifetime
{
    private readonly RoundRestoreState _restore = new();
    private readonly MatchEvents _events;

    public PendingRoundResultTests()
    {
        _events = new MatchEvents(
            NullLogger<MatchEvents>.Instance,
            (EnvironmentService)RuntimeHelpers.GetUninitializedObject(typeof(EnvironmentService)),
            (MatchService)RuntimeHelpers.GetUninitializedObject(typeof(MatchService)),
            null!,
            _restore
        );

        _restore.LoadStarted += _events.ClearPendingRoundResult;

        _events.PendingRoundResult = new MatchEvents.RoundResultSnapshot { Round = 9 };
    }

    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        return _events.Disconnect();
    }

    [Fact]
    public void ALoadForgetsTheRoundItUndoes()
    {
        _restore.StartLoad();

        Assert.Null(_events.PendingRoundResult);
    }

    [Fact]
    public void AVoteLeavesItAlone()
    {
        _restore.StartVote();

        Assert.NotNull(_events.PendingRoundResult);
    }

    [Fact]
    public void AnOwedLoadLeavesItAloneUntilItStarts()
    {
        _restore.StartOwing(9, Guid.NewGuid());

        Assert.NotNull(_events.PendingRoundResult);
    }
}
