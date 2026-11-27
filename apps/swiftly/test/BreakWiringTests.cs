using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// The halftime and overtime break pauses through the engine, so these read the
// plugin. Which rounds are breaks and who readies is in BreakUtilityTests.
public class BreakWiringTests
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

    private static string Method(string source, string signature)
    {
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    private static void Before(string method, string first, string then)
    {
        int at = method.IndexOf(first, StringComparison.Ordinal);

        Assert.True(at >= 0, $"no {first}");
        Assert.True(
            at < method.IndexOf(then, StringComparison.Ordinal),
            $"{first} is not before {then}"
        );
    }

    private static string Timeouts(string signature)
    {
        return Method(Code("FiveStack.Services", "TimeoutSystem.cs"), signature);
    }

    private static string Manager(string signature)
    {
        return Method(Code("FiveStack.Services", "MatchManager.cs"), signature);
    }

    // Out of play and while a round is restored the round start returns early,
    // and a missing player at the break is waited on without a reconnect timeout.
    [Fact]
    public void The_Break_Starts_With_A_Live_Round_Before_The_Roster_Check()
    {
        string roundStart = Method(
            Code("FiveStack.Events", "RoundStart.cs"),
            "public HookResult OnRoundStart("
        );

        Before(roundStart, "if (!isInPlay)", "timeoutSystem.StartBreak();");
        Before(roundStart, "HoldsRoundResults()", "timeoutSystem.StartBreak();");
        Before(roundStart, "ReconcileMemberTeams();", "timeoutSystem.StartBreak();");
        Before(roundStart, "timeoutSystem.StartBreak();", "HoldForRoster(");
    }

    [Fact]
    public void The_Break_Is_Skipped_On_A_Restore_A_Surrender_Or_When_Taken()
    {
        string start = Timeouts("private bool ArmBreak(");

        Assert.Contains("BreakUtility.BreakBefore(", start);
        Assert.Contains("BreakUtility.Owed(", start);
        Assert.Contains("matchData.options.halftime_pausematch", start);
        Assert.Contains("isSurrendered()", start);
        Assert.Contains("HoldsRoundResults()", start);
        Assert.Contains("IsResettingRound()", start);
        Assert.Contains("_breaksTaken.Contains(key)", start);
        Assert.Contains("ScoresTied()", start);
        Before(start, "_breaksTaken.Add(key);", ".PauseMatch(");
    }

    // Both teams owe a ready, and the queued retry behind a tactical timeout
    // drops once the break is over.
    [Fact]
    public void The_Break_Waits_For_Both_Teams()
    {
        string start = Timeouts("private bool ArmBreak(");

        Assert.Equal(2, Regex.Matches(start, @"_teamsPendingResume\.Add\(").Count);
        Before(start, "_teamsPendingResume.Clear();", "_teamsPendingResume.Add(");
        Assert.Contains("_requiresTeamResumeForCurrentPause = true;", start);
        Assert.Contains("_breakPause = true;", start);
        Assert.Contains("_breakPause && ShouldRequireTeamResume()", start);
        Assert.DoesNotContain("PhaseTimer(", start);
    }

    [Fact]
    public void A_New_Break_Is_Armed_Fresh()
    {
        Assert.Contains("ArmBreak(false);", Timeouts("public void StartBreak("));
        Assert.Contains("match.IsInPlay()", Timeouts("private bool ArmBreak("));
    }

    [Fact]
    public void Ending_The_Pause_Ends_The_Break()
    {
        Assert.Contains("_breakPause = false;", Timeouts("public void ClearPendingTeamResumes()"));
        Assert.Contains("ClearPendingTeamResumes();", Timeouts("public void Reset()"));
    }

    // A team readies by lineup, whatever the technical pause setting says, and
    // staff force the break past the camera and empty team holds.
    [Fact]
    public void A_Break_Is_Readied_Before_The_Technical_Pause_Setting_Is_Asked()
    {
        string resume = Timeouts("public void RequestResume(");

        Assert.Contains("BreakUtility.WhoReadies(", resume);
        Before(resume, "if (overrides)", "ReadyForBreak(match, matchData, player);");
        Before(resume, "ReadyForBreak(match, matchData, player);", "if (!CanPause(player))");
        Assert.Contains("&& !overrides\n            && _cameraSystem.IsBlocking()", resume);
        Assert.Contains("!overrides && IsATeamEmpty()", resume);
        Assert.DoesNotContain("IsAdminOrOrganizer(player, matchData))", resume);
    }

    [Fact]
    public void A_Team_Is_Readied_By_Its_Lineup_Not_Where_The_Player_Stands()
    {
        string ready = Timeouts("private void ReadyForBreak(");

        Assert.Contains("BreakUtility.LineupOf(", ready);
        Assert.Contains("TeamUtility.GetLineupSide(", ready);
        Assert.Contains("\"break.not_in_lineup\"", ready);
        Assert.Contains("\"break.team_already_ready\"", ready);
        Assert.Contains("\"break.team_ready\"", ready);
        Assert.Contains("\"break.both_ready\"", ready);
        Before(ready, "_teamsPendingResume.Remove(side);", "Resume(match,");
    }

    [Fact]
    public void Ready_Readies_The_Team_During_A_Break()
    {
        string ready = Method(Code("FiveStack.Commands", "Ready.cs"), "public void OnReady(");

        Before(ready, "timeoutSystem.IsBreak()", "ToggleWarmupReady(");
        Assert.Contains("RequestResume(player, readyCommand: true);", ready);
        Assert.DoesNotContain(
            "IsBreak()",
            Method(Code("FiveStack.Commands", "Ready.cs"), "public void OnUnready(")
        );
    }

    [Fact]
    public void The_Paused_Hint_Names_Who_The_Break_Waits_For()
    {
        Before(Manager("public void PauseMatch("), "BreakHint()", "ReconnectTimeoutSecondsLeft()");
    }

    // CS2's own halftime pause ends on any player's resume, so the plugin keeps
    // it off and pauses itself.
    [Fact]
    public void CS2s_Halftime_Pause_Stays_Off()
    {
        string live = Manager("private void StartLive()");
        string cvars = Manager("private void ApplyWorkshopBlockedCvars()");

        Assert.Matches(@"mp_halftime_pausematch""(\)\?\.SetValue\(|, )false\)", live);
        Assert.DoesNotContain("options.halftime_pausematch", live);
        Assert.Contains(
            "cvar.Key == \"mp_halftime_pausematch\" && _matchData.options.halftime_pausematch",
            cvars
        );
        Assert.Contains("[$\"{cvar.Key} 0\"]", cvars);
        Assert.DoesNotContain("halftime_pausematch ? \"1\"", cvars);
    }

    // A lineup player whose account is staff readies their own team like anyone.
    [Fact]
    public void Only_Connected_Staff_Force_A_Break()
    {
        Assert.Contains(
            "TimeoutUtility.IsStaff(ConnectedRole(player, matchData), null)",
            Timeouts("public void RequestResume(")
        );
    }

    // ResumeMatch refuses during a tactical timeout or a restore, and the break
    // must not be cleared by a resume that never happened.
    [Fact]
    public void A_Break_Is_Not_Cleared_By_A_Refused_Resume()
    {
        string resume = Timeouts("public void RequestResume(");
        string ready = Timeouts("private void ReadyForBreak(");
        string waits = Timeouts("private bool BreakResumeWaits(");

        Before(resume, "if (!BreakResumeWaits(player))", "Resume(match, resumeMessage);");
        Before(ready, "BreakResumeWaits(player)", "_teamsPendingResume.Remove(side);");
        Assert.Contains("IsTimeoutActive()", waits);
        Assert.Contains("IsResettingRound()", waits);
    }

    [Fact]
    public void Both_Teams_Ready_With_A_Player_Gone_Hands_The_Pause_To_The_Roster()
    {
        string ready = Timeouts("private void ReadyForBreak(");
        string hand = Timeouts("private bool HandBreakToRoster(");

        Before(ready, "HandBreakToRoster(match, matchData)", "\"break.both_ready\"");
        Assert.Contains("ReconnectPauseUtility.ShouldPause(", hand);
        Before(hand, "EndBreakFor(ePauseReason.Roster);", "HoldForRoster(players)");
        Assert.Contains("match.waiting_reconnect", hand);
    }

    [Fact]
    public void A_Restore_Ends_The_Break_And_Gives_It_Back()
    {
        string restore = Method(
            Code("FiveStack.Services", "GameBackUpRounds.cs"),
            "public void RestoreRound("
        );
        string end = Timeouts("public void EndBreakFor(");

        Before(restore, "EndBreakFor(ePauseReason.Restore);", "PauseMatch(ePauseReason.Restore);");
        Assert.Contains("_breaksTaken.Remove(", end);
        Assert.Contains("HandPauseTo(reason);", end);
    }

    [Fact]
    public void No_Tactical_Timeout_In_A_Break()
    {
        Before(Timeouts("public void CallTacTimeout("), "if (_breakPause)", "CanCallTacticalTimeout(player)");
    }

    // The new plugin knows nothing of the break, only that the api says paused.
    [Fact]
    public void A_Plugin_Reload_In_A_Break_Rearms_It()
    {
        string status = Manager("public void UpdateMapStatus(");
        string rearm = Timeouts("public bool RearmBreak()");

        Before(status, "_timeoutSystem.RearmBreak()", "PauseMatch(_pauseReason ?? ePauseReason.Admin, null, true);");
        Assert.Contains("IsFreezePeriod()", rearm);
        Assert.Contains("skipUpdate: reloaded", Timeouts("private bool ArmBreak("));
    }
}
