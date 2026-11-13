using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// Pausing and resuming need the engine, so these read the plugin. What a reason
// lets end a pause is tested in MatchPhaseTests.
public class PauseReasonWiringTests
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

    [Theory]
    [InlineData("FiveStack.Events", "PlayerDisconnected.cs", "public HookResult OnPlayerDisconnect(", "Roster")]
    [InlineData("FiveStack.Events", "RoundStart.cs", "public HookResult OnRoundStart(", "Roster")]
    [InlineData("FiveStack.Services", "CameraSystem.cs", "private void OnCamerasLost(", "Camera")]
    [InlineData("FiveStack.Services", "GameBackUpRounds.cs", "public void RestoreRound(", "Restore")]
    [InlineData("FiveStack.Services", "TimeoutSystem.cs", "private void PauseTechMatch(", "Tech")]
    public void Every_Pause_Says_Why(string folder, string file, string signature, string reason)
    {
        MatchCollection pauses = Regex.Matches(
            Method(Code(folder, file), signature),
            @"\.PauseMatch\(\s*([^,)]*)"
        );

        Assert.NotEmpty(pauses);
        Assert.All(pauses, pause => Assert.Equal($"ePauseReason.{reason}", pause.Groups[1].Value));
    }

    // match_state, or the status the api had when the plugin loaded, gives no
    // reason, so only a person ends it.
    [Fact]
    public void A_Pause_Set_From_The_Status_Alone_Is_An_Admin_Pause()
    {
        Assert.Contains(
            "PauseMatch(_pauseReason ?? ePauseReason.Admin, null, true);",
            Method(Code("FiveStack.Services", "MatchManager.cs"), "public void UpdateMapStatus(")
        );
    }

    // A leaver whose seat a substitute filled is still gone, and is reported when
    // their timer runs out.
    [Fact]
    public void A_Reconnect_Only_Ends_A_Pause_The_Roster_Made_And_Keeps_The_Vote_And_Leavers_Timers()
    {
        string resume = Method(
            Code("FiveStack.Services", "SurrenderSystem.cs"),
            "public void ResumeIfRosterWhole()"
        );

        Before(resume, "timeoutSystem.RosterOwnsPause()", "ResumeMatch();");
        Assert.DoesNotContain("IsPaused()", resume);
        Assert.DoesNotContain("Reset();", resume);
        Assert.DoesNotContain("EndVote();", resume);
        Assert.DoesNotContain("KillDisconnectTimers();", resume);
    }

    [Fact]
    public void A_Declined_Restore_Only_Ends_Its_Own_Pause()
    {
        string request = Method(
            Code("FiveStack.Services", "GameBackUpRounds.cs"),
            "public void RequestRestoreBackupRound("
        );

        Before(request, "\"restore round vote failed\"", "ResumeFrom(ePauseReason.Restore);");
        Assert.DoesNotContain("ResumeMatch(", request);
    }

    // Only staff and the console get past the camera gate, so play going on with a
    // camera still down is them overriding it.
    [Fact]
    public void Staff_Resuming_Over_A_Breach_Uses_Up_The_Camera_Pause()
    {
        string timeout = Code("FiveStack.Services", "TimeoutSystem.cs");
        string request = Method(timeout, "public void RequestResume(");

        Assert.Matches(
            @"ResumeMatch\(message\);\s*if \(!match\.IsPaused\(\) && _cameraSystem\.IsBlocking\(\)\)\s*\{\s*_cameraSystem\.Override\(\);\s*\}",
            Method(timeout, "private void Resume(")
        );
        Assert.Equal(2, Regex.Matches(request, @"\bResume\(match, resumeMessage\);").Count);
        Assert.Single(Regex.Matches(request, @"ResumeMatch\("));
    }

    // A vote started before the camera went down was voting to end a different
    // pause, and resuming over a camera is staff's call alone.
    [Fact]
    public void A_Lost_Camera_Ends_A_Resume_Vote_And_A_Passed_One_Never_Resumes_Over_A_Camera()
    {
        string lost = Method(Code("FiveStack.Services", "CameraSystem.cs"), "private void OnCamerasLost(");
        int inPlay = lost.IndexOf("eMatchPhase.InPlay", StringComparison.Ordinal);
        int abandoned = lost.IndexOf("match.timeoutSystem.resumeVote?.Abandon();", StringComparison.Ordinal);

        Assert.True(inPlay >= 0 && abandoned > inPlay, "a lost camera leaves a resume vote running");
        Before(lost, "match.timeoutSystem.resumeVote = null;", ".PauseMatch(");

        string request = Method(Code("FiveStack.Services", "TimeoutSystem.cs"), "public void RequestResume(");
        int passed = request.IndexOf("\"resume vote passed\"", StringComparison.Ordinal);

        Assert.True(passed >= 0, "the resume vote's success is gone");
        Assert.Matches(
            @"^""resume vote passed""\);\s*resumeVote = null;\s*if \(_cameraSystem\.IsBlocking\(\)\)\s*\{[^{}]*""camera\.cannot_resume""[^{}]*return;\s*\}\s*_matchService\s*\.GetCurrentMatch\(\)\s*\?\.ResumeMatch\(",
            request[passed..]
        );
    }

    // Behind a tactical timeout the pause is retried, and it can run out of
    // retries; a pause that never landed is still owed to the next round in play.
    [Fact]
    public void A_Camera_Pause_That_Never_Lands_Stays_Owed()
    {
        Assert.Matches(
            @"\.PauseMatch\(ePauseReason\.Camera, message, stillRequired: IsBlocking\);\s*_pauseOwed = !match\.IsPaused\(\);",
            Method(Code("FiveStack.Services", "CameraSystem.cs"), "private void OnCamerasLost(")
        );
    }

    [Fact]
    public void Cameras_Coming_Back_Only_Ask_For_A_Resume_While_Paused()
    {
        Before(
            Method(Code("FiveStack.Services", "CameraSystem.cs"), "private void OnCamerasRestored("),
            "IsPaused()",
            "\"camera.restored\""
        );
    }

    [Fact]
    public void An_Overtime_Round_End_Leaves_A_Pause_In_Place()
    {
        string ended = Method(Code("FiveStack.Events", "RoundEnd.cs"), "public HookResult OnRoundOfficiallyEnded(");

        Assert.Contains("match.MarkOvertime();", ended);
        Assert.DoesNotContain("UpdateMapStatus(eMapStatus.Overtime)", ended);
    }

    [Fact]
    public void A_New_Map_Owes_A_Pause_For_A_Camera_Still_Down()
    {
        Before(Code("FiveStack.Events", "MapChange.cs"), "EndPhase();", "_cameraSystem.NewMap();");
    }

    [Fact]
    public void A_Reconnect_Ends_A_Leavers_Vote_Once_Their_Lineup_Is_Whole()
    {
        string source = Code("FiveStack.Services", "SurrenderSystem.cs");
        string resume = Method(source, "public void ResumeIfRosterWhole()");

        Assert.Matches(
            @"if \(LeaverVoteLineupIsWhole\(match, connected\)\)\s*\{\s*surrenderingVote\?\.CancelVote\(\);\s*\}",
            resume
        );
        Before(resume, "LeaverVoteLineupIsWhole(", "RosterOwnsPause()");
        Assert.Contains(
            "_leaverVoteLineup = forLeaver ? surrenderingLineup : null;",
            Method(source, "public void SetupSurrender(")
        );
        Assert.Contains("forLeaver: true", Method(source, "private void OfferLeaverSurrender("));
        Assert.DoesNotContain("forLeaver", Code("FiveStack.Commands", "Surrender.cs"));
    }
}
