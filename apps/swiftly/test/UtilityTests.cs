using FiveStack.Utilities;
using Xunit;

public class UtilityTests
{
    [Theory]
    [InlineData(0, "Body")]
    [InlineData(1, "Head")]
    [InlineData(2, "Chest")]
    [InlineData(3, "Stomach")]
    [InlineData(4, "Left Arm")]
    [InlineData(5, "Right Arm")]
    [InlineData(6, "Left Leg")]
    [InlineData(7, "Right Leg")]
    [InlineData(10, "Gear")]
    [InlineData(99, "Unknown")]
    public void HitGroupToString_MapsHitboxes(int hitGroup, string expected)
    {
        Assert.Equal(expected, DamageUtility.HitGroupToString(hitGroup));
    }

    [Theory]
    [InlineData("TerroristsWin", "Terrorists Win")]
    [InlineData("BombDefused", "Bomb Defused")]
    [InlineData("A", "A")]
    [InlineData("", "")]
    public void ConvertCamelToHumanReadable_SplitsOnCase(string input, string expected)
    {
        Assert.Equal(expected, StringUtility.ConvertCamelToHumanReadable(input));
    }

    [Theory]
    [InlineData(1, "Spectator")]
    [InlineData(2, "TERRORIST")]
    [InlineData(3, "CT")]
    [InlineData(0, "None")]
    public void TeamNumToString_MapsTeamNumbers(int teamNum, string expected)
    {
        Assert.Equal(expected, TeamUtility.TeamNumToString(teamNum));
    }
}
