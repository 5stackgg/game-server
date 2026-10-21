using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

// After a changelevel from de_mirage the practice plugin filed de_mirage's places
// under de_cache: it walked the level at map load, while de_mirage's env_cs_place
// entities were still in the entity list, and a map that has answered is never
// walked again.
public class MapCalloutsWalkTests
{
    // Entity indices of the places each level spawned.
    private static readonly uint[] Mirage = { 101, 102, 103 };
    private static readonly uint[] Cache = { 210, 211 };

    private static void Spawn(MapCalloutsWalk walk, uint[] places)
    {
        foreach (uint index in places)
        {
            walk.Spawned(index);
        }
    }

    private static void Report(MapCalloutsWalk walk, string map)
    {
        Assert.True(walk.Begin(map));
        walk.Answered(map);
    }

    [Fact]
    public void NothingIsWalkedAtMapLoadBeforeTheNewLevelsPlacesSpawn()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();

        Assert.False(walk.Begin("de_cache"));
    }

    [Fact]
    public void TheOutgoingMapsPlacesNeverCountForTheNewMap()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();
        Spawn(walk, Mirage);
        Report(walk, "de_mirage");

        walk.MapLoaded();
        Spawn(walk, Cache);

        Assert.True(walk.Begin("de_cache"));
        Assert.All(Mirage, index => Assert.False(walk.Counts(index)));
        Assert.All(Cache, index => Assert.True(walk.Counts(index)));
    }

    // Excluding whatever existed at map load instead would drop a new place
    // that the engine put on an index the old level had used.
    [Fact]
    public void ANewPlaceOnAnIndexTheOldLevelUsedCounts()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();
        Spawn(walk, Mirage);
        Report(walk, "de_mirage");

        walk.MapLoaded();
        walk.Spawned(Mirage[0]);

        Assert.True(walk.Begin("de_cache"));
        Assert.True(walk.Counts(Mirage[0]));
        Assert.False(walk.Counts(Mirage[1]));
    }

    [Fact]
    public void MirageToCacheAndBackReportsEachMapsOwnPlaces()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();
        Spawn(walk, Mirage);
        Report(walk, "de_mirage");

        walk.MapLoaded();
        Assert.False(walk.Begin("de_cache"));
        Spawn(walk, Cache);
        Report(walk, "de_cache");

        uint[] mirageAgain = { 210, 305, 306 };
        walk.MapLoaded();
        Assert.False(walk.Begin("de_mirage"));
        Spawn(walk, mirageAgain);

        Assert.True(walk.Begin("de_mirage"));
        Assert.All(mirageAgain, index => Assert.True(walk.Counts(index)));
        Assert.False(walk.Counts(Cache[1]));
    }

    // A hot reload never sees a map load, and nothing is mid-change: everything
    // in the world is the current level's.
    [Fact]
    public void AHotReloadMidMapWalksStraightAwayAndEveryPlaceCounts()
    {
        var walk = new MapCalloutsWalk();

        Assert.True(walk.Begin("de_mirage"));
        Assert.All(Mirage, index => Assert.True(walk.Counts(index)));
    }

    [Fact]
    public void AHotReloadThenAChangelevelStillWaitsForTheNewLevel()
    {
        var walk = new MapCalloutsWalk();
        Report(walk, "de_mirage");

        walk.MapLoaded();

        Assert.False(walk.Begin("de_cache"));
        Assert.False(walk.Counts(Mirage[0]));
    }

    [Fact]
    public void EachMapIsReportedOnce()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();
        Spawn(walk, Cache);
        Report(walk, "de_cache");

        Assert.False(walk.Begin("de_cache"));
    }

    [Fact]
    public void ReloadingTheSameMapReportsItAgain()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();
        Spawn(walk, Mirage);
        Report(walk, "de_mirage");

        walk.MapLoaded();
        Assert.False(walk.Begin("de_mirage"));
        Spawn(walk, new uint[] { 120, 121 });

        Assert.True(walk.Begin("de_mirage"));
    }

    [Fact]
    public void WaitingForTheNewLevelSpendsNoAttempts()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();

        for (int second = 0; second < 60; second++)
        {
            Assert.False(walk.Begin("de_cache"));
        }

        Spawn(walk, Cache);

        Assert.True(walk.Begin("de_cache"));
    }

    // Places too thin to be anything or with no name walk as empty.
    [Fact]
    public void AWalkThatFindsNothingIsRetriedThenGivenUp()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();
        Spawn(walk, Cache);

        for (int attempt = 0; attempt < MapCalloutsWalk.MaxAttempts; attempt++)
        {
            Assert.True(walk.Begin("de_cache"));
        }

        Assert.False(walk.Begin("de_cache"));
    }

    [Fact]
    public void AMapWithNoNameIsNeverWalked()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();
        Spawn(walk, Cache);

        Assert.False(walk.Begin(null));
        Assert.False(walk.Begin(string.Empty));
    }

    // The `.callouts` dump walks again long after the answer, and a place
    // respawned in between is under a new index.
    [Fact]
    public void APlaceRespawnedAfterTheAnswerStillCounts()
    {
        var walk = new MapCalloutsWalk();
        walk.MapLoaded();
        Spawn(walk, Cache);
        Report(walk, "de_cache");

        walk.Spawned(260);

        Assert.True(walk.Counts(260));
        Assert.False(walk.Counts(Mirage[0]));
    }
}
