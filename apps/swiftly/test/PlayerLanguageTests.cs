using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Players;
using Xunit;

// SwiftlyS2's injected ILocalizer is always the server's language, while
// CounterStrikeSharp answers a command in the language of the player who typed it.
// What one player is told goes through GameServer.LocalizerFor, and a line for
// everyone goes through GameServer.MessageEach, which gives each player their own
// language and SourceTV the server's.
public class PlayerLanguageTests
{
    private static string AppDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, ".."));
    }

    private static string Source(string file)
    {
        string path = Path.Combine(AppDir(), "src", file);

        return Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
    }

    private static string Compact(string file)
    {
        return Regex.Replace(Source(file), @"\s+", "");
    }

    private static string Method(string source, string signature)
    {
        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    private static string TranslationsDir()
    {
        return Path.Combine(AppDir(), "src", "resources", "translations");
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
            .Matches(text, @"\[[A-Za-z]+\]")
            .Select(match => match.Value)
            .Order()
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

    private static IEnumerable<(string Key, string[] Args)> PlayerLookups(string source)
    {
        return Calls(source, @"(\.LocalizerFor\([\w.]+\)|(?<!\w)localizer)\[")
            .Select(args => (args[0], args.Skip(1).ToArray()));
    }

    // Where each kind of send takes the text a player reads.
    private static readonly (string Opening, int Text)[] Sends =
    [
        (@"_gameServer\.Message\(", 1),
        (@"\.Send(Chat|Center|Alert|Console)\(", 0),
        (@"context\.Reply\(", 0),
        (@"\.PauseMatch\(", 1),
        (@"\.ResumeMatch\(", 0),
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

    public static IEnumerable<object[]> ServiceAndEventFiles()
    {
        return SourceFiles()
            .Where(file =>
                ((string)file[0]).StartsWith("FiveStack.Services")
                || ((string)file[0]).StartsWith("FiveStack.Events")
            );
    }

    public static IEnumerable<object[]> CommandFiles()
    {
        return Directory
            .GetFiles(Path.Combine(AppDir(), "src", "FiveStack.Commands"), "*.cs")
            .Select(file => new object[] { Path.GetFileName(file) });
    }

    public static IEnumerable<object[]> SourceFiles()
    {
        string src = Path.Combine(AppDir(), "src");

        return Directory
            .GetFiles(src, "*.cs", SearchOption.AllDirectories)
            .Select(file => new object[] { Path.GetRelativePath(src, file) });
    }

    public static IEnumerable<object[]> Languages()
    {
        return Directory
            .GetFiles(TranslationsDir(), "*.jsonc")
            .Select(file => new object[] { Path.GetFileName(file) });
    }

    [Fact]
    public void APlayerGetsTheirLanguageWhenItShipsAndOtherwiseTheServers()
    {
        string source = Source(Path.Combine("FiveStack.Services", "GameServer.cs"));
        string helper = Regex.Replace(
            Method(source, "public ILocalizer LocalizerFor(IPlayer? player)"),
            @"\s+",
            ""
        );

        Assert.Contains(
            "if(player==null||!TranslationUtility.HasTranslation(_core.PluginPath,player.PlayerLanguage.Value))",
            helper
        );
        Assert.Contains("return_core.Localizer;", helper);
        Assert.Contains("return_core.Translation.GetPlayerLocalizer(player);", helper);
    }

    [Fact]
    public void MessageEachSendsEveryPlayerTheirOwnLanguage()
    {
        string each = Regex.Replace(
            Method(
                Source(Path.Combine("FiveStack.Services", "GameServer.cs")),
                "public void MessageEach(MessageType destination, Func<ILocalizer, string> message)"
            ),
            @"\s+",
            ""
        );

        Assert.Contains("foreach(IPlayerplayerinMatchUtility.Players())", each);
        Assert.Contains("message(LocalizerFor(player))", each);
        Assert.Matches(@"\.Controller\.IsHLTV\)\{Send\(destination,\w+,message\(_core\.Localizer\)\)", each);
    }

    [Theory]
    [InlineData("es-419", "es.jsonc")]
    [InlineData("pt", "pt-BR.jsonc")]
    public void AClientOnAVariantOfALanguageWeShipGetsThatLanguage(string language, string file)
    {
        string shipped = Path.Join(
            AppContext.BaseDirectory,
            "resources",
            "translations",
            $"{language}.jsonc"
        );

        Assert.True(File.Exists(shipped), $"{language} clients get no translation file");
        Assert.Equal(
            File.ReadAllText(Path.Combine(TranslationsDir(), file)),
            File.ReadAllText(shipped)
        );
    }

    [Theory]
    [InlineData("en", true)]
    [InlineData("es-419", true)]
    [InlineData("pt", true)]
    [InlineData("cs", false)]
    public void ALanguageShipsWhenItsFileIsBesideThePlugin(string language, bool ships)
    {
        Assert.Equal(ships, TranslationUtility.HasTranslation(AppContext.BaseDirectory, language));
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
        Assert.True(
            Translations("en.jsonc").ContainsKey(key.Groups[1].Value),
            $"{key.Groups[1].Value} is not a translation key"
        );
    }

    [Fact]
    public void RulesNamesTheMatchTypeThroughItsKey()
    {
        string help = Compact(Path.Combine("FiveStack.Commands", "Help.cs"));
        (string Key, string[] Args)[] details = PlayerLookups(help)
            .Where(lookup => lookup.Key == "\"rules.match_details\"")
            .ToArray();

        Assert.Single(details);
        Assert.Equal("MatchType(localizer,match.options.type)", details[0].Args[0]);
        Assert.DoesNotContain("\"rules.match_details\",match.options.type", help);
    }

    [Fact]
    public void EveryVoteIsNamedByATranslationKey()
    {
        Dictionary<string, string> english = Translations("en.jsonc");
        List<string> names = [];

        foreach (object[] file in SourceFiles())
        {
            string source = Compact((string)file[0]);

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

            Assert.True(
                key.Success && english.ContainsKey(key.Groups[1].Value),
                $"a vote is named {name}, not a translation key"
            );
        }
    }

    [Fact]
    public void TheVoteHudNamesTheVoteInEachPlayersLanguage()
    {
        string votes = Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"));
        string hud = Regex.Replace(
            Method(
                Source(Path.Combine("FiveStack.Services", "VoteSystem.cs")),
                "private void SendVoteMessage()"
            ),
            @"\s+",
            ""
        );

        Assert.Contains(
            "ILocalizerlocalizer=_gameServer.LocalizerFor(player);stringaction=localizer[_voteKey,_voteArgs].ToLower();",
            hud
        );
        Assert.DoesNotContain("_localizer", votes);
        Assert.DoesNotContain("Core.Localizer", votes);
    }

    [Theory]
    [InlineData("vote.failed")]
    [InlineData("vote.success")]
    public void TheVoteResultReachesEachPlayerInTheirLanguage(string key)
    {
        Assert.Contains(
            $"_gameServer.MessageEach(MessageType.Center,localizer=>localizer[\"{key}\",localizer[_voteKey,_voteArgs]]);",
            Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"))
        );
    }

    [Fact]
    public void ARefusedVoteIsTranslated()
    {
        Assert.DoesNotContain(
            "you are not allowed to vote",
            Source(Path.Combine("FiveStack.Services", "VoteSystem.cs"))
        );
        Assert.True(Translations("en.jsonc").ContainsKey("vote.not_allowed"));
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
            "_gameServer.MessageEach(MessageType.Alert,localizer=>localizer[\"knife.captain_picking\",localizer[TeamUtility.TeamNameKey(team)]]);",
            Compact(Path.Combine("FiveStack.Services", "KnifeSystem.cs"))
        );
    }

    [Theory]
    [InlineData(Team.T, "team.terrorist")]
    [InlineData(Team.CT, "team.ct")]
    [InlineData(Team.Spectator, "team.spectator")]
    public void ATeamIsNamedByATranslationKey(Team team, string key)
    {
        Assert.Equal(key, TeamUtility.TeamNameKey(team));
        Assert.True(Translations("en.jsonc").ContainsKey(key), $"{key} is not a translation key");
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

    // A clan tag is one string the whole server sees on the scoreboard.
    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void NothingIsLookedUpInTheServersLanguageForAPlayer(string file)
    {
        Assert.DoesNotContain(
            "_localizer[",
            Compact(file).Replace("_localizer[\"camera.tag\"]", "")
        );
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
    [InlineData("public void PauseMatch(", "_gameServer.MessageEach(MessageType.Alert,message);")]
    [InlineData("public void ResumeMatch(", "_gameServer.MessageEach(MessageType.Center,message);")]
    public void APauseOrResumeLineReachesEachPlayerInTheirLanguage(string signature, string send)
    {
        Assert.Contains(
            send,
            Regex.Replace(Method(Source(Path.Combine("FiveStack.Services", "MatchManager.cs")), signature), @"\s+", "")
        );
    }

    [Fact]
    public void ARestoreRefusalReachesEachPlayerInTheirLanguage()
    {
        string backups = Compact(Path.Combine("FiveStack.Services", "GameBackUpRounds.cs"));

        Assert.DoesNotContain("RoundRestoreGateUtility.WhyTheHoldMoved(", backups);
        Assert.DoesNotContain("RoundRestoreGateUtility.ResumeRefusal(", backups);
        Assert.DoesNotContain("OfflineMatchState.ResetToRound(", backups);
        Assert.Contains("GameServer.Render(localizer,", backups);
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
            Assert.True(Translations("en.jsonc").ContainsKey(key), $"{key} is not a translation key");
        }
    }

    [Theory]
    [InlineData("captain.claim_hint")]
    [InlineData("captain.show")]
    [InlineData("captain.assigned")]
    public void TheCaptainLinesNameTheTeamInEachPlayersLanguage(string key)
    {
        string captains = Compact(Path.Combine("FiveStack.Services", "CaptainSystem.cs"));

        Assert.Matches(
            $@"_gameServer\.MessageEach\(MessageType\.\w+,localizer=>localizer\[""{Regex.Escape(key)}"",[^;]*localizer\[TeamUtility\.TeamNameKey\(\w+\)\][^;]*\);",
            captains
        );
        Assert.DoesNotContain("TeamNumToString", captains);
    }

    [Theory]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "There is no surrender in a LAN match")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "There is no surrender in an offline match")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "A surrender vote is already in progress")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "Unable to start a surrender vote right now")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "A surrender can only be called while the map is being played")]
    [InlineData("FiveStack.Commands/Surrender.cs", "Only a player in this match can call a surrender")]
    public void ASurrenderRefusalIsNotEnglishForEveryone(string file, string english)
    {
        Assert.DoesNotContain(english, Source(file));
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
        Dictionary<string, string> korean = Translations("ko.jsonc");
        string line = korean[key];
        int at = line.IndexOf(placeholder, StringComparison.Ordinal) + placeholder.Length;
        string after = line[at..];

        Assert.True(
            after.StartsWith(particle, StringComparison.Ordinal)
                || after.StartsWith(eitherParticle, StringComparison.Ordinal),
            $"ko {key} reads {korean[word]}{after}"
        );
    }

    [Theory]
    [MemberData(nameof(CommandFiles))]
    public void CommandsAnswerInTheCallersLanguage(string file)
    {
        Assert.DoesNotContain("_localizer", Source(Path.Combine("FiveStack.Commands", file)));
    }

    [Theory]
    [InlineData("FiveStack.Services/ReadySystem.cs", "camera.cannot_ready")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.not_allowed")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.ready")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.not_ready")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.marked")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.waiting_for_players")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.type_to_ready")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.admin_will_start")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.waiting_captains")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.waiting_coach")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.cannot_pause_not_live")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.already_active")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.not_allowed")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "camera.cannot_resume")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.cannot_resume_empty_team")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.cannot_tac_not_live")]
    [InlineData("FiveStack.Services/TimeoutSystem.cs", "timeout.no_timeouts_left")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.not_captain")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.captain_prompt")]
    [InlineData("FiveStack.Events/GagPlayer.cs", "gag.you_are_gagged")]
    [InlineData("FiveStack.Events/PlayerConnected.cs", "player.join.ready_hint")]
    [InlineData("FiveStack.Events/PlayerConnected.cs", "player.join.help_hint")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.other_team_timer")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.other_team")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.prompt_count_timer")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.prompt_count")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.prompt_options_timer")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.prompt_options")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.not_allowed")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.failed")]
    [InlineData("FiveStack.Services/VoteSystem.cs", "vote.success")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.captain_picking")]
    [InlineData("FiveStack.Services/CaptainSystem.cs", "captain.claim_hint")]
    [InlineData("FiveStack.Services/CaptainSystem.cs", "captain.show")]
    [InlineData("FiveStack.Services/CaptainSystem.cs", "captain.assigned")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.lan")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.offline")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.in_progress")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.unavailable")]
    [InlineData("FiveStack.Services/SurrenderSystem.cs", "surrender.not_in_play")]
    [InlineData("FiveStack.Commands/Surrender.cs", "surrender.not_in_match")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.start")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.captain_picked_stay")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.captain_picked_swap")]
    [InlineData("FiveStack.Services/KnifeSystem.cs", "knife.skipping")]
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
    [InlineData("FiveStack.Services/GameBackUpRounds.cs", "backup.round_restored")]
    [InlineData("FiveStack.Services/GameBackUpRounds.cs", "backup.waiting_for_vote")]
    [InlineData("FiveStack.Services/GameBackUpRounds.cs", "backup.blocked")]
    [InlineData("FiveStack.Services/GameBackUpRounds.cs", "backup.disabled")]
    [InlineData("FiveStack.Services/ReadySystem.cs", "ready.forced_start")]
    [InlineData("FiveStack.Services/CameraSystem.cs", "camera.paused")]
    [InlineData("FiveStack.Services/CameraSystem.cs", "camera.waiting")]
    [InlineData("FiveStack.Services/CameraSystem.cs", "camera.restored")]
    [InlineData("FiveStack.Services/GameDemos.cs", "demos.recording")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.resume_hint")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.offline_over")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.received_data")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.tv_delay")]
    [InlineData("FiveStack.Services/MatchManager.cs", "match.assigned_team")]
    [InlineData("FiveStack.Events/GameEnd.cs", "match.map_over_next_map")]
    [InlineData("FiveStack.Events/GameEnd.cs", "match.map_over_series_done")]
    [InlineData("FiveStack.Events/GameEnd.cs", "match.offline_over")]
    [InlineData("FiveStack.Events/PlayerDisconnected.cs", "match.player_disconnected")]
    [InlineData("FiveStack.Events/RoundStart.cs", "match.waiting_reconnect")]
    public void WhatOnePlayerIsToldIsInTheirLanguage(string file, string key)
    {
        string source = Compact(file);
        string lookup = $"[\"{key}\"";
        int at = source.IndexOf(lookup, StringComparison.Ordinal);

        Assert.True(at >= 0, $"{file} no longer says {key}");

        while (at >= 0)
        {
            Assert.True(
                Regex.IsMatch(source[..at], @"(\.LocalizerFor\([\w.]+\)|[^\w]localizer)$"),
                $"{file} says {key} in the server's language"
            );

            at = source.IndexOf(lookup, at + 1, StringComparison.Ordinal);
        }

        Assert.Equal(
            Regex.Matches(source, "ILocalizerlocalizer=").Count,
            Regex.Matches(source, @"ILocalizerlocalizer=_gameServer\.LocalizerFor\(").Count
        );
    }

    [Theory]
    [MemberData(nameof(SourceFiles))]
    public void NoEnglishWordIsPassedIntoAPlayersLine(string file)
    {
        foreach ((string key, string[] args) in PlayerLookups(Compact(file)))
        {
            foreach (string arg in args)
            {
                Assert.False(
                    Regex.IsMatch(arg, @"^\$?""") && !Regex.IsMatch(arg, @"^""\[[A-Za-z]+\]""$"),
                    $"{file} passes {arg} into {key}"
                );
                Assert.False(
                    arg.Contains("ConvertCamelToHumanReadable"),
                    $"{file} passes an English setting name into {key}"
                );
            }
        }
    }

    [Fact]
    public void ARefusedTimeoutNamesWhatWasRefusedInThePlayersLanguage()
    {
        Dictionary<string, string> english = Translations("en.jsonc");
        MatchCollection refusals = Regex.Matches(
            Compact(Path.Combine("FiveStack.Services", "TimeoutSystem.cs")),
            @"CannotPauseMessage\(player,""([^""]*)""\)"
        );

        Assert.Equal(2, refusals.Count);

        foreach (Match refusal in refusals)
        {
            Assert.True(
                english.ContainsKey(refusal.Groups[1].Value),
                $"\"{refusal.Groups[1].Value}\" is English, not a translation key"
            );
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguageHasEveryLine(string language)
    {
        Dictionary<string, string> english = Translations("en.jsonc");
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
                Record.Exception(() => string.Format(translated[key], args)) == null,
                $"{language} {key} has a brace that is not a placeholder"
            );
            Assert.True(
                ColorTags(text).SequenceEqual(ColorTags(translated[key])),
                $"{language} {key} does not use the colors English does"
            );
        }
    }

    [Fact]
    public void TheNotReadyListIsInEachPlayersLanguage()
    {
        string ready = Compact(Path.Combine("FiveStack.Services", "ReadySystem.cs"));

        Assert.Contains(
            "_gameServer.MessageEach(MessageType.Notify,localizer=>localizer[\"ready.players_not_ready\",",
            ready
        );
        Assert.Matches(@"[^_]localizer\[""ready\.not_connected"",", ready);
        Assert.Matches(@"[^_]localizer\[""ready\.not_linked"",", ready);
        Assert.True(Translations("en.jsonc").ContainsKey("ready.not_linked"));
        Assert.DoesNotContain("_localizer[\"ready.players_not_ready\"", ready);
        Assert.True(Translations("en.jsonc").ContainsKey("ready.not_connected"));
    }

    [Fact]
    public void AnUnlinkedPlayerIsToldInTheirLanguageHowToTakeTheirSeat()
    {
        Assert.Contains(
            "_gameServer.Message(MessageType.Chat,_gameServer.LocalizerFor(player)[\"player.join.link_discord\"],player);",
            Compact(Path.Combine("FiveStack.Events", "PlayerConnected.cs"))
        );
        Assert.True(Translations("en.jsonc").ContainsKey("player.join.link_discord"));
    }
}
