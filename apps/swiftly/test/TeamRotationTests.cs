using FiveStack.Utilities;
using Xunit;

public class TeamRotationTests
{
    [Theory]
    [InlineData(0, 12, 6, false)]
    [InlineData(11, 12, 6, false)]
    [InlineData(12, 12, 6, true)]
    [InlineData(23, 12, 6, true)]
    public void RegularTime_FirstHalfStarting_SecondHalfOpposite(
        int round,
        int mr,
        int overtimeMr,
        bool expected
    )
    {
        Assert.Equal(expected, TeamRotation.IsOnOppositeSide(round, mr, overtimeMr));
    }

    [Theory]
    [InlineData(24, 12, 6, true)]
    [InlineData(26, 12, 6, true)]
    [InlineData(27, 12, 6, false)]
    [InlineData(29, 12, 6, false)]
    [InlineData(30, 12, 6, false)]
    [InlineData(32, 12, 6, false)]
    [InlineData(33, 12, 6, true)]
    [InlineData(35, 12, 6, true)]
    public void Overtime_AlternatesEachHalf(int round, int mr, int overtimeMr, bool expected)
    {
        Assert.Equal(expected, TeamRotation.IsOnOppositeSide(round, mr, overtimeMr));
    }

    [Fact]
    public void RegularTime_BoundaryFlipsExactlyAtHalf()
    {
        Assert.False(TeamRotation.IsOnOppositeSide(7, 8, 6));
        Assert.True(TeamRotation.IsOnOppositeSide(8, 8, 6));
    }

    [Theory]
    [InlineData(0, 10, false)]
    [InlineData(1, 10, true)]
    [InlineData(4, 10, true)]
    [InlineData(5, 10, false)]
    [InlineData(6, 10, false)]
    [InlineData(10, 10, false)]
    [InlineData(1, 2, false)]
    [InlineData(1, 3, true)]
    [InlineData(0, 0, false)]
    [InlineData(1, 0, false)]
    public void ShouldReconcile_OnlyAStrictMinority(int mismatched, int placed, bool expected)
    {
        Assert.Equal(expected, TeamRotation.ShouldReconcile(mismatched, placed));
    }

    [Theory]
    [InlineData(2, false, 2)]
    [InlineData(3, false, 3)]
    [InlineData(1, false, 1)]
    [InlineData(0, false, 0)]
    [InlineData(2, true, 3)]
    [InlineData(3, true, 2)]
    [InlineData(1, true, 1)]
    [InlineData(0, true, 0)]
    public void PlacementSide_PreSwapSideOnlyWhileSwitching(
        int expectedTeamNum,
        bool switchingAtReset,
        int placement
    )
    {
        Assert.Equal(placement, TeamRotation.PlacementSide(expectedTeamNum, switchingAtReset));
    }
}
