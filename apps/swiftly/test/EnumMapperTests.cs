using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

public class EnumMapperTests
{
    [Theory]
    [InlineData("administrator", ePlayerRoles.Administrator)]
    [InlineData("match_organizer", ePlayerRoles.MatchOrganizer)]
    [InlineData("tournament_organizer", ePlayerRoles.MatchOrganizer)]
    [InlineData("streamer", ePlayerRoles.Streamer)]
    [InlineData("verified_user", ePlayerRoles.VerifiedUser)]
    [InlineData("anything_else", ePlayerRoles.User)]
    [InlineData("", ePlayerRoles.User)]
    public void PlayerRole_MapsApiStrings(string input, ePlayerRoles expected)
    {
        Assert.Equal(expected, PlayerRoleUtility.PlayerRoleStringToEnum(input));
    }

    [Theory]
    [InlineData("Captains", eReadySettings.Captains)]
    [InlineData("Coach", eReadySettings.Coach)]
    [InlineData("Admin", eReadySettings.Admin)]
    [InlineData("Players", eReadySettings.Players)]
    public void ReadySetting_MapsApiStrings(string input, eReadySettings expected)
    {
        Assert.Equal(expected, ReadyUtility.ReadySettingStringToEnum(input));
    }

    [Fact]
    public void ReadySetting_UnknownThrows()
    {
        Assert.Throws<ArgumentException>(() => ReadyUtility.ReadySettingStringToEnum("Nope"));
    }

    [Theory]
    [InlineData("Coach", eTimeoutSettings.Coach)]
    [InlineData("CoachAndCaptains", eTimeoutSettings.CoachAndCaptains)]
    [InlineData("CoachAndPlayers", eTimeoutSettings.CoachAndPlayers)]
    [InlineData("Admin", eTimeoutSettings.Admin)]
    public void TimeoutSetting_MapsApiStrings(string input, eTimeoutSettings expected)
    {
        Assert.Equal(expected, TimeoutUtility.TimeoutSettingStringToEnum(input));
    }

    [Fact]
    public void TimeoutSetting_UnknownThrows()
    {
        Assert.Throws<ArgumentException>(() => TimeoutUtility.TimeoutSettingStringToEnum("Nope"));
    }

    [Theory]
    [InlineData("Warmup", eMapStatus.Warmup)]
    [InlineData("Knife", eMapStatus.Knife)]
    [InlineData("Live", eMapStatus.Live)]
    [InlineData("Overtime", eMapStatus.Overtime)]
    [InlineData("Paused", eMapStatus.Paused)]
    [InlineData("WaitingForTV", eMapStatus.WaitingForTV)]
    [InlineData("UploadingDemo", eMapStatus.UploadingDemo)]
    [InlineData("Finished", eMapStatus.Finished)]
    [InlineData("Surrendered", eMapStatus.Surrendered)]
    [InlineData("Scheduled", eMapStatus.Scheduled)]
    [InlineData("Unknown", eMapStatus.Unknown)]
    public void MapStatus_MapsApiStrings(string input, eMapStatus expected)
    {
        Assert.Equal(expected, MatchUtility.MapStatusStringToEnum(input));
    }

    [Fact]
    public void MapStatus_UnknownStringThrows()
    {
        Assert.Throws<ArgumentException>(() => MatchUtility.MapStatusStringToEnum("Bogus"));
    }
}
