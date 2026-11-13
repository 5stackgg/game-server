using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// A disconnect, a round start and a timer running out are engine events, so
// these read the plugin. What a timeout costs is tested in ReconnectTimeoutsTests,
// and who ends a roster pause in MatchPhaseTests.
public class ReconnectTimeoutWiringTests
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

    private static string Timeout(string signature)
    {
        return Method(Code("FiveStack.Services", "TimeoutSystem.cs"), signature);
    }

    [Theory]
    [InlineData("PlayerDisconnected.cs", "public HookResult OnPlayerDisconnect(", "match")]
    [InlineData("RoundStart.cs", "public HookResult OnRoundStart(", "matchManager")]
    public void A_Missing_Player_Only_Pauses_A_Match_That_Holds_For_Them(
        string file,
        string signature,
        string match
    )
    {
        string handler = Method(Code("FiveStack.Events", file), signature);

        Before(handler, $"{match}.timeoutSystem.HoldForRoster(", ".PauseMatch(");
        Assert.Single(Regex.Matches(handler, @"\.PauseMatch\("));
    }

    // Behind a tactical timeout the pause is retried, and it must not land after
    // the timeout it was given has already run out.
    [Theory]
    [InlineData("PlayerDisconnected.cs", "public HookResult OnPlayerDisconnect(", "match")]
    [InlineData("RoundStart.cs", "public HookResult OnRoundStart(", "matchManager")]
    public void A_Retried_Pause_Is_Dropped_Once_Nothing_Holds_For_The_Roster(
        string file,
        string signature,
        string match
    )
    {
        string handler = Method(Code("FiveStack.Events", file), signature);
        int retry = handler.IndexOf("stillRequired:", StringComparison.Ordinal);

        Assert.True(retry >= 0, "the pause is no longer checked before a retry");
        Assert.Matches(
            $@"^stillRequired:\s*\(\)\s*=>\s*ReconnectPauseUtility\.ShouldPause\([^;]*?\)\s*&&\s*{match}\.timeoutSystem\.HoldsForRoster\(\)\s*\);",
            handler[retry..]
        );
    }

    // player_disconnect runs with the leaver still on the player list.
    [Fact]
    public void A_Leaver_Is_Not_Counted_As_Still_Here()
    {
        Assert.Matches(
            @"HoldForRoster\(\s*MatchUtility\s*\.Players\(\)\s*\.Where\(connected => connected\.SteamID != player\.SteamID\)",
            Method(
                Code("FiveStack.Events", "PlayerDisconnected.cs"),
                "public HookResult OnPlayerDisconnect("
            )
        );
    }

    [Fact]
    public void A_Timeout_Is_Charged_Before_Its_Timer_Starts_And_Dies_With_The_Phase()
    {
        string start = Timeout("private bool StartReconnectTimeout(");

        Before(start, "_reconnectTimeouts.Charge(", "match.PhaseTimer(seconds, EndReconnectTimeout);");
        Assert.Contains("\"mp_technical_timeout_duration_s\"", start);
        Assert.Contains("\"mp_technical_timeout_per_team\"", start);
        Assert.Contains("return false;", start);
    }

    // A person, a camera or a restore that took the pause over ends it instead.
    [Fact]
    public void A_Spent_Timeout_Only_Resumes_A_Pause_The_Roster_Still_Owns()
    {
        string end = Timeout("private void EndReconnectTimeout(");

        Before(end, "StopReconnectTimeout();", "RosterOwnsPause()");
        Before(end, "!RosterOwnsPause()", "ResumeMatch(");
        Assert.Matches(@"!RosterOwnsPause\(\)\s*\)\s*\{\s*return;\s*\}", end);
    }

    // A lineup with nobody left would have the other side play rounds out alone.
    [Fact]
    public void A_Spent_Timeout_Is_Followed_By_The_Next_One_And_Never_Resumes_Into_An_Empty_Lineup()
    {
        string end = Timeout("private void EndReconnectTimeout(");

        Assert.Matches(
            @"if \(\s*ReconnectPauseUtility\.IsALineupEmpty\((?:[^()]|\([^()]*\))*\)\s*\|\|\s*StartReconnectTimeout\(match, matchData, connected\)\s*\)\s*\{\s*return;\s*\}\s*match\.ResumeMatch\(",
            end
        );
        Assert.Contains("\"match.reconnect_timeout_over\"", end);
    }

    [Fact]
    public void Resuming_Or_Ending_The_Phase_Stops_The_Timeout()
    {
        Assert.Contains("StopReconnectTimeout();", Timeout("public void Reset()"));
        Assert.Matches(
            @"KillPhaseTimer\(_reconnectTimer\);\s*\}\s*_reconnectTimer = null;\s*_reconnectTimeoutEnds = null;",
            Timeout("private void StopReconnectTimeout()")
        );
    }

    [Fact]
    public void A_Timeout_Already_Running_Is_Not_Charged_Twice()
    {
        string hold = Timeout("public bool HoldForRoster(");

        Before(hold, "match.IsLan() || _reconnectTimer != null", "StartReconnectTimeout(");
        Before(hold, "held != ePauseReason.Roster", "StartReconnectTimeout(");
    }

    [Fact]
    public void The_Pause_Reminder_Counts_The_Timeout_Down()
    {
        string pause = Method(Code("FiveStack.Services", "MatchManager.cs"), "public void PauseMatch(");

        Before(pause, "_timeoutSystem.ReconnectTimeoutSecondsLeft()", "\"match.reconnect_timeout\"");
        Before(pause, "\"match.reconnect_timeout\"", "\"match.resume_hint\"");
    }
}
