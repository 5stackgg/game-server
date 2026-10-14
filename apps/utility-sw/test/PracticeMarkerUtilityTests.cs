using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

namespace FiveStack.Tests;

public class PracticeMarkerUtilityTests
{
    private const uint Forward = 1 << 3;
    private const uint Back = 1 << 4;
    private const uint Jump = 1 << 1;
    private const uint Walk = 1 << 16;

    private static LineupRecord At(string id, float x, float y, float z, string type = "Smoke")
    {
        return new LineupRecord
        {
            client_id = id,
            utility_type = type,
            release = new ThrowSnapshot { feet_position = new Vec3(x, y, z) },
        };
    }

    private static List<ApproachSample> Approach(params (int t, uint buttons, bool grounded)[] ticks)
    {
        return ticks
            .Select(tick => new ApproachSample
            {
                t = tick.t,
                buttons = tick.buttons,
                on_ground = tick.grounded,
                pos = new Vec3(tick.t, 0f, 0f),
            })
            .ToList();
    }

    [Fact]
    public void TheFinderGoesOnceTheCrosshairIsClose()
    {
        Assert.Equal(eAimStage.Find, PracticeMarkerUtility.AimStage(eAimStage.Find, 8.5f));
        Assert.Equal(eAimStage.Near, PracticeMarkerUtility.AimStage(eAimStage.Find, 7.9f));
        Assert.Equal(eAimStage.On, PracticeMarkerUtility.AimStage(eAimStage.Near, 1.4f));
    }

    // Sat on a boundary, the crosshair wobbles either side of it. Without the
    // gap the marker would be rebuilt on every wobble.
    [Fact]
    public void AStageDoesNotFlickerOnItsBoundary()
    {
        Assert.Equal(eAimStage.Near, PracticeMarkerUtility.AimStage(eAimStage.Near, 9f));
        Assert.Equal(eAimStage.Find, PracticeMarkerUtility.AimStage(eAimStage.Near, 9.6f));
        Assert.Equal(eAimStage.On, PracticeMarkerUtility.AimStage(eAimStage.On, 2f));
        Assert.Equal(eAimStage.Near, PracticeMarkerUtility.AimStage(eAimStage.On, 2.3f));
    }

    [Theory]
    [InlineData(20f, eAimStage.Find)]
    [InlineData(5f, eAimStage.Near)]
    [InlineData(0.2f, eAimStage.On)]
    public void AMarkerIsBornInTheStageItsErrorCalls(float error, eAimStage expected)
    {
        Assert.Equal(expected, PracticeMarkerUtility.AimStage(error));
    }

    [Fact]
    public void SubtendIsTheSizeThatCoversAnAngleAtADistance()
    {
        Assert.Equal(100f, PracticeMarkerUtility.Subtend(100f, 45f), 3);
        Assert.Equal(0f, PracticeMarkerUtility.Subtend(1900f, 0f), 3);
    }

    [Fact]
    public void TheRingClosesAndHoldsItsRadius()
    {
        List<MarkerSegment> ring = PracticeMarkerUtility.Ring(8f, 10);

        Assert.Equal(10, ring.Count);
        Assert.Equal(ring[0].Along1, ring[^1].Along2, 3);
        Assert.Equal(ring[0].Across1, ring[^1].Across2, 3);

        foreach (MarkerSegment segment in ring)
        {
            Assert.Equal(8f, MathF.Sqrt(segment.Along1 * segment.Along1 + segment.Across1 * segment.Across1), 3);
        }
    }

    [Fact]
    public void AShortRunUpGetsNoLane()
    {
        Assert.Empty(PracticeMarkerUtility.Lane(30f));
    }

    [Fact]
    public void ALaneHasTwoRailsAnEndBarAndChevrons()
    {
        List<MarkerSegment> lane = PracticeMarkerUtility.Lane(160f);

        Assert.Equal(3 + (2 * 4), lane.Count);
        Assert.Contains(lane, segment => segment.Along1 == 160f && segment.Along2 == 160f);
    }

    [Fact]
    public void ThrowsFromOnePlaceAreOneSpot()
    {
        List<LineupSpot> spots = PracticeMarkerUtility.Spots(
            new[]
            {
                At("a", 0f, 0f, 0f),
                At("b", 20f, 10f, 4f, "Flash"),
                At("c", 400f, 0f, 0f),
                At("d", 5f, 5f, 200f),
            },
            40f,
            72f
        );

        Assert.Equal(3, spots.Count);
        Assert.Equal(new[] { "a", "b" }, spots[0].Lineups.Select(lineup => lineup.client_id));
        Assert.Equal(new[] { "Smoke", "Flash" }, spots[0].Types);
    }

    [Fact]
    public void ATargetToTheLeftHasAPositiveBearing()
    {
        var from = new Vec3(0f, 0f, 0f);

        Assert.Equal(90f, PracticeMarkerUtility.Bearing(0f, from, new Vec3(0f, 100f, 0f)), 3);
        Assert.Equal(-90f, PracticeMarkerUtility.Bearing(0f, from, new Vec3(0f, -100f, 0f)), 3);
        Assert.Equal(0f, PracticeMarkerUtility.Bearing(90f, from, new Vec3(0f, 100f, 0f)), 3);
        Assert.Equal(2f, PracticeMarkerUtility.Bearing(359f, from, new Vec3(100f, 1.7455f, 0f)), 1);
    }

    [Fact]
    public void TheCompassRunsLeftToRight()
    {
        Assert.Equal(32, PracticeMarkerUtility.CompassColumn(0f, 62f, 65));
        Assert.Equal(0, PracticeMarkerUtility.CompassColumn(62f, 62f, 65));
        Assert.Equal(64, PracticeMarkerUtility.CompassColumn(-62f, 62f, 65));
        Assert.Equal(-1, PracticeMarkerUtility.CompassColumn(63f, 62f, 65));
        Assert.Equal(-1, PracticeMarkerUtility.CompassColumn(-170f, 62f, 65));
    }

    [Fact]
    public void ARunUpReadsAsTheKeysHeldForMostOfIt()
    {
        RunUp? runUp = PracticeMarkerUtility.RunUpOf(
            Approach(
                (-800, 0, true),
                (-600, Forward | Walk, true),
                (-400, Forward | Walk, true),
                (-200, Forward | Walk, true),
                (0, Forward | Walk, true)
            )
        );

        Assert.NotNull(runUp);
        Assert.Equal(new[] { "W" }, runUp!.Keys);
        Assert.True(runUp.Walk);
        Assert.False(runUp.Jump);
        Assert.Equal(600, runUp.Milliseconds);
        Assert.Equal(-600f, runUp.From.x);
        Assert.Equal("Hold Shift + W 0.6s first", PracticeMarkerUtility.RunUpLine(runUp));
    }

    [Fact]
    public void OppositeKeysCancelToNoRunUp()
    {
        Assert.Null(
            PracticeMarkerUtility.RunUpOf(
                Approach((-200, Forward | Back, true), (-100, Forward | Back, true), (0, Forward | Back, true))
            )
        );
    }

    [Fact]
    public void AJumpJustBeforeTheReleaseIsAJumpThrow()
    {
        RunUp? runUp = PracticeMarkerUtility.RunUpOf(
            Approach((-600, Forward, true), (-300, Forward, true), (-100, Forward | Jump, true), (0, Forward, false))
        );

        Assert.NotNull(runUp);
        Assert.True(runUp!.Jump);
    }

    [Fact]
    public void StandingStillIsNotARunUp()
    {
        Assert.Null(PracticeMarkerUtility.RunUpOf(Approach((-100, 0, true), (0, 0, true))));
        Assert.Null(PracticeMarkerUtility.RunUpOf(null));
    }

    private static LineupRecord Aimed(string id, float yaw, float pitch)
    {
        return new LineupRecord
        {
            client_id = id,
            utility_type = "Smoke",
            release = new ThrowSnapshot { yaw = yaw, pitch = pitch },
        };
    }

    // Two throws off one spot aimed at the same point drew the lead's ring
    // with the other's inside it: one marker that looked broken.
    [Fact]
    public void AThrowAimedInsideTheLeadsRingGetsNoRingOfItsOwn()
    {
        var eye = new Vec3(0f, 0f, 0f);
        var aims = new List<Vec3>
        {
            new(1000f, 0f, 0f),
            new(1000f, 5f, 0f),
            new(1000f, 100f, 0f),
        };

        Assert.Equal(new[] { true, false, true }, PracticeMarkerUtility.Ringed(eye, aims, 0));
    }

    [Fact]
    public void WithNoLeadTheFirstOfTwoAimsAtOnePointKeepsTheRing()
    {
        var eye = new Vec3(0f, 0f, 0f);
        var aims = new List<Vec3> { new(1000f, 0f, 0f), new(1000f, 0f, 3f) };

        Assert.Equal(new[] { true, false }, PracticeMarkerUtility.Ringed(eye, aims, -1));
    }

    [Fact]
    public void AimsFarEnoughApartForTwoRingsBothGetOne()
    {
        var eye = new Vec3(0f, 0f, 0f);
        float side = PracticeMarkerUtility.Subtend(
            1000f,
            PracticeMarkerUtility.CrowdedDegrees + 0.1f
        );
        var aims = new List<Vec3> { new(1000f, 0f, 0f), new(1000f, side, 0f) };

        Assert.Equal(new[] { true, true }, PracticeMarkerUtility.Ringed(eye, aims, 0));
    }

    [Fact]
    public void ApartIsTheAngleBetweenTwoPointsFromTheEye()
    {
        var eye = new Vec3(0f, 0f, 0f);
        var ahead = new Vec3(10f, 0f, 0f);

        Assert.Equal(45f, PracticeMarkerUtility.Apart(eye, ahead, new Vec3(10f, 10f, 0f)), 3);
        Assert.Equal(0f, PracticeMarkerUtility.Apart(eye, ahead, new Vec3(500f, 0f, 0f)), 3);
    }

    // The crosshair between two throws aimed at one point picked whichever
    // was nearer by a hair, so the loaded throw flipped as the hand shook.
    [Fact]
    public void TheLoadedThrowStaysWhenAnotherIsAimedAtTheSamePoint()
    {
        LineupRecord loaded = Aimed("loaded", 90f, -10f);
        LineupRecord twin = Aimed("twin", 90.3f, -10f);

        Assert.Same(loaded, PracticeMarkerUtility.Chosen(twin, loaded));
    }

    [Fact]
    public void LookingAtAThrowAimedElsewhereChoosesIt()
    {
        LineupRecord loaded = Aimed("loaded", 90f, -10f);
        LineupRecord other = Aimed("other", 100f, -10f);

        Assert.Same(other, PracticeMarkerUtility.Chosen(other, loaded));
        Assert.Same(other, PracticeMarkerUtility.Chosen(other, null));
    }

    // A wall close to the stance drew lines wider than the ring's pieces are
    // long, and the ring came out as a blot.
    [Theory]
    [InlineData(30f)]
    [InlineData(2000f)]
    public void TheLinesAreThinnerThanTheRingsPiecesAreLong(float away)
    {
        float radius = PracticeMarkerUtility.Subtend(away, PracticeMarkerUtility.RingDegrees);
        float piece = 2f * MathF.PI * radius / PracticeMarkerUtility.RingSegments;
        float width = PracticeMarkerUtility.Subtend(away, PracticeMarkerUtility.LineDegrees);

        Assert.True(width < piece / 2f);
    }
}
