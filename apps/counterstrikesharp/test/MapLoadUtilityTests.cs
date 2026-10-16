using FiveStack.Entities;
using FiveStack.Utilities;
using Xunit;

public class MapLoadUtilityTests
{
    private static readonly Func<string, bool> Shipped = name =>
        name is "de_poseidon" or "de_dust2";

    private static Map Poseidon => new Map { name = "de_poseidon", workshop_map_id = "3522144043" };

    private static Map Sanctum => new Map { name = "de_sanctum", workshop_map_id = "3643331442" };

    [Fact]
    public void ChangeCommand_LoadsTheClientsCopyEvenWithAWorkshopId()
    {
        Assert.Equal("changelevel \"de_poseidon\"", MapLoadUtility.ChangeCommand(Poseidon, Shipped));
    }

    [Fact]
    public void ChangeCommand_FetchesFromTheWorkshopWhenTheClientDoesNotShipIt()
    {
        Assert.Equal("host_workshop_map 3643331442", MapLoadUtility.ChangeCommand(Sanctum, Shipped));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ChangeCommand_LoadsByNameWithoutAWorkshopId(string? workshopMapId)
    {
        Map map = new Map { name = "de_dust2", workshop_map_id = workshopMapId! };

        Assert.Equal("changelevel \"de_dust2\"", MapLoadUtility.ChangeCommand(map, Shipped));
    }

    [Fact]
    public void IsLoaded_ChecksTheNameOfAMapTheClientShips()
    {
        Assert.True(MapLoadUtility.IsLoaded(Poseidon, "de_poseidon", "", Shipped));
        Assert.False(MapLoadUtility.IsLoaded(Poseidon, "de_inferno", "", Shipped));
    }

    [Fact]
    public void IsLoaded_ChecksTheWorkshopIdOfAMapTheClientDoesNotShip()
    {
        Assert.True(MapLoadUtility.IsLoaded(Sanctum, "aim_internal_name", "3643331442", Shipped));
        Assert.False(MapLoadUtility.IsLoaded(Sanctum, "de_sanctum", "", Shipped));
    }

    [Fact]
    public void IsLoaded_MatchesTheWholeMapName()
    {
        Map dust2 = new Map { name = "de_dust2", workshop_map_id = null! };

        Assert.True(MapLoadUtility.IsLoaded(dust2, "DE_DUST2", "", Shipped));
        Assert.False(MapLoadUtility.IsLoaded(dust2, "de_dust2_night", "", Shipped));
    }

    // SwiftlyS2's IsMapValid also answers true for a workshop id whose download
    // is on disk, and a hand-added workshop map is named after its id.
    private static readonly Func<string, bool> ShippedOrDownloaded = name =>
        Shipped(name) || name == "3615968422";

    private static Map HandAdded => new Map { name = "3615968422", workshop_map_id = "3615968422" };

    [Fact]
    public void ChangeCommand_FetchesAMapNamedAfterItsWorkshopIdFromTheWorkshop()
    {
        Assert.Equal(
            "host_workshop_map 3615968422",
            MapLoadUtility.ChangeCommand(HandAdded, ShippedOrDownloaded)
        );
    }

    [Fact]
    public void IsLoaded_ChecksTheWorkshopIdOfAMapNamedAfterIt()
    {
        Assert.True(MapLoadUtility.IsLoaded(HandAdded, "aim_x", "3615968422", ShippedOrDownloaded));
        Assert.False(MapLoadUtility.IsLoaded(HandAdded, "de_inferno", "", ShippedOrDownloaded));
    }
}
