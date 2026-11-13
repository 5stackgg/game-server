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

    [Fact]
    public void A_Reconnect_Only_Ends_A_Pause_The_Roster_Made_And_Keeps_A_Surrender_Vote()
    {
        string resume = Method(
            Code("FiveStack.Services", "SurrenderSystem.cs"),
            "public void ResumeIfRosterWhole()"
        );

        Before(resume, "timeoutSystem.RosterOwnsPause()", "ResumeMatch();");
        Assert.DoesNotContain("IsPaused()", resume);
        Assert.DoesNotContain("Reset();", resume);
        Assert.DoesNotContain("EndVote();", resume);
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
}
