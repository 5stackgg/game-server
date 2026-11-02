using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;

// The two plugins share RoundRestoreState, but each has its own copy of the code
// that acts on it, and a restore has to behave the same on both.
public class RestoreParityTests
{
    private static string Apps([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", ".."));
    }

    private static string Method(string app, string signature)
    {
        string source = File.ReadAllText(
            Path.Combine(Apps(), app, "src", "FiveStack.Services", "GameBackUpRounds.cs")
        );

        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{app} has no {signature}");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        return Regex.Replace(source[from..to], @"\s+", " ").Replace("HudDestination.", "MessageType.");
    }

    // Only this plugin reads backups off disk. The api raises P0002 for a round it
    // has no row for and answers nothing, so offering one could never load.
    [Fact]
    public void OnlyDiskRoundsTheBackendHasARowForAreOffered()
    {
        string source = File.ReadAllText(
            Path.Combine(Apps(), "swiftly", "src", "FiveStack.Services", "GameBackUpRounds.cs")
        );

        Assert.Contains("RoundRestoreGateUtility.IsUsableOnDisk(", source);
        Assert.DoesNotContain("from_disk", source);
    }

    [Theory]
    [InlineData("private void HoldForRestore(int round)")]
    [InlineData("private bool CanHoldAgain(int round)")]
    [InlineData("private void ReturnToHold(int round)")]
    [InlineData("private void ReturnOwedToHold()")]
    [InlineData("private void FailLoad()")]
    [InlineData("private void OweLoad(int round)")]
    [InlineData("private void OwedLoadTimedOut(int owing)")]
    [InlineData("public void TellWhyResumeWaits()")]
    [InlineData("public void AbandonRestore()")]
    [InlineData("private Guid? ActiveMapId()")]
    [InlineData("public void ForgetOtherMaps(Guid? map)")]
    [InlineData("public bool IsResettingRound()")]
    [InlineData("public bool IsHoldingForRestore()")]
    [InlineData("public bool HoldsRoundResults()")]
    public void BothPluginsActTheSame(string signature)
    {
        Assert.Equal(Method("swiftly", signature), Method("counterstrikesharp", signature));
    }
}
