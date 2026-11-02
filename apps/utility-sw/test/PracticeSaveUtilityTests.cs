using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

public class PracticeSaveUtilityTests
{
    private const string Mirage = "de_mirage";
    private const string PanelId = "6f1d2c3b-4a59-4e68-9d7c-1a2b3c4d5e6f";

    private static LineupRecord Saved(string? id = PanelId)
    {
        return new LineupRecord { client_id = Guid.NewGuid().ToString(), id = id, map = Mirage };
    }

    [Fact]
    public void AnAnswerGivesTheSaveItsPanelId()
    {
        LineupRecord saved = Saved(null);

        PracticeSaveUtility.GivePanelId(saved, PanelId);

        Assert.Equal(PanelId, saved.id);
    }

    [Fact]
    public void AnAnswerWithNoIdKeepsTheOneItHad()
    {
        LineupRecord saved = Saved();

        PracticeSaveUtility.GivePanelId(saved, null);

        Assert.Equal(PanelId, saved.id);
    }

    [Theory]
    [InlineData(PanelId)]
    [InlineData(null)]
    public void ADeleteLetsGoOfEverySaveWhateverItsId(string? id)
    {
        LineupRecord lineup = Saved(id);
        List<LineupRecord> released = new();
        List<string> withdrawn = new();

        Assert.Equal(id, PracticeSaveUtility.ForgetSaves(lineup, released.Add, withdrawn.Add));

        Assert.Same(lineup, Assert.Single(released));
        Assert.Equal(lineup.client_id, Assert.Single(withdrawn));
        Assert.Null(lineup.id);
    }

    [Fact]
    public void PruningAWalkKeepsThePlayerOnTheLineupTheyWereOn()
    {
        LineupRecord gone = Saved("gone");
        LineupRecord standing = Saved("standing");
        List<LineupRecord> results = new() { gone, Saved("kept"), standing };

        int index = PracticeSaveUtility.PruneResults(results, 2, row => row == gone);

        Assert.Equal(2, results.Count);
        Assert.Same(standing, results[index]);
    }

    [Fact]
    public void PruningTheLineupThePlayerWasOnStartsTheWalkAgain()
    {
        LineupRecord gone = Saved("gone");
        List<LineupRecord> results = new() { Saved("kept"), gone };

        Assert.Equal(-1, PracticeSaveUtility.PruneResults(results, 1, row => row == gone));
    }

    [Fact]
    public void PruningAWalkNobodyStartedLeavesItUnstarted()
    {
        LineupRecord gone = Saved("gone");
        List<LineupRecord> results = new() { gone, Saved("kept") };

        Assert.Equal(-1, PracticeSaveUtility.PruneResults(results, -1, row => row == gone));
    }

    [Fact]
    public void APushSwapsAnEditedRowIntoTheWalkWhereItStood()
    {
        LineupRecord first = Saved("6f1d2c3b-4a59-4e68-9d7c-1a2b3c4d5e60");
        LineupRecord edited = Saved(PanelId);
        LineupRecord last = Saved("6f1d2c3b-4a59-4e68-9d7c-1a2b3c4d5e61");
        LineupRecord fresh = Saved(PanelId);
        List<LineupRecord> results = new() { first, edited, last };

        PracticeSaveUtility.RefreshResults(results, new[] { fresh });

        Assert.Equal(new[] { first, fresh, last }, results);
    }

    [Fact]
    public void APushNeverAddsToAWalk()
    {
        List<LineupRecord> results = new() { Saved(PanelId) };

        PracticeSaveUtility.RefreshResults(
            results,
            new[] { Saved(PanelId), Saved("6f1d2c3b-4a59-4e68-9d7c-1a2b3c4d5e60") }
        );

        Assert.Single(results);
    }

    [Fact]
    public void ARowNotYetOnThePanelStaysInTheWalk()
    {
        LineupRecord local = Saved(null);
        List<LineupRecord> results = new() { local };

        PracticeSaveUtility.RefreshResults(results, new[] { Saved(PanelId) });

        Assert.Same(local, Assert.Single(results));
    }

    [Fact]
    public void ADeleteGivesUpThePanelId()
    {
        LineupRecord deleted = Saved();

        Assert.Equal(PanelId, PracticeSaveUtility.GiveUpPanelId(deleted));
        Assert.Null(deleted.id);
    }

    [Fact]
    public void ASaveARefreshDroppedGoesBackIn()
    {
        Assert.True(
            PracticeSaveUtility.BelongsBackInLibrary(Saved(), Mirage, new List<LineupRecord>())
        );
    }

    [Fact]
    public void ASaveFromAnotherMapStaysOut()
    {
        Assert.False(
            PracticeSaveUtility.BelongsBackInLibrary(Saved(), "de_inferno", new List<LineupRecord>())
        );
    }

    [Fact]
    public void ARowThePanelAlreadySentIsNotDoubled()
    {
        LineupRecord row = new() { client_id = PanelId, id = PanelId, map = Mirage };

        Assert.False(PracticeSaveUtility.BelongsBackInLibrary(Saved(), Mirage, new[] { row }));
    }

    [Fact]
    public void ASaveStillInTheLibraryIsNotAddedAgain()
    {
        LineupRecord saved = Saved(null);

        Assert.False(PracticeSaveUtility.BelongsBackInLibrary(saved, Mirage, new[] { saved }));
    }

    [Fact]
    public void ASaveWithNoIdIsNotTakenForALocalRowWithNone()
    {
        LineupRecord local = Saved(null);

        Assert.True(PracticeSaveUtility.BelongsBackInLibrary(Saved(null), Mirage, new[] { local }));
    }
}
