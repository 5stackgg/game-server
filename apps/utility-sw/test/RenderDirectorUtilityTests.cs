using System.Text.RegularExpressions;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Xunit;

// game-streamer greps these lines out of the render client's console.log, so
// their shape is pinned here rather than left to the formatter.
public class RenderDirectorUtilityTests
{
    [Fact]
    public void ALineIsThePrefixTheEventAndKeyValuePairs()
    {
        string line = RenderDirectorUtility.Line(
            "detonated",
            ("lineup", "1afc5d54-805e-482b-a9a1-351f911758ca"),
            ("x", -1234.5f),
            ("ok", true)
        );

        Assert.Equal(
            "[5stack-render] detonated lineup=1afc5d54-805e-482b-a9a1-351f911758ca x=-1234.50 ok=1",
            line
        );
    }

    [Fact]
    public void AValueNeverBreaksTheLineApart()
    {
        string line = RenderDirectorUtility.Line(
            "error",
            ("reason", "no lineup\nhere"),
            ("detail", null),
            ("blank", "  ")
        );

        Assert.Matches(new Regex(@"^\S+ error reason=no_lineup_here detail=- blank=-$"), line);
    }

    [Fact]
    public void LookAtAndForwardAgree()
    {
        var from = new Vec3(10f, 20f, 30f);
        var to = new Vec3(110f, -80f, 130f);

        (float pitch, float yaw) = RenderDirectorUtility.LookAt(from, to);
        Vec3 forward = RenderDirectorUtility.Forward(pitch, yaw);
        Vec3 expected = (to - from).Normalized();

        Assert.Equal(expected.x, forward.x, 3);
        Assert.Equal(expected.y, forward.y, 3);
        Assert.Equal(expected.z, forward.z, 3);
        Assert.True(pitch < 0f, "looking up is a negative pitch in Source");
    }

    [Theory]
    [InlineData(179f, -179f, -2f)]
    [InlineData(-179f, 179f, 2f)]
    [InlineData(10f, 350f, 20f)]
    [InlineData(0.1f, 0f, 0.1f)]
    public void AngleDeltaTakesTheShortWayRound(float a, float b, float expected)
    {
        Assert.Equal(expected, RenderDirectorUtility.AngleDelta(a, b), 3);
    }

    [Fact]
    public void AimMatchesAcrossTheYawSeam()
    {
        Assert.True(RenderDirectorUtility.AimMatches(-30f, 179.9f, -30f, -179.95f));
        Assert.False(RenderDirectorUtility.AimMatches(-30f, 90f, -31f, 90f));
    }

    [Fact]
    public void TheStanceShotLooksAtTheSpotFromBehindAndAbove()
    {
        var feet = new Vec3(100f, 200f, 0f);

        (Vec3 eye, Vec3 lookAt) = RenderDirectorUtility.StanceShot(feet, 90f);

        Assert.True(eye.z > feet.z + RenderDirectorUtility.StandingEyeHeight);
        Assert.True(eye.y < feet.y, "behind a player facing +y is at lower y");
        Assert.True(lookAt.y > feet.y, "the view leans into the throw direction");
    }

    [Fact]
    public void TheChaseCameraTrailsTheGrenade()
    {
        var projectile = new Vec3(0f, 0f, 100f);
        Vec3 direction = RenderDirectorUtility.ChaseDirection(
            new Vec3(800f, 0f, 400f),
            new Vec3(0f, 1f, 0f)
        );

        Vec3 eye = RenderDirectorUtility.ChaseEye(projectile, direction);

        Assert.True(eye.x < projectile.x, "behind the travel direction");
        Assert.True(eye.z > projectile.z, "a little above it");
    }

    [Fact]
    public void ASettlingGrenadeKeepsTheLastBearing()
    {
        var previous = new Vec3(0f, 1f, 0f);

        Vec3 direction = RenderDirectorUtility.ChaseDirection(new Vec3(5f, -3f, 0f), previous);

        Assert.Equal(previous.x, direction.x);
        Assert.Equal(previous.y, direction.y);
    }

    [Fact]
    public void TheBloomShotFacesTheCloudFromTheThrowersSide()
    {
        var landing = new Vec3(0f, 0f, 0f);
        var stance = new Vec3(-1000f, 0f, 0f);

        (Vec3 eye, Vec3 lookAt) = RenderDirectorUtility.BloomShot(
            landing,
            stance,
            new Vec3(1f, 0f, 0f)
        );

        Assert.True(eye.x < 0f);
        Assert.Equal(RenderDirectorUtility.BloomHeight, eye.z, 3);
        Assert.Equal(RenderDirectorUtility.BloomLookHeight, lookAt.z, 3);
    }

    [Fact]
    public void TheBloomShotFallsBackToTheFlightWhenStanceIsOverhead()
    {
        var landing = new Vec3(50f, 50f, 0f);

        (Vec3 eye, _) = RenderDirectorUtility.BloomShot(landing, landing, new Vec3(0f, 1f, 0f));

        Assert.True(eye.y < landing.y, "opposite the way it was flying");
    }

    [Fact]
    public void ApproachIsHalfwayAfterOneHalfLife()
    {
        Vec3 halfway = RenderDirectorUtility.Approach(
            new Vec3(0f, 0f, 0f),
            new Vec3(100f, 0f, 0f),
            0.2f,
            0.2f
        );

        Assert.Equal(50f, halfway.x, 3);
    }

    [Fact]
    public void ApproachDoesNotDependOnTheTickRate()
    {
        var start = new Vec3(0f, 0f, 0f);
        var target = new Vec3(100f, 0f, 0f);

        Vec3 once = RenderDirectorUtility.Approach(start, target, 0.5f, 0.15f);

        Vec3 stepped = start;

        for (int tick = 0; tick < 32; tick++)
        {
            stepped = RenderDirectorUtility.Approach(stepped, target, 0.5f / 32f, 0.15f);
        }

        Assert.Equal(once.x, stepped.x, 2);
    }

    [Fact]
    public void AWallPullsTheCameraIn()
    {
        var subject = new Vec3(0f, 0f, 0f);
        var eye = new Vec3(100f, 0f, 0f);

        Vec3 clear = RenderDirectorUtility.Unobstructed(subject, eye, null, 8f);
        Vec3 blocked = RenderDirectorUtility.Unobstructed(subject, eye, new Vec3(40f, 0f, 0f), 8f);

        Assert.Equal(100f, clear.x, 3);
        Assert.Equal(32f, blocked.x, 3);
    }

    [Fact]
    public void OnlyTheSameKindOfGrenadeStandsInForTheLineup()
    {
        Assert.True(RenderDirectorUtility.SameUtility("Smoke", "smoke"));
        Assert.False(RenderDirectorUtility.SameUtility("Flash", "Smoke"));
        Assert.False(RenderDirectorUtility.SameUtility(null, "Smoke"));
    }

    [Fact]
    public void TheClockTheLinesCarryIsMillisecondsOfGameTicks()
    {
        Assert.Equal(1000, RenderDirectorUtility.Milliseconds(RenderDirectorUtility.TickRate));
        Assert.Equal(16, RenderDirectorUtility.Milliseconds(1));
    }

    [Fact]
    public void ThePodsOwnThrowClockLandsAtTheThrowBeat()
    {
        Assert.Equal(
            RenderDirectorUtility.StanceSeconds
                + RenderDirectorUtility.AimSeconds
                + RenderDirectorUtility.AimCloseSeconds,
            RenderDirectorUtility.ActAtSeconds,
            3
        );
        Assert.True(RenderDirectorUtility.ThrowTimeoutSeconds > 2f);
    }

    [Fact]
    public void ASmokeIsHeldLongerThanAFlash()
    {
        Assert.True(
            RenderDirectorUtility.BloomHoldSeconds("Smoke")
                > RenderDirectorUtility.BloomHoldSeconds("Flash")
        );
        Assert.True(
            RenderDirectorUtility.LandingStillSeconds("Smoke")
                < RenderDirectorUtility.BloomHoldSeconds("Smoke")
        );
    }
}
