using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;
using Xunit;

// CounterStrikeSharp answers a command in the language of the player who typed it,
// so a word passed into that answer has to come from the same translation file.
public class PlayerLanguageTests
{
    private static string AppDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, ".."));
    }

    private static string Compact(string file)
    {
        string source = File.ReadAllText(Path.Combine(AppDir(), "src", file));

        return Regex.Replace(Regex.Replace(source, @"//[^\n]*", ""), @"\s+", "");
    }

    private static string TranslationsDir()
    {
        return Path.Combine(AppDir(), "src", "lang");
    }

    private static Dictionary<string, string> Translations(string file)
    {
        JsonSerializerOptions options = new()
        {
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        return JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(TranslationsDir(), file)),
            options
        )!;
    }

    private static string[] Languages()
    {
        return Directory
            .GetFiles(TranslationsDir(), "*.json")
            .Select(file => Path.GetFileName(file))
            .ToArray();
    }

    private static IEnumerable<string[]> Calls(string source, string opening)
    {
        foreach (Match start in Regex.Matches(source, opening))
        {
            List<string> args = [];
            int from = start.Index + start.Length;
            int depth = 0;

            for (int at = from; at < source.Length; at++)
            {
                char c = source[at];

                if (c == '"')
                {
                    at = source.IndexOf('"', at + 1);

                    if (at < 0)
                    {
                        break;
                    }

                    continue;
                }

                if (c is '(' or '[' or '{')
                {
                    depth++;
                    continue;
                }

                if (c is ')' or ']' or '}')
                {
                    if (depth == 0)
                    {
                        args.Add(source[from..at]);
                        break;
                    }

                    depth--;
                    continue;
                }

                if (c == ',' && depth == 0)
                {
                    args.Add(source[from..at]);
                    from = at + 1;
                }
            }

            Assert.True(args.Count > 0, $"the call at {start.Index} never closes");

            yield return args.ToArray();
        }
    }

    // Where each kind of send takes the text a player reads.
    private static readonly (string Opening, int Text)[] Sends =
    [
        (@"_gameServer\.Message\(", 1),
        (@"\.PrintTo(Chat|Center|CenterAlert|Console)\(", 0),
        (@"\.ReplyToCommand\(", 0),
        (@"Server\.PrintToChatAll\(", 0),
        (@"\.PauseMatch\(", 1),
        (@"\.ResumeMatch\(", 0),
        (@"_gameServer\.MessageEach\(", 1),
        (@"UpdatePlayerName\(", 2),
    ];

    private static bool IsEnglish(string text)
    {
        foreach (Match literal in Regex.Matches(text, @"\$?""([^""]*)"""))
        {
            string words = Regex.Replace(literal.Groups[1].Value, @"\{[^}]*\}|\[[A-Za-z]+\]", "");

            if (Regex.IsMatch(words, @"^[a-z_]+(\.[a-z_]+)+$"))
            {
                continue;
            }

            if (Regex.IsMatch(words, "[A-Za-z]{2,}"))
            {
                return true;
            }
        }

        return false;
    }

    public static IEnumerable<object[]> SourceFiles()
    {
        string src = Path.Combine(AppDir(), "src");

        return Directory
            .GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(file => new object[] { Path.GetRelativePath(src, file) });
    }

    public static IEnumerable<object[]> ServiceAndEventFiles()
    {
        return SourceFiles()
            .Where(file =>
                ((string)file[0]).StartsWith("FiveStack.Services")
                || ((string)file[0]).StartsWith("FiveStack.Events")
            );
    }

    public static IEnumerable<object[]> LanguageFiles()
    {
        return Languages().Select(file => new object[] { file });
    }

    private static string[] Placeholders(string text)
    {
        return Regex
            .Matches(text, @"\{\d+\}")
            .Select(match => match.Value)
            .Distinct()
            .Order()
            .ToArray();
    }

    private static string[] ColorTags(string text)
    {
        return Regex
            .Matches(text, @"\{[A-Za-z]+\}")
            .Select(match => match.Value)
            .Order()
            .ToArray();
    }

    private static void EveryLanguageHas(string key)
    {
        foreach (string language in Languages())
        {
            Assert.True(Translations(language).ContainsKey(key), $"{language} has no {key}");
        }
    }

    [Theory]
    [InlineData("Competitive")]
    [InlineData("Wingman")]
    [InlineData("Duel")]
    [InlineData("Rush")]
    [InlineData("Coach")]
    [InlineData("CoachAndCaptains")]
    [InlineData("CoachAndPlayers")]
    [InlineData("Admin")]
    public void RulesSaysEverySettingInThePlayersLanguage(string setting)
    {
        Match key = Regex.Match(
            Compact(Path.Combine("FiveStack.Commands", "Help.cs")),
            $@"""{setting}""=>""([^""]+)"""
        );

        Assert.True(key.Success, $".rules says {setting} in English");
        EveryLanguageHas(key.Groups[1].Value);
    }

    [Fact]
    public void RulesPassesNoEnglishSettingIntoItsLines()
    {
        string help = Compact(Path.Combine("FiveStack.Commands", "Help.cs"));

        Assert.DoesNotContain("\"rules.allow_timeouts\",StringUtility.ConvertCamelToHumanReadable", help);
        Assert.DoesNotContain("\"rules.tech_timeouts\",StringUtility.ConvertCamelToHumanReadable", help);
        Assert.DoesNotContain("\"rules.match_details\",match.options.type", help);
    }

    [Fact]
    public void ARefusedTimeoutNamesWhatWasRefusedInThePlayersLanguage()
    {
        MatchCollection refusals = Regex.Matches(
            Compact(Path.Combine("FiveStack.Services", "TimeoutSystem.cs")),
            @"CannotPauseMessage\(player,""([^""]*)""\)"
        );

        Assert.Equal(2, refusals.Count);

        foreach (Match refusal in refusals)
        {
            EveryLanguageHas(refusal.Groups[1].Value);
        }

        Assert.Contains(
            "_gameServer.Localize(player,\"timeout.not_allowed\",ChatColors.Red,_gameServer.Localize(player,refusedKey))",
            Compact(Path.Combine("FiveStack.Services", "TimeoutSystem.cs"))
        );
    }

    [Fact]
    public void OnlyTheRulesCommandAnswersDotRules()
    {
        string help = Compact(Path.Combine("FiveStack.Commands", "Help.cs"));

        Assert.Single(Regex.Matches(help, @"ConsoleCommand\(""css_rules"""));
        Assert.Matches(@"ConsoleCommand\(""css_rules"",[^\]]*\]\[CommandHelper\([^\]]*\]publicvoidOnRules\(", help);
    }

    [Fact]
    public void EveryVoteIsNamedByATranslationKey()
    {
        string src = Path.Combine(AppDir(), "src");
        List<string> names = [];

        foreach (string file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            string source = Compact(Path.GetRelativePath(src, file));

            foreach (Match call in Regex.Matches(source, @"(\w*)\.StartVote\(([^,)]*)"))
            {
                string receiver = call.Groups[1].Value;

                if (
                    receiver != ""
                    && Regex.IsMatch(source, $@"RoundRestoreState\??{Regex.Escape(receiver)}[;=,)]")
                )
                {
                    continue;
                }

                names.Add(call.Groups[2].Value);
            }
        }

        Assert.NotEmpty(names);

        foreach (string name in names)
        {
            Match key = Regex.Match(name, @"^""([^""]+)""$");

            Assert.True(key.Success, $"a vote is named {name}, not a translation key");
            EveryLanguageHas(key.Groups[1].Value);
        }
    }

    [Fact]
    public void ALanguageLookupNeverAsksCounterStrikeSharpAboutSteamIdZero()
    {
        string src = Path.Combine(AppDir(), "src");
        int lookups = Directory
            .GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Sum(file =>
                Regex.Matches(Compact(Path.GetRelativePath(src, file)), @"\.ForPlayer\(").Count
            );

        Assert.Equal(2, lookups);
        Assert.Contains(
            "publicstringLocalize(CCSPlayerController?player,stringkey,paramsobject[]args){if(player==null||player.SteamID==0){return_localizer.ForPlayer(null,key,args);}return_localizer.ForPlayer(player,key,args);}",
            Compact(Path.Combine("FiveStack.Services", "GameServer.cs"))
        );
    }

    [Fact]
    public void SourceTvGetsTheServersLanguageNotTheLastCallers()
    {
        Assert.DoesNotContain(
            "_localizer[",
            Compact(Path.Combine("FiveStack.Services", "GameServer.cs"))
        );
    }

    [Fact]
    public void SourceTvIsNeverAMatchPlayer()
    {
        string utility = Compact(Path.Combine("FiveStack.Utilities", "MatchUtility.cs"));
        int from = utility.IndexOf("publicstaticList<CCSPlayerController>Players()", StringComparison.Ordinal);

        Assert.True(from >= 0, "MatchUtility.Players is gone");
        Assert.Contains(
            "||player.IsHLTV",
            utility[from..utility.IndexOf("returnvalidPlayers;", from, StringComparison.Ordinal)]
        );
    }

    [Fact]
    public void TheVoteHudNamesTheVoteInEachPlayersLanguage()
    {
        string votes = Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"));

        Assert.Contains(
            "stringaction=_gameServer.Localize(player,_voteKey,_voteArgs).ToLower();",
            votes
        );
        Assert.DoesNotContain("_localizer", votes);
        Assert.NotEmpty(Regex.Matches(votes, @"PrintToCenter\("));
        Assert.Empty(Regex.Matches(votes, @"PrintToCenter\((?!_gameServer\.Localize\(player,)"));
    }

    [Theory]
    [InlineData("vote.failed")]
    [InlineData("vote.success")]
    public void TheVoteResultReachesEachPlayerInTheirLanguage(string key)
    {
        Assert.Contains(
            $"_gameServer.MessageEach(HudDestination.Center,viewer=>_gameServer.Localize(viewer,\"{key}\",_gameServer.Localize(viewer,_voteKey,_voteArgs)));",
            Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"))
        );
    }

    [Theory]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.lan", "There is no surrender in a LAN match")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.offline", "There is no surrender in an offline match")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.in_progress", "A surrender vote is already in progress")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.unavailable", "Unable to start a surrender vote right now")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.not_in_play", "A surrender can only be called while the map is being played")]
    [InlineData("FiveStack.Commands/Surrender.cs", "surrender.not_in_match", "Only a player in this match can call a surrender")]
    public void ASurrenderRefusalIsInThePlayersLanguage(string file, string key, string english)
    {
        string source = Compact(file);

        Assert.DoesNotContain(Regex.Replace(english, @"\s+", ""), source);
        Assert.Contains($"PrintToConsole(_gameServer.Localize(player,\"{key}\"))", source);
        EveryLanguageHas(key);
    }

    [Theory]
    [InlineData("_voteFailedCallback")]
    [InlineData("_voteSuccessCallback")]
    public void AVoteEndsEvenIfAMessageFails(string outcome)
    {
        string votes = Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"));

        Assert.Matches($@"try\{{_gameServer\.MessageEach\([^;]*\);\}}finally\{{{outcome}\(\);\}}", votes);
        Assert.Contains("_votes[player.SteamID]=vote;CheckVotes();SendVoteMessage();", votes);
    }

    [Fact]
    public void MessageEachSendsEveryPlayerTheirOwnLineAndSourceTvTheServers()
    {
        string server = Compact(Path.Combine("FiveStack.Services", "GameServer.cs"));

        Assert.Contains(
            "foreach(CCSPlayerControllerplayerinMatchUtility.Players()){Send(destination,player,message(player));}",
            server
        );
        Assert.Matches(
            @"CounterStrikeSharp\.API\.Utilities\.GetPlayers\(\)\)\{if\(\w+\.IsHLTV\)\{Send\(destination,\w+,message\(null\)\);",
            server
        );
    }

    [Fact]
    public void ARefusedVoteIsTranslated()
    {
        string votes = Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"));

        Assert.DoesNotContain("youarenotallowedtovote", votes);
        Assert.Contains("_gameServer.Localize(player,\"vote.not_allowed\",ChatColors.Red)", votes);
        EveryLanguageHas("vote.not_allowed");
    }

    [Fact]
    public void VoteLogsKeepTheVotesArguments()
    {
        string[] logs = Regex
            .Matches(
                Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs")),
                @"_logger\.LogInformation\(([^;]*)\);"
            )
            .Select(match => match.Groups[1].Value)
            .Where(log => log.Contains("oteKey"))
            .ToArray();

        Assert.Equal(4, logs.Length);

        foreach (string log in logs)
        {
            Assert.Contains("oteArgs", log);
        }
    }

    [Fact]
    public void TheKnifeAlertNamesTheTeamInEachPlayersLanguage()
    {
        Assert.Contains(
            "_gameServer.MessageEach(HudDestination.Alert,viewer=>_gameServer.Localize(viewer,\"knife.captain_picking\",_gameServer.Localize(viewer,TeamUtility.TeamNameKey(team))));",
            Compact(Path.Combine("FiveStack.Services", "KnifeSystem.cs"))
        );
    }

    [Theory]
    [InlineData("captain.claim_hint")]
    [InlineData("captain.show")]
    [InlineData("captain.assigned")]
    public void TheCaptainLinesNameTheTeamInEachPlayersLanguage(string key)
    {
        string captains = Compact(Path.Combine("FiveStack.Services", "CaptainSystem.cs"));

        Assert.Matches(
            $@"_gameServer\.MessageEach\(HudDestination\.\w+,viewer=>_gameServer\.Localize\(viewer,""{Regex.Escape(key)}"",[^;]*_gameServer\.Localize\(viewer,TeamUtility\.TeamNameKey\(\w+\)\)[^;]*\);",
            captains
        );
        Assert.DoesNotContain("TeamNumToString", captains);
    }

    [Theory]
    [MemberData(nameof(LanguageFiles))]
    public void EveryLanguageHasEveryLine(string language)
    {
        Dictionary<string, string> english = Translations("en.json");
        Dictionary<string, string> translated = Translations(language);
        object[] args = Enumerable.Repeat<object>("x", 10).ToArray();

        foreach ((string key, string text) in english)
        {
            Assert.True(translated.ContainsKey(key), $"{language} has no {key}");
            Assert.True(
                Placeholders(text).SequenceEqual(Placeholders(translated[key])),
                $"{language} {key} does not take the placeholders English does"
            );
            Assert.True(
                Record.Exception(() =>
                    string.Format(Regex.Replace(translated[key], @"\{[A-Za-z]+\}", ""), args)
                ) == null,
                $"{language} {key} has a brace that is not a placeholder"
            );
            Assert.True(
                ColorTags(text).SequenceEqual(ColorTags(translated[key])),
                $"{language} {key} does not use the colors English does"
            );
        }
    }

    [Theory]
    [InlineData(CsTeam.Terrorist, "team.terrorist")]
    [InlineData(CsTeam.CounterTerrorist, "team.ct")]
    [InlineData(CsTeam.Spectator, "team.spectator")]
    public void ATeamIsNamedByATranslationKey(CsTeam team, string key)
    {
        Assert.Equal(key, TeamUtility.TeamNameKey(team));
        EveryLanguageHas(key);
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void NoEnglishIsSentToAPlayer(string file)
    {
        string source = Compact(file);

        foreach ((string opening, int text) in Sends)
        {
            foreach (string[] args in Calls(source, opening))
            {
                Assert.False(
                    args.Length > text && IsEnglish(args[text]),
                    $"{file} sends {string.Join(",", args)}"
                );
            }
        }
    }

    // A command's own reply is already in the caller's language; outside one, a
    // plain lookup is in whatever culture the thread has. A clan tag is one string
    // the whole server sees on the scoreboard.
    [Theory]
    [MemberData(nameof(ServiceAndEventFiles))]
    public void NothingIsLookedUpInTheServersLanguageForAPlayer(string file)
    {
        if (file.EndsWith("GameServer.cs"))
        {
            return;
        }

        string source = Compact(file).Replace("_localizer[\"camera.tag\"]", "");

        Assert.DoesNotContain("_localizer[", source);
        Assert.DoesNotMatch(@"(?<![\w.])Localizer\[", source);
    }

    // Chat a player typed is relayed as they wrote it.
    [Theory]
    [MemberData(nameof(ServiceAndEventFiles))]
    public void ALineForEveryoneGoesThroughMessageEach(string file)
    {
        if (file.EndsWith("PlayerChat.cs"))
        {
            return;
        }

        foreach (string[] args in Calls(Compact(file), @"_gameServer\.Message\("))
        {
            Assert.True(args.Length == 3, $"{file} sends {string.Join(",", args)} to everyone in one language");
        }
    }

    [Theory]
    [InlineData("_gameServer.MessageEach(HudDestination.Alert,message);")]
    [InlineData("_gameServer.MessageEach(HudDestination.Center,message);")]
    public void APauseOrResumeLineReachesEachPlayerInTheirLanguage(string send)
    {
        Assert.Contains(send, Compact(Path.Combine("FiveStack.Services", "MatchManager.cs")));
    }

    [Fact]
    public void ARestoreRefusalReachesEachPlayerInTheirLanguage()
    {
        string backups = Compact(Path.Combine("FiveStack.Services", "GameBackUpRounds.cs"));

        Assert.DoesNotContain("RoundRestoreGateUtility.WhyTheHoldMoved(", backups);
        Assert.DoesNotContain("RoundRestoreGateUtility.ResumeRefusal(", backups);
        Assert.DoesNotContain("OfflineMatchState.ResetToRound(", backups);
        Assert.Contains(
            "publicstringLocalize(CCSPlayerController?player,PlayerLineline)",
            Compact(Path.Combine("FiveStack.Services", "GameServer.cs"))
        );
    }

    // A clan tag is one string the whole server sees on the scoreboard, and the
    // ready system finds its own tags by that string.
    [Fact]
    public void AReadyTagIsInTheServersLanguage()
    {
        string ready = Compact(Path.Combine("FiveStack.Services", "ReadySystem.cs"));

        Assert.DoesNotContain("\"ready\"", ready);
        Assert.DoesNotContain("\"notready\"", ready);
        Assert.Contains(
            "_gameServer.Localize(null,ready?\"ready.tag.ready\":\"ready.tag.not_ready\")",
            ready
        );
        EveryLanguageHas("ready.tag.ready");
        EveryLanguageHas("ready.tag.not_ready");
    }

    // CS:GO cut a clan tag at 15 bytes, and the ready system finds its own tags
    // by reading them back off the scoreboard.
    [Theory]
    [MemberData(nameof(LanguageFiles))]
    public void AScoreboardTagFitsInFifteenBytes(string language)
    {
        foreach (string key in new[] { "ready.tag.ready", "ready.tag.not_ready", "camera.tag" })
        {
            string tag = Translations(language)[key];
            int bytes = System.Text.Encoding.UTF8.GetByteCount(tag);

            Assert.True(bytes <= 15, $"{language} {key} \"{tag}\" is {bytes} bytes");
        }
    }

    // The api reads the refusal off the RCON reply and shows it to the organizer.
    [Fact]
    public void TheApiReadsARestoreRefusalInEnglish()
    {
        string command = Compact(Path.Combine("FiveStack.Commands", "BackupRounds.cs"));

        Assert.Contains("string?refusal=_gameBackupRounds.RestoreRefusal(round)?.English;", command);
        Assert.Contains(
            "command.ReplyToCommand($\"{RoundRestoreGateUtility.RefusedPrefix}{refusal}\");",
            command
        );
        int from = command.IndexOf("publicvoidOnResetRound(CCSPlayerController?player,CommandInfocommand)", StringComparison.Ordinal);

        Assert.True(from >= 0, "the restore_round command is gone");
        Assert.DoesNotMatch(
            @"Localiz|localizer",
            command[from..command.IndexOf("publicvoid", from + 1, StringComparison.Ordinal)]
        );
    }

    // Polish, Russian and Ukrainian inflect a noun after most words, and a team
    // name is one fixed form, so it is set off by brackets or a colon.
    [Theory]
    [InlineData("timeout.team_resumed", "{2}")]
    [InlineData("captain.assigned", "{1}")]
    [InlineData("match.assigned_team", "{1}")]
    public void ASlavicLineNeverInflectsATeamName(string key, string team)
    {
        foreach (string language in new[] { "pl-PL.json", "ru-RU.json", "uk-UA.json" })
        {
            string line = Translations(language)[key];
            string before = Regex.Replace(line[..line.IndexOf(team, StringComparison.Ordinal)], @"\{\d+\}$", "");

            Assert.Matches(@"(^|[(\[:]\s*)$", before);
        }
    }

    [Fact]
    public void EveryKeyASharedLineUsesIsTranslated()
    {
        string shared = Path.GetFullPath(
            Path.Combine(AppDir(), "..", "..", "shared", "dotnet", "FiveStack.Utilities")
        );
        string[] keys = Directory
            .GetFiles(shared, "*.cs")
            .SelectMany(file =>
                Regex
                    .Matches(File.ReadAllText(file), @"new PlayerLine\(\s*""([^""]+)""")
                    .Select(match => match.Groups[1].Value)
            )
            .ToArray();

        Assert.NotEmpty(keys);

        foreach (string key in keys)
        {
            EveryLanguageHas(key);
        }
    }

    [Theory]
    [InlineData("FiveStack.Events/GagPlayer.cs", "gag.you_are_gagged")]
    [InlineData("FiveStack.Events/PlayerConnected.cs", "player.join.ready_hint")]
    [InlineData("FiveStack.Events/PlayerConnected.cs", "player.join.help_hint")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.start")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.captain_prompt")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.not_captain")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.captain_picked_stay")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.captain_picked_swap")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.skipping")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.waiting_for_players")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.type_to_ready")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.marked")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.forced_start")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.cannot_pause_not_live")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.called_tactical")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.called_admin")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.admin_paused")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.player_paused")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.vote.technical_passed")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.admin_resumed")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.player_resumed")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.vote.resume_passed")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.team_already_resumed")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.team_resumed")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.check_missing")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.check_whole")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.check_resumed")]
    [InlineData("FiveStack.Services/GameBackUpRounds.cs", "backup.round_restored")]
    [InlineData("FiveStack.Services/GameBackUpRounds.cs", "backup.waiting_for_vote")]
    [InlineData("FiveStack.Services/GameBackUpRounds.cs", "backup.blocked")]
    [InlineData("FiveStack.Services/GameBackUpRounds.cs", "backup.disabled")]
    [InlineData("FiveStack.Services/CameraSystem.cs", "camera.paused")]
    [InlineData("FiveStack.Services/CameraSystem.cs", "camera.waiting")]
    [InlineData("FiveStack.Services/CameraSystem.cs", "camera.restored")]
    [InlineData("FiveStack.Services/GameDemos.cs", "demos.recording")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.resume_hint")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.reconnect_timeout")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "match.reconnect_timeout_over")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.offline_over")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.received_data")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.tv_delay")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.assigned_team")]
    [InlineData("FiveStack.Events/GameEnd.cs", "match.map_over_next_map")]
    [InlineData("FiveStack.Events/GameEnd.cs", "match.map_over_series_done")]
    [InlineData("FiveStack.Events/GameEnd.cs", "match.offline_over")]
    [InlineData("FiveStack.Events/PlayerDisconnected.cs", "match.player_disconnected")]
    [InlineData("FiveStack.Events/RoundStart.cs", "match.waiting_reconnect")]
    public void WhatEachPlayerIsToldIsInTheirLanguage(string file, string key)
    {
        string source = Compact(file);
        string lookup = $"\"{key}\"";
        int at = source.IndexOf(lookup, StringComparison.Ordinal);

        Assert.True(at >= 0, $"{file} no longer says {key}");

        while (at >= 0)
        {
            Assert.Matches(@"_gameServer\.Localize\(\w+,$", source[..at]);

            at = source.IndexOf(lookup, at + 1, StringComparison.Ordinal);
        }

        EveryLanguageHas(key);
    }

    [Theory]
    [InlineData("timeout.not_allowed", "{1}", "timeout.not_allowed.tactical_timeout", "을", "을(를)")]
    [InlineData("timeout.not_allowed", "{1}", "timeout.not_allowed.resume", "를", "을(를)")]
    [InlineData("ready.marked", "{0}", "ready.ready", "으로", "(으)로")]
    [InlineData("ready.marked", "{0}", "ready.not_ready", "로", "(으)로")]
    public void AKoreanParticleFitsTheWordBeforeIt(
        string key,
        string placeholder,
        string word,
        string particle,
        string eitherParticle
    )
    {
        Dictionary<string, string> korean = Translations("ko-KR.json");
        string line = korean[key];
        int at = line.IndexOf(placeholder, StringComparison.Ordinal) + placeholder.Length;
        string after = line[at..];

        Assert.True(korean.ContainsKey(word), $"ko-KR has no {word}");
        Assert.True(
            after.StartsWith(particle, StringComparison.Ordinal)
                || after.StartsWith(eitherParticle, StringComparison.Ordinal),
            $"ko-KR {key} reads {korean[word]}{after}"
        );
    }

    // A command is typed the same in every language.
    [Theory]
    [MemberData(nameof(LanguageFiles))]
    public void HelpNamesTheCheckCommandAsItIsTyped(string language)
    {
        Assert.Contains("{0}check - ", Translations(language)["help.check"]);
    }

    [Fact]
    public void TheNotReadyListIsInEachPlayersLanguage()
    {
        string ready = Compact(Path.Combine("FiveStack.Services", "ReadySystem.cs"));

        Assert.Contains(
            "_gameServer.MessageEach(HudDestination.Notify,viewer=>_gameServer.Localize(viewer,\"ready.players_not_ready\",",
            ready
        );
        Assert.Contains("_gameServer.Localize(viewer,\"ready.not_connected\",", ready);
        Assert.Contains("_gameServer.Localize(viewer,\"ready.not_linked\",", ready);
        EveryLanguageHas("ready.not_linked");
        Assert.DoesNotContain("_localizer[\"ready.players_not_ready\"", ready);
        EveryLanguageHas("ready.not_connected");
    }

    [Fact]
    public void AnUnlinkedPlayerIsToldInTheirLanguageHowToTakeTheirSeat()
    {
        Assert.Contains(
            "_gameServer.Message(HudDestination.Chat,_gameServer.Localize(player,\"player.join.link_discord\"),player);",
            Compact(Path.Combine("FiveStack.Events", "PlayerConnected.cs"))
        );
        EveryLanguageHas("player.join.link_discord");
    }
}
