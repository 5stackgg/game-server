using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FiveStack.Utilities;
using Xunit;

// A restore that could not write its file never reaches the timer that turns
// backups back on. What the state decides is tested. RestoreRound needs the engine
// from its first line, so its half of this is read.
public class BackupWritingTests
{
    private const int HeldRound = 5;
    private const int Round = 8;

    private static readonly Guid Map = Guid.NewGuid();

    private readonly RoundRestoreState _state = new();

    private static string SourceDir([CallerFilePath] string testFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(testFile)!, "..", "src"));
    }

    private static string Method(string signature)
    {
        string source = Regex.Replace(
            File.ReadAllText(Path.Combine(SourceDir(), "FiveStack.Services", "GameBackUpRounds.cs")),
            @"//[^\n]*",
            ""
        );

        int from = source.IndexOf(signature, StringComparison.Ordinal);

        Assert.True(from >= 0, $"{signature} is gone");

        int to = source.IndexOf("\n    }\n", from, StringComparison.Ordinal);

        Assert.True(to > from, $"the end of {signature} was not found");

        return source[from..to];
    }

    private static string FailedWrite()
    {
        string restore = Method("public void RestoreRound(int round)");
        int write = restore.IndexOf("File.WriteAllText(", StringComparison.Ordinal);
        int caught = restore.IndexOf("catch (Exception", write, StringComparison.Ordinal);
        int load = restore.IndexOf("mp_backup_restore_load_file", StringComparison.Ordinal);

        Assert.True(write >= 0, "RestoreRound no longer writes the backup file");
        Assert.True(caught > write && caught < load, "a failed write is not caught before the load");

        return restore[caught..load];
    }

    [Fact]
    public void AnOwedLoadWhoseWriteFailedHoldsItsRoundAgain()
    {
        _state.StartOwing(Round, Map);
        _state.StartLoad();

        Assert.Equal(Round, _state.FailedLoad());
        Assert.False(_state.LoadingBackup);
    }

    [Fact]
    public void ALoadThatTookOverAHoldPutsItBack()
    {
        _state.StartLoad(HeldRound);

        Assert.Equal(HeldRound, _state.FailedLoad());
    }

    [Fact]
    public void AnOwedLoadKeepsItsOwnRoundOverAHoldItTookOver()
    {
        _state.StartOwing(Round, Map);
        _state.StartLoad(HeldRound);

        Assert.Equal(Round, _state.FailedLoad());
    }

    [Fact]
    public void ALoadWithNothingHeldHoldsNothing()
    {
        _state.StartLoad();

        Assert.Null(_state.FailedLoad());
    }

    [Fact]
    public void TheLoadIsToldWhatItTookOver()
    {
        string restore = Method("public void RestoreRound(int round)");
        int taken = restore.IndexOf("int? held = _restore.ReleaseHold();", StringComparison.Ordinal);
        int load = restore.IndexOf("_restore.StartLoad(held);", StringComparison.Ordinal);
        int write = restore.IndexOf("File.WriteAllText(", StringComparison.Ordinal);

        Assert.True(taken >= 0 && load > taken, "the hold is let go with nothing keeping it");
        Assert.True(load < write, "a failed write has nothing to put back");
    }

    [Fact]
    public void AFailedWriteIsSettledOneWay()
    {
        Assert.Contains("FailLoad", FailedWrite());
    }

    // A hold offers its vote straight away, which empties it, so anything that
    // reads the hold afterwards would turn backups on during that vote.
    [Fact]
    public void BackupsOnlyComeBackWhenTheFailedLoadHoldsNothing()
    {
        string failed = Method("private void FailLoad()");
        int asked = failed.IndexOf("_restore.FailedLoad()", StringComparison.Ordinal);
        int hold = failed.IndexOf("ReturnToHold(", StringComparison.Ordinal);
        int returned = failed.IndexOf("return;", hold, StringComparison.Ordinal);
        int writing = failed.IndexOf("SetBackupWriting(!IsHoldingForRestore());", StringComparison.Ordinal);

        Assert.True(asked >= 0 && hold > asked, "the state is not asked before acting");
        Assert.True(returned > hold && writing > returned, "backups can come back during the vote");
    }
}
