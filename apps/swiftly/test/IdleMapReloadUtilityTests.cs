using FiveStack.Utilities;
using Xunit;

public class IdleMapReloadUtilityTests
{
    private const float FourHours = 4 * 60 * 60;

    [Fact]
    public void ShouldReload_AnEmptyServerWhoseClockHasRunLong()
    {
        Assert.True(IdleMapReloadUtility.ShouldReload(FourHours, players: 0, hasMatch: false));
    }

    [Fact]
    public void ShouldReload_NotAFreshMap()
    {
        Assert.False(IdleMapReloadUtility.ShouldReload(60, players: 0, hasMatch: false));
        Assert.False(
            IdleMapReloadUtility.ShouldReload(
                IdleMapReloadUtility.MaxCurTime,
                players: 0,
                hasMatch: false
            )
        );
    }

    [Fact]
    public void ShouldReload_NotWithAnyoneOnTheServer()
    {
        Assert.False(IdleMapReloadUtility.ShouldReload(FourHours, players: 1, hasMatch: false));
    }

    [Fact]
    public void ShouldReload_NotWhileTheServerHasAMatch()
    {
        Assert.False(IdleMapReloadUtility.ShouldReload(FourHours, players: 0, hasMatch: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ReloadCommand_ChangesLevelToAShippedMap(string? workshopId)
    {
        Assert.Equal(
            "changelevel \"de_dust2\"",
            IdleMapReloadUtility.ReloadCommand("de_dust2", workshopId)
        );
    }

    [Fact]
    public void ReloadCommand_ReloadsAWorkshopMapByItsId()
    {
        Assert.Equal(
            "host_workshop_map 3643331442",
            IdleMapReloadUtility.ReloadCommand("aim_internal_name", "3643331442")
        );
    }
}
