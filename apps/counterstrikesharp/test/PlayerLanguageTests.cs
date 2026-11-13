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
    public void TheVoteHudNamesTheVoteInEachPlayersLanguage()
    {
        string votes = Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"));

        Assert.Contains(
            "stringaction=_localizer.ForPlayer(player,_voteKey,_voteArgs).ToLower();",
            votes
        );
        Assert.DoesNotContain("_localizer[", votes);
        Assert.NotEmpty(Regex.Matches(votes, @"PrintToCenter\("));
        Assert.Empty(Regex.Matches(votes, @"PrintToCenter\((?!_localizer\.ForPlayer\(player,)"));
    }

    [Theory]
    [InlineData("vote.failed")]
    [InlineData("vote.success")]
    public void TheVoteResultReachesEachPlayerInTheirLanguage(string key)
    {
        Assert.Contains(
            $"_gameServer.MessageEach(HudDestination.Center,player=>_localizer.ForPlayer(player,\"{key}\",_localizer.ForPlayer(player,_voteKey,_voteArgs)));",
            Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"))
        );
    }

    [Fact]
    public void MessageEachSendsEveryPlayerTheirOwnLine()
    {
        Assert.Contains(
            "foreach(CCSPlayerControllerplayerinMatchUtility.Players()){stringtext=message(player);",
            Compact(Path.Combine("FiveStack.Services", "GameServer.cs"))
        );
    }

    [Fact]
    public void ARefusedVoteIsTranslated()
    {
        string votes = Compact(Path.Combine("FiveStack.Services", "VoteSystem.cs"));

        Assert.DoesNotContain("youarenotallowedtovote", votes);
        Assert.Contains("_localizer.ForPlayer(player,\"vote.not_allowed\",ChatColors.Red)", votes);
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
        Match teams = Regex.Match(
            Compact(Path.Combine("FiveStack.Services", "KnifeSystem.cs")),
            @"_gameServer\.MessageEach\(HudDestination\.Alert,player=>_localizer\.ForPlayer\(player,""knife\.captain_picking"",_localizer\.ForPlayer\(player,team==CsTeam\.Terrorist\?""([^""]+)"":""([^""]+)""\)\)\)"
        );

        Assert.True(teams.Success, "the knife alert names the team in English or in one language");
        EveryLanguageHas(teams.Groups[1].Value);
        EveryLanguageHas(teams.Groups[2].Value);
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
}
