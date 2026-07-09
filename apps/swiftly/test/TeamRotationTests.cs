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
}
