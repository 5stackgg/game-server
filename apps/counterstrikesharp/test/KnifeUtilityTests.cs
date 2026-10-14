using FiveStack.Utilities;
using Xunit;

public class KnifeUtilityTests
{
    [Fact]
    public void SideOnTheServerWithNobodyAliveNeverPlayed()
    {
        Assert.True(KnifeUtility.IsUnplayedTimeout(2, 2, 2, 0));
        Assert.True(KnifeUtility.IsUnplayedTimeout(5, 0, 5, 3));
    }

    [Fact]
    public void BothSidesAliveIsAResult()
    {
        Assert.False(KnifeUtility.IsUnplayedTimeout(2, 2, 2, 1));
        Assert.False(KnifeUtility.IsUnplayedTimeout(5, 1, 5, 1));
    }

    [Fact]
    public void SideWithNobodyOnTheServerIsAResult()
    {
        Assert.False(KnifeUtility.IsUnplayedTimeout(2, 2, 0, 0));
    }
}
