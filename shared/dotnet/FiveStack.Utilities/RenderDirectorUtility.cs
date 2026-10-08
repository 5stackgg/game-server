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
    Glide,
    StanceEyes,
    Tilt,
    Aim,
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

    public const float StanceSeconds = 1.4f;
    public const float StanceStillAt = 0.9f;

    // The stance camera flies down into the thrower's eyes, looks at the
    // ground around their feet (where to stand, from where they stand), then
    // tilts up onto the exact aim -- so the cut to first person is invisible.
    public const float GlideSeconds = 0.7f;
    public const float StanceEyesSeconds = 1.0f;
    public const float StanceEyesStillAt = 0.6f;
    public const float StanceEyesPitch = 45f;
    public const float TiltSeconds = 0.6f;
    public const float AimSeconds = 1.0f;
    public const float AimStillAt = 0.6f;
    public const float AimCloseSeconds = 1.2f;
    public const float AimCloseStillAt = 0.8f;
    public const float ZoomSeconds = 0.3f;
    public const int AimCloseFov = 30;

    // The pod presses the throw on `act`, or on its own clock when the line is
    // slow to reach it; this is how long it gets either way.
    public const float ThrowTimeoutSeconds = 10f;

    // Long enough to see the grenade leave the hand before the view lets go.
    public const float DetachSeconds = 0.2f;

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
        + GlideSeconds
        + StanceEyesSeconds
        + TiltSeconds
        + AimSeconds
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

    // Behind the thrower's shoulder, a little above head height: the whole
    // player stands in frame on the spot, facing the way they throw.
    public static (Vec3 eye, Vec3 lookAt) StanceShot(Vec3 feet, float yaw)
    {
        Vec3 forward = Forward(0f, yaw);
        var right = new Vec3(forward.y, -forward.x, 0f);
        var up = new Vec3(0f, 0f, 1f);

        Vec3 eye = feet - (forward * 160f) + (right * 48f) + (up * 96f);
        Vec3 lookAt = feet + (forward * 60f) + (up * 40f);

        return (eye, lookAt);
    }

    // Stops just in front of the eyes, so the last frames of the glide never
    // look out through the back of the thrower's own head.
    public const float GlideEndAhead = 6f;

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

        return (headEye + (forward * GlideEndAhead), headEye + (forward * 400f));
    }

    // s in 0..1 along the tilt from the ground at the thrower's feet up onto the aim.
    public static (Vec3 eye, Vec3 lookAt) TiltShot(Vec3 headEye, float aimPitch, float yaw, float s)
    {
        float pitch = StanceEyesPitch + ((aimPitch - StanceEyesPitch) * Ease(s));

        return EyesShot(headEye, pitch, yaw);
    }

    // s in 0..1 along the glide from the stance camera to the eyes at (pitch, yaw).
    public static (Vec3 eye, Vec3 lookAt) GlideShot(
        Vec3 stanceEye,
        Vec3 stanceLook,
        Vec3 headEye,
        float pitch,
        float yaw,
        float s
    )
    {
        Vec3 forward = Forward(pitch, yaw);
        Vec3 endEye = headEye + (forward * GlideEndAhead);
        Vec3 endLook = headEye + (forward * 400f);
        float eased = Ease(s);

        return (Lerp(stanceEye, endEye, eased), Lerp(stanceLook, endLook, eased));
    }

    public const float ChaseDistance = 96f;
    public const float ChaseHeight = 22f;

    // The chase rides the grenade rigidly; only its bearing is smoothed, so a
    // bounce swings the camera round instead of snapping it, and the grenade
    // stays the same size in frame however fast it flies.
    public const float ChaseTurnHalfLife = 0.15f;

    // How long the view takes to pull back from the eyes onto the chase.
    public const float DetachBlendSeconds = 0.35f;

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

    public static Vec3 ChaseEye(Vec3 projectile, Vec3 direction)
    {
        return projectile - (direction * ChaseDistance) + new Vec3(0f, 0f, ChaseHeight);
    }

    public static Vec3 Turn(Vec3 current, Vec3 target, float dt)
    {
        Vec3 turned = Approach(current, target, dt, ChaseTurnHalfLife).Normalized();

        return turned.Length() <= float.Epsilon ? target : turned;
    }

    public const float BloomDistance = 380f;
    public const float BloomHeight = 150f;
    public const float BloomLookHeight = 48f;

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
