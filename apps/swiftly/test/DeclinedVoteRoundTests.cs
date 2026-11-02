using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FiveStack;
using FiveStack.Utilities;
using Xunit;

// A .reset vote leaves the round running until the next freezetime, so a round
// that ends during one is real unless the vote passes. A recovery vote is the
// other way round: the server is behind the backend, and has nothing to report.
public class DeclinedVoteRoundTests
{
    private const int Round = 4;

    private readonly RoundRestoreState _restore = new();

    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Code(params string[] path)
    {
        return Regex.Replace(
            File.ReadAllText(Path.Combine(new[] { SourceDir() }.Concat(path).ToArray())),
            @"//[^\n]*",
            ""
        );
    }

    private static void Set(object target, string field, object? value)
    {
        target
            .GetType()
            .GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(target, value);
    }

    private static VoteSystem FinishedVote()
    {
        return (VoteSystem)RuntimeHelpers.GetUninitializedObject(typeof(VoteSystem));
    }

    private static VoteSystem OpenVote()
    {
        VoteSystem vote = FinishedVote();
        FieldInfo timer = typeof(VoteSystem).GetField(
            "_playerMessageTimer",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;

        timer.SetValue(vote, RuntimeHelpers.GetUninitializedObject(timer.FieldType));

        return vote;
    }

    private GameBackUpRounds Backups(VoteSystem? vote = null)
    {
        GameBackUpRounds backups = (GameBackUpRounds)
            RuntimeHelpers.GetUninitializedObject(typeof(GameBackUpRounds));

        Set(backups, "_restore", _restore);
        backups.restoreRoundVote = vote;

        return backups;
    }

    [Fact]
    public void ARoundEndingDuringAResetVoteIsKept()
    {
        _restore.StartVote();

        Assert.False(Backups(OpenVote()).HoldsRoundResults());
    }

    [Fact]
    public void NothingIsHeldWithNoRestoreGoingOn()
    {
        Assert.False(Backups().HoldsRoundResults());
    }

    [Fact]
    public void ARecoveryVoteHoldsResultsWhileItIsOpen()
    {
        _restore.StartVote(recovering: true);

        Assert.True(Backups(OpenVote()).HoldsRoundResults());
    }

    [Fact]
    public void ARecoveryVoteKeepsTheBackupsHeld()
    {
        _restore.StartVote(recovering: true);

        Assert.True(Backups(OpenVote()).IsHoldingForRestore());
    }

    [Fact]
    public void AResetVoteHoldsNoBackups()
    {
        _restore.StartVote();

        Assert.False(Backups(OpenVote()).IsHoldingForRestore());
    }

    [Fact]
    public void ARestoreHeldForPlayersHoldsResults()
    {
        _restore.Hold(Round);

        Assert.True(Backups().HoldsRoundResults());
    }

    [Fact]
    public void ALoadingBackupHoldsResults()
    {
        _restore.StartLoad();

        Assert.True(Backups().HoldsRoundResults());
    }

    [Fact]
    public void ALoadAfterAVoteFinishedHoldsResults()
    {
        _restore.StartLoad();

        Assert.True(Backups(FinishedVote()).HoldsRoundResults());
    }

    [Fact]
    public void NoScoreSlipsOutWhileALoadIsOwed()
    {
        _restore.StartOwing(Round, Guid.NewGuid());

        Assert.True(Backups(FinishedVote()).HoldsRoundResults());
    }

    [Fact]
    public void ARestoreSaysWhenItsLoadStarts()
    {
        int started = 0;
        _restore.LoadStarted += () => started++;

        _restore.StartVote();
        _restore.End();
        _restore.StartOwing(Round, Guid.NewGuid());

        Assert.Equal(0, started);

        _restore.StartLoad();

        Assert.Equal(1, started);
    }

    [Fact]
    public void ALoadForgetsTheResultOfTheRoundItUndoes()
    {
        Assert.Contains(
            "_restore.LoadStarted += matchEvents.ClearPendingRoundResult;",
            Code("FiveStack.Services", "GameBackUpRounds.cs")
        );
    }

    [Theory]
    [InlineData("RoundEnd.cs", 2)]
    [InlineData("RoundStart.cs", 1)]
    public void TheRoundHooksAskWhetherResultsAreHeld(string file, int sites)
    {
        string source = Code("FiveStack.Events", file);

        Assert.DoesNotContain("IsResettingRound", source);
        Assert.Equal(sites, Regex.Matches(source, @"_gameBackupRounds\.HoldsRoundResults\(\)").Count);
    }

    [Fact]
    public void AVoteSaysWhetherItIsARecoveryVote()
    {
        Assert.Contains(
            "_restore.StartVote(recovering);",
            Code("FiveStack.Services", "GameBackUpRounds.cs")
        );
    }
}
