using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// The gate the whole solver stands on.
//
// A solve fires hundreds of real grenades and reports back an aim. Two things
// have to be true for that to be worth anything, and neither has ever been
// observed on a live server:
//
//   1. handing the engine a recorded throw's seed reproduces that throw, so a
//      solved seed is a lineup somebody can replay;
//   2. PracticeLaunchUtility turns an aim into that seed the same way the game
//      does, so the aim a solve reports is the aim that produces the throw.
//
// Both are checked here against throws the engine itself recorded, before a
// single solver grenade is emitted. Getting this wrong in the other direction
// -- assuming and proceeding -- produces a library full of lineups that land
// somewhere plausible and cannot be thrown, which is worse than no solver.
//
// Check 2 is arithmetic over throws already in hand and costs nothing. Check 1
// needs one live grenade and is driven by the framework shell.
public static class PracticeCalibrationUtility
{
    // Only stationary throws are used. The launch model adds the thrower's own
    // velocity, and for a moving thrower the snapshot and the engine's read of
    // that velocity are a tick apart -- which shows up as model error that is
    // really sampling error. A solve always throws from a standstill, so
    // standing throws are also the exact regime being licensed.
    public const int MaxSamples = 8;

    // The projectile spawns a fixed offset from the eye. Three units covers
    // float noise on a 16 unit offset and nothing else; a wrong offset or a
    // wrong eye height misses by far more.
    public const float MaxPositionError = 3f;

    // The pitch remap is the part a player feels: aim reported a degree out is
    // a lineup that misses by metres at range. A correct remap reproduces the
    // direction to well under a tenth of a degree.
    public const float MaxDirectionError = 1.5f;

    // Release speed is allowed to be systematically off, because a constant
    // being slightly wrong is absorbed by carrying the measured ratio forward.
    // Outside this band the formula is not slightly wrong, it is wrong.
    public const float MinSpeedRatio = 0.5f;
    public const float MaxSpeedRatio = 1.5f;

    // How far the re-emitted grenade may land from where the original one did.
    //
    // If the premise holds this should be near zero: same engine, same mesh,
    // same seed. It is not set to zero because a landing point is read at a
    // tick boundary from a projectile that is still settling, and because a
    // grenade at rest still creeps a unit or two. Twelve units is comfortably
    // above that noise and far below a wrong bounce, which throws a grenade
    // hundreds of units away rather than tens. Nothing lands 12 units out by
    // accident and 200 units out by the same cause.
    public const float SeedReplayTolerance = 12f;

    // A throw the engine described in full: it wrote the seed, and the plugin
    // caught the release edge, so both halves of the comparison are real.
    public static bool IsUsableSample(LineupRecord lineup)
    {
        return lineup.HasPhysicsSeed()
            && lineup.release.eye_position.Length() > 0f
            && lineup.release.on_ground
            && !lineup.release.jump_throw
            && lineup.release.speed <= TrajectoryUtility.StationarySpeed
            && lineup.detonation_position.Length() > 0f;
    }

    public static List<LineupRecord> Samples(
        IEnumerable<LineupRecord> lineups,
        int maxSamples = MaxSamples
    )
    {
        var usable = new List<LineupRecord>();

        // Newest first: a throw from this session is on the map and the build
        // the solve is about to run against.
        foreach (LineupRecord lineup in lineups.Reverse())
        {
            if (!IsUsableSample(lineup))
            {
                continue;
            }

            usable.Add(lineup);

            if (usable.Count >= maxSamples)
            {
                break;
            }
        }

        return usable;
    }

    public static LaunchCheck Check(LineupRecord sample)
    {
        LaunchSeed predicted = PracticeLaunchUtility.Predict(sample.release);

        float observedSpeed = sample.initial_velocity.Length();
        float speedRatio = predicted.speed <= float.Epsilon ? 0f : observedSpeed / predicted.speed;

        var check = new LaunchCheck
        {
            client_id = sample.client_id,
            strength = TrajectoryUtility
                .ClassifyStrength(sample.release.throw_strength_raw)
                .ToString(),
            pitch = sample.release.pitch,
            position_error = (predicted.position - sample.initial_position).Length(),
            direction_error = PracticeLaunchUtility.AngleBetween(
                predicted.direction,
                sample.initial_velocity
            ),
            speed_ratio = speedRatio,
        };

        check.passed =
            check.position_error <= MaxPositionError
            && check.direction_error <= MaxDirectionError
            && check.speed_ratio >= MinSpeedRatio
            && check.speed_ratio <= MaxSpeedRatio;

        return check;
    }

    // Everything that can be decided without emitting anything. The result is
    // never Ready: only a live seed replay can grant that.
    public static CalibrationReport CheckLaunchModel(
        string map,
        IEnumerable<LineupRecord> lineups,
        int maxSamples = MaxSamples
    )
    {
        var report = new CalibrationReport { map = map };
        List<LineupRecord> samples = Samples(lineups, maxSamples);

        if (samples.Count == 0)
        {
            report.status = nameof(eCalibrationStatus.NoSample);
            report.message =
                "no throw to calibrate against; stand still and throw one grenade, then try again";
            return report;
        }

        var ratios = new Dictionary<string, List<float>>();

        foreach (LineupRecord sample in samples)
        {
            LaunchCheck check = Check(sample);
            report.launch_checks.Add(check);

            if (!check.passed)
            {
                continue;
            }

            if (!ratios.TryGetValue(check.strength, out List<float>? bucket))
            {
                bucket = new List<float>();
                ratios[check.strength] = bucket;
            }

            bucket.Add(check.speed_ratio);
        }

        LaunchCheck? failed = report.launch_checks.FirstOrDefault(check => !check.passed);

        if (failed != null)
        {
            report.status = nameof(eCalibrationStatus.LaunchModelMismatch);
            report.message = Explain(failed);
            return report;
        }

        foreach ((string strength, List<float> bucket) in ratios)
        {
            report.speed_corrections[strength] = bucket.Average();
        }

        if (report.speed_corrections.Count == 0)
        {
            report.status = nameof(eCalibrationStatus.NoSample);
            report.message = "no throw cleared the launch model";
            return report;
        }

        report.message = "launch model agrees; seed replay not run yet";
        return report;
    }

    // The throw to re-emit. Fewest bounces wins: a straight throw that lands in
    // the open is the cleanest statement of "same seed, same landing", where a
    // grenade that clipped three corners is testing the mesh as much as the
    // premise.
    public static LineupRecord? PickReplaySample(IEnumerable<LineupRecord> lineups)
    {
        return Samples(lineups, int.MaxValue)
            .OrderBy(sample => sample.bounces)
            .ThenByDescending(sample => sample.release.tick)
            .FirstOrDefault();
    }

    public static CalibrationReport WithSeedReplay(
        CalibrationReport report,
        LineupRecord sample,
        Vec3? observedLanding,
        float tolerance = SeedReplayTolerance
    )
    {
        report.seed_replay_client_id = sample.client_id;
        report.seed_replay_utility = sample.utility_type;

        if (observedLanding == null)
        {
            report.status = nameof(eCalibrationStatus.SeedReplayTimedOut);
            report.message =
                "the re-emitted grenade never reported a landing; the emit API did not produce a projectile this plugin can follow";
            return report;
        }

        float error = (observedLanding.Value - sample.detonation_position).Length();
        report.seed_replay_error = error;

        if (error > tolerance)
        {
            report.status = nameof(eCalibrationStatus.SeedReplayMismatch);
            report.message =
                $"a re-emitted throw landed {error:0.0}u from where it originally did, over the {tolerance:0.0}u tolerance; the engine does not reproduce a seeded throw on this build, so no solve can be trusted";
            return report;
        }

        report.status = nameof(eCalibrationStatus.Ready);
        report.message =
            $"seeded replay landed {error:0.0}u from the original; solving {string.Join(", ", report.SolvableStrengths())}";

        return report;
    }

    // CheckLaunchModel leaves a passing report undecided, because the verdict
    // is not its to give: only the live seed replay can say Ready.
    public static bool LaunchModelPassed(CalibrationReport report)
    {
        return report.status == nameof(eCalibrationStatus.Unknown)
            && report.speed_corrections.Count > 0;
    }

    public static CalibrationReport Unsupported(string map, string why)
    {
        return new CalibrationReport
        {
            map = map,
            status = nameof(eCalibrationStatus.Unsupported),
            message = why,
        };
    }

    private static string Explain(LaunchCheck check)
    {
        if (check.position_error > MaxPositionError)
        {
            return $"the launch model puts the grenade {check.position_error:0.0}u from where the engine spawned it (limit {MaxPositionError:0.0}u); the eye offset or forward offset is wrong for this build";
        }

        if (check.direction_error > MaxDirectionError)
        {
            return $"the launch model throws {check.direction_error:0.00} degrees off the engine's own direction (limit {MaxDirectionError:0.00}); the pitch remap is wrong for this build, so any aim a solve reported would be wrong too";
        }

        return $"the launch model predicts a release speed {check.speed_ratio:0.000}x the engine's (allowed {MinSpeedRatio:0.0}-{MaxSpeedRatio:0.0}); the speed formula is wrong for this build";
    }
}
