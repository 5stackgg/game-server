using System.Reflection;
using System.Runtime.CompilerServices;
using FiveStack;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

// Only here: the other plugin's vote timers are native handles.
public class OpenVoteTests
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

    private VoteSystem OpenVote()
    {
        VoteSystem vote = (VoteSystem)RuntimeHelpers.GetUninitializedObject(typeof(VoteSystem));

        Set(vote, "_logger", NullLogger<VoteSystem>.Instance);
        Set(vote, "_voteFailedCallback", (Action)(() => _failures++));
        Set(vote, "_voteSuccessCallback", (Action)(() => _passes++));
        Set(vote, "_playerMessageTimer", new CancellationTokenSource());

        return vote;
    }

    [Fact]
    public void CancellingAnOpenVoteRunsItsFailure()
    {
        VoteSystem vote = OpenVote();

        vote.CancelVote();

        Assert.Equal(1, _failures);
        Assert.False(vote.IsVoteActive());
    }

    [Fact]
    public void AbandoningAnOpenVoteRunsNeitherCallback()
    {
        VoteSystem vote = OpenVote();

        vote.Abandon();

        Assert.Equal(0, _failures);
        Assert.Equal(0, _passes);
        Assert.False(vote.IsVoteActive());
    }
}
