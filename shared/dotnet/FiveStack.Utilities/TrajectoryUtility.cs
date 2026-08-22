using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// Pure maths shared by both plugin runtimes: classification of how a throw was
// made, and compaction of the sampled flight path. No engine types, so this is
// the part of the recorder that can actually be unit tested.
public static class TrajectoryUtility
{
    // CS2 release strengths are discrete. m_flThrowStrength reads ~1.0 for a
    // left click, ~0.5 for both buttons and ~0.0 for a right click; the
    // midpoints below are deliberately generous because the value is sampled a
    // tick either side of release.
    public const float FullStrengthFloor = 0.75f;
    public const float HalfStrengthFloor = 0.25f;

    // Source movement speeds. Walking is capped at ~135 u/s on most weapons,
    // and anything under a few units is standing still with float noise.
    public const float StationarySpeed = 5f;
    public const float WalkSpeed = 135f;

    // A hand-timed jump throw does not always set m_bJumpThrow, so an upward
    // velocity at release counts too.
    public const float JumpVelocityZ = 50f;

    public static eThrowStrength ClassifyStrength(float raw)
    {
        if (raw >= FullStrengthFloor)
        {
            return eThrowStrength.Full;
        }

        if (raw >= HalfStrengthFloor)
        {
            return eThrowStrength.Half;
        }

        return eThrowStrength.Drop;
    }

    public static eThrowTechnique ClassifyTechnique(ThrowSnapshot release)
    {
        bool airborne =
            release.jump_throw
            || !release.on_ground
            || release.velocity.z > JumpVelocityZ;

        if (airborne)
        {
            if (release.ducked)
            {
                return eThrowTechnique.CrouchJump;
            }

            if (release.speed <= StationarySpeed)
            {
                return eThrowTechnique.Jump;
            }

            return release.walking || release.speed <= WalkSpeed
                ? eThrowTechnique.WalkJump
                : eThrowTechnique.RunJump;
        }

        if (release.ducked)
        {
            return eThrowTechnique.Crouch;
        }

        if (release.speed <= StationarySpeed)
        {
            return eThrowTechnique.Stationary;
        }

        return release.walking || release.speed <= WalkSpeed
            ? eThrowTechnique.Walking
            : eThrowTechnique.Running;
    }

    // Yaw/pitch of a velocity vector, in the engine's convention: yaw measured
    // counter-clockwise from +X, pitch negative when looking up.
    public static (float pitch, float yaw) AnglesFromVelocity(Vec3 velocity)
    {
        float yaw = MathF.Atan2(velocity.y, velocity.x) * (180f / MathF.PI);
        float length = velocity.Length();

        if (length <= float.Epsilon)
        {
            return (0f, yaw);
        }

        float pitch = -MathF.Asin(velocity.z / length) * (180f / MathF.PI);
        return (pitch, yaw);
    }

    // Ramer-Douglas-Peucker, with every bounce pinned as a vertex. A 20 second
    // smoke samples ~640 points; this brings it to a few dozen without moving
    // the line anywhere a viewer could see.
    public static List<TrajectoryPoint> Simplify(
        List<TrajectoryPoint> points,
        float epsilon = 4f
    )
    {
        if (points.Count <= 2)
        {
            return new List<TrajectoryPoint>(points);
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[points.Count - 1] = true;

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i].bounce)
            {
                keep[i] = true;
            }
        }

        // Simplify each run between pinned vertices independently, so a bounce
        // can never be smoothed away by a straight segment on either side.
        var pinned = new List<int>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                pinned.Add(i);
            }
        }

        for (int segment = 0; segment < pinned.Count - 1; segment++)
        {
            SimplifySegment(points, pinned[segment], pinned[segment + 1], epsilon, keep);
        }

        var result = new List<TrajectoryPoint>();
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                result.Add(points[i]);
            }
        }

        return result;
    }

    private static void SimplifySegment(
        List<TrajectoryPoint> points,
        int first,
        int last,
        float epsilon,
        bool[] keep
    )
    {
        if (last <= first + 1)
        {
            return;
        }

        float worst = 0f;
        int worstIndex = -1;

        for (int i = first + 1; i < last; i++)
        {
            float distance = PerpendicularDistance(points[i].p, points[first].p, points[last].p);
            if (distance > worst)
            {
                worst = distance;
                worstIndex = i;
            }
        }

        if (worstIndex == -1 || worst <= epsilon)
        {
            return;
        }

        keep[worstIndex] = true;
        SimplifySegment(points, first, worstIndex, epsilon, keep);
        SimplifySegment(points, worstIndex, last, epsilon, keep);
    }

    private static float PerpendicularDistance(Vec3 point, Vec3 lineStart, Vec3 lineEnd)
    {
        Vec3 line = lineEnd - lineStart;
        float lineLength = line.Length();

        if (lineLength <= float.Epsilon)
        {
            return (point - lineStart).Length();
        }

        Vec3 toPoint = point - lineStart;
        float cx = (toPoint.y * line.z) - (toPoint.z * line.y);
        float cy = (toPoint.z * line.x) - (toPoint.x * line.z);
        float cz = (toPoint.x * line.y) - (toPoint.y * line.x);

        return MathF.Sqrt((cx * cx) + (cy * cy) + (cz * cz)) / lineLength;
    }
}
