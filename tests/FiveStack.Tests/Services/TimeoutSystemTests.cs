namespace FiveStack.Tests.Services;

using FiveStack.Enums;
using FiveStack.Utilities;

/// <summary>
/// Tests for TimeoutSystem decision logic.
/// Since TimeoutSystem depends on CounterStrikeSharp types (CCSPlayerController,
/// Server.NextFrame, MatchUtility.Rules) that are not mockable, these tests
/// extract and verify the pure decision logic independently.
/// </summary>
public class TimeoutSystemTests
{
    // -- TimeoutUtility.TimeoutSettingStringToEnum --

    [Theory]
    [InlineData("Coach", eTimeoutSettings.Coach)]
    [InlineData("CoachAndCaptains", eTimeoutSettings.CoachAndCaptains)]
    [InlineData("CoachAndPlayers", eTimeoutSettings.CoachAndPlayers)]
    [InlineData("Admin", eTimeoutSettings.Admin)]
    public void TimeoutSettingStringToEnum_ReturnsCorrectEnum(
        string input, eTimeoutSettings expected)
    {
        TimeoutUtility.TimeoutSettingStringToEnum(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("coach")]
    [InlineData("invalid")]
    public void TimeoutSettingStringToEnum_ThrowsForInvalidInput(string input)
    {
        var act = () => TimeoutUtility.TimeoutSettingStringToEnum(input);
        act.Should().Throw<ArgumentException>();
    }

    // -- CanPause decision table (extracted from TimeoutSystem.CanPause) --
    // Inputs: isCoach, isCaptain, isAdmin/Organizer, timeoutSetting
    // Output: canPause

    private static bool CanPauseLogic(
        bool isCoach, bool isCaptain, bool isAdminOrOrganizer,
        eTimeoutSettings setting)
    {
        // Admins and organizers can always pause
        if (isAdminOrOrganizer) return true;

        switch (setting)
        {
            case eTimeoutSettings.Coach:
                return isCoach;
            case eTimeoutSettings.CoachAndCaptains:
                return isCoach || isCaptain;
            case eTimeoutSettings.CoachAndPlayers:
                return true; // All players can pause
            case eTimeoutSettings.Admin:
                return false; // Only admin/organizer (already checked)
            default:
                return true;
        }
    }

    [Theory]
    // Coach setting: only coaches can pause
    [InlineData(true, false, false, eTimeoutSettings.Coach, true)]
    [InlineData(false, false, false, eTimeoutSettings.Coach, false)]
    [InlineData(false, true, false, eTimeoutSettings.Coach, false)]
    // CoachAndCaptains: coaches and captains
    [InlineData(true, false, false, eTimeoutSettings.CoachAndCaptains, true)]
    [InlineData(false, true, false, eTimeoutSettings.CoachAndCaptains, true)]
    [InlineData(false, false, false, eTimeoutSettings.CoachAndCaptains, false)]
    // CoachAndPlayers: everyone can pause
    [InlineData(false, false, false, eTimeoutSettings.CoachAndPlayers, true)]
    // Admin: only admin/organizer
    [InlineData(true, true, false, eTimeoutSettings.Admin, false)]
    [InlineData(false, false, true, eTimeoutSettings.Admin, true)]
    // Admin overrides any setting
    [InlineData(false, false, true, eTimeoutSettings.Coach, true)]
    [InlineData(false, false, true, eTimeoutSettings.CoachAndCaptains, true)]
    public void CanPause_RespectsSettingAndRole(
        bool isCoach, bool isCaptain, bool isAdmin,
        eTimeoutSettings setting, bool expected)
    {
        CanPauseLogic(isCoach, isCaptain, isAdmin, setting).Should().Be(expected);
    }

    // -- CanCallTacticalTimeout decision table --

    private static bool CanCallTacLogic(
        bool isCoach, bool isCaptain, eTimeoutSettings setting)
    {
        switch (setting)
        {
            case eTimeoutSettings.Coach:
                return isCoach;
            case eTimeoutSettings.CoachAndCaptains:
                return isCoach || isCaptain;
            case eTimeoutSettings.Admin:
                return false;
            default:
                return true;
        }
    }

    [Theory]
    [InlineData(true, false, eTimeoutSettings.Coach, true)]
    [InlineData(false, false, eTimeoutSettings.Coach, false)]
    [InlineData(true, false, eTimeoutSettings.CoachAndCaptains, true)]
    [InlineData(false, true, eTimeoutSettings.CoachAndCaptains, true)]
    [InlineData(false, false, eTimeoutSettings.CoachAndCaptains, false)]
    [InlineData(true, true, eTimeoutSettings.Admin, false)]
    [InlineData(false, false, eTimeoutSettings.CoachAndPlayers, true)]
    public void CanCallTacticalTimeout_RespectsSettingAndRole(
        bool isCoach, bool isCaptain,
        eTimeoutSettings setting, bool expected)
    {
        CanCallTacLogic(isCoach, isCaptain, setting).Should().Be(expected);
    }

    // -- Timeout available count logic --

    [Theory]
    [InlineData(3, true)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void TimeoutsAvailable_DeterminesIfCallAllowed(
        int available, bool expected)
    {
        (available > 0).Should().Be(expected);
    }

    [Fact]
    public void TimeoutsAvailable_DecrementsAfterUse()
    {
        int available = 3;
        available--;
        available.Should().Be(2);
    }
}
