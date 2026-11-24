using System.Runtime.CompilerServices;
using FiveStack.Utilities;
using Xunit;

public class TimerUtilityTests
{
    private static void Fire(CancellationTokenSource timer, Action callback)
    {
        typeof(TimerUtility).GetMethod("RunOnce", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .Invoke(null, new object[] { timer, callback });
    }

    [Fact]
    public void Completed_Timers_Are_Forgotten_Even_When_Their_Callback_Throws()
    {
        using var timer = new CancellationTokenSource();
        TimerUtility.Timers.Add(timer);
        var error = Assert.Throws<System.Reflection.TargetInvocationException>(() =>
            Fire(timer, () => throw new InvalidOperationException("fixture")));
        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.DoesNotContain(timer, TimerUtility.Timers);
    }

    [Fact]
    public void Completed_Timers_Release_Only_Their_Own_Registry_Entry()
    {
        using var completed = new CancellationTokenSource();
        using var waiting = new CancellationTokenSource();
        TimerUtility.Timers.Add(completed);
        TimerUtility.Timers.Add(waiting);
        try
        {
            int calls = 0;
            Fire(completed, () => calls++);
            Assert.Equal(1, calls);
            Assert.DoesNotContain(completed, TimerUtility.Timers);
            Assert.Contains(waiting, TimerUtility.Timers);
        }
        finally { TimerUtility.Kill(waiting); }
    }

    [Fact]
    public void Canceled_One_Shot_Callbacks_Do_Not_Run_And_Are_Forgotten()
    {
        using var timer = new CancellationTokenSource();
        TimerUtility.Timers.Add(timer);
        timer.Cancel();
        Fire(timer, () => throw new InvalidOperationException("must not run"));
        Assert.DoesNotContain(timer, TimerUtility.Timers);
    }
    private static string Source([CallerFilePath] string testFile = "")
    {
        return File.ReadAllText(
            Path.Combine(
                Path.GetDirectoryName(testFile)!,
                "..",
                "src",
                "FiveStack.Utilities",
                "TimerUtility.cs"
            )
        );
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(0.0005f)]
    public void A_Delay_Under_A_Millisecond_Is_Too_Short_To_Delay(float interval)
    {
        Assert.True(TimerUtility.TooShortToDelay(interval));
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(1f)]
    [InlineData(115f)]
    public void Any_Longer_Delay_Is_Not(float interval)
    {
        Assert.False(TimerUtility.TooShortToDelay(interval));
    }

    [Fact]
    public void A_Delay_That_Is_Too_Short_Runs_On_The_Next_Tick_Instead()
    {
        string source = Source();
        int add = source.IndexOf("public static CancellationTokenSource AddTimer(", StringComparison.Ordinal);
        int tooShort = source.IndexOf("if (TooShortToDelay(interval))", add, StringComparison.Ordinal);
        int nextTick = source.IndexOf("_core!.Scheduler.NextTick(", add, StringComparison.Ordinal);
        int delay = source.IndexOf("_core!.Scheduler.DelayBySeconds(", add, StringComparison.Ordinal);

        Assert.True(add >= 0 && tooShort > add, "a zero delay goes to SwiftlyS2, which drops it");
        Assert.True(tooShort < nextTick && nextTick < delay, "a zero delay goes to SwiftlyS2, which drops it");
    }
}
