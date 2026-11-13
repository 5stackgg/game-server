using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            "_localizer[\"timeout.not_allowed\",ChatColors.Red,_localizer[refusedKey]]",
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

    [Fact]
    public void ATeamIsNamedByATranslationKey()
    {
        Match keys = Regex.Match(
            Compact(Path.Combine("FiveStack.Utilities", "TeamUtility.cs")),
            @"publicstaticstringTeamNameKey\(CsTeamteam\)\{returnteam==CsTeam\.Terrorist\?""([^""]+)"":""([^""]+)"";\}"
        );

        Assert.True(keys.Success, "TeamUtility.TeamNameKey is gone");
        EveryLanguageHas(keys.Groups[1].Value);
        EveryLanguageHas(keys.Groups[2].Value);
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

    [Fact]
    public void TheNotReadyListIsInEachPlayersLanguage()
    {
        string ready = Compact(Path.Combine("FiveStack.Services", "ReadySystem.cs"));

        Assert.Contains(
            "_gameServer.MessageEach(HudDestination.Notify,viewer=>_gameServer.Localize(viewer,\"ready.players_not_ready\",",
            ready
        );
        Assert.Contains("_gameServer.Localize(viewer,\"ready.not_connected\",", ready);
        Assert.DoesNotContain("_localizer[\"ready.players_not_ready\"", ready);
        EveryLanguageHas("ready.not_connected");
    }
}
