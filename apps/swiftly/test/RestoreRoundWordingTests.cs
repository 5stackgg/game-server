using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// Backup N is the state after round N, so loading it puts the teams at the start
// of round N + 1, the number the web shows for it. Players are told and type that
// round; the backup files, server commands, events and the api keep the backup number.
public class RestoreRoundWordingTests
{
    private static string AppDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, ".."));
    }

    private static string Backups()
    {
        string path = Path.Combine(AppDir(), "src", "FiveStack.Services", "GameBackUpRounds.cs");

        return Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
    }

    private static string Commands()
    {
        string path = Path.Combine(AppDir(), "src", "FiveStack.Commands", "BackupRounds.cs");

        return Regex.Replace(File.ReadAllText(path), @"//[^\n]*", "");
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

    private static string Translation(string file, string key)
    {
        Match match = Regex.Match(
            File.ReadAllText(Path.Combine(TranslationsDir(), file)),
            $"\"{Regex.Escape(key)}\":\\s*\"((?:[^\"\\\\]|\\\\.)*)\""
        );

        Assert.True(match.Success, $"{file} has no {key}");

        return match.Groups[1].Value;
    }

    public static IEnumerable<object[]> Languages()
    {
        return Directory
            .GetFiles(TranslationsDir(), "*.jsonc")
            .Select(file => new object[] { Path.GetFileName(file) });
    }

    [Fact]
    public void TheVoteNamesTheRoundPlayedNext()
    {
        Assert.Contains(
            "_localizer[\"backup.vote.restore_to\", RoundRestoreGateUtility.StartOfRound(round)]",
            Backups()
        );
    }

    [Fact]
    public void TheRestoredMessageNamesTheRoundPlayedNext()
    {
        string source = Backups();
        int from = source.IndexOf("\"backup.round_restored\"", StringComparison.Ordinal);
        int to = source.IndexOf("CommandUtility.PublicChatTrigger", from, StringComparison.Ordinal);

        Assert.True(from >= 0 && to > from, "the restored message has moved");
        Assert.Contains("RoundRestoreGateUtility.StartOfRound(round)", source[from..to]);
    }

    [Fact]
    public void TheWaitingMessageNamesTheRoundPlayedNext()
    {
        Assert.Contains(
            "the start of round {RoundRestoreGateUtility.StartOfRound(round)}",
            Method(Backups(), "public void OfferPendingRestore()")
        );
    }

    [Fact]
    public void TheApiAndTheEngineKeepTheBackupNumber()
    {
        string source = Backups();

        Assert.Contains("SendRestoreRoundToBackend(round);", source);
        Assert.DoesNotContain("SendRestoreRoundToBackend(RoundRestoreGateUtility", source);
        Assert.DoesNotContain("RestoreRound(RoundRestoreGateUtility", source);
    }

    [Fact]
    public void ResetTakesTheRoundTheVoteNames()
    {
        string reset = Method(Commands(), "public void OnRestoreRound(");

        Assert.Contains("RoundRestoreGateUtility.ResetBackup(_round)", reset);
        Assert.Contains("RequestRestoreBackupRound(backup.Value", reset);
        Assert.DoesNotContain("int.TryParse", reset);
    }

    [Fact]
    public void AResetToNoRoundShowsHowToUseIt()
    {
        Assert.Contains(
            "\"help.restore_round\"",
            Method(Commands(), "public void OnRestoreRound(")
        );
    }

    [Fact]
    public void AResetToNoRoundShowsHowToUseItInThePlayersLanguage()
    {
        string reset = Method(Commands(), "public void OnRestoreRound(");

        Assert.Contains("Core.Translation.GetPlayerLocalizer(context.Sender)", reset);
        Assert.DoesNotContain("_localizer[\"help.restore_round\"", reset);
    }

    [Fact]
    public void EnglishHelpSaysResetTakesTheRoundItStarts()
    {
        Assert.Equal(
            " {0}reset <round> - Restore to the start of the round",
            Translation("en.jsonc", "help.restore_round")
        );
    }

    [Fact]
    public void EnglishNamesTheStartOfTheRound()
    {
        Assert.Equal(
            "Restore to the Start of Round {0}",
            Translation("en.jsonc", "backup.vote.restore_to")
        );
        Assert.Equal(
            " {0}Restored to the start of round {1} ({2}resume to continue)",
            Translation("en.jsonc", "backup.round_restored")
        );
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguageKeepsThePlaceholders(string language)
    {
        string vote = Translation(language, "backup.vote.restore_to");
        string restored = Translation(language, "backup.round_restored");

        Assert.Contains("{0}", vote);
        Assert.DoesNotContain("{1}", vote);
        Assert.Contains("{0}", restored);
        Assert.Contains("{1}", restored);
        Assert.Contains("{2}resume", restored);
        Assert.DoesNotContain("{3}", restored);
        Assert.Contains("{0}reset <round> - ", Translation(language, "help.restore_round"));
    }
}
