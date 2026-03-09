namespace FiveStack.Tests.Utilities;

using FiveStack.Utilities;
using FiveStack.Enums;

public class EnumConversionTests
{
    [Theory]
    [InlineData("Coach", eTimeoutSettings.Coach)]
    [InlineData("CoachAndCaptains", eTimeoutSettings.CoachAndCaptains)]
    [InlineData("CoachAndPlayers", eTimeoutSettings.CoachAndPlayers)]
    [InlineData("Admin", eTimeoutSettings.Admin)]
    public void TimeoutSettingStringToEnum_ValidInput_ReturnsCorrectEnum(
        string input, eTimeoutSettings expected)
    {
        TimeoutUtility.TimeoutSettingStringToEnum(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("coach")]  // case-sensitive
    public void TimeoutSettingStringToEnum_InvalidInput_ThrowsArgumentException(string input)
    {
        var act = () => TimeoutUtility.TimeoutSettingStringToEnum(input);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("Captains", eReadySettings.Captains)]
    [InlineData("Coach", eReadySettings.Coach)]
    [InlineData("Admin", eReadySettings.Admin)]
    [InlineData("Players", eReadySettings.Players)]
    public void ReadySettingStringToEnum_ValidInput_ReturnsCorrectEnum(
        string input, eReadySettings expected)
    {
        ReadyUtility.ReadySettingStringToEnum(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("players")]  // case-sensitive
    public void ReadySettingStringToEnum_InvalidInput_ThrowsArgumentException(string input)
    {
        var act = () => ReadyUtility.ReadySettingStringToEnum(input);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("administrator", ePlayerRoles.Administrator)]
    [InlineData("match_organizer", ePlayerRoles.MatchOrganizer)]
    [InlineData("tournament_organizer", ePlayerRoles.MatchOrganizer)]
    [InlineData("streamer", ePlayerRoles.Streamer)]
    [InlineData("verified_user", ePlayerRoles.VerifiedUser)]
    [InlineData("user", ePlayerRoles.User)]
    public void PlayerRoleStringToEnum_ValidInput_ReturnsCorrectEnum(
        string input, ePlayerRoles expected)
    {
        PlayerRoleUtility.PlayerRoleStringToEnum(input).Should().Be(expected);
    }

    [Fact]
    public void PlayerRoleStringToEnum_UnknownRole_DefaultsToUser()
    {
        PlayerRoleUtility.PlayerRoleStringToEnum("unknown").Should().Be(ePlayerRoles.User);
        PlayerRoleUtility.PlayerRoleStringToEnum("").Should().Be(ePlayerRoles.User);
    }
}
