using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// The launch model is the only physics the solver contains, and none of it can
// be verified from here -- these tests pin the shape of the function so a
// change to it is deliberate. Whether the constants match CS2 is a question
// only a live server answers, which is what calibration is for.
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

    [Fact]
    public void SpeedSaturates()
    {
        Assert.Equal(
            PracticeLaunchUtility.MaxSpeed,
            PracticeLaunchUtility.BaseSpeed(-60f),
            3
        );
        Assert.True(PracticeLaunchUtility.BaseSpeed(0f) < PracticeLaunchUtility.MaxSpeed);
        Assert.True(PracticeLaunchUtility.BaseSpeed(45f) < PracticeLaunchUtility.BaseSpeed(0f));
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
        Assert.Equal(
            0f,
            PracticeLaunchUtility.AngleBetween(seed.position - Eye, seed.velocity),
            2
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
