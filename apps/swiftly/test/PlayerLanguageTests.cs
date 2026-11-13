using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

// SwiftlyS2's injected ILocalizer is always the server's language, while
// CounterStrikeSharp answers a command in the language of the player who typed it.
// What one player is told goes through GameServer.LocalizerFor; broadcasts stay
// in the server's language.
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

    private static IEnumerable<(string Key, string[] Args)> PlayerLookups(string source)
    {
        foreach (Match start in Regex.Matches(source, @"(\.LocalizerFor\([\w.]+\)|(?<!\w)localizer)\["))
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

            Assert.True(args.Count > 0, $"the lookup at {start.Index} never closes");

            yield return (args[0], args.Skip(1).ToArray());
        }
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
    public void APlayerGetsTheirLanguageAndTheConsoleGetsTheServers()
    {
        string helper = Method(
            Source(Path.Combine("FiveStack.Services", "GameServer.cs")),
            "public ILocalizer LocalizerFor(IPlayer? player)"
        );

        Assert.Contains("if (player == null)", helper);
        Assert.Contains("return _core.Localizer;", helper);
        Assert.Contains("return _core.Translation.GetPlayerLocalizer(player);", helper);
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
}
