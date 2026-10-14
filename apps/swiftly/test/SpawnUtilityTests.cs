using FiveStack.Utilities;
using Xunit;

public class SpawnUtilityTests
{
    [Theory]
    [InlineData(0f, 0f, true)]
    [InlineData(0.4f, -0.6f, true)]
    [InlineData(1f, 0f, false)]
    [InlineData(0f, -1f, false)]
    [InlineData(592f, 545f, false)]
    [InlineData(-1662f, 288f, false)]
    public void WorldOriginIsTheOriginColumnOnly(float x, float y, bool expected)
    {
        Assert.Equal(expected, SpawnUtility.IsAtWorldOrigin(x, y));
    }

    [Fact]
    public void SideOnTheServerWithNobodyAliveNeverPlayed()
    {
        Assert.True(SpawnUtility.IsUnplayedKnifeTimeout(2, 2, 2, 0));
        Assert.True(SpawnUtility.IsUnplayedKnifeTimeout(5, 0, 5, 3));
    }

    [Fact]
    public void BothSidesAliveIsAResult()
    {
        Assert.False(SpawnUtility.IsUnplayedKnifeTimeout(2, 2, 2, 1));
        Assert.False(SpawnUtility.IsUnplayedKnifeTimeout(5, 1, 5, 1));
    }

    [Fact]
    public void SideWithNobodyOnTheServerIsAResult()
    {
        Assert.False(SpawnUtility.IsUnplayedKnifeTimeout(2, 2, 0, 0));
    }
}
