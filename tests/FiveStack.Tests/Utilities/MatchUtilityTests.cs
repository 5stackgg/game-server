namespace FiveStack.Tests.Utilities;

using FiveStack.Utilities;
using FiveStack.Enums;

public class MatchUtilityTests
{
    [Theory]
    [InlineData("Scheduled", eMapStatus.Scheduled)]
    [InlineData("Finished", eMapStatus.Finished)]
    [InlineData("Knife", eMapStatus.Knife)]
    [InlineData("Live", eMapStatus.Live)]
    [InlineData("Overtime", eMapStatus.Overtime)]
    [InlineData("Paused", eMapStatus.Paused)]
    [InlineData("Warmup", eMapStatus.Warmup)]
    [InlineData("UploadingDemo", eMapStatus.UploadingDemo)]
    [InlineData("Surrendered", eMapStatus.Surrendered)]
    [InlineData("Unknown", eMapStatus.Unknown)]
    public void MapStatusStringToEnum_ValidInput_ReturnsCorrectEnum(
        string input, eMapStatus expected)
    {
        MatchUtility.MapStatusStringToEnum(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("live")]  // case-sensitive
    public void MapStatusStringToEnum_InvalidInput_ThrowsArgumentException(string input)
    {
        var act = () => MatchUtility.MapStatusStringToEnum(input);
        act.Should().Throw<ArgumentException>()
            .WithMessage($"Unsupported status string: {input}");
    }
}
