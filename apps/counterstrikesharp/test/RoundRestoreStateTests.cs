using System.Reflection;
using System.Runtime.CompilerServices;
using FiveStack;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// One debt: a recovery vote passed and the server is owed its load. Nothing else
// about a restore changes, so .reset, organizer restores and declined votes are
// pinned here as well as the new paths.
public class RoundRestoreStateTests
{
    private const int Round = 8;

    private static readonly Guid Map = Guid.NewGuid();
    private static readonly Guid OtherMap = Guid.NewGuid();

    private readonly RoundRestoreState _state = new();

    private GameBackUpRounds Backups()
    {
        GameBackUpRounds backups = (GameBackUpRounds)
            RuntimeHelpers.GetUninitializedObject(typeof(GameBackUpRounds));

        typeof(GameBackUpRounds)
            .GetField("_restore", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(backups, _state);

        return backups;
    }

    [Fact]
    public void AnOwedLoadCannotBeResumedOver()
    {
        _state.StartOwing(Round, Map);

        Assert.Equal(Round, _state.OwedRound);
        Assert.True(Backups().IsResettingRound());
        Assert.True(Backups().IsHoldingForRestore());
    }

    [Fact]
    public void AnOwedLoadDoesNotStandInTheWayOfItsAnswer()
    {
        _state.StartOwing(Round, Map);

        Assert.False(_state.VotingOrLoading);
    }

    [Fact]
    public void TheAnswerSettlesTheDebt()
    {
        _state.StartOwing(Round, Map);
        _state.StartLoad();

        Assert.Null(_state.OwedRound);
        Assert.True(_state.LoadingBackup);
        Assert.True(Backups().IsResettingRound());

        _state.End();

        Assert.False(Backups().IsResettingRound());
    }

    [Fact]
    public void AnOwedLoadThatCouldNotStartGoesBackToTheHold()
    {
        _state.StartOwing(Round, Map);

        Assert.Equal(Round, _state.FailedLoad());
        Assert.False(_state.InProgress);
    }

    [Fact]
    public void AFailedLoadNobodyWasOwedHoldsNothing()
    {
        Assert.Null(_state.FailedLoad());
    }

    [Fact]
    public void AnOwedLoadNobodyAnsweredGoesBackToTheHold()
    {
        int owing = _state.StartOwing(Round, Map);

        Assert.Equal(new OwedLoadTimeout(true, Round), _state.TimedOut(owing, Map));
        Assert.False(_state.InProgress);
    }

    [Fact]
    public void ADebtThatNeverWentOutStillComesBackToTheHold()
    {
        int owing = _state.StartOwing(Round, null);

        Assert.Equal(new OwedLoadTimeout(true, Round), _state.TimedOut(owing, null));
    }

    [Fact]
    public void ATimerOnAnotherMapEndsQuietly()
    {
        int owing = _state.StartOwing(Round, Map);

        Assert.Equal(new OwedLoadTimeout(true), _state.TimedOut(owing, OtherMap));
        Assert.False(_state.InProgress);
    }

    [Fact]
    public void ATimerFromAnEarlierDebtDoesNothing()
    {
        int earlier = _state.StartOwing(Round, Map);
        _state.End();
        _state.StartOwing(Round, Map);

        Assert.Equal(default, _state.TimedOut(earlier, Map));
        Assert.Equal(Round, _state.OwedRound);
    }

    [Fact]
    public void ATimerAfterTheLoadStartedDoesNothing()
    {
        int owing = _state.StartOwing(Round, Map);
        _state.StartLoad();

        Assert.Equal(default, _state.TimedOut(owing, Map));
        Assert.True(_state.LoadingBackup);
    }

    [Fact]
    public void ATimerAfterRestartMapDoesNothing()
    {
        int owing = _state.StartOwing(Round, Map);
        _state.Abandon();

        Assert.Equal(default, _state.TimedOut(owing, Map));
    }

    [Fact]
    public void RestartMapClearsAHold()
    {
        _state.Hold(Round);
        _state.Abandon();

        Assert.Null(_state.HeldRound);
        Assert.False(Backups().IsResettingRound());
        Assert.False(Backups().IsHoldingForRestore());
    }

    [Fact]
    public void RestartMapClearsAnOwedLoad()
    {
        _state.StartOwing(Round, Map);
        _state.Abandon();

        Assert.False(Backups().IsResettingRound());
        Assert.False(Backups().IsHoldingForRestore());
    }

    [Fact]
    public void RestartMapClearsAVoteAndAHoldTogether()
    {
        _state.Hold(Round);
        _state.StartVote();
        _state.Abandon();

        Assert.False(Backups().IsResettingRound());
    }

    [Fact]
    public void AHoldIsOfferedItsVoteWhenNothingElseIsRunning()
    {
        _state.Hold(Round);

        Assert.True(_state.CanOfferVote);
    }

    [Fact]
    public void NoHoldMeansNoVote()
    {
        Assert.False(_state.CanOfferVote);
    }

    [Fact]
    public void ALoadInProgressKeepsAHoldFromBeingOffered()
    {
        _state.Hold(Round);
        _state.StartLoad();

        Assert.False(_state.CanOfferVote);
        Assert.Equal(Round, _state.HeldRound);
    }

    [Fact]
    public void AnOwedLoadKeepsAHoldFromBeingOffered()
    {
        _state.Hold(Round);
        _state.StartOwing(Round, Map);

        Assert.False(_state.CanOfferVote);
    }

    [Fact]
    public void AVoteInProgressKeepsAHoldFromBeingOffered()
    {
        _state.Hold(Round);
        _state.StartVote();

        Assert.False(_state.CanOfferVote);
    }

    [Fact]
    public void TakingTheHoldHandsBackItsRound()
    {
        _state.Hold(Round);

        Assert.Equal(Round, _state.ReleaseHold());
        Assert.Null(_state.HeldRound);
    }

    [Fact]
    public void AnAnswerDuringAVoteOrALoadIsIgnoredAsOnMain()
    {
        _state.StartVote();
        Assert.True(_state.VotingOrLoading);

        _state.End();
        _state.StartLoad();
        Assert.True(_state.VotingOrLoading);
    }

    [Fact]
    public void AnAnswerWithNothingGoingOnIsLoadedAsOnMain()
    {
        Assert.False(_state.VotingOrLoading);
    }

    [Fact]
    public void ResetToRoundZeroFindsItsBackupAsOnMain()
    {
        BackupRound first = new() { round = 0, backup_file = "round00" };
        BackupRound newest = new() { round = 0, backup_file = "round00 again" };
        BackupRound later = new() { round = Round, backup_file = "round08" };

        Assert.Same(newest, RoundRestoreGateUtility.LatestBackup(new[] { first, later, newest }, 0));
    }

    [Fact]
    public void ResetToRoundZeroIsRefusedOnlyForAMissingBackup()
    {
        Assert.Equal(
            "the server has no backup of round 0",
            RoundRestoreGateUtility.Refusal(false, false, eMapStatus.Live, 0, false, 10, 10, "Competitive")
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOwedLoadDoesNotStopRoundZeroLoading(bool owing)
    {
        if (owing)
        {
            _state.StartOwing(Round, Map);
        }

        Assert.False(_state.VotingOrLoading);
        Assert.Null(
            RoundRestoreGateUtility.Refusal(
                false,
                _state.VotingOrLoading,
                eMapStatus.Live,
                0,
                true,
                10,
                10,
                "Competitive"
            )
        );
    }

    [Fact]
    public void TheNextMatchEndsALoadOwedOnTheLastOne()
    {
        _state.StartOwing(Round, Map);

        Assert.True(_state.LeaveMap(OtherMap));
        Assert.Null(_state.OwedRound);
        Assert.False(_state.Resetting);
    }

    [Fact]
    public void TheNextMatchEndsAHoldOnTheLastOne()
    {
        _state.Hold(Round, Map);

        Assert.True(_state.LeaveMap(OtherMap));
        Assert.Null(_state.HeldRound);
        Assert.False(_state.HoldingRounds);
    }

    [Fact]
    public void SetupOnTheSameMapKeepsAHoldAndAnOwedLoad()
    {
        _state.Hold(Round, Map);
        _state.StartOwing(Round, Map);

        Assert.False(_state.LeaveMap(Map));
        Assert.Equal(Round, _state.HeldRound);
        Assert.Equal(Round, _state.OwedRound);
    }

    [Fact]
    public void ANewMatchWithNothingToLeaveChangesNothing()
    {
        Assert.False(_state.LeaveMap(OtherMap));
    }

    [Fact]
    public void ARoundTheBackendKeptIsHeldAgain()
    {
        BackupRound[] rounds = { new() { round = Round, backup_file = "kept" } };

        Assert.Equal(Round, RoundRestoreGateUtility.RoundToHoldAgain(rounds, Round));
    }

    [Fact]
    public void ARoundOnlyOnDiskFallsBackToTheNewestTheBackendKept()
    {
        BackupRound[] rounds =
        {
            new() { round = 5, backup_file = "kept" },
            new() { round = 6, backup_file = "kept" },
            new() { round = Round, backup_file = "disk", from_disk = true },
        };

        Assert.Equal(6, RoundRestoreGateUtility.RoundToHoldAgain(rounds, Round));
    }

    [Fact]
    public void ARoundARefetchDroppedFallsBackTheSameWay()
    {
        BackupRound[] rounds = { new() { round = 5, backup_file = "kept" } };

        Assert.Equal(5, RoundRestoreGateUtility.RoundToHoldAgain(rounds, Round));
    }

    [Fact]
    public void NothingTheBackendCanLoadMeansNothingToHold()
    {
        BackupRound[] rounds = { new() { round = Round, backup_file = "disk", from_disk = true } };

        Assert.Null(RoundRestoreGateUtility.RoundToHoldAgain(rounds, Round));
    }

    [Fact]
    public void AnOwedLoadDoesNotRefuseAnOrganizerRestore()
    {
        _state.StartOwing(Round, Map);

        Assert.Null(
            RoundRestoreGateUtility.Refusal(
                false,
                _state.VotingOrLoading,
                eMapStatus.Paused,
                Round,
                true,
                10,
                10,
                "Competitive"
            )
        );
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void OnlyARecoveryOnAServerWithAPanelIsOwedALoad(bool recovering, bool offline, bool owed)
    {
        Assert.Equal(owed, RoundRestoreGateUtility.OwesLoad(recovering, offline));
    }

    [Fact]
    public void PlayersAreToldWhyResumeWaits()
    {
        Assert.Contains(Round.ToString(), RoundRestoreGateUtility.ResumeRefusal(Round));
    }

    [Fact]
    public void NothingIsSaidWhenNoLoadIsOwed()
    {
        Assert.Null(RoundRestoreGateUtility.ResumeRefusal(null));
    }
}
