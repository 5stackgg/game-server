using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// How an aim becomes a grenade.
//
// The solver models no physics at all -- a live practice server is the physics
// engine, and every candidate throw is a real grenade. This one mapping still
// has to exist, for two reasons the rest of the solver cannot avoid:
//
//   * the emit API takes a velocity, and a solver candidate is an aim;
//   * a solved lineup is worthless to a human unless the aim reported back is
//     the aim that produces it.
//
// Every constant here is a claim about a CS2 build that nobody in this repo can
// check by reading it. So none of them are trusted: PracticeCalibrationUtility
// replays them against throws the engine itself recorded, and a solve does not
// start until they reproduce those throws.
public static class PracticeLaunchUtility
{
    // CS2 does not throw along the crosshair. It bends the aim down by ten
    // degrees at the horizon and stretches the rest of the range to fit, which
    // is why a grenade always leaves slightly above where you are looking.
    public const float PitchRemapOffset = -10f;
    public const float PitchRemapSlope = (90f + 10f) / 90f;

    // Release speed falls off linearly with the remapped pitch and saturates,
    // so everything from roughly 22 degrees up throws at the same speed.
    public const float SpeedPerDegree = 6f;
    public const float MaxSpeed = 750f;

    // The projectile spawns ahead of the eye, not at it.
    public const float ForwardOffset = 16f;

    // A moving player's own velocity is added to the release. The solver only
    // ever throws from a standstill, so this term is exercised by calibration
    // and not by a solve.
    public const float PlayerVelocityScale = 1.25f;

    // A right click still throws, it just throws weakly. This is the shape of
    // the curve between a right click and a left one; calibration measures the
    // real value per strength bucket and the solver uses the measurement.
    public const float MinStrengthScale = 0.2f;

    private const float DegreesToRadians = MathF.PI / 180f;
    private const float RadiansToDegrees = 180f / MathF.PI;

    // Eye angles arrive normalized from both frameworks, but a lineup mined
    // from a demo can carry the engine's unwrapped form.
    public static float NormalizePitch(float pitch)
    {
        float wrapped = pitch % 360f;

        if (wrapped > 180f)
        {
            wrapped -= 360f;
        }
        else if (wrapped < -180f)
        {
            wrapped += 360f;
        }

        return wrapped;
    }

    public static float NormalizeYaw(float yaw)
    {
        return NormalizePitch(yaw);
    }

    public static float RemapPitch(float pitch)
    {
        return PitchRemapOffset + (NormalizePitch(pitch) * PitchRemapSlope);
    }

    // The inverse, for reading an aim back out of a seed the engine recorded.
    public static float UnremapPitch(float remapped)
    {
        return (remapped - PitchRemapOffset) / PitchRemapSlope;
    }

    // Source's convention: yaw counter-clockwise from +X, pitch positive when
    // looking down.
    public static Vec3 Forward(float pitch, float yaw)
    {
        float pitchRadians = pitch * DegreesToRadians;
        float yawRadians = yaw * DegreesToRadians;

        float cosPitch = MathF.Cos(pitchRadians);

        return new Vec3(
            cosPitch * MathF.Cos(yawRadians),
            cosPitch * MathF.Sin(yawRadians),
            -MathF.Sin(pitchRadians)
        );
    }

    public static Vec3 ThrowDirection(float pitch, float yaw)
    {
        return Forward(RemapPitch(pitch), yaw);
    }

    public static float BaseSpeed(float pitch)
    {
        return MathF.Min((90f - RemapPitch(pitch)) * SpeedPerDegree, MaxSpeed);
    }

    public static float StrengthScale(float strength)
    {
        float clamped = Math.Clamp(strength, 0f, 1f);

        return MinStrengthScale + ((1f - MinStrengthScale) * clamped);
    }

    // m_flThrowStrength for each of the three releases a player can actually
    // make. The solver searches these and nothing between them: a value a human
    // cannot produce is a lineup a human cannot throw.
    public static float RawStrength(eThrowStrength strength)
    {
        switch (strength)
        {
            case eThrowStrength.Full:
                return 1f;
            case eThrowStrength.Half:
                return 0.5f;
            default:
                return 0f;
        }
    }

    public static LaunchSeed Seed(
        Vec3 eye,
        float pitch,
        float yaw,
        float strength,
        Vec3 playerVelocity,
        float speedCorrection = 1f
    )
    {
        Vec3 direction = ThrowDirection(pitch, yaw);
        float speed = BaseSpeed(pitch) * StrengthScale(strength) * speedCorrection;

        return new LaunchSeed
        {
            position = eye + (direction * ForwardOffset),
            velocity =
                (direction * speed) + (playerVelocity * PlayerVelocityScale),
            direction = direction,
            speed = speed,
        };
    }

    // The seed a recorded throw should have had, so calibration can hold it up
    // against the seed the engine actually wrote.
    public static LaunchSeed Predict(ThrowSnapshot release, float speedCorrection = 1f)
    {
        return Seed(
            release.eye_position,
            release.pitch,
            release.yaw,
            release.throw_strength_raw,
            release.velocity,
            speedCorrection
        );
    }

    public static float AngleBetween(Vec3 a, Vec3 b)
    {
        Vec3 first = a.Normalized();
        Vec3 second = b.Normalized();

        if (first.Length() <= 0f || second.Length() <= 0f)
        {
            return 180f;
        }

        return MathF.Acos(Math.Clamp(first.Dot(second), -1f, 1f)) * RadiansToDegrees;
    }

    // Yaw from one point to another, which is where a solve's search window is
    // centred.
    public static float BearingTo(Vec3 from, Vec3 to)
    {
        Vec3 delta = to - from;

        return MathF.Atan2(delta.y, delta.x) * RadiansToDegrees;
    }
}
