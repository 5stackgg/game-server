using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// What the state decides is tested in RoundRestoreStateTests. Calling it needs
// the engine, so these read the plugin.
public class RestoreWiringTests
{
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

    private static string Backups()
    {
        return Code("FiveStack.Services", "GameBackUpRounds.cs");
    }

    private static string Method(string source, string signature)
    {
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    private static string Between(string source, string start, string end)
    {
        int from = source.IndexOf(start, StringComparison.Ordinal);
        int to = source.IndexOf(end, from + 1, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, $"{start} has moved");

        return source[from..to];
    }

    private static string PassedVote()
    {
        return Between(
            Method(Backups(), "public void RequestRestoreBackupRound("),
            "\"restore round vote passed\"",
            "\"restore round vote failed\""
        );
    }

    [Fact]
    public void AnOrganizerRestoreIsOnlyRefusedForAVoteOrALoadAsOnMain()
    {
        string refusal = Method(Backups(), "public string? RestoreRefusal(int round)");

        Assert.Contains("_restore.VotingOrLoading,", refusal);
        Assert.DoesNotContain("_restore.InProgress", refusal);
    }

    [Fact]
    public void AVoteIsLetGoAsSoonAsItPasses()
    {
        string passed = PassedVote();

        Assert.Contains("restoreRoundVote = null;", Between(passed, "passed", "RestoreRefusal(round)"));
    }

    [Fact]
    public void APassedVoteAsksWhetherItOwesALoadBeforeSendingIt()
    {
        string passed = PassedVote();
        Match owes = Regex.Match(
            passed,
            @"RoundRestoreGateUtility\.OwesLoad\(\s*recovering,\s*_environmentService\.IsOfflineMode\(\)\s*\)"
        );
        int sent = passed.IndexOf("SendRestoreRoundToBackend(round);", StringComparison.Ordinal);

        Assert.True(owes.Success, "a passed vote does not ask");
        Assert.True(sent > owes.Index, "the answer can beat the debt");
    }

    // A result captured during the vote is from a round the restore is about to
    // undo, and it would publish at the next round start, before the load begins.
    // A refused restore undoes nothing, so its result is real and stays.
    [Fact]
    public void APassedVoteForgetsTheResultOfARoundItIsAboutToUndo()
    {
        string passed = PassedVote();
        int refused = passed.IndexOf("return;", StringComparison.Ordinal);
        int forgotten = passed.IndexOf("_matchEvents.ClearPendingRoundResult();", StringComparison.Ordinal);
        int sent = passed.IndexOf("SendRestoreRoundToBackend(round);", StringComparison.Ordinal);

        Assert.True(forgotten >= 0, "a passed vote leaves the result to publish");
        Assert.True(forgotten > refused, "a refused restore throws away a real result");
        Assert.True(forgotten < sent, "the result can publish after the restore is sent");
    }

    [Fact]
    public void AnOfflineRecoveryTurnsBackupsBackOnAndDeletesNothing()
    {
        string passed = PassedVote();

        Assert.Matches(
            @"\{\s*OweLoad\(round\);\s*\}\s*else if \(recovering\)\s*\{\s*SetBackupWriting\(true\);\s*\}",
            passed
        );
        Assert.DoesNotContain("ReplayMap", passed);
        Assert.DoesNotContain("File.Delete", passed);
        Assert.DoesNotContain("Backups(", passed);
    }

    [Fact]
    public void OnlyADeclinedVoteReplaysTheMap()
    {
        string vote = Method(Backups(), "public void RequestRestoreBackupRound(");
        string declined = vote[vote.IndexOf("\"restore round vote failed\"", StringComparison.Ordinal)..];

        Assert.Contains("ReplayMap(", declined);
        Assert.DoesNotContain("ReplayMap", Method(Backups(), "private void OwedLoadTimedOut(int owing)"));
        Assert.DoesNotContain("ReplayMap", Method(Backups(), "private void ReturnToHold(int round)"));
        Assert.DoesNotContain("ReplayMap", Method(Backups(), "public void RestoreRound(int round)"));
    }

    [Fact]
    public void AnOwedLoadIsTimedFromTheMomentItIsOwed()
    {
        string owe = Method(Backups(), "private void OweLoad(int round)");

        Assert.Contains("_restore.StartOwing(round, ActiveMapId())", owe);
        Assert.Contains("RoundRestoreState.OwedSeconds", owe);
    }

    [Fact]
    public void TheAnswerIsNotGatedOnWhatWasAskedFor()
    {
        string restore = Method(Backups(), "public void RestoreRound(int round)");
        string opening = restore[restore.IndexOf('{')..restore.IndexOf("if (!CanRestoreRound(round))", StringComparison.Ordinal)];

        Assert.Contains("if (_restore.VotingOrLoading)", opening);
        Assert.Single(Regex.Matches(opening, @"\bif\s*\("));
        Assert.DoesNotContain("OwedRound", restore);
        Assert.DoesNotMatch(@"\bround\s*(==|!=|<|>)", restore);
    }

    [Fact]
    public void EveryWayOutOfRestoreRoundSettlesAnOwedLoad()
    {
        string restore = Method(Backups(), "public void RestoreRound(int round)");
        string guarded = Between(restore, "CanRestoreRound(round)", "_restore.StartLoad(");

        MatchCollection exits = Regex.Matches(guarded, @"[^\n]*\n\s*return;");

        Assert.Equal(3, exits.Count);
        Assert.All(exits.Select(exit => exit.Value), exit => Assert.Contains("ReturnOwedToHold();", exit));
    }

    [Fact]
    public void AHoldOnlyComesBackOnAMapStillBeingPlayed()
    {
        string hold = Method(Backups(), "private void ReturnToHold(int round)");
        int played = hold.IndexOf("RoundRestoreGateUtility.IsInPlay(", StringComparison.Ordinal);
        int held = hold.IndexOf("HoldForRestore(round);", StringComparison.Ordinal);

        Assert.True(played >= 0 && held > played, "a hold can come back on a map that is over");
    }

    [Fact]
    public void AnOwedLoadOnAnotherMapTurnsBackupsBackOnIfNothingHolds()
    {
        Assert.Contains(
            "SetBackupWriting(!IsHoldingForRestore());",
            Method(Backups(), "private void OwedLoadTimedOut(int owing)")
        );
    }

    [Fact]
    public void AnOwedLoadThatTimedOutStopsBeingResentBeforeTheHoldReturns()
    {
        string timedOut = Method(Backups(), "private void OwedLoadTimedOut(int owing)");
        int withdrawn = timedOut.IndexOf("_matchEvents.WithdrawRestoreRequests();", StringComparison.Ordinal);
        int held = timedOut.IndexOf("ReturnToHold(", StringComparison.Ordinal);

        Assert.True(withdrawn >= 0, "the request can land after a declined vote");
        Assert.True(withdrawn < held, "the hold can be offered with the request still going out");
    }

    [Fact]
    public void AnUnansweredHoldOnlyComesBackForARoundTheBackendCanLoad()
    {
        Assert.Contains(
            "RoundRestoreGateUtility.RoundToHoldAgain(",
            Method(Backups(), "private void OwedLoadTimedOut(int owing)")
        );
    }

    [Fact]
    public void TheNextMatchLeavesWhatTheLastOneHeldAndPutsBackupsRight()
    {
        string forget = Method(Backups(), "public void ForgetOtherMaps(Guid? map)");
        int left = forget.IndexOf("_restore.LeaveMap(map)", StringComparison.Ordinal);
        int writing = forget.IndexOf("SetBackupWriting(!IsHoldingForRestore());", StringComparison.Ordinal);

        Assert.True(left >= 0 && writing > left, "backups stay off with nothing holding them");
    }

    [Fact]
    public void AMatchIsSetUpBeforeAnythingElseLooksAtItsHold()
    {
        string setup = Method(Code("FiveStack.Services", "MatchManager.cs"), "public void SetupMatch(");
        int active = setup.IndexOf("_activeMapId = _currentMap.id;", StringComparison.Ordinal);
        int forgotten = setup.IndexOf("_backUpManagement.ForgetOtherMaps(_activeMapId);", StringComparison.Ordinal);
        int waiting = setup.IndexOf("eMapStatus.WaitingForTV", active, StringComparison.Ordinal);

        Assert.True(active >= 0 && forgotten > active, "a hold outlives its match");
        Assert.True(forgotten < waiting, "an early return skips it");
    }

    [Fact]
    public void AHoldRemembersItsMapEverywhereItIsSet()
    {
        MatchCollection holds = Regex.Matches(Backups(), @"_restore\.Hold\(([^;]*)\);");

        Assert.Equal(3, holds.Count);
        Assert.All(holds.Select(hold => hold.Groups[1].Value), hold => Assert.EndsWith("ActiveMapId()", hold));
    }

    [Fact]
    public void AHoldIsOnlyOfferedThroughTheState()
    {
        string offer = Method(Backups(), "public void OfferPendingRestore()");
        int asked = offer.IndexOf("_restore.CanOfferVote", StringComparison.Ordinal);
        int taken = offer.IndexOf("_restore.ReleaseHold()", StringComparison.Ordinal);

        Assert.True(asked >= 0 && taken > asked, "a hold can be taken during a load");
    }

    [Fact]
    public void RestartMapAbandonsTheRestore()
    {
        string abandon = Method(Backups(), "public void AbandonRestore()");

        Assert.Contains("_restore.Abandon();", abandon);
        Assert.Contains("SetBackupWriting(true);", abandon);
        Assert.Contains(".Abandon();", abandon.Replace("_restore.Abandon();", ""));
        Assert.DoesNotContain("CancelVote", abandon);
    }

    [Fact]
    public void RestartMapOnlyAbandonsOnceItWasAccepted()
    {
        string state = Between(Code("FiveStack.Commands", "Match.cs"), "SetMatchState(", "match?.UpdateMapStatus(status);");
        int refused = state.IndexOf("refusal != null", StringComparison.Ordinal);
        int abandoned = state.IndexOf("_gameBackupRounds.AbandonRestore();", StringComparison.Ordinal);

        Assert.True(abandoned > refused, "a refused restart can drop a hold");
    }

    [Fact]
    public void ResumingNoLongerReachesARestoreVote()
    {
        Assert.DoesNotContain("restoreRoundVote", Code("FiveStack.Services", "MatchManager.cs"));
    }

    [Fact]
    public void ResumeSaysWhyItIsWaiting()
    {
        string resume = Method(Code("FiveStack.Services", "MatchManager.cs"), "public void ResumeMatch(");

        Assert.Contains("TellWhyResumeWaits();", Between(resume, "IsResettingRound()", "return;"));
    }

    [Fact]
    public void AnOpenVoteStillRunsItsFailure()
    {
        string cancel = Method(Code("FiveStack.Services", "VoteSystem.cs"), "public void CancelVote()");
        int guarded = cancel.IndexOf("if (!IsVoteActive())", StringComparison.Ordinal);
        int failed = cancel.IndexOf("_voteFailedCallback?.Invoke();", StringComparison.Ordinal);

        Assert.True(guarded >= 0 && failed > guarded, "CancelVote no longer fails an open vote");
    }
}
