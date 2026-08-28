using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

public class HudSteadyTests
{
    private const int Hold = 22;

    [Fact]
    public void HoldsTheStartingValue()
    {
        Assert.False(HudSteady.Start(false).Committed);
        Assert.True(HudSteady.Start(true).Committed);
    }

    [Fact]
    public void DoesNotFollowUntilTheReadingHasHeld()
    {
        HudSteady steady = HudSteady.Start(false);

        steady = steady.Read(true, 100, Hold);
        Assert.False(steady.Committed);

        steady = steady.Read(true, 100 + Hold - 1, Hold);
        Assert.False(steady.Committed);

        steady = steady.Read(true, 100 + Hold, Hold);
        Assert.True(steady.Committed);
    }

    // The whole point: standing on the edge of the circle flips the raw test
    // many times a second, and the panel must not move for any of them.
    [Fact]
    public void FlickerNeverCommits()
    {
        HudSteady steady = HudSteady.Start(false);

        for (int tick = 0; tick < 2000; tick++)
        {
            steady = steady.Read(tick % 2 == 0, tick, Hold);

            Assert.False(steady.Committed);
        }
    }

    // A reading that goes back to the committed value drops the pending move
    // entirely, rather than letting near-misses accumulate towards one.
    [Fact]
    public void GoingBackResetsTheTimer()
    {
        HudSteady steady = HudSteady.Start(false);

        steady = steady.Read(true, 0, Hold);
        steady = steady.Read(false, Hold - 1, Hold);
        steady = steady.Read(true, Hold, Hold);

        Assert.False(steady.Committed);

        steady = steady.Read(true, Hold * 2, Hold);

        Assert.True(steady.Committed);
    }

    [Fact]
    public void CommitsInBothDirections()
    {
        HudSteady steady = HudSteady.Start(true);

        for (int tick = 0; tick <= Hold; tick++)
        {
            steady = steady.Read(false, tick, Hold);
        }

        Assert.False(steady.Committed);
    }

    [Fact]
    public void StayingPutNeverChanges()
    {
        HudSteady steady = HudSteady.Start(true);

        for (int tick = 0; tick < 500; tick++)
        {
            steady = steady.Read(true, tick, Hold);

            Assert.True(steady.Committed);
        }
    }
}
