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
}
