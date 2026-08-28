using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

// The same cases the panel's resolver is tested against
// (api/src/utility/utility-callouts.service.spec.ts). They are duplicated
// deliberately: a name the HUD gives and a name the website gives for the same
// throw must never disagree, and only matching tests keep that true.
public class CalloutLookupTests
{
    private static MapCalloutBox Box(float[] min, float[] max)
    {
        return new MapCalloutBox { min = min, max = max };
    }

    private static MapCalloutPayload Place(string name, params MapCalloutBox[] boxes)
    {
        return new MapCalloutPayload { name = name, boxes = boxes.ToList() };
    }

    private static readonly List<MapCalloutPayload> Map = new()
    {
        Place("BombsiteA", Box(new[] { 0f, 0f, 0f }, new[] { 1000f, 1000f, 200f })),
        Place("Goose", Box(new[] { 100f, 100f, 0f }, new[] { 300f, 300f, 200f })),
        Place("Ramp", Box(new[] { 2000f, 0f, 0f }, new[] { 2400f, 400f, 200f })),
    };

    private static readonly List<MapCalloutPayload> Stacked = new()
    {
        Place("Upper", Box(new[] { 0f, 0f, 100f }, new[] { 500f, 500f, 300f })),
        Place("Lower", Box(new[] { 0f, 0f, -400f }, new[] { 500f, 500f, -100f })),
    };

    [Fact]
    public void NamesThePlaceAPointStandsIn()
    {
        Assert.Equal("BombsiteA", CalloutLookup.Resolve(new Vec3(800, 800, 50), Map));
    }

    // The specific name is the one a player would say.
    [Fact]
    public void PrefersTheSmallerOfTwoNestedVolumes()
    {
        Assert.Equal("Goose", CalloutLookup.Resolve(new Vec3(200, 200, 50), Map));
    }

    [Fact]
    public void NamesThePlaceBeneathAPointRestingAboveIt()
    {
        Assert.Equal("BombsiteA", CalloutLookup.Resolve(new Vec3(800, 800, 900), Map));
    }

    // Two places at the same XY on different levels is the Nuke/Vertigo case.
    [Theory]
    [InlineData(200f, "Upper")]
    [InlineData(-200f, "Lower")]
    public void UsesZToSeparateStackedPlaces(float z, string expected)
    {
        Assert.Equal(expected, CalloutLookup.Resolve(new Vec3(250, 250, z), Stacked));
    }

    [Fact]
    public void SnapsToANearbyPlaceOutsideEveryVolume()
    {
        Assert.Equal("BombsiteA", CalloutLookup.Resolve(new Vec3(1100, 500, 50), Map));
    }

    [Fact]
    public void SaysNothingWhenTheNearestPlaceIsTooFarToMeanAnything()
    {
        Assert.Null(CalloutLookup.Resolve(new Vec3(9000, 9000, 50), Map));
    }

    [Fact]
    public void SaysNothingWhenTheMapHasNoCallouts()
    {
        Assert.Null(CalloutLookup.Resolve(new Vec3(0, 0, 0), new List<MapCalloutPayload>()));
        Assert.Null(CalloutLookup.Resolve(new Vec3(0, 0, 0), null));
    }

    [Theory]
    [InlineData("BombsiteA", "A Site")]
    [InlineData("BombsiteB", "B Site")]
    [InlineData("TSpawn", "T Spawn")]
    [InlineData("CTSpawn", "CT Spawn")]
    [InlineData("Catwalk", "Catwalk")]
    [InlineData("LongDoors", "Long Doors")]
    [InlineData("back_alley", "back alley")]
    [InlineData("TopofMid", "Top of Mid")]
    [InlineData("BackofA", "Back of A")]
    [InlineData("Roof", "Roof")]
    [InlineData("", "")]
    public void HumanisesAPlaceName(string raw, string expected)
    {
        Assert.Equal(expected, CalloutLookup.Humanize(raw));
    }

    [Fact]
    public void ResolvesAndHumanisesInOneStep()
    {
        Assert.Equal("A Site", CalloutLookup.ResolveLabel(new Vec3(800, 800, 50), Map));
        Assert.Equal(string.Empty, CalloutLookup.ResolveLabel(new Vec3(9000, 9000, 50), Map));
    }

    // A place is legitimately several disjoint volumes, and either half of it
    // has to answer with the same name.
    [Fact]
    public void ReadsEveryVolumeOfAMultiBoxPlace()
    {
        var banana = new List<MapCalloutPayload>
        {
            Place(
                "Banana",
                Box(new[] { 0f, 0f, 0f }, new[] { 200f, 200f, 100f }),
                Box(new[] { 900f, 900f, 0f }, new[] { 1100f, 1100f, 100f })
            ),
        };

        Assert.Equal("Banana", CalloutLookup.Resolve(new Vec3(100, 100, 50), banana));
        Assert.Equal("Banana", CalloutLookup.Resolve(new Vec3(1000, 1000, 50), banana));
    }
}
