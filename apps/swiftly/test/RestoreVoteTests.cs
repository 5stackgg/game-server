using System.Reflection;
using System.Runtime.CompilerServices;
using FiveStack;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// A recovery vote's failure callback replays the map from 0-0, which sets every
// round the backend recorded aside. It must only ever run for a vote that lost.
public class RestoreVoteTests
{
    private int _failures;
    private int _passes;

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

    private VoteSystem FinishedVote()
    {
        VoteSystem vote = (VoteSystem)RuntimeHelpers.GetUninitializedObject(typeof(VoteSystem));

        Set(vote, "_logger", NullLogger<VoteSystem>.Instance);
        Set(vote, "_voteFailedCallback", (Action)(() => _failures++));
        Set(vote, "_voteSuccessCallback", (Action)(() => _passes++));

        return vote;
    }

    [Fact]
    public void AFinishedVoteCannotBeCancelledIntoItsFailure()
    {
        FinishedVote().CancelVote();

        Assert.Equal(0, _failures);
    }

    [Fact]
    public void AbandoningAFinishedVoteRunsNoCallback()
    {
        VoteSystem vote = FinishedVote();

        vote.Abandon();

        Assert.Equal(0, _failures);
        Assert.Equal(0, _passes);
        Assert.Null(Get(vote, "_voteFailedCallback"));
        Assert.Null(Get(vote, "_voteSuccessCallback"));
    }
}
