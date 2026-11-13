using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// .resume only answers a pause. CS2 holds the round for one at halftime and for
// a restore without the map ever reading Paused, and the knife side pick is not one.
public class TimeoutResumeTests
{
    [Theory]
    [InlineData(eMapStatus.Paused, false)]
    [InlineData(eMapStatus.Paused, true)]
    [InlineData(eMapStatus.Live, true)]
    [InlineData(eMapStatus.Overtime, true)]
    public void A_Pause_Or_A_Held_Round_In_Play_Can_Be_Resumed(eMapStatus status, bool waitingForResume)
    {
        Assert.True(TimeoutUtility.HasPauseToResume(status, waitingForResume));
    }

    [Theory]
    [InlineData(eMapStatus.Warmup, false)]
    [InlineData(eMapStatus.Warmup, true)]
    [InlineData(eMapStatus.Knife, true)]
    [InlineData(eMapStatus.Live, false)]
    [InlineData(eMapStatus.Overtime, false)]
    [InlineData(eMapStatus.WaitingForTV, true)]
    [InlineData(eMapStatus.Finished, true)]
    [InlineData(eMapStatus.Unknown, true)]
    public void Nothing_Else_Can_Be_Resumed(eMapStatus status, bool waitingForResume)
    {
        Assert.False(TimeoutUtility.HasPauseToResume(status, waitingForResume));
    }
}
