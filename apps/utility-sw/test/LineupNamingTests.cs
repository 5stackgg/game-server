using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

// Pinned against UtilityCalloutsService.autoName in the api. The name this
// produces in game and the name the website shows for the same throw are the
// same string or the feature is worse than no name at all.
public class LineupNamingTests
{
    private static MapCalloutPayload Place(string name, float x, float y)
    {
        return new MapCalloutPayload
        {
            name = name,
            boxes = new List<MapCalloutBox>
            {
                new MapCalloutBox
                {
                    min = new[] { x - 100f, y - 100f, -100f },
                    max = new[] { x + 100f, y + 100f, 100f },
                },
            },
        };
    }

    private static List<MapCalloutPayload> Mirage()
    {
        return new List<MapCalloutPayload>
        {
            Place("Window", 0f, 0f),
            Place("TSpawn", 2000f, 0f),
            Place("BombsiteA", -2000f, 0f),
        };
    }

    [Fact]
    public void NamesBothEnds()
    {
        Assert.Equal(
            "Window Smoke from T Spawn",
            LineupNaming.Auto(
                nameof(eUtilityType.Smoke),
                new Vec3(2000f, 0f, 0f),
                new Vec3(0f, 0f, 0f),
                Mirage()
            )
        );
    }

    // Thrown from the place it lands in: "Window Smoke from Window" says nothing.
    [Fact]
    public void CollapsesWhenBothEndsAreTheSamePlace()
    {
        Assert.Equal(
            "Window Smoke",
            LineupNaming.Auto(
                nameof(eUtilityType.Smoke),
                new Vec3(0f, 0f, 0f),
                new Vec3(20f, 20f, 0f),
                Mirage()
            )
        );
    }

    [Fact]
    public void NamesTheDestinationAloneWhenTheOriginIsNowhere()
    {
        Assert.Equal(
            "Window Smoke",
            LineupNaming.Auto(
                nameof(eUtilityType.Smoke),
                new Vec3(50000f, 50000f, 0f),
                new Vec3(0f, 0f, 0f),
                Mirage()
            )
        );
    }

    [Fact]
    public void NamesTheOriginAloneWhenTheDestinationIsNowhere()
    {
        Assert.Equal(
            "Smoke from T Spawn",
            LineupNaming.Auto(
                nameof(eUtilityType.Smoke),
                new Vec3(2000f, 0f, 0f),
                new Vec3(50000f, 50000f, 0f),
                Mirage()
            )
        );
    }

    // Empty rather than a bad guess: the caller's own fallback is better than a
    // name that says nothing.
    [Fact]
    public void EmptyWhenTheMapCannotAnswer()
    {
        Assert.Equal(
            "",
            LineupNaming.Auto(
                nameof(eUtilityType.Smoke),
                new Vec3(50000f, 50000f, 0f),
                new Vec3(60000f, 60000f, 0f),
                Mirage()
            )
        );

        Assert.Equal(
            "",
            LineupNaming.Auto(nameof(eUtilityType.Smoke), new Vec3(), new Vec3(), null)
        );

        Assert.Equal(
            "",
            LineupNaming.Auto(
                nameof(eUtilityType.Smoke),
                new Vec3(),
                new Vec3(),
                new List<MapCalloutPayload>()
            )
        );
    }

    // The api's TYPE_LABELS: only HighExplosive differs from its enum name.
    [Theory]
    [InlineData(nameof(eUtilityType.Smoke), "Smoke")]
    [InlineData(nameof(eUtilityType.Flash), "Flash")]
    [InlineData(nameof(eUtilityType.Molotov), "Molotov")]
    [InlineData(nameof(eUtilityType.Decoy), "Decoy")]
    [InlineData(nameof(eUtilityType.HighExplosive), "HE")]
    public void UsesThePanelsTypeLabels(string utilityType, string expected)
    {
        Assert.Equal(expected, LineupNaming.TypeLabel(utilityType));
    }

    [Fact]
    public void HighExplosiveReadsAsHeInAFullName()
    {
        Assert.Equal(
            "Window HE from T Spawn",
            LineupNaming.Auto(
                nameof(eUtilityType.HighExplosive),
                new Vec3(2000f, 0f, 0f),
                new Vec3(0f, 0f, 0f),
                Mirage()
            )
        );
    }

    // Humanised through the shared lookup, so "BombsiteA" is "A Site" here and
    // on the website both.
    [Fact]
    public void PlaceNamesAreHumanised()
    {
        Assert.Equal(
            "A Site Smoke from T Spawn",
            LineupNaming.Auto(
                nameof(eUtilityType.Smoke),
                new Vec3(2000f, 0f, 0f),
                new Vec3(-2000f, 0f, 0f),
                Mirage()
            )
        );
    }
}
