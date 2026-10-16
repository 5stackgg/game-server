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
        Assert.True(MapLoadUtility.IsLoaded(Sanctum, "de_sanctum", "3643331442", Shipped));
        Assert.False(MapLoadUtility.IsLoaded(Sanctum, "de_inferno", "", Shipped));
    }
}
