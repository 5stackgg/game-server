using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// A drill is a queue and a verdict. The ways it goes wrong quietly are a run
// that hands out a lineup the panel cannot score, a "worst first" that is
// really alphabetical, and a count somebody typed being taken literally.
public class DrillUtilityTests
{
    private static LineupRecord Lineup(string id, string? name = null, string utility = "Smoke")
    {
        return new LineupRecord
        {
            id = id,
            client_id = id,
            name = name ?? id,
            utility_type = utility,
            release = new ThrowSnapshot { feet_position = new Vec3(100f, 200f, 64f) },
            detonation_position = new Vec3(900f, -400f, 64f),
        };
    }

    private static UtilityPracticeResult Result(
        bool success,
        int attempts,
        int successes,
        bool mastered = false
    )
    {
        return new UtilityPracticeResult
        {
            success = success,
            distance = 40f,
            radius = 80f,
            attempts = attempts,
            successes = successes,
            current_streak = success ? 1 : 0,
            best_streak = 1,
            mastered_at = mastered ? DateTime.UtcNow : null,
        };
    }

    private static Func<LineupRecord, DrillProgress?> Progress(
        params (string id, int attempts, int successes)[] rows
    )
    {
        var book = rows.ToDictionary(
            row => row.id,
            row => new DrillProgress { Attempts = row.attempts, Successes = row.successes }
        );

        return lineup =>
            lineup.id != null && book.TryGetValue(lineup.id, out DrillProgress? progress)
                ? progress
                : null;
    }

    [Fact]
    public void ALineupThePanelHasNeverSeenCannotBeDrilled()
    {
        LineupRecord local = Lineup("keep");
        local.id = null;

        Assert.False(DrillUtility.IsDrillable(local));
    }

    [Fact]
    public void ALineupWithNoOriginCannotBeDrilled()
    {
        LineupRecord lineup = Lineup("a");
        lineup.release = new ThrowSnapshot();

        Assert.False(DrillUtility.IsDrillable(lineup));
    }

    [Fact]
    public void ALineupWithNoLandingPointCannotBeDrilled()
    {
        LineupRecord lineup = Lineup("a");
        lineup.detonation_position = new Vec3(0f, 0f, 0f);

        Assert.False(DrillUtility.IsDrillable(lineup));
    }

    [Fact]
    public void ASavedLineupCanBeDrilled()
    {
        Assert.True(DrillUtility.IsDrillable(Lineup("a")));
    }

    [Fact]
    public void DrillableDropsWhatCannotBeScored()
    {
        LineupRecord local = Lineup("local");
        local.id = "";

        List<LineupRecord> drillable = DrillUtility.Drillable(
            new[] { Lineup("a"), local, Lineup("b") }
        );

        Assert.Equal(new[] { "a", "b" }, drillable.Select(lineup => lineup.id));
    }

    [Fact]
    public void ANamelessLineupFallsBackToItsUtility()
    {
        LineupRecord lineup = Lineup("a", name: "");

        Assert.Equal("Smoke", DrillUtility.Name(lineup));
    }

    [Fact]
    public void AnUnattemptedLineupSortsBetweenMissedAndPerfect()
    {
        float missed = DrillUtility.Priority(new DrillProgress { Attempts = 4, Successes = 0 });
        float perfect = DrillUtility.Priority(new DrillProgress { Attempts = 4, Successes = 4 });

        Assert.True(missed < DrillUtility.UnattemptedPriority);
        Assert.True(DrillUtility.UnattemptedPriority < perfect);
        Assert.Equal(DrillUtility.UnattemptedPriority, DrillUtility.Priority(null));
    }

    [Fact]
    public void AMasteredLineupIsAlwaysLast()
    {
        float mastered = DrillUtility.Priority(
            new DrillProgress
            {
                Attempts = 10,
                Successes = 10,
                Mastered = true,
            }
        );

        Assert.True(
            mastered > DrillUtility.Priority(new DrillProgress { Attempts = 1, Successes = 1 })
        );
    }

    [Fact]
    public void WorstFirstPutsTheOnesGoingWorstFirst()
    {
        var lineups = new[] { Lineup("perfect"), Lineup("half"), Lineup("never"), Lineup("bad") };

        List<LineupRecord> ordered = DrillUtility.WorstFirst(
            lineups,
            Progress(("perfect", 6, 6), ("half", 6, 3), ("bad", 6, 1))
        );

        Assert.Equal(
            new[] { "bad", "half", "never", "perfect" },
            ordered.Select(lineup => lineup.id)
        );
    }

    // Two lineups going equally badly are not equally well known.
    [Fact]
    public void EquallyBadLineupsAreOrderedByHowMuchIsKnown()
    {
        List<LineupRecord> ordered = DrillUtility.WorstFirst(
            new[] { Lineup("thin"), Lineup("thick") },
            Progress(("thin", 1, 0), ("thick", 12, 0))
        );

        Assert.Equal(new[] { "thick", "thin" }, ordered.Select(lineup => lineup.id));
    }

    [Fact]
    public void AQueueIsAsLongAsItWasAskedFor()
    {
        List<LineupRecord> queue = DrillUtility.Queue(
            new[] { Lineup("a"), Lineup("b"), Lineup("c") },
            7,
            eDrillOrder.Random,
            _ => null,
            new Random(4)
        );

        Assert.Equal(7, queue.Count);
    }

    [Fact]
    public void ARunIsCappedNoMatterWhatWasAskedFor()
    {
        List<LineupRecord> queue = DrillUtility.Queue(
            new[] { Lineup("a"), Lineup("b") },
            5000,
            eDrillOrder.Random,
            _ => null,
            new Random(4)
        );

        Assert.Equal(DrillUtility.MaxCount, queue.Count);
    }

    // A book shorter than the run is drilled in whole passes, so nothing comes
    // round twice before everything has come round once.
    [Fact]
    public void EveryLineupIsDrilledBeforeAnyIsRepeated()
    {
        var lineups = new[] { Lineup("a"), Lineup("b"), Lineup("c"), Lineup("d") };

        List<LineupRecord> queue = DrillUtility.Queue(
            lineups,
            8,
            eDrillOrder.Random,
            _ => null,
            new Random(11)
        );

        Assert.Equal(4, queue.Take(4).Select(lineup => lineup.id).Distinct().Count());
        Assert.Equal(4, queue.Skip(4).Select(lineup => lineup.id).Distinct().Count());
    }

    [Fact]
    public void APassSeamNeverRepeatsTheSameLineupBackToBack()
    {
        var lineups = new[] { Lineup("a"), Lineup("b"), Lineup("c") };

        for (int seed = 0; seed < 50; seed++)
        {
            List<LineupRecord> queue = DrillUtility.Queue(
                lineups,
                12,
                eDrillOrder.Random,
                _ => null,
                new Random(seed)
            );

            for (int index = 1; index < queue.Count; index++)
            {
                Assert.NotEqual(queue[index - 1].client_id, queue[index].client_id);
            }
        }
    }

    [Fact]
    public void AWorstFirstQueueStartsWithTheWorst()
    {
        List<LineupRecord> queue = DrillUtility.Queue(
            new[] { Lineup("good"), Lineup("bad") },
            2,
            eDrillOrder.Worst,
            Progress(("good", 5, 5), ("bad", 5, 0)),
            new Random(1)
        );

        Assert.Equal(new[] { "bad", "good" }, queue.Select(lineup => lineup.id));
    }

    [Fact]
    public void AnEmptyLibraryIsAnEmptyQueue()
    {
        Assert.Empty(
            DrillUtility.Queue(
                new List<LineupRecord>(),
                10,
                eDrillOrder.Random,
                _ => null,
                new Random(1)
            )
        );
    }

    [Fact]
    public void ALibraryOfUnscorableLineupsIsAnEmptyQueue()
    {
        LineupRecord local = Lineup("local");
        local.id = null;

        Assert.Empty(
            DrillUtility.Queue(new[] { local }, 10, eDrillOrder.Random, _ => null, new Random(1))
        );
    }

    [Fact]
    public void NoArgumentsIsAShuffledRunOfTheDefaultLength()
    {
        DrillRequest request = DrillUtility.Parse("");

        Assert.True(request.Valid);
        Assert.False(request.Stop);
        Assert.Equal(eDrillOrder.Random, request.Order);
        Assert.Equal(DrillUtility.DefaultCount, request.Count);
    }

    [Fact]
    public void StopIsRead()
    {
        Assert.True(DrillUtility.Parse(" stop ").Stop);
        Assert.True(DrillUtility.Parse("end").Stop);
    }

    [Fact]
    public void ACountIsRead()
    {
        Assert.Equal(25, DrillUtility.Parse("25").Count);
    }

    [Fact]
    public void ACountIsCappedRatherThanRefused()
    {
        DrillRequest request = DrillUtility.Parse("900");

        Assert.True(request.Valid);
        Assert.Equal(DrillUtility.MaxCount, request.Count);
    }

    // A player typing this into chat is not consulting a usage line.
    [Fact]
    public void OrderAndCountAreReadInEitherOrder()
    {
        DrillRequest first = DrillUtility.Parse("worst 12");
        DrillRequest second = DrillUtility.Parse("12 worst");

        Assert.Equal(eDrillOrder.Worst, first.Order);
        Assert.Equal(12, first.Count);
        Assert.Equal(eDrillOrder.Worst, second.Order);
        Assert.Equal(12, second.Count);
    }

    [Fact]
    public void RandomCanBeAskedForOutLoud()
    {
        Assert.Equal(eDrillOrder.Random, DrillUtility.Parse("random").Order);
    }

    [Fact]
    public void AnUnreadableArgumentIsARefusalRatherThanAGuess()
    {
        Assert.False(DrillUtility.Parse("banana").Valid);
        Assert.False(DrillUtility.Parse("0").Valid);
        Assert.False(DrillUtility.Parse("-3").Valid);
    }

    [Fact]
    public void QuotesAndSpacingAreTolerated()
    {
        DrillRequest request = DrillUtility.Parse("\"  worst   8 \"");

        Assert.True(request.Valid);
        Assert.Equal(eDrillOrder.Worst, request.Order);
        Assert.Equal(8, request.Count);
    }

    // The panel's counters are absolute, so a result replaces what we thought
    // rather than adding to it: two throws are not four attempts.
    [Fact]
    public void AResultReplacesTheProgressItReportsOn()
    {
        var book = new DrillProgressBook();

        book.Record(1, "lineup", Result(true, attempts: 3, successes: 1));
        book.Record(1, "lineup", Result(true, attempts: 4, successes: 2));

        DrillProgress? progress = book.For(1, "lineup");

        Assert.NotNull(progress);
        Assert.Equal(4, progress!.Attempts);
        Assert.Equal(2, progress.Successes);
        Assert.Equal(0.5f, progress.Rate);
    }

    [Fact]
    public void ProgressIsPerPlayer()
    {
        var book = new DrillProgressBook();

        book.Record(1, "lineup", Result(true, attempts: 10, successes: 10));

        Assert.NotNull(book.For(1, "lineup"));
        Assert.Null(book.For(2, "lineup"));
    }

    [Fact]
    public void AThrowThePanelDidNotAnswerTeachesNothing()
    {
        var book = new DrillProgressBook();

        book.Record(1, "lineup", null);

        Assert.Null(book.For(1, "lineup"));
    }

    [Fact]
    public void MasteryIsCarriedThrough()
    {
        var book = new DrillProgressBook();

        book.Record(1, "lineup", Result(true, attempts: 9, successes: 9, mastered: true));

        Assert.True(book.For(1, "lineup")!.Mastered);
    }

    [Fact]
    public void ForgettingAPlayerForgetsTheirProgress()
    {
        var book = new DrillProgressBook();

        book.Record(1, "lineup", Result(true, attempts: 2, successes: 2));
        book.Record(2, "lineup", Result(true, attempts: 2, successes: 2));
        book.Forget(1);

        Assert.Null(book.For(1, "lineup"));
        Assert.NotNull(book.For(2, "lineup"));
    }

    [Fact]
    public void ClearingTheBookForgetsEverybody()
    {
        var book = new DrillProgressBook();

        book.Record(1, "lineup", Result(true, attempts: 2, successes: 2));
        book.Clear();

        Assert.Null(book.For(1, "lineup"));
    }

    [Fact]
    public void TheBookAnswersForWholeLineups()
    {
        var book = new DrillProgressBook();

        book.Record(7, "lineup", Result(false, attempts: 5, successes: 1));

        Func<LineupRecord, DrillProgress?> lookup = book.Lookup(7);

        Assert.Equal(5, lookup(Lineup("lineup"))!.Attempts);
        Assert.Null(lookup(Lineup("other")));
    }
}
