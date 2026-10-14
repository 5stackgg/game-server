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

    [Fact]
    public void LettingGoAfterTheLineupDidIsLate()
    {
        List<ApproachSample> lineup = Approach((-16, true, 0), (0, true, Jump));
        List<ApproachSample> thrown = Approach((-63, true, 0), (-47, false, Jump), (0, false, 0));

        int? offset = PracticeThrowTiming.ReleaseOffset(thrown, lineup);

        Assert.Equal(47, offset);
        Assert.Equal("let go 47ms late", PracticeThrowTiming.Describe(offset));
    }

    [Fact]
    public void WithinATickIsOnTime()
    {
        Assert.Null(PracticeThrowTiming.Describe(15));
        Assert.Null(PracticeThrowTiming.Describe(-15));
        Assert.Equal("let go 32ms early", PracticeThrowTiming.Describe(-32));
    }
}
