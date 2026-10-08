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
                + RenderDirectorUtility.GlideSeconds
                + RenderDirectorUtility.StanceEyesSeconds
                + RenderDirectorUtility.TiltSeconds
                + RenderDirectorUtility.AimSeconds
                + RenderDirectorUtility.AimCloseSeconds,
            RenderDirectorUtility.ActAtSeconds,
            3
        );
        Assert.True(
            RenderDirectorUtility.ActAtSeconds <= 6.0f,
            "the pod throws on its own clock at 6.3s if act is late"
        );
        Assert.True(RenderDirectorUtility.ThrowTimeoutSeconds > 2f);
    }

    [Fact]
    public void TheGlideStartsOnTheStanceCameraAndEndsOnTheAim()
    {
        var stanceEye = new Vec3(0f, 0f, 200f);
        var stanceLook = new Vec3(100f, 0f, 40f);
        var head = new Vec3(300f, 300f, 64f);

        (Vec3 startEye, Vec3 startLook) = RenderDirectorUtility.GlideShot(
            stanceEye, stanceLook, head, -30f, 45f, 0f);
        (Vec3 endEye, Vec3 endLook) = RenderDirectorUtility.GlideShot(
            stanceEye, stanceLook, head, -30f, 45f, 1f);

        Assert.Equal(stanceEye.z, startEye.z, 3);
        Assert.Equal(stanceLook.x, startLook.x, 3);

        (float pitch, float yaw) = RenderDirectorUtility.LookAt(endEye, endLook);
        Assert.Equal(-30f, pitch, 1);
        Assert.Equal(45f, yaw, 1);
        Assert.True((endEye - head).Length() <= RenderDirectorUtility.GlideEndAhead + 0.01f);
    }

    [Fact]
    public void TheTiltRisesFromTheFeetOntoTheAim()
    {
        var head = new Vec3(0f, 0f, 64f);

        (Vec3 startEye, Vec3 startLook) = RenderDirectorUtility.TiltShot(head, -30f, 90f, 0f);
        (Vec3 endEye, Vec3 endLook) = RenderDirectorUtility.TiltShot(head, -30f, 90f, 1f);

        Assert.Equal(
            RenderDirectorUtility.StanceEyesPitch,
            RenderDirectorUtility.LookAt(startEye, startLook).pitch,
            1
        );
        Assert.Equal(-30f, RenderDirectorUtility.LookAt(endEye, endLook).pitch, 1);
        Assert.Equal(90f, RenderDirectorUtility.LookAt(endEye, endLook).yaw, 1);
    }

    [Fact]
    public void ALineupWithARunUpIsStagedWhereTheRunUpStarts()
    {
        var stance = new Vec3(10f, 20f, 0f);
        var runUp = new List<ApproachSample>
        {
            new ApproachSample { t = -400, pos = new Vec3(-70f, 20f, 0f) },
            new ApproachSample { t = 0, pos = new Vec3(10f, 20f, 0f) },
        };

        Assert.Equal(-70f, RenderDirectorUtility.StageAt(stance, runUp).x);
        Assert.Equal(10f, RenderDirectorUtility.StageAt(stance, null).x);
        Assert.Equal(10f, RenderDirectorUtility.StageAt(stance, runUp.Take(1).ToList()).x);
    }

    [Fact]
    public void TheBloomIsFramedFromFarBackOnTheThrowersSideFirst()
    {
        var landing = new Vec3(0f, 0f, 0f);
        var stance = new Vec3(-2000f, 0f, 0f);

        var candidates = RenderDirectorUtility
            .BloomCandidates(landing, stance, new Vec3(1f, 0f, 0f))
            .ToList();

        (Vec3 firstEye, Vec3 lookAt) = candidates[0];
        Assert.True(firstEye.x < -600f, "first try is far back toward the thrower");
        Assert.Equal(RenderDirectorUtility.BloomLookHeight, lookAt.z, 3);
        Assert.True(
            (candidates[^1].eye - landing).LengthXY() < (firstEye - landing).LengthXY(),
            "closer vantages are only tried after every far one"
        );
        Assert.Equal(24, candidates.Count);
    }

    [Fact]
    public void TheThirdPersonCameraSitsBehindAndAboveTheThrower()
    {
        var feet = new Vec3(0f, 0f, 0f);

        (Vec3 eye, Vec3 look) = RenderDirectorUtility.ThirdPersonShot(feet, 0f);

        Assert.True(eye.x < 0f, "behind a thrower facing +x");
        Assert.True(eye.z > RenderDirectorUtility.StandingEyeHeight, "above their head");
        Assert.True(look.x > eye.x);
    }

    [Fact]
    public void EaseHoldsTheEndsStill()
    {
        Assert.Equal(0f, RenderDirectorUtility.Ease(-1f));
        Assert.Equal(1f, RenderDirectorUtility.Ease(2f));
        Assert.Equal(0.5f, RenderDirectorUtility.Ease(0.5f), 3);
    }

    [Fact]
    public void TheChaseTurnsTowardTheFlightWithoutSnapping()
    {
        var current = new Vec3(1f, 0f, 0f);
        var target = new Vec3(0f, 1f, 0f);

        Vec3 turned = RenderDirectorUtility.Turn(current, target, 1f / 64f);

        Assert.Equal(1f, turned.Length(), 3);
        Assert.True(turned.x > 0.5f, "one tick only starts the turn");
        Assert.True(turned.y > 0f);
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
