using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// A disconnect, a round start and a timer running out are engine events, so
// these read the plugin. What a timeout costs is tested in ReconnectTimeoutsTests,
// and who holds for a roster and ends its pause in MatchPhaseTests.
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

    // The last line a cfg gives a setting is the one the server keeps, and
    // Valve's block at the top of each gives 1.
    [Theory]
    [InlineData("competitive")]
    [InlineData("wingman")]
    [InlineData("duel")]
    [InlineData("rush")]
    public void Every_Match_Type_Gives_Each_Team_Two_Technical_Timeouts(string type)
    {
        string cfg = File.ReadAllText(
            Path.Combine(SourceDir(), "..", "..", "..", "shared", "cfg", $"5stack.{type}.cfg")
        );

        Assert.Equal(
            "2",
            Regex
                .Matches(cfg, @"^mp_technical_timeout_per_team\s+(\S+)", RegexOptions.Multiline)
                .Last()
                .Groups[1]
                .Value
        );
        Assert.Contains(
            "TechnicalTimeoutSetting(\"mp_technical_timeout_per_team\", 2)",
            Timeout("private bool ChargeReconnectTimeout(")
        );
    }

    [Fact]
    public void A_Timeout_Takes_Its_Count_And_Length_From_The_Servers_Settings()
    {
        string charge = Timeout("private bool ChargeReconnectTimeout(");

        Before(charge, "\"mp_technical_timeout_duration_s\"", "_reconnectTimeouts.Charge(");
        Before(charge, "_reconnectTimeouts.Charge(", "\"mp_technical_timeout_per_team\"");
        Assert.Contains(
            "match.PhaseTimer(seconds, EndReconnectTimeout);",
            Timeout("private void StartReconnectTimer(")
        );
    }

    // Behind a tactical timeout the pause is retried for up to 45 seconds, and a
    // clock started at the drop would run, or run out, before anyone is paused.
    [Fact]
    public void The_Clock_Starts_When_The_Pause_Lands()
    {
        string hold = Timeout("public bool HoldForRoster(");

        Assert.Matches(
            @"if \(held == ePauseReason\.Roster\)\s*\{\s*StartReconnectTimer\(match\);\s*\}\s*else\s*\{\s*_reconnectTimeoutOwed = true;\s*\}",
            hold
        );
        Assert.Matches(
            @"if \(IsPaused\(\)\)\s*\{\s*HoldPauseFor\(reason\);\s*if \(_pauseReason == ePauseReason\.Roster\)\s*\{\s*_timeoutSystem\.RosterPauseLanded\(\);\s*\}\s*return;\s*\}\s*_pauseReason = reason;\s*if \(reason == ePauseReason\.Roster\)\s*\{\s*_timeoutSystem\.RosterPauseLanded\(\);\s*\}",
            Method(Code("FiveStack.Services", "MatchManager.cs"), "public void PauseMatch(")
        );
        Assert.Matches(
            @"_reconnectTimeoutOwed = false;\s*StartReconnectTimer\(match\);",
            Timeout("public void RosterPauseLanded()")
        );
    }

    // A person, a camera or a restore that took the pause over ends it instead.
    [Fact]
    public void A_Spent_Timeout_Only_Settles_A_Pause_The_Roster_Still_Owns()
    {
        string end = Timeout("private void EndReconnectTimeout(");

        Before(end, "StopReconnectTimer();", "RosterOwnsPause()");
        Assert.Matches(
            @"!RosterOwnsPause\(\)\s*\)\s*\{\s*return;\s*\}\s*SettleRosterPause\(",
            end
        );
        Assert.DoesNotContain("ResumeMatch(", end);
    }

    // ResumeMatch refuses during a tactical timeout or a round restore, and
    // nothing else comes back to a pause whose timeouts are spent. A reconnect
    // settles twice in one tick, so the timer it re-arms replaces the last one.
    [Fact]
    public void A_Timeout_That_Runs_Out_While_A_Resume_Is_Refused_Looks_Again()
    {
        Assert.Matches(
            @"^[^{]*\{\s*StopReconnectTimer\(\);\s*if \(IsTimeoutActive\(\) \|\| _backUpManagement\.IsResettingRound\(\)\)\s*\{\s*_reconnectTimer = match\.PhaseTimer\(1, EndReconnectTimeout\);\s*return;\s*\}",
            Timeout("private void SettleRosterPause(")
        );
    }

    // A lineup with nobody left would have the other side play rounds out alone.
    [Fact]
    public void A_Spent_Timeout_Is_Followed_By_The_Next_One_And_Never_Resumes_Into_An_Empty_Lineup()
    {
        string settle = Timeout("private void SettleRosterPause(");

        Before(settle, "ReconnectPauseUtility.IsALineupEmpty(", "ChargeReconnectTimeout(");
        Assert.Matches(
            @"if \(_waitsOnEmptyLineup\)\s*\{\s*return;\s*\}\s*if \(ChargeReconnectTimeout\(match, matchData, connected\)\)\s*\{\s*StartReconnectTimer\(match\);\s*return;\s*\}\s*match\.ResumeMatch\(",
            settle
        );
        Assert.Contains("\"match.reconnect_timeout_over\"", settle);
    }

    [Fact]
    public void A_Lineup_With_Nobody_Left_Is_Waited_On_Before_Anything_Is_Charged()
    {
        string hold = Timeout("public bool HoldForRoster(");

        Before(hold, "ReconnectPauseUtility.IsALineupEmpty(", "ChargeReconnectTimeout(");
        Assert.Matches(@"if \(_waitsOnEmptyLineup\)\s*\{\s*return true;\s*\}", hold);
    }

    // The roster is whole only a tick after player_connect_full, which is where
    // a reconnect is looked at.
    [Fact]
    public void A_Reconnect_Is_Told_To_The_Timeouts_Before_The_Roster_Resumes()
    {
        string resume = Method(
            Code("FiveStack.Services", "SurrenderSystem.cs"),
            "public void ResumeIfRosterWhole()"
        );

        Before(resume, "match.timeoutSystem.RosterChanged(connected);", "RosterOwnsPause()");
    }

    // The lineup is charged at the drop. Its player can be back before the pause
    // lands with the other lineup still short, and that one pays instead.
    [Fact]
    public void A_Timeout_Still_Owed_Is_Paid_By_Whoever_Is_Short_After_A_Reconnect()
    {
        string changed = Timeout("public void RosterChanged(");

        Assert.Matches(
            @"if \(_reconnectTimeoutOwed\)\s*\{\s*_reconnectTimeouts\.Refund\(\);\s*_waitsOnEmptyLineup = ReconnectPauseUtility\.IsALineupEmpty\([^;]*\);\s*_reconnectTimeoutOwed =\s*!_waitsOnEmptyLineup && ChargeReconnectTimeout\(match, matchData, players\);\s*return;\s*\}\s*if \(\s*!_waitsOnEmptyLineup",
            changed
        );
        Before(changed, "if (!shortHanded)", "_reconnectTimeouts.Refund();\n            _waitsOnEmptyLineup");
    }

    // A lineup that was empty and is now only short has its timeouts to spend.
    [Fact]
    public void A_Player_Back_On_An_Empty_Lineup_Ends_The_Wait()
    {
        string changed = Timeout("public void RosterChanged(");

        Assert.Matches(
            @"if \(RosterOwnsPause\(\)\)\s*\{\s*SettleRosterPause\(match, matchData, players\);\s*return;\s*\}\s*_waitsOnEmptyLineup = false;\s*_reconnectTimeoutOwed =\s*match\.GetPauseReason\(\) == null && ChargeReconnectTimeout\(match, matchData, players\);",
            changed
        );
    }

    [Fact]
    public void Resuming_Or_Ending_The_Phase_Stops_The_Timeout()
    {
        Assert.Contains("StopReconnectTimeout();", Timeout("public void Reset()"));
        Assert.Matches(
            @"StopReconnectTimer\(\);\s*if \(_reconnectTimeoutOwed\)\s*\{\s*_reconnectTimeouts\.Refund\(\);\s*\}\s*_reconnectTimeoutOwed = false;\s*_waitsOnEmptyLineup = false;",
            Timeout("private void StopReconnectTimeout()")
        );
        Assert.Matches(
            @"KillPhaseTimer\(_reconnectTimer\);\s*\}\s*_reconnectTimer = null;\s*_reconnectTimeoutEnds = null;",
            Timeout("private void StopReconnectTimer()")
        );
    }

    [Fact]
    public void A_Timeout_Already_Given_Is_Not_Charged_Twice()
    {
        string hold = Timeout("public bool HoldForRoster(");

        Before(
            hold,
            "match.IsLan() || _reconnectTimeoutOwed || _reconnectTimer != null",
            "ChargeReconnectTimeout("
        );
        Before(hold, "held != ePauseReason.Roster", "ChargeReconnectTimeout(");
    }

    [Fact]
    public void The_Pause_Reminder_Counts_The_Timeout_Down()
    {
        string pause = Method(Code("FiveStack.Services", "MatchManager.cs"), "public void PauseMatch(");

        Before(pause, "_timeoutSystem.ReconnectTimeoutSecondsLeft()", "\"match.reconnect_timeout\"");
        Before(pause, "\"match.reconnect_timeout\"", "\"match.resume_hint\"");
    }

    // On a LAN the roster never ends its own pause, so a check is how it ends
    // there, held to what the roster's own resume is held to.
    [Fact]
    public void A_Check_Holds_Any_Match_To_What_The_Rosters_Own_Resume_Waits_On()
    {
        string check = Timeout("public void CheckRoster(");

        Assert.Matches(
            @"ReconnectPauseUtility\.Check\(\s*match\.GetPauseReason\(\),\s*ReconnectPauseUtility\.ShouldPause\(matchData, connected, match\.GetExpectedPlayerCount\(\)\),\s*ShouldRequireTeamResume\(\) \|\| _cameraSystem\.IsBlocking\(\) \|\| IsTimeoutActive\(\)\s*\)",
            check
        );
        Assert.DoesNotContain("IsLan()", check);
    }

    [Fact]
    public void A_Check_Tells_Only_Its_Caller_That_Nothing_Is_Paused()
    {
        Assert.Matches(
            @"if \(check == eRosterCheck\.NotPaused\)\s*\{\s*_gameServer\.Message\([^{}]*""timeout\.not_paused""[^{}]*,\s*player\s*\);\s*return;\s*\}",
            Timeout("public void CheckRoster(")
        );
    }

    [Fact]
    public void A_Check_Names_Whoever_Is_Still_Out_To_Everyone_And_Stays_Paused()
    {
        Assert.Matches(
            @"if \(check == eRosterCheck\.Missing\)\s*\{[^{}]*ReadyUtility\.EmptySeats\(matchData, connected, match\.GetExpectedPlayerCount\(\)\)[^{}]*_gameServer\.MessageEach\([^{}]*""timeout\.check_missing""[^{}]*return;\s*\}",
            Timeout("public void CheckRoster(")
        );
    }

    [Fact]
    public void A_Check_Only_Resumes_Once_Every_Other_Answer_Is_Ruled_Out()
    {
        string check = Timeout("public void CheckRoster(");

        Assert.Matches(
            @"if \(check == eRosterCheck\.Whole\)\s*\{\s*_gameServer\.MessageEach\([^{}]*""timeout\.check_whole""[^{}]*return;\s*\}\s*string checkedBy = [^;]*;\s*match\.ResumeMatch\([^;]*""timeout\.check_resumed"", checkedBy,",
            check
        );
        Assert.Single(Regex.Matches(check, @"ResumeMatch\("));
    }

    [Fact]
    public void Check_Is_A_Command_Any_Player_In_The_Server_Can_Type()
    {
        string commands = Code("FiveStack.Commands", "Timeout.cs");

        Assert.Matches(
            @"\[Command\(""check"", registerRaw: false, permission: """"\)\]\s*public void OnCheck\(ICommandContext context\)",
            commands
        );
        Assert.Matches(
            @"if \(context\.Sender == null\)\s*\{\s*return;\s*\}\s*_matchService\.GetCurrentMatch\(\)\?\.timeoutSystem\.CheckRoster\(context\.Sender\);",
            Method(commands, "public void OnCheck(")
        );
        Assert.Contains(
            "localizer[\"help.check\", CommandUtility.PublicChatTrigger]",
            Method(Code("FiveStack.Commands", "Help.cs"), "public void OnHelp(")
        );
    }
}
