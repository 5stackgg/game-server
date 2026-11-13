using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// A timer outlives whatever started it. Anything said or done for one phase of a
// map -- warmup, knife, play, the end -- goes through MatchManager.PhaseTimer,
// which EndPhase kills when the phase changes. Only the timers listed here run
// on their own, and none of them speaks for a phase.
public class PhaseTimerWiringTests
{
    private static readonly string[] OwnTimers =
    {
        "MatchManager.PhaseTimer",
        "IdleMapReload.Start",
        "MapChange.ListenForMapChange",
        "MatchEvents.ConnectAndMonitor",
        "MatchEvents.MonitorConnection",
        "MatchService.GetMatchFromApi",
        "MatchManager.StartPlayerNameCheck",
        "MatchManager.SetupTeams",
        "MatchManager.EnforceMemberTeam",
        "GameEnd.ConcludeMap",
        "GameEnd.HandleEndOfMap",
        "MatchManager.StartPlaycastWindowHeartbeat",
        "MatchManager.delayChangeMap",
        "GameBackUpRounds.OweLoad",
        "GameBackUpRounds.RestoreRound",
        "VoteSystem.StartVote",
    };

    private static readonly string[] MovesLater = { "SurrenderSystem" };

    private static readonly Regex RawTimer = new(
        @"\b(?:AddTimer|Repeat|RepeatBySeconds|DelayBySeconds|Delay)\s*\(|\bnew\s+Timer\s*\("
    );

    private static readonly Regex Member = new(
        @"^[ \t]+(?:public|private|protected|internal)\b[^\n;={}]*?\b(\w+)\s*\(",
        RegexOptions.Multiline
    );

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

    private static string ManagerMethod(string signature)
    {
        return Method(Code("FiveStack.Services", "MatchManager.cs"), signature);
    }

    private static string ReadyMethod(string signature)
    {
        return Method(Code("FiveStack.Services", "ReadySystem.cs"), signature);
    }

    private static string TimeoutMethod(string signature)
    {
        return Method(Code("FiveStack.Services", "TimeoutSystem.cs"), signature);
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

    [Fact]
    public void Only_The_Listed_Timers_Run_Outside_A_Phase()
    {
        List<string> unlisted = new List<string>();

        foreach (string folder in new[] { "FiveStack.Services", "FiveStack.Events" })
        {
            foreach (string path in Directory.GetFiles(Path.Combine(SourceDir(), folder), "*.cs"))
            {
                string file = Path.GetFileNameWithoutExtension(path);

                if (MovesLater.Contains(file))
                {
                    continue;
                }

                string source = Code(folder, Path.GetFileName(path));
                List<Match> members = Member.Matches(source).ToList();

                foreach (Match timer in RawTimer.Matches(source))
                {
                    Match? member = members.LastOrDefault(candidate => candidate.Index < timer.Index);
                    string owner = $"{file}.{member?.Groups[1].Value}";

                    if (!OwnTimers.Contains(owner))
                    {
                        unlisted.Add(owner);
                    }
                }
            }
        }

        Assert.True(
            unlisted.Count == 0,
            $"use MatchManager.PhaseTimer in {string.Join(", ", unlisted.Distinct())}"
        );
    }

    [Fact]
    public void A_Phase_Timer_From_An_Ended_Phase_Does_Nothing()
    {
        string timer = ManagerMethod("PhaseTimer(float ");

        Assert.Matches(
            @"int (\w+) = _phaseGeneration;[\s\S]*if \(\1 != _phaseGeneration\)\s*\{\s*return;\s*\}\s*callback\(\);",
            timer
        );
    }

    [Fact]
    public void A_Status_In_Another_Phase_Ends_The_Phase_Before_The_New_One_Starts()
    {
        string update = ManagerMethod("public void UpdateMapStatus(");

        Assert.Contains("MatchPhaseUtility.PhaseOf(", update);
        Before(update, "EndPhase();", "switch (status)");
    }

    [Fact]
    public void A_Map_That_Is_Over_Is_Never_Taken_Back_Into_Play()
    {
        string update = ManagerMethod("public void UpdateMapStatus(");

        Before(update, "MatchPhaseUtility.Reopens(_currentMapStatus, status)", "EndPhase();");
    }

    [Fact]
    public void Ending_A_Phase_Kills_Its_Timers_Its_Votes_And_The_Ready_Up()
    {
        string end = ManagerMethod("public void EndPhase()");

        Assert.Contains("_phaseGeneration++;", end);
        Assert.Contains("_phaseTimers.Clear();", end);
        Assert.Contains("_timeoutSystem.Reset();", end);
        Assert.Contains("readySystem.Reset();", end);

        string reset = TimeoutMethod("public void Reset()");

        Assert.Contains("pauseVote?.Abandon();", reset);
        Assert.Contains("resumeVote?.Abandon();", reset);
        Assert.Contains("ClearPendingTeamResumes();", reset);
    }

    [Fact]
    public void A_Reset_A_New_Map_And_A_New_Match_End_The_Phase()
    {
        Assert.Contains("EndPhase();", ManagerMethod("public void Reset()"));

        Before(
            Method(Code("FiveStack.Events", "MapChange.cs"), "public void ListenForMapChange()"),
            "EndPhase();",
            "Task.Delay("
        );

        string service = Code("FiveStack.Services", "MatchService.cs");

        foreach (string signature in new[] { "void GetMatchFromApi()", "void GetMatchFromOffline()" })
        {
            string fetch = Method(service, signature);
            int create = fetch.IndexOf("GetRequiredService(typeof(MatchManager))", StringComparison.Ordinal);

            Assert.True(create > 0, $"{signature} no longer builds a MatchManager");

            int returned = Math.Max(0, fetch.LastIndexOf("return;", create, StringComparison.Ordinal));

            Assert.True(
                fetch[returned..create].Contains("Reset()"),
                $"{signature} replaces a match without resetting it"
            );
        }
    }

    [Fact]
    public void The_Not_Ready_List_Waits_On_A_Phase_Timer_And_An_Open_Ready_Up()
    {
        string notReady = ReadyMethod("void SendNotReadyMessage()");

        Assert.DoesNotContain("Task.Delay(", notReady);
        Before(notReady, "_notReadyMessageTimer = match.PhaseTimer(", "IsWaitingForReady()");
        Before(notReady, "IsWaitingForReady()", "GetNotReadyPlayers()");
    }

    [Fact]
    public void Closing_The_Ready_Up_Drops_A_Pending_Not_Ready_List()
    {
        Assert.Contains("KillPhaseTimer(_notReadyMessageTimer);", ReadyMethod("public void Reset("));
    }

    [Fact]
    public void A_Leaver_Is_Only_Unreadied_While_The_Ready_Up_Is_Open()
    {
        Before(ReadyMethod("public void UnreadyPlayer("), "IsWaitingForReady()", "SendNotReadyMessage();");
    }

    [Fact]
    public void Ready_Needs_The_Map_In_Warmup_Not_The_Knife_Side_Pick()
    {
        string ready = Method(Code("FiveStack.Commands", "Ready.cs"), "public void OnReady(");

        Assert.Contains("GetCurrentMapStatus() != eMapStatus.Warmup", ready);
        Assert.DoesNotContain("IsWarmup()", ready);
    }

    [Fact]
    public void The_Knife_Alert_And_Captain_Prompt_End_With_The_Knife()
    {
        string knife = Code("FiveStack.Services", "KnifeSystem.cs");

        Assert.Contains("PhaseTimer(", Method(knife, "public void Start()"));
        Before(
            Method(knife, "public void SetWinningTeam("),
            "KillPhaseTimer(_knifeRoundTimer);",
            "_knifeRoundTimer = "
        );
    }

    [Fact]
    public void Resume_Is_Refused_When_Nothing_Is_Paused()
    {
        string resume = TimeoutMethod("public void RequestResume(");

        Before(resume, "IsPaused()", "StartVote(");
        Before(resume, "\"timeout.not_paused\"", "StartVote(");

        string[] locales = Directory.GetFiles(Path.Combine(SourceDir(), "lang"), "*.json");

        Assert.Equal(17, locales.Length);
        foreach (string locale in locales)
        {
            Assert.Contains("\"timeout.not_paused\"", File.ReadAllText(locale));
        }
    }

    [Fact]
    public void A_Tech_Pause_Is_Not_Called_Over_A_Pause()
    {
        Before(TimeoutMethod("public void RequestPause("), "IsPaused()", "StartVote(");
    }

    [Fact]
    public void A_Passed_Pause_Vote_Only_Pauses_A_Map_Still_In_Play()
    {
        string pause = TimeoutMethod("private void PauseTechMatch(");

        Before(pause, "IsInPlay()", "PauseMatch(");
        Before(pause, "IsPaused()", "PauseMatch(");
    }

    [Fact]
    public void Resuming_Ends_Both_Votes()
    {
        Assert.Contains("_timeoutSystem.Reset();", ManagerMethod("public void ResumeMatch("));
    }
}
