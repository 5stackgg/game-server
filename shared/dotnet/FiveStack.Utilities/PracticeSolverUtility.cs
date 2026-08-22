using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// Everything the solver decides, with no server attached: which throws to try,
// in what order, when to stop, and what a winning throw becomes.
//
// The search is a coarse sweep followed by local refinement around several
// separated near-misses, and it is that way because of what a grenade does.
// Landing point is not a smooth function of aim: a degree of pitch can be the
// difference between clearing a wall and bouncing off it, and either side of
// that edge the landing point jumps. Anything that follows a gradient walks
// straight into the nearest wall and stays there. A sweep finds the basins; the
// refinement finds the bottom of each one; keeping several means the basin that
// looked second-best after eight grenades can still win after eighty.
public static class PracticeSolverUtility
{
    public const float DefaultTolerance = 40f;
    public const float MinTolerance = 8f;
    public const float MaxTolerance = 256f;

    // A batch is grenades in flight at once. Twenty is a compromise measured in
    // two directions: fewer and a solve is mostly waiting out flight times,
    // more and the entity count plus the per-tick sampling start costing the
    // server frames.
    public const int DefaultBatchSize = 20;
    public const int MaxBatchSize = 32;

    public const int DefaultMaxGrenades = 300;
    public const int MaxGrenadeCap = 600;

    public const float DefaultMaxSeconds = 120f;
    public const float MaxSecondsCap = 600f;

    // The sweep gets most of the budget and the refinement gets the rest. A
    // sweep too small to find the right basin cannot be rescued by refining.
    public const float CoarseShare = 0.6f;

    public static readonly float[] CoarseYawOffsets =
    {
        0f,
        -5f,
        5f,
        -12f,
        12f,
        -22f,
        22f,
        -35f,
        35f,
    };

    public const float CoarsePitchFloor = -54f;
    public const float CoarsePitchCeil = 18f;
    public const float CoarsePitchStep = 9f;

    public const int RefineSeeds = 4;
    public const int RefinePasses = 4;
    public const float RefineStartStep = 4.5f;
    public const float RefineShrink = 3f;

    // Two candidates closer than this are the same throw with noise on it, so
    // refining both spends the budget twice on one basin.
    public const float MinSeparationDegrees = 8f;

    // A refinement pass that moves the best landing less than this has learned
    // nothing.
    //
    // One such pass is not a reason to stop: the neighbours of a seed exclude
    // the seed itself, so a step that overshoots the answer can leave the best
    // exactly where it was even though a smaller step is about to find it. Two
    // in a row, with the step a third of the size the second time, is a search
    // that has genuinely run out of room.
    public const float MinProgress = 2f;
    public const int MaxStallPasses = 2;

    // A target the thrower is standing on is not a throw.
    public const float MinTargetDistance = 24f;

    private const float DuplicateEpsilon = 0.05f;

    public static SolveRequest Defaults(SolveRequest request)
    {
        request.tolerance = Math.Clamp(
            request.tolerance <= 0f ? DefaultTolerance : request.tolerance,
            MinTolerance,
            MaxTolerance
        );
        request.batch_size = Math.Clamp(
            request.batch_size <= 0 ? DefaultBatchSize : request.batch_size,
            1,
            MaxBatchSize
        );
        request.max_grenades = Math.Clamp(
            request.max_grenades <= 0 ? DefaultMaxGrenades : request.max_grenades,
            request.batch_size,
            MaxGrenadeCap
        );
        request.max_seconds = Math.Clamp(
            request.max_seconds <= 0f ? DefaultMaxSeconds : request.max_seconds,
            1f,
            MaxSecondsCap
        );

        return request;
    }

    // Ordered by how likely a throw is to be the answer, because the sweep is
    // truncated to fit the budget and truncation should drop the least likely
    // candidates rather than an arbitrary corner of the grid.
    public static List<SolveCandidate> CoarseSweep(SolveRequest request)
    {
        var candidates = new List<SolveCandidate>();

        if ((request.target - request.eye).Length() < MinTargetDistance)
        {
            return candidates;
        }

        float bearing = PracticeLaunchUtility.BearingTo(request.eye, request.target);
        float straight = StraightLinePitch(request.eye, request.target);

        foreach (string bucket in request.strengths)
        {
            if (!Enum.TryParse(bucket, out eThrowStrength strength))
            {
                continue;
            }

            float raw = PracticeLaunchUtility.RawStrength(strength);

            for (float pitch = CoarsePitchFloor; pitch <= CoarsePitchCeil; pitch += CoarsePitchStep)
            {
                foreach (float offset in CoarseYawOffsets)
                {
                    candidates.Add(
                        new SolveCandidate
                        {
                            pitch = pitch,
                            yaw = PracticeLaunchUtility.NormalizeYaw(bearing + offset),
                            strength = raw,
                            strength_bucket = strength.ToString(),
                        }
                    );
                }
            }
        }

        return candidates
            .OrderBy(candidate =>
                MathF.Abs(
                    PracticeLaunchUtility.NormalizeYaw(candidate.yaw - bearing)
                )
            )
            .ThenBy(candidate => MathF.Abs(candidate.pitch - straight))
            .ThenByDescending(candidate => candidate.strength)
            .ToList();
    }

    // Where the target sits relative to the eye, ignoring the arc. Only used to
    // order the sweep: a grenade always has to be aimed above this, but how far
    // above is what the sweep is for.
    public static float StraightLinePitch(Vec3 eye, Vec3 target)
    {
        Vec3 delta = target - eye;
        float flat = delta.LengthXY();

        if (flat <= float.Epsilon)
        {
            return delta.z >= 0f ? -89f : 89f;
        }

        return -MathF.Atan2(delta.z, flat) * (180f / MathF.PI);
    }

    public static List<SolveCandidate> Neighbours(SolveCandidate around, float step)
    {
        var neighbours = new List<SolveCandidate>();

        for (int pitchStep = -1; pitchStep <= 1; pitchStep++)
        {
            for (int yawStep = -1; yawStep <= 1; yawStep++)
            {
                if (pitchStep == 0 && yawStep == 0)
                {
                    continue;
                }

                neighbours.Add(
                    new SolveCandidate
                    {
                        pitch = Math.Clamp(around.pitch + (pitchStep * step), -89f, 89f),
                        yaw = PracticeLaunchUtility.NormalizeYaw(around.yaw + (yawStep * step)),
                        strength = around.strength,
                        strength_bucket = around.strength_bucket,
                    }
                );
            }
        }

        return neighbours;
    }

    // Best first, but never two from the same basin. This is the whole reason
    // the refinement survives a piecewise landing function.
    public static List<SolveObservation> PickDistinct(
        IEnumerable<SolveObservation> observations,
        int count,
        float separation
    )
    {
        var picked = new List<SolveObservation>();

        foreach (
            SolveObservation observation in observations
                .Where(observation => observation.landed)
                .OrderBy(observation => observation.distance)
        )
        {
            if (picked.Count >= count)
            {
                break;
            }

            bool crowded = picked.Any(chosen =>
                Separation(chosen.candidate, observation.candidate) < separation
            );

            if (!crowded)
            {
                picked.Add(observation);
            }
        }

        return picked;
    }

    // Distance in aim space. Different strengths are never the same basin: the
    // same aim thrown harder is a different throw, not a nearby one.
    public static float Separation(SolveCandidate a, SolveCandidate b)
    {
        if (a.strength_bucket != b.strength_bucket)
        {
            return float.MaxValue;
        }

        float yaw = PracticeLaunchUtility.NormalizeYaw(a.yaw - b.yaw);

        return MathF.Max(MathF.Abs(yaw), MathF.Abs(a.pitch - b.pitch));
    }

    public static string CandidateKey(SolveCandidate candidate)
    {
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{candidate.strength_bucket}:{MathF.Round(candidate.pitch / DuplicateEpsilon)}:{MathF.Round(PracticeLaunchUtility.NormalizeYaw(candidate.yaw) / DuplicateEpsilon)}"
        );
    }

    // Whether a winning candidate, thrown once more with nothing else in the
    // air, did the same thing again.
    //
    // A search grenade shares the sky with nineteen others and grenades bounce
    // off each other, so a candidate deflected onto the target by a sibling
    // looks exactly like the answer. Nothing downstream could tell the two
    // apart: the lineup would simply not reproduce for whoever saved it.
    public static bool Confirms(SolveObservation confirmation, SolveRequest request)
    {
        return confirmation.landed && confirmation.distance <= request.tolerance;
    }

    public static LaunchSeed SeedFor(
        SolveRequest request,
        SolveCandidate candidate,
        CalibrationReport calibration
    )
    {
        return PracticeLaunchUtility.Seed(
            request.eye,
            candidate.pitch,
            candidate.yaw,
            candidate.strength,
            new Vec3(0f, 0f, 0f),
            calibration.CorrectionFor(candidate.strength_bucket)
        );
    }

    // The winning throw as a lineup.
    //
    // The seed is the point of it: an aim and a strength are what a human
    // reproduces, but the seed is what makes the saved lineup replayable
    // exactly, the same way a throw the plugin watched is.
    public static LineupRecord ToLineup(
        SolveRequest request,
        SolveObservation best,
        CalibrationReport calibration,
        string pluginRuntime,
        string pluginVersion
    )
    {
        LaunchSeed seed = SeedFor(request, best.candidate, calibration);

        var release = new ThrowSnapshot
        {
            feet_position = request.feet,
            eye_position = request.eye,
            pitch = best.candidate.pitch,
            yaw = best.candidate.yaw,
            velocity = new Vec3(0f, 0f, 0f),
            speed = 0f,
            on_ground = true,
            ducked = false,
            walking = false,
            throw_strength_raw = best.candidate.strength,
            jump_throw = false,
        };

        return new LineupRecord
        {
            client_id = Guid.NewGuid().ToString(),
            map = request.map,
            name = request.name,
            utility_type = request.utility_type,
            side = request.side,
            visibility = nameof(eLineupVisibility.Private),
            author_steam_id = request.requested_by,
            release = release,
            initial_position = seed.position,
            initial_velocity = seed.velocity,
            detonation_position = best.landing,
            bounces = best.bounces,
            technique = nameof(eThrowTechnique.Stationary),
            strength = best.candidate.strength_bucket,
            // The server threw this one and watched where it went, same as a
            // player's own throw. Never sent: the panel stamps provenance.
            confidence = LineupRecord.Exact,
            recorded_tickrate = 64,
            plugin_runtime = pluginRuntime,
            plugin_version = pluginVersion,
        };
    }

    // utility_solver_solve is driven over RCON, where a positional argument list is
    // a silent misfire waiting to happen. Named arguments make a wrong call an
    // error rather than a lineup for the wrong place.
    public static bool TryParse(
        IEnumerable<string> args,
        out SolveRequest request,
        out string error
    )
    {
        request = new SolveRequest();
        error = "";

        Vec3? target = null;
        Vec3? from = null;

        foreach (string argument in args)
        {
            string trimmed = argument.Trim().Trim('"');

            if (trimmed.Length == 0)
            {
                continue;
            }

            int split = trimmed.IndexOf('=');

            if (split <= 0)
            {
                error = $"unexpected argument \"{trimmed}\"; every argument is key=value";
                return false;
            }

            string key = trimmed.Substring(0, split).ToLowerInvariant();
            string value = trimmed.Substring(split + 1);

            switch (key)
            {
                case "target":
                    if (!TryVec3(value, out Vec3 parsedTarget))
                    {
                        error = $"target must be x,y,z; got \"{value}\"";
                        return false;
                    }
                    target = parsedTarget;
                    break;
                case "from":
                    if (!TryVec3(value, out Vec3 parsedFrom))
                    {
                        error = $"from must be x,y,z; got \"{value}\"";
                        return false;
                    }
                    from = parsedFrom;
                    break;
                case "utility":
                    request.utility_type = PracticeLineupUtility.NormalizeUtilityType(value);
                    break;
                case "side":
                    request.side = value.ToUpperInvariant();
                    break;
                case "name":
                    request.name = value;
                    break;
                case "steam":
                    request.requested_by = value;
                    break;
                case "tolerance":
                    if (!TryFloat(value, out float tolerance))
                    {
                        error = $"tolerance must be a number; got \"{value}\"";
                        return false;
                    }
                    request.tolerance = tolerance;
                    break;
                case "grenades":
                    if (!int.TryParse(value, out int grenades))
                    {
                        error = $"grenades must be a whole number; got \"{value}\"";
                        return false;
                    }
                    request.max_grenades = grenades;
                    break;
                case "seconds":
                    if (!TryFloat(value, out float seconds))
                    {
                        error = $"seconds must be a number; got \"{value}\"";
                        return false;
                    }
                    request.max_seconds = seconds;
                    break;
                default:
                    error = $"unknown argument \"{key}\"";
                    return false;
            }
        }

        if (target == null)
        {
            error = "target=x,y,z is required";
            return false;
        }

        request.target = target.Value;

        if (from != null)
        {
            request.feet = from.Value;
            // A standing player's eyes; the caller gave a floor position.
            request.eye = new Vec3(from.Value.x, from.Value.y, from.Value.z + StandingEyeHeight);
        }

        Defaults(request);

        return true;
    }

    // CS2's standing view offset. Only used when a caller supplies a throwing
    // position by coordinate rather than by standing on it.
    public const float StandingEyeHeight = 64f;

    private static bool TryVec3(string value, out Vec3 parsed)
    {
        parsed = new Vec3(0f, 0f, 0f);

        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != 3)
        {
            return false;
        }

        if (
            !TryFloat(parts[0], out float x)
            || !TryFloat(parts[1], out float y)
            || !TryFloat(parts[2], out float z)
        )
        {
            return false;
        }

        parsed = new Vec3(x, y, z);
        return true;
    }

    private static bool TryFloat(string value, out float parsed)
    {
        return float.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out parsed
        );
    }
}
