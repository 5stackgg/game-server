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
            "the server has no backup for the start of round 1",
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOpenVoteEndsWithItsMap(bool recovering)
    {
        _state.StartVote(recovering, Map);

        Assert.True(_state.LeaveMap(OtherMap));
        Assert.False(_state.VotingOrLoading);
        Assert.False(_state.HoldingRounds);
        Assert.False(_state.Resetting);
    }

    [Fact]
    public void AnOpenVoteStaysOnItsOwnMap()
    {
        _state.StartVote(map: Map);

        Assert.False(_state.LeaveMap(Map));
        Assert.True(_state.VotingOrLoading);
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(4, true)]
    [InlineData(6, false)]
    [InlineData(Round, false)]
    [InlineData(3, false)]
    public void OnlyADiskRoundTheBackendHasARowForIsUsable(int round, bool usable)
    {
        BackupRound[] rounds =
        {
            new() { round = 4, backup_file = "kept" },
            new() { round = 5 },
        };

        Assert.Equal(usable, RoundRestoreGateUtility.IsUsableOnDisk(rounds, round, false));
    }

    [Fact]
    public void WithNoRowThereIsNothingOnDiskToUse()
    {
        Assert.False(
            RoundRestoreGateUtility.IsUsableOnDisk(Array.Empty<BackupRound>(), Round, false)
        );
    }

    [Fact]
    public void ARowARestoreUndidStaysUnusable()
    {
        BackupRound[] rounds = { new() { round = Round, deleted_at = "2026-11-02" } };

        Assert.False(RoundRestoreGateUtility.IsUsableOnDisk(rounds, Round, false));
    }

    [Fact]
    public void OfflineKeepsItsDiskRoundsAsOnMain()
    {
        BackupRound[] undone = { new() { round = 5, deleted_at = "2026-11-02" } };

        Assert.True(
            RoundRestoreGateUtility.IsUsableOnDisk(Array.Empty<BackupRound>(), Round, true)
        );
        Assert.False(RoundRestoreGateUtility.IsUsableOnDisk(undone, 5, true));
    }

    private static BackupRound Row(int round, string file = "", string deleted = "")
    {
        return new BackupRound
        {
            round = round,
            backup_file = file,
            deleted_at = deleted,
        };
    }

    private static int? RoundToHold(BackupRound[] rounds, int round = Round, int played = 0)
    {
        return RoundRestoreGateUtility.RoundToHold(eMapStatus.Paused, rounds, round, played);
    }

    [Fact]
    public void AHoldStaysOnARoundThatCanStillLoad()
    {
        Assert.Equal(Round, RoundToHold(new[] { Row(5, "kept"), Row(Round, "kept") }));
    }

    [Fact]
    public void AGoneBackupFallsBackToTheNewestTheBackendKept()
    {
        Assert.Equal(5, RoundToHold(new[] { Row(4, "kept"), Row(5, "kept"), Row(Round) }));
    }

    [Fact]
    public void AGoneBackupDoesNotFallBackToARoundARestoreUndid()
    {
        BackupRound[] rounds = { Row(4, "kept"), Row(5, "undone", "2026-11-02"), Row(Round) };

        Assert.Equal(4, RoundToHold(rounds));
    }

    [Fact]
    public void NoBackupWithRoundsRecordedAheadNeedsRestartMap()
    {
        Assert.Equal(RoundRestoreState.NothingToLoad, RoundToHold(new[] { Row(5), Row(Round) }));
    }

    [Fact]
    public void NoRowLeftForTheHeldRoundStillProtectsTheOnesAhead()
    {
        Assert.Equal(RoundRestoreState.NothingToLoad, RoundToHold(new[] { Row(5) }, played: 2));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void NothingRecordedAheadEndsTheHold(int played)
    {
        Assert.Null(RoundToHold(new[] { Row(5) }, played: played));
    }

    [Fact]
    public void NoRowsAtAllEndsTheHold()
    {
        Assert.Null(RoundToHold(Array.Empty<BackupRound>()));
    }

    [Fact]
    public void AnOldBackupIsNoReasonToHoldWithNothingAhead()
    {
        Assert.Null(RoundToHold(new[] { Row(5, "kept") }, played: 5));
    }

    [Fact]
    public void AnOlderBackupStillRecoversRoundsRecordedAhead()
    {
        Assert.Equal(3, RoundToHold(new[] { Row(3, "kept"), Row(5) }, played: 4));
    }

    [Theory]
    [InlineData(eMapStatus.Warmup)]
    [InlineData(eMapStatus.Finished)]
    public void AMapNotInPlayEndsTheHold(eMapStatus status)
    {
        BackupRound[] rounds = { Row(5, "kept"), Row(Round, "kept") };

        Assert.Null(RoundRestoreGateUtility.RoundToHold(status, rounds, Round, 0));
    }

    [Fact]
    public void AHoldOnNothingToLoadStaysPutSoPlayersAreOnlyToldOnce()
    {
        Assert.Equal(
            RoundRestoreState.NothingToLoad,
            RoundToHold(new[] { Row(Round) }, RoundRestoreState.NothingToLoad)
        );
    }

    [Fact]
    public void AHoldOnNothingToLoadKeepsTheMatchHeldWithNoVote()
    {
        _state.Hold(RoundRestoreState.NothingToLoad, Map);

        Assert.True(_state.NeedsRestartMap);
        Assert.False(_state.CanOfferVote);
        Assert.True(_state.HoldingRounds);
        Assert.True(_state.Resetting);
    }

    [Fact]
    public void AnOrdinaryHoldDoesNotNeedRestartMap()
    {
        _state.Hold(Round, Map);

        Assert.False(_state.NeedsRestartMap);
        Assert.True(_state.CanOfferVote);
    }

    [Fact]
    public void RestartMapEndsAHoldOnNothingToLoad()
    {
        _state.Hold(RoundRestoreState.NothingToLoad, Map);
        _state.Abandon();

        Assert.False(_state.NeedsRestartMap);
        Assert.False(_state.HoldingRounds);
    }

    [Fact]
    public void AHoldOnNothingToLoadEndsWithItsMap()
    {
        _state.Hold(RoundRestoreState.NothingToLoad, Map);

        Assert.True(_state.LeaveMap(OtherMap));
        Assert.False(_state.HoldingRounds);
    }

    [Fact]
    public void PlayersAreToldAnAdminNeedsRestartMap()
    {
        Assert.Contains(
            "Restart Map",
            RoundRestoreGateUtility.WhyTheHoldMoved(Round, RoundRestoreState.NothingToLoad)
        );
    }

    [Theory]
    [InlineData(5)]
    [InlineData(null)]
    public void PlayersAreToldWhichRoundCanNoLongerBeRestored(int? next)
    {
        string told = RoundRestoreGateUtility.WhyTheHoldMoved(Round, next);

        Assert.Contains($"start of round {Round + 1}", told);
        Assert.DoesNotContain("Restart Map", told);
    }

    [Fact]
    public void AResumeSaysWhyItWaitsForRestartMap()
    {
        Assert.Equal(
            RoundRestoreGateUtility.RestartMapNeeded,
            RoundRestoreGateUtility.ResumeRefusal(null, needsRestartMap: true)
        );
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
        Assert.Contains(
            $"start of round {Round + 1}",
            RoundRestoreGateUtility.ResumeRefusal(Round)
        );
    }

    [Fact]
    public void NothingIsSaidWhenNoLoadIsOwed()
    {
        Assert.Null(RoundRestoreGateUtility.ResumeRefusal(null));
    }
}
