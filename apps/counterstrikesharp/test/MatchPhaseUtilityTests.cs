using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// A phase is what a timer or a vote belongs to: Paused is still play, and
// WaitingForTV is already the end of the map.
public class MatchPhaseUtilityTests
{
    [Theory]
    [InlineData(eMapStatus.Scheduled, eMatchPhase.Warmup)]
    [InlineData(eMapStatus.Warmup, eMatchPhase.Warmup)]
    [InlineData(eMapStatus.Knife, eMatchPhase.Knife)]
    [InlineData(eMapStatus.Live, eMatchPhase.InPlay)]
    [InlineData(eMapStatus.Overtime, eMatchPhase.InPlay)]
    [InlineData(eMapStatus.Paused, eMatchPhase.InPlay)]
    [InlineData(eMapStatus.WaitingForTV, eMatchPhase.Over)]
    [InlineData(eMapStatus.UploadingDemo, eMatchPhase.Over)]
    [InlineData(eMapStatus.Finished, eMatchPhase.Over)]
    [InlineData(eMapStatus.Surrendered, eMatchPhase.Over)]
    [InlineData(eMapStatus.Unknown, eMatchPhase.None)]
    public void Every_Status_Belongs_To_One_Phase(eMapStatus status, eMatchPhase phase)
    {
        Assert.Equal(phase, MatchPhaseUtility.PhaseOf(status));
    }

    [Fact]
    public void Only_Unknown_Has_No_Phase()
    {
        Assert.Equal(
            new[] { eMapStatus.Unknown },
            Enum.GetValues<eMapStatus>()
                .Where(status => MatchPhaseUtility.PhaseOf(status) == eMatchPhase.None)
                .ToArray()
        );
    }

    [Theory]
    [InlineData(eMapStatus.WaitingForTV, eMapStatus.Paused)]
    [InlineData(eMapStatus.WaitingForTV, eMapStatus.Live)]
    [InlineData(eMapStatus.WaitingForTV, eMapStatus.Warmup)]
    [InlineData(eMapStatus.Finished, eMapStatus.Knife)]
    [InlineData(eMapStatus.UploadingDemo, eMapStatus.Overtime)]
    [InlineData(eMapStatus.Surrendered, eMapStatus.Scheduled)]
    [InlineData(eMapStatus.WaitingForTV, eMapStatus.Surrendered)]
    [InlineData(eMapStatus.UploadingDemo, eMapStatus.Surrendered)]
    [InlineData(eMapStatus.Finished, eMapStatus.Surrendered)]
    public void A_Map_That_Is_Over_Is_Never_Played_Again_Or_Surrendered(
        eMapStatus current,
        eMapStatus next
    )
    {
        Assert.True(MatchPhaseUtility.Reopens(current, next));
    }

    [Theory]
    [InlineData(eMapStatus.WaitingForTV, eMapStatus.UploadingDemo)]
    [InlineData(eMapStatus.WaitingForTV, eMapStatus.Finished)]
    [InlineData(eMapStatus.Surrendered, eMapStatus.WaitingForTV)]
    [InlineData(eMapStatus.Live, eMapStatus.Surrendered)]
    [InlineData(eMapStatus.Paused, eMapStatus.Surrendered)]
    [InlineData(eMapStatus.Finished, eMapStatus.Unknown)]
    [InlineData(eMapStatus.Live, eMapStatus.WaitingForTV)]
    [InlineData(eMapStatus.Paused, eMapStatus.Live)]
    [InlineData(eMapStatus.Unknown, eMapStatus.Paused)]
    [InlineData(eMapStatus.Knife, eMapStatus.Warmup)]
    public void Every_Other_Move_Is_Allowed(eMapStatus current, eMapStatus next)
    {
        Assert.False(MatchPhaseUtility.Reopens(current, next));
    }
}
