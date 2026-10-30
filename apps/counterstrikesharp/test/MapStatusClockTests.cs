using FiveStack.Utilities;
using Xunit;

// The api keeps only the newest status of a map, by this time, since a resent
// status can arrive after a newer one.
public class MapStatusClockTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StampsToTheMillisecondInUtc()
    {
        DateTime stamp = new MapStatusClock().Next(Now.AddTicks(1234));

        Assert.Equal(Now, stamp);
        Assert.Equal(DateTimeKind.Utc, stamp.Kind);
    }

    [Fact]
    public void KeepsTwoStatusesInTheSameMillisecondInOrder()
    {
        var clock = new MapStatusClock();

        DateTime warmup = clock.Next(Now);
        DateTime live = clock.Next(Now.AddTicks(10));

        Assert.Equal(Now.AddMilliseconds(1), live);
        Assert.True(live > warmup);
    }

    [Fact]
    public void NeverGoesBackWhenTheClockDoes()
    {
        var clock = new MapStatusClock();

        DateTime first = clock.Next(Now);
        DateTime second = clock.Next(Now.AddSeconds(-5));

        Assert.True(second > first);
    }
}
