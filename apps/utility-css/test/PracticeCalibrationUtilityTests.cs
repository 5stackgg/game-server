using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Xunit;

// The gate. Every test here is about refusing rather than solving: the failure
// this exists to prevent is a solve that ran anyway and handed back lineups
// that land somewhere plausible and cannot be thrown.
public class PracticeCalibrationUtilityTests
{
    private static readonly Vec3 Eye = new Vec3(100f, 200f, 64f);
    private static readonly Vec3 Landing = new Vec3(900f, 1200f, 0f);

    // A throw as a perfect engine would have recorded it: the seed is exactly
    // what the launch model predicts.
    private static LineupRecord Sample(
        float pitch = -15f,
        float yaw = 40f,
        float strength = 1f,
        int bounces = 0,
        string id = "sample"
    )
    {
        LaunchSeed seed = PracticeLaunchUtility.Seed(
            Eye,
            pitch,
            yaw,
            strength,
            new Vec3(0f, 0f, 0f)
        );

        return new LineupRecord
        {
            client_id = id,
            utility_type = "Smoke",
            bounces = bounces,
            release = new ThrowSnapshot
            {
                feet_position = new Vec3(Eye.x, Eye.y, 0f),
                eye_position = Eye,
                pitch = pitch,
                yaw = yaw,
                on_ground = true,
                speed = 0f,
                throw_strength_raw = strength,
            },
            initial_position = seed.position,
            initial_velocity = seed.velocity,
            detonation_position = Landing,
        };
    }

    [Fact]
    public void APerfectSampleClearsTheLaunchModel()
    {
        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { Sample() }
        );

        Assert.True(PracticeCalibrationUtility.LaunchModelPassed(report));
        Assert.Single(report.launch_checks);
        Assert.True(report.launch_checks[0].passed);
        Assert.Equal(1f, report.CorrectionFor(nameof(eThrowStrength.Full)), 3);
    }

    // Passing the launch model is not permission to solve. Only a live seed
    // replay grants that, and it has not happened yet.
    [Fact]
    public void ClearingTheLaunchModelIsNotReady()
    {
        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { Sample() }
        );

        Assert.False(report.CanSolve());
        Assert.Equal(nameof(eCalibrationStatus.Unknown), report.status);
    }

    [Fact]
    public void RefusesWhenTheThrowDirectionIsWrong()
    {
        LineupRecord sample = Sample();
        LaunchSeed skewed = PracticeLaunchUtility.Seed(
            Eye,
            sample.release.pitch - 4f,
            sample.release.yaw,
            1f,
            new Vec3(0f, 0f, 0f)
        );
        sample.initial_velocity = skewed.velocity;

        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { sample }
        );

        Assert.Equal(nameof(eCalibrationStatus.LaunchModelMismatch), report.status);
        Assert.Contains("pitch remap", report.message);
        Assert.False(report.CanSolve());
    }

    [Fact]
    public void RefusesWhenTheGrenadeSpawnsSomewhereElse()
    {
        LineupRecord sample = Sample();
        sample.initial_position = sample.initial_position + new Vec3(0f, 0f, 20f);

        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { sample }
        );

        Assert.Equal(nameof(eCalibrationStatus.LaunchModelMismatch), report.status);
        Assert.Contains("forward offset", report.message);
    }

    [Fact]
    public void RefusesWhenTheSpeedFormulaIsWrong()
    {
        LineupRecord sample = Sample();
        sample.initial_velocity = sample.initial_velocity * 3f;

        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { sample }
        );

        Assert.Equal(nameof(eCalibrationStatus.LaunchModelMismatch), report.status);
        Assert.Contains("speed formula", report.message);
    }

    // A constant being a few percent out is absorbed rather than refused: the
    // measured ratio is carried into every throw the solver makes.
    [Fact]
    public void CarriesASmallSpeedErrorForwardAsACorrection()
    {
        LineupRecord sample = Sample();
        sample.initial_velocity = sample.initial_velocity * 1.08f;

        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { sample }
        );

        Assert.True(PracticeCalibrationUtility.LaunchModelPassed(report));
        Assert.Equal(1.08f, report.CorrectionFor(nameof(eThrowStrength.Full)), 3);
    }

    // A throw made on the move is measured a tick away from where the engine
    // read it, so it fails the model for a reason that is not the model.
    [Fact]
    public void OnlyStandingThrowsAreUsable()
    {
        LineupRecord running = Sample();
        running.release.speed = 220f;

        LineupRecord jumping = Sample();
        jumping.release.jump_throw = true;

        LineupRecord airborne = Sample();
        airborne.release.on_ground = false;

        Assert.False(PracticeCalibrationUtility.IsUsableSample(running));
        Assert.False(PracticeCalibrationUtility.IsUsableSample(jumping));
        Assert.False(PracticeCalibrationUtility.IsUsableSample(airborne));
        Assert.True(PracticeCalibrationUtility.IsUsableSample(Sample()));
    }

    // A throw whose release edge was missed has a zeroed snapshot; comparing
    // the model against it would compare it against nothing.
    [Fact]
    public void ASnapshotlessThrowIsNotASample()
    {
        LineupRecord sample = Sample();
        sample.release = new ThrowSnapshot { on_ground = true };

        Assert.False(PracticeCalibrationUtility.IsUsableSample(sample));
    }

    [Fact]
    public void NothingToCalibrateAgainstIsSaidPlainly()
    {
        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new List<LineupRecord>()
        );

        Assert.Equal(nameof(eCalibrationStatus.NoSample), report.status);
        Assert.Contains("throw one grenade", report.message);
        Assert.False(report.CanSolve());
    }

    [Fact]
    public void OnlyMeasuredStrengthsBecomeSolvable()
    {
        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { Sample(strength: 1f, id: "a"), Sample(strength: 0.5f, id: "b") }
        );

        PracticeCalibrationUtility.WithSeedReplay(
            report,
            Sample(),
            Landing + new Vec3(2f, 0f, 0f)
        );

        Assert.Equal(
            new[] { nameof(eThrowStrength.Full), nameof(eThrowStrength.Half) },
            report.SolvableStrengths()
        );
        Assert.DoesNotContain(nameof(eThrowStrength.Drop), report.SolvableStrengths());
    }

    [Fact]
    public void AReproducedLandingOpensTheGate()
    {
        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { Sample() }
        );

        PracticeCalibrationUtility.WithSeedReplay(
            report,
            Sample(),
            Landing + new Vec3(3f, 4f, 0f)
        );

        Assert.Equal(nameof(eCalibrationStatus.Ready), report.status);
        Assert.True(report.CanSolve());
        Assert.Equal(5f, report.seed_replay_error, 3);
    }

    [Fact]
    public void AMissedReproductionShutsIt()
    {
        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { Sample() }
        );

        PracticeCalibrationUtility.WithSeedReplay(
            report,
            Sample(),
            Landing + new Vec3(0f, 400f, 0f)
        );

        Assert.Equal(nameof(eCalibrationStatus.SeedReplayMismatch), report.status);
        Assert.False(report.CanSolve());
        Assert.Contains("does not reproduce a seeded throw", report.message);
    }

    [Fact]
    public void AGrenadeThatNeverLandedIsNotAPass()
    {
        CalibrationReport report = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { Sample() }
        );

        PracticeCalibrationUtility.WithSeedReplay(report, Sample(), null);

        Assert.Equal(nameof(eCalibrationStatus.SeedReplayTimedOut), report.status);
        Assert.False(report.CanSolve());
    }

    // The tolerance is the whole claim. A throw just inside it passes and one
    // just outside does not, so a change to the constant is a change to the
    // claim rather than a quiet loosening.
    [Fact]
    public void TheToleranceIsTheClaim()
    {
        CalibrationReport inside = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { Sample() }
        );
        PracticeCalibrationUtility.WithSeedReplay(
            inside,
            Sample(),
            Landing
                + new Vec3(PracticeCalibrationUtility.SeedReplayTolerance - 0.5f, 0f, 0f)
        );

        CalibrationReport outside = PracticeCalibrationUtility.CheckLaunchModel(
            "de_mirage",
            new[] { Sample() }
        );
        PracticeCalibrationUtility.WithSeedReplay(
            outside,
            Sample(),
            Landing
                + new Vec3(PracticeCalibrationUtility.SeedReplayTolerance + 0.5f, 0f, 0f)
        );

        Assert.True(inside.CanSolve());
        Assert.False(outside.CanSolve());
    }

    // A grenade that clipped three corners tests the collision mesh as much as
    // the premise, so it is the last throw to reach for.
    [Fact]
    public void TheCleanestThrowIsReplayed()
    {
        LineupRecord bouncy = Sample(bounces: 5, id: "bouncy");
        LineupRecord clean = Sample(bounces: 0, id: "clean");

        LineupRecord? picked = PracticeCalibrationUtility.PickReplaySample(
            new[] { bouncy, clean }
        );

        Assert.Equal("clean", picked?.client_id);
    }

    [Fact]
    public void PickingAReplayNeedsAUsableThrow()
    {
        LineupRecord moving = Sample();
        moving.release.speed = 250f;

        Assert.Null(PracticeCalibrationUtility.PickReplaySample(new[] { moving }));
    }

    [Fact]
    public void SamplesAreCappedAndNewestFirst()
    {
        var pool = new List<LineupRecord>();

        for (int index = 0; index < 20; index++)
        {
            pool.Add(Sample(id: $"throw-{index}"));
        }

        List<LineupRecord> samples = PracticeCalibrationUtility.Samples(pool);

        Assert.Equal(PracticeCalibrationUtility.MaxSamples, samples.Count);
        Assert.Equal("throw-19", samples[0].client_id);
    }

    [Fact]
    public void AnUnsupportedRuntimeIsItsOwnAnswer()
    {
        CalibrationReport report = PracticeCalibrationUtility.Unsupported("de_nuke", "no emit api");

        Assert.Equal(nameof(eCalibrationStatus.Unsupported), report.status);
        Assert.False(report.CanSolve());
        Assert.Equal("de_nuke", report.map);
    }
}
