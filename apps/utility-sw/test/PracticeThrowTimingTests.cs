using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

public class PracticeThrowTimingTests
{
    private const uint Jump = 1 << 1;

    private static List<ApproachSample> Approach(params (int t, bool grounded, uint buttons)[] ticks)
    {
        return ticks
            .Select(tick => new ApproachSample
            {
                t = tick.t,
                on_ground = tick.grounded,
                buttons = tick.buttons,
            })
            .ToList();
    }

    [Fact]
    public void AJumpThrowIsTimedFromTheFirstTickInTheAir()
    {
        List<ApproachSample> thrown = Approach((-63, true, 0), (-47, false, Jump), (-31, false, 0), (0, false, 0));

        Assert.Equal(47, PracticeThrowTiming.TakeoffToRelease(thrown));
    }

    // The jump bind presses jump and lets go on one tick, still standing.
    [Fact]
    public void AJumpBindLetsGoAtTakeoff()
    {
        List<ApproachSample> thrown = Approach((-16, true, 0), (0, true, Jump));

        Assert.Equal(0, PracticeThrowTiming.TakeoffToRelease(thrown));
    }

    [Fact]
    public void AThrowMadeStandingHasNoJumpTiming()
    {
        Assert.Null(PracticeThrowTiming.TakeoffToRelease(Approach((-16, true, 0), (0, true, 0))));
        Assert.Null(PracticeThrowTiming.TakeoffToRelease(null));
    }

    private static LineupRecord Lineup(
        bool bind,
        List<ApproachSample>? approach = null,
        string technique = "Stationary"
    )
    {
        return new LineupRecord
        {
            technique = technique,
            release = new ThrowSnapshot { jump_throw = bind },
            approach = approach ?? new List<ApproachSample>(),
        };
    }

    // The library comes without run-up samples, which left every lineup with
    // no take-off to compare against and the timing never shown.
    [Fact]
    public void ALineupThrownWithTheBindLetsGoAtTakeoffWithoutItsSamples()
    {
        LineupRecord thrown = Lineup(false, Approach((-63, true, 0), (-47, false, Jump), (0, false, 0)));

        int? offset = PracticeThrowTiming.ReleaseOffset(thrown, Lineup(bind: true));

        Assert.Equal(47, offset);
        Assert.Equal("47ms late", PracticeThrowTiming.Describe(offset));
    }

    [Fact]
    public void AJumpLineupWithNoSamplesIsTakenToLetGoAtTakeoff()
    {
        LineupRecord thrown = Lineup(false, Approach((-63, true, 0), (-47, false, Jump), (0, false, 0)));

        Assert.Equal(47, PracticeThrowTiming.ReleaseOffset(thrown, Lineup(false, technique: "Jump")));
        Assert.Null(PracticeThrowTiming.ReleaseOffset(thrown, Lineup(false, technique: "Stationary")));
    }

    // A jump from a standstill has its run-up cut at the release itself, so
    // the throw carries no take-off of its own.
    [Fact]
    public void AThrowWithNoTakeoffOnRecordUsesTheEnginesJumpThrowFlag()
    {
        LineupRecord bound = Lineup(bind: true);

        Assert.Equal(0, PracticeThrowTiming.ThrowTakeoffToRelease(bound));
        Assert.Null(PracticeThrowTiming.ThrowTakeoffToRelease(Lineup(bind: false)));
    }

    [Fact]
    public void AHandThrownLineupIsTimedFromItsOwnSamples()
    {
        List<ApproachSample> lineup = Approach((-63, true, 0), (-31, false, Jump), (0, false, 0));
        LineupRecord thrown = Lineup(false, Approach((-16, true, 0), (0, true, Jump)));

        Assert.Equal(-31, PracticeThrowTiming.ReleaseOffset(thrown, Lineup(bind: false, lineup)));
    }

    [Fact]
    public void WithinATickIsOnTime()
    {
        Assert.Equal("on time", PracticeThrowTiming.Describe(15));
        Assert.Equal("on time", PracticeThrowTiming.Describe(-15));
        Assert.Equal("32ms early", PracticeThrowTiming.Describe(-32));
        Assert.Null(PracticeThrowTiming.Describe(null));
    }
}
