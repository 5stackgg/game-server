using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// The launch model is the only physics the solver contains. The recorded-throw
// tests at the bottom hold it to seeds the engine wrote on de_mirage; the rest
// pin its shape so a change to it is deliberate.
public class PracticeLaunchUtilityTests
{
    private static readonly Vec3 Eye = new Vec3(100f, 200f, 64f);

    [Fact]
    public void BendsTheAimDownAtTheHorizon()
    {
        Assert.Equal(-10f, PracticeLaunchUtility.RemapPitch(0f), 3);
    }

    [Fact]
    public void RemapRoundTrips()
    {
        foreach (float pitch in new[] { -80f, -45f, -10f, 0f, 12f, 60f })
        {
            float back = PracticeLaunchUtility.UnremapPitch(
                PracticeLaunchUtility.RemapPitch(pitch)
            );

            Assert.Equal(pitch, back, 3);
        }
    }

    [Fact]
    public void NormalizesAnUnwrappedPitch()
    {
        Assert.Equal(-30f, PracticeLaunchUtility.NormalizePitch(330f), 3);
        Assert.Equal(45f, PracticeLaunchUtility.NormalizePitch(405f), 3);
    }

    [Fact]
    public void ForwardFollowsTheSourceConvention()
    {
        Vec3 level = PracticeLaunchUtility.Forward(0f, 0f);
        Assert.Equal(1f, level.x, 4);
        Assert.Equal(0f, level.y, 4);
        Assert.Equal(0f, level.z, 4);

        // Positive pitch is looking down.
        Assert.True(PracticeLaunchUtility.Forward(45f, 0f).z < 0f);
        Assert.True(PracticeLaunchUtility.Forward(-45f, 0f).z > 0f);

        Vec3 quarter = PracticeLaunchUtility.Forward(0f, 90f);
        Assert.Equal(0f, quarter.x, 4);
        Assert.Equal(1f, quarter.y, 4);
    }

    // The grenade does not leave along the crosshair, and a solver that assumed
    // it did would report an aim a degree or two under every throw it found.
    [Fact]
    public void ThrowDirectionIsNotTheCrosshair()
    {
        Vec3 crosshair = PracticeLaunchUtility.Forward(0f, 0f);
        Vec3 thrown = PracticeLaunchUtility.ThrowDirection(0f, 0f);

        Assert.True(PracticeLaunchUtility.AngleBetween(crosshair, thrown) > 9f);
        Assert.True(thrown.z > 0f);
    }

    // Recorded throws from -67 to +15 degrees of throw pitch all leave at the
    // same speed: CS2 has none of CS:GO's fall-off towards the floor.
    [Fact]
    public void AFullThrowLeavesAtTheSameSpeedAtEveryPitch()
    {
        foreach (float pitch in new[] { -80f, -45f, 0f, 22f, 60f })
        {
            Assert.Equal(PracticeLaunchUtility.FullSpeed, PracticeLaunchUtility.BaseSpeed(pitch), 3);
        }
    }

    // The two halves of the range are squeezed differently, and the horizon is
    // where they meet.
    [Fact]
    public void RemapsLookingUpAndLookingDownDifferently()
    {
        Assert.Equal(-10f + (-45f * 80f / 90f), PracticeLaunchUtility.RemapPitch(-45f), 3);
        Assert.Equal(-10f + (45f * 100f / 90f), PracticeLaunchUtility.RemapPitch(45f), 3);
        Assert.Equal(-10f, PracticeLaunchUtility.RemapPitch(-0.0001f), 2);
    }

    [Fact]
    public void AWeakThrowSpawnsLower()
    {
        Vec3 none = new Vec3(0f, 0f, 0f);
        LaunchSeed full = PracticeLaunchUtility.Seed(Eye, -30f, 0f, 1f, none);
        LaunchSeed half = PracticeLaunchUtility.Seed(Eye, -30f, 0f, 0.5f, none);
        LaunchSeed drop = PracticeLaunchUtility.Seed(Eye, -30f, 0f, 0f, none);

        Assert.Equal(6f, full.position.z - half.position.z, 3);
        Assert.Equal(12f, full.position.z - drop.position.z, 3);
        Assert.Equal(0.65f * full.speed, half.speed, 2);
        Assert.Equal(0.3f * full.speed, drop.speed, 2);
    }

    [Fact]
    public void StrengthScaleIsMonotoneAndFullIsUnscaled()
    {
        Assert.Equal(1f, PracticeLaunchUtility.StrengthScale(1f), 4);
        Assert.Equal(PracticeLaunchUtility.MinStrengthScale, PracticeLaunchUtility.StrengthScale(0f), 4);
        Assert.True(
            PracticeLaunchUtility.StrengthScale(0.5f) > PracticeLaunchUtility.StrengthScale(0f)
        );
        Assert.True(
            PracticeLaunchUtility.StrengthScale(0.5f) < PracticeLaunchUtility.StrengthScale(1f)
        );
    }

    [Fact]
    public void MapsTheThreeReleasesAPlayerCanMake()
    {
        Assert.Equal(1f, PracticeLaunchUtility.RawStrength(eThrowStrength.Full));
        Assert.Equal(0.5f, PracticeLaunchUtility.RawStrength(eThrowStrength.Half));
        Assert.Equal(0f, PracticeLaunchUtility.RawStrength(eThrowStrength.Drop));
    }

    [Fact]
    public void SeedSpawnsAheadOfTheEyeAlongTheThrow()
    {
        LaunchSeed seed = PracticeLaunchUtility.Seed(
            Eye,
            -12f,
            35f,
            1f,
            new Vec3(0f, 0f, 0f)
        );

        Assert.Equal(
            PracticeLaunchUtility.ForwardOffset,
            (seed.position - Eye).Length(),
            2
        );
        // Acos of a float dot product near 1 is only good to a few hundredths.
        Assert.InRange(
            PracticeLaunchUtility.AngleBetween(seed.position - Eye, seed.velocity),
            0f,
            0.05f
        );
        Assert.Equal(seed.speed, seed.velocity.Length(), 2);
    }

    [Fact]
    public void SeedCarriesTheThrowersOwnVelocity()
    {
        var running = new Vec3(0f, 250f, 0f);

        LaunchSeed still = PracticeLaunchUtility.Seed(Eye, 0f, 0f, 1f, new Vec3(0f, 0f, 0f));
        LaunchSeed moving = PracticeLaunchUtility.Seed(Eye, 0f, 0f, 1f, running);

        Assert.Equal(
            running.y * PracticeLaunchUtility.PlayerVelocityScale,
            moving.velocity.y - still.velocity.y,
            2
        );
    }

    [Fact]
    public void SpeedCorrectionScalesTheRelease()
    {
        LaunchSeed plain = PracticeLaunchUtility.Seed(Eye, -10f, 0f, 1f, new Vec3(0f, 0f, 0f));
        LaunchSeed corrected = PracticeLaunchUtility.Seed(
            Eye,
            -10f,
            0f,
            1f,
            new Vec3(0f, 0f, 0f),
            1.25f
        );

        Assert.Equal(plain.speed * 1.25f, corrected.speed, 2);
    }

    [Fact]
    public void BearingPointsAtTheTarget()
    {
        Assert.Equal(
            90f,
            PracticeLaunchUtility.BearingTo(new Vec3(0f, 0f, 0f), new Vec3(0f, 500f, 0f)),
            3
        );
        Assert.Equal(
            0f,
            PracticeLaunchUtility.BearingTo(new Vec3(0f, 0f, 0f), new Vec3(500f, 0f, 200f)),
            3
        );
    }

    // Seeds the engine wrote for real throws on de_mirage, with what the
    // plugin knew at the release: the stance, the eye and the aim. The aim and
    // the strength alone have to give the velocity the engine wrote.
    private static void AssertSeed(
        LaunchSeed predicted,
        Vec3 position,
        Vec3 velocity,
        float positionTolerance = 0.1f
    )
    {
        Assert.InRange((predicted.velocity - velocity).Length(), 0f, 0.5f);
        Assert.InRange((predicted.position - position).Length(), 0f, positionTolerance);
    }

    [Fact]
    public void ReproducesStandingThrowsTheEngineRecorded()
    {
        // Top of Mid Smoke from T Spawn, a left click looking up.
        AssertSeed(
            PracticeLaunchUtility.Seed(
                new Vec3(1422.9536f, 36.457386f, -104f),
                -42.33331f,
                -160.5022f,
                1f,
                new Vec3(0f, 0f, 0f)
            ),
            new Vec3(1412.7892f, 32.858425f, -92.17914f),
            new Vec3(-428.81012f, -151.83116f, 498.69247f)
        );

        // B Site Cat Smoke from Back Alley.
        AssertSeed(
            PracticeLaunchUtility.Seed(
                new Vec3(-160.04878f, 887.93933f, -71.67128f),
                -45.843662f,
                -134.48611f,
                1f,
                new Vec3(0f, 0f, 0f)
            ),
            new Vec3(-167.14255f, 880.71716f, -59.28101f),
            new Vec3(-299.26822f, -304.68524f, 522.7145f)
        );
    }

    // A jump throw bind releases with the player rising and the eye raised, the
    // same on every throw (TechniqueProfile.JumpThrow).
    [Fact]
    public void ReproducesJumpThrowsTheEngineRecorded()
    {
        Vec3 lift = new Vec3(0f, 0f, TechniqueProfile.JumpThrowLift);

        // Jungle Smoke from Palace Alley, the throw the render aim fix was
        // checked against.
        AssertSeed(
            PracticeLaunchUtility.Seed(
                new Vec3(908.68005f, -1036.0312f, -251.96875f + TechniqueProfile.JumpThrowEye),
                -55.324947f,
                -161.02596f,
                1f,
                lift
            ),
            new Vec3(900.9274f, -1038.6968f, -148.32233f),
            new Vec3(-327.06262f, -112.45098f, 853.15137f)
        );

        // Middle Smoke from Top of Mid: both buttons, so 65% of the speed and
        // six units lower.
        AssertSeed(
            PracticeLaunchUtility.Seed(
                new Vec3(343.30295f, -621.6206f, -162.3318f + TechniqueProfile.JumpThrowEye),
                -8.508511f,
                -168.26045f,
                0.5f,
                lift
            ),
            new Vec3(328.36786f, -624.72424f, -73.59377f),
            new Vec3(-409.54797f, -85.108154f, 405.88336f)
        );
    }

    // Clear Smoke in Snipers Nest: a right click looking down, the other half
    // of the pitch remap and the weakest throw.
    [Fact]
    public void ReproducesADroppedThrowLookingDown()
    {
        LaunchSeed predicted = PracticeLaunchUtility.Seed(
            new Vec3(-1247.6334f, -924.0628f, -111.046875f),
            22.598167f,
            57.574562f,
            0f,
            new Vec3(0f, 0f, 221.9f)
        );

        Assert.InRange(
            (new Vec3(predicted.velocity.x, predicted.velocity.y, 0f)
                - new Vec3(104.82733f, 165.01947f, 0f)).Length(),
            0f,
            0.5f
        );
    }

    [Fact]
    public void PredictReadsTheReleaseSnapshot()
    {
        var release = new ThrowSnapshot
        {
            eye_position = Eye,
            pitch = -20f,
            yaw = 15f,
            throw_strength_raw = 0.5f,
            velocity = new Vec3(0f, 0f, 0f),
        };

        LaunchSeed predicted = PracticeLaunchUtility.Predict(release);
        LaunchSeed direct = PracticeLaunchUtility.Seed(
            Eye,
            -20f,
            15f,
            0.5f,
            new Vec3(0f, 0f, 0f)
        );

        Assert.Equal(direct.speed, predicted.speed, 3);
        Assert.Equal(direct.position.x, predicted.position.x, 3);
    }
}
