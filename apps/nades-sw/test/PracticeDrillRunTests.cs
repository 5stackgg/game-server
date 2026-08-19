using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// A run advances on a scored throw and on nothing else. The failures that
// matter are the silent ones: a step that resolves on the throw itself, so a
// miss is skipped past before it is read, and a step that never resolves at
// all because the panel stopped answering.
public class PracticeDrillRunTests
{
    private static readonly DateTime Now = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static LineupRecord Lineup(string id, string utility = "Smoke")
    {
        return new LineupRecord
        {
            id = id,
            client_id = id,
            name = id,
            utility_type = utility,
            release = new ThrowSnapshot { feet_position = new Vec3(10f, 20f, 30f) },
            detonation_position = new Vec3(900f, 900f, 30f),
        };
    }

    private static PracticeDrillRun Run(params string[] ids)
    {
        return new PracticeDrillRun(ids.Select(id => Lineup(id)).ToList());
    }

    private static NadePracticeResult Result(bool success)
    {
        return new NadePracticeResult
        {
            success = success,
            distance = success ? 20f : 300f,
            radius = 80f,
            attempts = 1,
            successes = success ? 1 : 0,
            current_streak = success ? 1 : 0,
            best_streak = success ? 1 : 0,
        };
    }

    // Move on, throw, score -- the whole loop, once, in the order the runner
    // drives it.
    private static void Throws(PracticeDrillRun run, bool hit)
    {
        LineupRecord? lineup = run.Next();

        Assert.NotNull(lineup);
        Assert.True(run.Thrown(lineup!.utility_type, Now));
        Assert.True(run.Score(lineup.id, Result(hit)));
    }

    [Fact]
    public void ARunHandsOutItsQueueInOrder()
    {
        PracticeDrillRun run = Run("a", "b", "c");

        Assert.Equal("a", run.Next()!.id);
        Assert.Equal("b", run.Next()!.id);
        Assert.Equal("c", run.Next()!.id);
        Assert.Null(run.Next());
        Assert.Equal(eDrillEnd.Completed, run.Ending);
    }

    [Fact]
    public void PositionReadsAsAPlaceInTheRun()
    {
        PracticeDrillRun run = Run("a", "b");

        run.Next();

        Assert.Equal(1, run.Position);
        Assert.Equal(2, run.Length);
    }

    [Fact]
    public void AFinishedRunHandsOutNothingMore()
    {
        PracticeDrillRun run = Run("a", "b");

        run.Next();
        run.End(eDrillEnd.Stopped);

        Assert.Null(run.Next());
        Assert.Equal(eDrillEnd.Stopped, run.Ending);
    }

    [Fact]
    public void StoppingTwiceKeepsTheFirstReason()
    {
        PracticeDrillRun run = Run("a");

        run.End(eDrillEnd.Stopped);
        run.End(eDrillEnd.Completed);

        Assert.Equal(eDrillEnd.Stopped, run.Ending);
    }

    // Throwing a flash while a smoke is loaded is a different throw, not a
    // missed one.
    [Fact]
    public void AThrowOfTheWrongUtilityIsNotAnAttempt()
    {
        PracticeDrillRun run = Run("a");
        run.Next();

        Assert.False(run.Thrown("Flash", Now));
        Assert.False(run.Waiting);
    }

    [Fact]
    public void AThrowOfTheRightUtilityIsWaitedOn()
    {
        PracticeDrillRun run = Run("a");
        run.Next();

        Assert.True(run.Thrown("Smoke", Now));
        Assert.True(run.Waiting);
    }

    [Fact]
    public void ASecondThrowOfTheSameStepIsIgnored()
    {
        PracticeDrillRun run = Run("a");
        run.Next();
        run.Thrown("Smoke", Now);

        Assert.False(run.Thrown("Smoke", Now.AddSeconds(1)));
    }

    [Fact]
    public void AScoreForSomethingElseIsNotThisStep()
    {
        PracticeDrillRun run = Run("a");
        run.Next();
        run.Thrown("Smoke", Now);

        Assert.False(run.Score("another-lineup", Result(true)));
        Assert.True(run.Waiting);
    }

    [Fact]
    public void AScoreWithNothingInFlightIsIgnored()
    {
        PracticeDrillRun run = Run("a");
        run.Next();

        Assert.False(run.Score("a", Result(true)));
        Assert.Equal(0, run.Attempts);
    }

    [Fact]
    public void AHitCountsAndBuildsTheStreak()
    {
        PracticeDrillRun run = Run("a", "b");

        Throws(run, hit: true);
        Throws(run, hit: true);

        Assert.Equal(2, run.Hits);
        Assert.Equal(0, run.Misses);
        Assert.Equal(2, run.Streak);
        Assert.Equal(2, run.BestStreak);
    }

    [Fact]
    public void AMissBreaksTheStreakButKeepsTheBest()
    {
        PracticeDrillRun run = Run("a", "b", "c");

        Throws(run, hit: true);
        Throws(run, hit: true);
        Throws(run, hit: false);

        Assert.Equal(2, run.Hits);
        Assert.Equal(1, run.Misses);
        Assert.Equal(0, run.Streak);
        Assert.Equal(2, run.BestStreak);
    }

    // Nobody knows whether it landed, so it is not a miss and it does not
    // break a streak.
    [Fact]
    public void AThrowThePanelDidNotAnswerIsNotAMiss()
    {
        PracticeDrillRun run = Run("a", "b");

        Throws(run, hit: true);
        run.Next();

        run.Thrown("Smoke", Now);
        Assert.True(run.Score("b", null));

        Assert.Equal(1, run.Unscored);
        Assert.Equal(0, run.Misses);
        Assert.Equal(1, run.Streak);
    }

    [Fact]
    public void ARunGivesUpOnAPanelThatKeepsNotAnswering()
    {
        PracticeDrillRun run = Run("a", "b", "c", "d");

        for (int step = 0; step < DrillUtility.MaxUnscoredInARow; step++)
        {
            run.Next();
            run.Thrown("Smoke", Now);
            run.Score(run.Current!.id, null);
        }

        Assert.Equal(eDrillEnd.Unscorable, run.Ending);
    }

    [Fact]
    public void AnAnsweredThrowForgivesTheOnesBefore()
    {
        PracticeDrillRun run = Run("a", "b", "c", "d", "e");

        run.Next();
        run.Thrown("Smoke", Now);
        run.Score("a", null);

        Throws(run, hit: true);

        run.Next();
        run.Thrown("Smoke", Now);
        run.Score("c", null);
        run.Next();
        run.Thrown("Smoke", Now);
        run.Score("d", null);

        Assert.Equal(eDrillEnd.Running, run.Ending);
    }

    [Fact]
    public void AThrowIsWaitedOnUntilItsDeadline()
    {
        PracticeDrillRun run = Run("a");
        run.Next();
        run.Thrown("Smoke", Now);

        Assert.False(run.Expired(Now.AddSeconds(DrillUtility.ScoreWaitSeconds - 1)));
        Assert.True(run.Expired(Now.AddSeconds(DrillUtility.ScoreWaitSeconds)));
        Assert.Equal(1, run.Unscored);
    }

    [Fact]
    public void NothingExpiresWhenNothingIsInFlight()
    {
        PracticeDrillRun run = Run("a");
        run.Next();

        Assert.False(run.Expired(Now.AddHours(1)));
    }

    [Fact]
    public void AnExpiredThrowIsOnlyWrittenOffOnce()
    {
        PracticeDrillRun run = Run("a");
        run.Next();
        run.Thrown("Smoke", Now);

        Assert.True(run.Expired(Now.AddMinutes(5)));
        Assert.False(run.Expired(Now.AddMinutes(6)));
        Assert.Equal(1, run.Unscored);
    }

    // The answer arrived after the run stopped waiting for it; the step it
    // belonged to is gone.
    [Fact]
    public void AScoreThatArrivesAfterTheDeadlineIsIgnored()
    {
        PracticeDrillRun run = Run("a", "b");
        run.Next();
        run.Thrown("Smoke", Now);
        run.Expired(Now.AddMinutes(1));
        run.Next();

        Assert.False(run.Score("a", Result(true)));
        Assert.Equal(0, run.Hits);
    }

    [Fact]
    public void MovingOnDropsAThrowNobodyAnsweredFor()
    {
        PracticeDrillRun run = Run("a", "b");
        run.Next();
        run.Thrown("Smoke", Now);
        run.Next();

        Assert.False(run.Waiting);
    }

    [Fact]
    public void ALineupThatCannotBeStoodOnIsDropped()
    {
        PracticeDrillRun run = Run("a", "b");

        run.Next();
        run.CannotLoad();

        Assert.Equal(1, run.Dropped);
        Assert.Equal(eDrillEnd.Running, run.Ending);
    }

    [Fact]
    public void ARunOfLineupsThatCannotBeStoodOnEndsTheRun()
    {
        PracticeDrillRun run = Run("a", "b", "c", "d");

        for (int step = 0; step < DrillUtility.MaxUnloadableInARow; step++)
        {
            run.Next();
            run.CannotLoad();
        }

        Assert.Equal(eDrillEnd.Unloadable, run.Ending);
    }

    [Fact]
    public void OneLineupThatLoadsForgivesTheOnesBefore()
    {
        PracticeDrillRun run = Run("a", "b", "c", "d", "e");

        run.Next();
        run.CannotLoad();
        run.Next();
        run.CannotLoad();
        run.Next();
        run.Loaded();
        run.Next();
        run.CannotLoad();
        run.Next();
        run.CannotLoad();

        Assert.Equal(eDrillEnd.Running, run.Ending);
        Assert.Equal(4, run.Dropped);
    }

    [Fact]
    public void ASkippedLineupIsNeitherAHitNorAMiss()
    {
        PracticeDrillRun run = Run("a", "b");

        Throws(run, hit: true);
        run.Next();

        Assert.True(run.Skip());
        Assert.Equal(1, run.Skipped);
        Assert.Equal(1, run.Hits);
        Assert.Equal(0, run.Misses);
        Assert.Equal(0, run.Streak);
    }

    [Fact]
    public void SkippingDropsTheThrowInFlightWithIt()
    {
        PracticeDrillRun run = Run("a", "b");
        run.Next();
        run.Thrown("Smoke", Now);
        run.Skip();

        Assert.False(run.Waiting);
        Assert.False(run.Score("a", Result(true)));
    }

    [Fact]
    public void ThereIsNothingToSkipBeforeARunStarts()
    {
        Assert.False(Run("a").Skip());
    }

    [Fact]
    public void ASummaryIsHitsOutOfAttemptsAndTheBestStreak()
    {
        PracticeDrillRun run = Run("a", "b", "c");

        Throws(run, hit: true);
        Throws(run, hit: true);
        Throws(run, hit: false);
        run.Next();

        List<string> summary = run.Summary();

        Assert.Contains("2/3 hit", summary[0]);
        Assert.Contains("best streak 2", summary[0]);
        Assert.Contains("over", summary[0]);
    }

    // The run is supposed to point at what to practise next.
    [Fact]
    public void ASummaryNamesWhatWasMissedMostOften()
    {
        PracticeDrillRun run = Run("xbox", "window", "xbox");

        Throws(run, hit: false);
        Throws(run, hit: true);
        Throws(run, hit: false);

        run.Next();

        string missed = run.Summary().Single(line => line.StartsWith("missed: "));

        Assert.Equal("missed: xbox (2)", missed);
    }

    [Fact]
    public void ASummaryNamesWhatWasSkipped()
    {
        PracticeDrillRun run = Run("a", "b");

        run.Next();
        run.Skip();
        run.Next();
        run.Skip();
        run.Next();

        Assert.Contains(run.Summary(), line => line == "skipped: a, b");
    }

    [Fact]
    public void ASummarySaysHowManyThrowsWereNeverScored()
    {
        PracticeDrillRun run = Run("a", "b");

        run.Next();
        run.Thrown("Smoke", Now);
        run.Score("a", null);
        run.Next();

        Assert.Contains(run.Summary(), line => line.Contains("1 throw could not be scored"));
    }

    [Fact]
    public void ASummarySaysWhatCouldNotBeLoaded()
    {
        PracticeDrillRun run = Run("a", "b");

        run.Next();
        run.CannotLoad();
        run.Next();
        run.CannotLoad();
        run.Next();

        Assert.Contains(run.Summary(), line => line == "2 could not be loaded");
    }

    [Fact]
    public void ASummarySaysWhyARunStoppedEarly()
    {
        PracticeDrillRun stopped = Run("a", "b");
        stopped.Next();
        stopped.End(eDrillEnd.Stopped);

        PracticeDrillRun unscorable = Run("a", "b");
        unscorable.Next();
        unscorable.End(eDrillEnd.Unscorable);

        PracticeDrillRun unloadable = Run("a", "b");
        unloadable.Next();
        unloadable.End(eDrillEnd.Unloadable);

        Assert.Contains("stopped", stopped.Summary()[0]);
        Assert.Contains("the panel is not scoring throws right now", unscorable.Summary()[0]);
        Assert.Contains("could not be loaded", unloadable.Summary()[0]);
    }

    [Fact]
    public void ARunThatWasNeverThrownSummarisesAsNothing()
    {
        PracticeDrillRun run = Run("a");
        run.Next();
        run.End(eDrillEnd.Stopped);

        List<string> summary = run.Summary();

        Assert.Single(summary);
        Assert.Contains("0/0 hit", summary[0]);
    }

    [Fact]
    public void TwoRunsKeepTheirOwnCounts()
    {
        PracticeDrillRun mine = Run("a", "b");
        PracticeDrillRun theirs = Run("a", "b");

        Throws(mine, hit: true);
        Throws(theirs, hit: false);

        Assert.Equal(1, mine.Hits);
        Assert.Equal(0, mine.Misses);
        Assert.Equal(0, theirs.Hits);
        Assert.Equal(1, theirs.Misses);
    }
}
