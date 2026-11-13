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
        "MapChange.OnMapLoad",
        "MatchEvents.ConnectAndMonitor",
        "MatchEvents.MonitorConnection",
        "MatchService.GetMatchFromApi",
        "RankSystem.Start",
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
        "TimerUtility.AddTimer",
        "TimerUtility.Repeat",
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
        string[] built = { "bin", "obj", "build" };

        foreach (string path in Directory.GetFiles(SourceDir(), "*.cs", SearchOption.AllDirectories))
        {
            string file = Path.GetFileNameWithoutExtension(path);
            string[] folders = Path.GetRelativePath(SourceDir(), path).Split(Path.DirectorySeparatorChar);

            if (folders.Any(folder => built.Contains(folder)) || MovesLater.Contains(file))
            {
                continue;
            }

            string source = Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
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
    public void The_Old_Phase_Ends_Before_The_New_Status_Sets_Up()
    {
        Before(ManagerMethod("public void UpdateMapStatus("), "EndPhase();", "switch (status)");
    }

    [Fact]
    public void Warmup_Sets_Up_The_Ready_Up_Only_While_It_Is_Still_Warmup()
    {
        Assert.Matches(
            @"int (\w+) = _phaseGeneration;\s*HibernationUtility\.Next(?:Frame|Tick)\(\(\) =>\s*\{\s*if \(\1 != _phaseGeneration\)\s*\{\s*return;\s*\}",
            ManagerMethod("private void StartWarmup()")
        );
    }

    [Fact]
    public void A_Map_That_Is_Over_Is_Never_Taken_Back_Into_Play()
    {
        string update = ManagerMethod("public void UpdateMapStatus(");

        Before(update, "MatchPhaseUtility.Reopens(_currentMapStatus, status)", "EndPhase();");
    }

    // Closing a ready-up redraws every player's tag, which needs the engine.
    [Fact]
    public void Ending_A_Phase_Closes_The_Ready_Up_Only_While_It_Is_Open()
    {
        Assert.Matches(
            @"if \(readySystem\.IsWaitingForReady\(\)\)\s*\{\s*readySystem\.Reset\(\);\s*\}",
            ManagerMethod("public void EndPhase()")
        );
    }

    [Fact]
    public void A_Reset_A_New_Map_And_A_New_Match_End_The_Phase()
    {
        Assert.Contains("EndPhase();", ManagerMethod("public void Reset()"));

        Before(
            Method(Code("FiveStack.Events", "MapChange.cs"), "public async void OnMapLoad("),
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
        string start = Method(knife, "public void Start()");

        Assert.Contains("PhaseTimer(", start);
        Assert.DoesNotContain("NextTick(", start);
        Before(
            Method(knife, "public void SetWinningTeam("),
            "KillPhaseTimer(_knifeRoundTimer);",
            "_knifeRoundTimer = "
        );
    }

    private static void EveryLocaleHas(string key)
    {
        string[] locales = Directory.GetFiles(
            Path.Combine(SourceDir(), "resources", "translations"),
            "*.jsonc"
        );

        Assert.Equal(17, locales.Length);
        foreach (string locale in locales)
        {
            Assert.Contains($"\"{key}\"", File.ReadAllText(locale));
        }
    }

    // During a tactical timeout there is nothing a player can resume, and it ends
    // on its own.
    [Fact]
    public void Resume_Says_Nothing_Is_Paused_Unless_A_Tactical_Timeout_Is_On()
    {
        string resume = TimeoutMethod("public void RequestResume(");
        Match refusal = Regex.Match(
            resume,
            @"bool (\w+) = TimeoutUtility\.HasPauseToResume\(\s*match\.GetCurrentMapStatus\(\),\s*MatchUtility\.Rules\(\)\?\.MatchWaitingForResume == true\s*\);\s*if \(player != null && !\1\)\s*\{\s*if \(!IsTimeoutActive\(\)\)\s*\{[^{}]*""timeout\.not_paused""[^{}]*\}\s*return;\s*\}"
        );

        Assert.True(refusal.Success, "resume is not refused when nothing is paused");
        Assert.True(refusal.Index < resume.IndexOf("StartVote(", StringComparison.Ordinal));
        EveryLocaleHas("timeout.not_paused");
    }

    [Fact]
    public void A_Tech_Pause_Is_Only_Refused_Over_One_Both_Teams_Must_Resume()
    {
        string pause = TimeoutMethod("public void RequestPause(");
        Match refusal = Regex.Match(
            pause,
            @"if \(ShouldRequireTeamResume\(\)\)\s*\{\s*SendAlreadyPausedMessage\(player\);\s*return;\s*\}"
        );

        Assert.True(refusal.Success, "a tech pause is refused over a pause it could still turn into one");
        Assert.True(refusal.Index < pause.IndexOf("StartVote(", StringComparison.Ordinal));
        Assert.DoesNotContain("IsPaused()", pause);
        Assert.Contains(
            "_gameServer.LocalizerFor(player)[\"timeout.already_paused\", ChatColors.Red]",
            TimeoutMethod("private void SendAlreadyPausedMessage(")
        );
        EveryLocaleHas("timeout.already_paused");
    }

    // Anything else holding the match -- a drop, a camera, an admin -- becomes a
    // pause both teams resume, so it holds once the missing player is back.
    [Fact]
    public void A_Passed_Pause_Vote_Turns_Any_Other_Pause_Into_One_Both_Teams_Resume()
    {
        string pause = TimeoutMethod("private void PauseTechMatch(");

        Assert.Matches(
            @"if \(match == null \|\| ShouldRequireTeamResume\(\) \|\| !match\.IsInPlay\(\)\)\s*\{\s*return;\s*\}",
            pause
        );
        Assert.DoesNotContain("IsPaused()", pause);
    }

    [Fact]
    public void A_Pause_That_Never_Happened_Owes_No_Team_Resume()
    {
        Assert.Matches(
            @"if \(retriesLeft <= 0\)\s*\{[^{}]*if \(!IsPaused\(\)\)\s*\{\s*_timeoutSystem\.ClearPendingTeamResumes\(\);\s*\}\s*return;\s*\}",
            ManagerMethod("public void PauseMatch(")
        );
    }

    [Fact]
    public void Turning_A_Pause_Into_A_Tech_Pause_Ends_A_Resume_Vote()
    {
        string pause = TimeoutMethod("private void PauseTechMatch(");
        int guard = pause.IndexOf("!match.IsInPlay()", StringComparison.Ordinal);
        int abandoned = pause.IndexOf("resumeVote?.Abandon();", StringComparison.Ordinal);

        Assert.True(guard >= 0 && abandoned > guard, "a resume vote outlives the tech pause");
        Assert.True(abandoned < pause.IndexOf("resumeVote = null;", StringComparison.Ordinal));
        Assert.True(abandoned < pause.IndexOf("PauseMatch(", StringComparison.Ordinal));
    }

    [Fact]
    public void Resuming_Ends_Both_Votes()
    {
        Assert.Contains("_timeoutSystem.Reset();", ManagerMethod("public void ResumeMatch("));
    }
}
