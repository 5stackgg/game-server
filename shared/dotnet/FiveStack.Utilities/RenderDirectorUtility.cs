using System.Globalization;
using System.Text;
using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

public enum eRenderBeat
{
    Idle,
    Staging,
    Staged,
    Stance,
    StanceEyes,
    Tilt,
    Aim,
    Pin,
    AimClose,
    Throw,
    Follow,
    Bloom,
    Wrap,
}

// The render pod films a lineup the plugin directs. Everything here is a
// contract with game-streamer's nade-clip.sh, which greps the client's
// console.log for these lines: the prefix, the event names and the key names
// are what it matches on.
public static class RenderDirectorUtility
{
    public const string Prefix = "[5stack-render]";

    public const int TickRate = 64;

    // Where to stand, from above; then the same spot from the eyes, looking
    // down at it. Nothing of the plugin's is drawn in a render.
    public const float StanceSeconds = 1.0f;
    public const float StanceStillAt = 0.6f;
    public const float StanceEyesSeconds = 0.8f;
    public const float StanceEyesStillAt = 0.5f;
    public const float StanceEyesPitch = 45f;
    public const float TiltSeconds = 0.8f;
    public const float AimSeconds = 0.8f;
    public const float AimStillAt = 0.5f;

    // The pin comes out as the tilt starts and stays out through the close-up.
    // cs2's grenade lineup reticle only pops up ~2s after the pin is pulled, so
    // the pulled-pin still waits for it: tilt (0.8) + aim (0.8) + 0.5 = 2.1s.
    public const float PinSeconds = 0.7f;
    public const float PinStillAt = 0.5f;
    public const float AimCloseSeconds = 0.8f;
    public const float AimCloseStillAt = 0.5f;
    public const float ZoomSeconds = 0.3f;
    public const float DefaultFov = 90f;
    public const int AimCloseFov = 30;

    // The pod presses the throw on `act`, or on its own clock when the line is
    // slow to reach it; this is how long it gets either way.
    public const float ThrowTimeoutSeconds = 10f;

    // Long enough to watch the throw itself -- the arm, the jump -- before the
    // view lets go of the eyes.
    public const float DetachSeconds = 0.7f;

    // A grenade that never detonates (stuck in a wall, out of the map) still
    // has to end the clip.
    public const float FollowMaxSeconds = 20f;

    // After `done` the camera stays where it is: the pod reads the line some
    // time after it was printed, and stops recording only then.
    public const float WrapSeconds = 3f;

    public const float StandingEyeHeight = 64f;

    // How far the measured stance may sit from the lineup's before staging is
    // refused rather than filmed.
    public const float StagedPositionTolerance = 16f;
    public const float StagedAngleTolerance = 0.25f;

    // A lineup with a run-up is thrown from where the run-up starts, which the
    // pod then walks; the pod treats fewer than two samples as no run-up.
    public static Vec3 StageAt(Vec3 stance, IReadOnlyList<ApproachSample>? approach)
    {
        return approach != null && approach.Count >= 2 ? approach[0].pos : stance;
    }

    public static int Ticks(float seconds)
    {
        return (int)MathF.Round(seconds * TickRate);
    }

    public static int Milliseconds(int ticks)
    {
        return (int)MathF.Round(ticks * 1000f / TickRate);
    }

    // When, after go, the pod should throw if `act` has not reached it.
    public static float ActAtSeconds =>
        StanceSeconds
        + StanceEyesSeconds
        + TiltSeconds
        + AimSeconds
        + PinSeconds
        + AimCloseSeconds;

    // A grenade only stands in for the lineup if it is the same kind of grenade.
    public static bool SameUtility(string? projectileType, string? lineupType)
    {
        return !string.IsNullOrEmpty(projectileType)
            && string.Equals(projectileType, lineupType, StringComparison.OrdinalIgnoreCase);
    }

    public static float BloomHoldSeconds(string? utilityType)
    {
        return utilityType switch
        {
            "Smoke" => 5f,
            "Molotov" => 4f,
            _ => 2f,
        };
    }

    // A smoke is still growing for a couple of seconds after it pops; the
    // still is taken once it has filled out.
    public static float LandingStillSeconds(string? utilityType)
    {
        return utilityType switch
        {
            "Smoke" => 3f,
            "Molotov" => 2f,
            _ => 0.4f,
        };
    }

    // <prefix> <event> key=value ...
    //
    // Values never contain a space: one line is split on spaces by the reader.
    // Floats are invariant with two decimals so a locale cannot turn 1.5 into
    // 1,5.
    public static string Line(string eventName, params (string key, object? value)[] fields)
    {
        var line = new StringBuilder(Prefix).Append(' ').Append(eventName);

        foreach ((string key, object? value) in fields)
        {
            line.Append(' ').Append(key).Append('=').Append(Format(value));
        }

        return line.ToString();
    }

    private static string Format(object? value)
    {
        string text = value switch
        {
            null => "-",
            float number => number.ToString("0.00", CultureInfo.InvariantCulture),
            double number => number.ToString("0.00", CultureInfo.InvariantCulture),
            bool flag => flag ? "1" : "0",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "-",
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return "-";
        }

        var clean = new StringBuilder(text.Length);

        foreach (char character in text.Trim())
        {
            clean.Append(char.IsWhiteSpace(character) || char.IsControl(character) ? '_' : character);
        }

        return clean.ToString();
    }

    public static Vec3 Forward(float pitch, float yaw)
    {
        float p = pitch * MathF.PI / 180f;
        float y = yaw * MathF.PI / 180f;

        return new Vec3(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));
    }

    public static (float pitch, float yaw) LookAt(Vec3 from, Vec3 to)
    {
        Vec3 delta = to - from;
        float yaw = MathF.Atan2(delta.y, delta.x) * 180f / MathF.PI;
        float flat = delta.LengthXY();

        if (flat <= float.Epsilon && MathF.Abs(delta.z) <= float.Epsilon)
        {
            return (0f, yaw);
        }

        float pitch = -MathF.Atan2(delta.z, flat) * 180f / MathF.PI;

        return (pitch, yaw);
    }

    // Smallest signed difference between two angles, in degrees.
    public static float AngleDelta(float a, float b)
    {
        float delta = (a - b) % 360f;

        if (delta > 180f)
        {
            delta -= 360f;
        }
        else if (delta < -180f)
        {
            delta += 360f;
        }

        return delta;
    }

    public static bool AimMatches(float pitch, float yaw, float wantPitch, float wantYaw)
    {
        return MathF.Abs(AngleDelta(pitch, wantPitch)) <= StagedAngleTolerance
            && MathF.Abs(AngleDelta(yaw, wantYaw)) <= StagedAngleTolerance;
    }

    // Behind and well above the spot, looking down on it: where to stand, and
    // which way to face, before the cut into the eyes.
    public static (Vec3 eye, Vec3 lookAt) SpotShot(Vec3 feet, float yaw)
    {
        Vec3 forward = Forward(0f, yaw);
        var right = new Vec3(forward.y, -forward.x, 0f);
        var up = new Vec3(0f, 0f, 1f);

        Vec3 eye = feet - (forward * 150f) + (right * 30f) + (up * 170f);
        Vec3 lookAt = feet + (forward * 30f) + (up * 20f);

        return (eye, lookAt);
    }

    // Just in front of the eyes, so the camera never sees the inside of the
    // thrower's own head.
    public const float EyesAhead = 6f;

    public static float Ease(float s)
    {
        float x = Math.Clamp(s, 0f, 1f);

        return x * x * (3f - (2f * x));
    }

    public static Vec3 Lerp(Vec3 a, Vec3 b, float s)
    {
        return a + ((b - a) * s);
    }

    // The eyes, looking along (pitch, yaw), from just in front of the face.
    public static (Vec3 eye, Vec3 lookAt) EyesShot(Vec3 headEye, float pitch, float yaw)
    {
        Vec3 forward = Forward(pitch, yaw);

        return (headEye + (forward * EyesAhead), headEye + (forward * 400f));
    }

    // s in 0..1 along the tilt from the ground at the thrower's feet up onto the aim.
    public static (Vec3 eye, Vec3 lookAt) TiltShot(Vec3 headEye, float aimPitch, float yaw, float s)
    {
        float pitch = StanceEyesPitch + ((aimPitch - StanceEyesPitch) * Ease(s));

        return EyesShot(headEye, pitch, yaw);
    }

    // The chase rides the grenade's own flight path, this far back along it:
    // the grenade has already been everywhere the camera goes, so the camera
    // never meets a wall the grenade did not, and stays behind it round the arc.
    public const float ChaseDistance = 110f;
    public const float ChaseHeight = 22f;

    // After letting go of the eyes the camera runs up the path from the release
    // point to its place behind the grenade, starting from rest and arriving at
    // the grenade's own pace.
    public const float ChaseCatchUpSeconds = 0.9f;

    // How long the cut from the eyes takes to settle onto the path.
    public const float DetachHandOffSeconds = 0.25f;

    // Further than this in one tick is a cut, not a camera move: the fastest
    // chase (the run up the path) covers ~40u a tick.
    public const float CameraCutDistance = 96f;

    // The lift above the path is traced for clearance every tick, and a thin
    // wire it clips for one tick used to drop the camera by the whole lift.
    public const float ChaseRiseHalfLife = 0.08f;

    // How far along the flown path (in units from the release) the camera is,
    // `sinceDetach` seconds after it let go of the eyes.
    public static float ChaseArc(float flown, float sinceDetach)
    {
        return MathF.Max(0f, flown - ChaseDistance) * Ease(sinceDetach / ChaseCatchUpSeconds);
    }

    // The point `distance` units along a path whose running lengths are `arc`.
    public static Vec3 PointAlong(IReadOnlyList<Vec3> path, IReadOnlyList<float> arc, float distance)
    {
        if (path.Count == 0)
        {
            return default;
        }

        if (path.Count == 1 || distance <= 0f)
        {
            return path[0];
        }

        if (distance >= arc[^1])
        {
            return path[^1];
        }

        int low = 0;
        int high = arc.Count - 1;

        while (high - low > 1)
        {
            int middle = (low + high) / 2;

            if (arc[middle] <= distance)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        float span = arc[high] - arc[low];
        float along = span <= float.Epsilon ? 0f : (distance - arc[low]) / span;

        return Lerp(path[low], path[high], along);
    }

    // The bloom's fallback bearing follows the flight, smoothed so a bounce
    // swings it round rather than snapping it.
    public const float ChaseTurnHalfLife = 0.15f;

    // Below this the projectile is rolling or settling and its velocity says
    // nothing about which way it is going; the camera keeps its last bearing.
    public const float ChaseMinSpeed = 60f;

    public static Vec3 ChaseDirection(Vec3 velocity, Vec3 previous)
    {
        if (velocity.Length() < ChaseMinSpeed)
        {
            return previous;
        }

        // Mostly the horizontal travel: a camera that pitches with the arc
        // ends up under the grenade on the way down.
        var flattened = new Vec3(velocity.x, velocity.y, velocity.z * 0.35f);
        Vec3 direction = flattened.Normalized();

        return direction.Length() <= float.Epsilon ? previous : direction;
    }

    public static Vec3 Turn(Vec3 current, Vec3 target, float dt)
    {
        Vec3 turned = Approach(current, target, dt, ChaseTurnHalfLife).Normalized();

        return turned.Length() <= float.Epsilon ? target : turned;
    }

    public const float BloomDistance = 380f;
    public const float BloomHeight = 150f;
    public const float BloomLookHeight = 48f;

    public static Vec3 RotateZ(Vec3 v, float degrees)
    {
        float r = degrees * MathF.PI / 180f;
        float c = MathF.Cos(r);
        float s = MathF.Sin(r);

        return new Vec3((v.x * c) - (v.y * s), (v.x * s) + (v.y * c), v.z);
    }

    private static readonly float[] BloomDistances = { 650f, 520f, 400f };
    private static readonly float[] BloomBearings = { 0f, 35f, -35f, 70f, -70f, 110f, -110f, 180f };

    // Where the bloom may be filmed from, best first: far enough back that the
    // cloud sits in its surroundings rather than filling the frame, from the
    // thrower's side first, then working round it. The director keeps the
    // first one a trace says has a clear view of the cloud.
    public static IEnumerable<(Vec3 eye, Vec3 lookAt)> BloomCandidates(
        Vec3 landing,
        Vec3 stance,
        Vec3 fallbackDirection
    )
    {
        var toward = new Vec3(stance.x - landing.x, stance.y - landing.y, 0f);
        Vec3 direction = toward.Normalized();

        if (direction.Length() <= float.Epsilon)
        {
            direction = new Vec3(-fallbackDirection.x, -fallbackDirection.y, 0f).Normalized();
        }

        if (direction.Length() <= float.Epsilon)
        {
            direction = new Vec3(1f, 0f, 0f);
        }

        Vec3 lookAt = landing + new Vec3(0f, 0f, BloomLookHeight);

        foreach (float distance in BloomDistances)
        {
            foreach (float bearing in BloomBearings)
            {
                Vec3 around = RotateZ(direction, bearing);
                Vec3 eye = landing + (around * distance) + new Vec3(0f, 0f, distance * 0.33f);

                yield return (eye, lookAt);
            }
        }
    }

    // Back toward the thrower, so the cloud is framed against the side it was
    // thrown to cover.
    public static (Vec3 eye, Vec3 lookAt) BloomShot(Vec3 landing, Vec3 stance, Vec3 fallbackDirection)
    {
        var toward = new Vec3(stance.x - landing.x, stance.y - landing.y, 0f);
        Vec3 direction = toward.Normalized();

        if (direction.Length() <= float.Epsilon)
        {
            direction = new Vec3(-fallbackDirection.x, -fallbackDirection.y, 0f).Normalized();
        }

        if (direction.Length() <= float.Epsilon)
        {
            direction = new Vec3(1f, 0f, 0f);
        }

        Vec3 lookAt = landing + new Vec3(0f, 0f, BloomLookHeight);
        Vec3 eye = landing + (direction * BloomDistance) + new Vec3(0f, 0f, BloomHeight);

        return (eye, lookAt);
    }

    // Exponential approach with a half-life, so the result does not depend on
    // how often it is called: at dt == halfLife the camera covers half the gap.
    public static Vec3 Approach(Vec3 current, Vec3 target, float dt, float halfLife)
    {
        if (halfLife <= 0f)
        {
            return target;
        }

        float keep = MathF.Pow(0.5f, dt / halfLife);

        return target + ((current - target) * keep);
    }

    // Pulls a camera in along the line from its subject so it never ends up
    // inside a wall. `hit` is where a trace from the subject toward the camera
    // stopped, or null when it reached the camera unobstructed.
    public static Vec3 Unobstructed(Vec3 subject, Vec3 eye, Vec3? hit, float margin)
    {
        if (hit == null)
        {
            return eye;
        }

        Vec3 toward = eye - subject;
        float length = toward.Length();
        Vec3 blocked = hit.Value - subject;
        float reach = blocked.Length() - margin;

        if (length <= float.Epsilon || reach >= length)
        {
            return eye;
        }

        return subject + (toward.Normalized() * MathF.Max(reach, 0f));
    }
}
