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
// Every constant here was fitted to seeds the engine itself recorded, and the
// tests hold the function to those seeds. PracticeCalibrationUtility still
// replays them against throws on the live server before a solve starts: a CS2
// update can move any of them.
public static class PracticeLaunchUtility
{
    // CS2 does not throw along the crosshair. It tips the throw ten degrees up
    // at the horizon and squeezes the rest of the range to fit, with a
    // different squeeze above and below the horizon: looking up, the throw
    // pitch is -10 + p * 80/90; looking down, -10 + p * 100/90. Every seed the
    // engine has recorded fits that to a hundredth of a degree. A single slope
    // put an aim at -55 twelve degrees under its own grenade.
    public const float PitchRemapOffset = -10f;
    public const float PitchRemapSlopeUp = (90f + PitchRemapOffset) / 90f;
    public const float PitchRemapSlopeDown = (90f - PitchRemapOffset) / 90f;

    // A full throw leaves at 675 u/s whatever the pitch: recorded throws from
    // -67 to +15 degrees of throw pitch all fit it exactly, with none of
    // CS:GO's fall-off towards the floor.
    public const float FullSpeed = 675f;

    // The projectile spawns ahead of the eye, not at it.
    public const float ForwardOffset = 16f;

    // ...and lower for a weaker throw: twelve units for a right click, six for
    // both buttons. Recorded half throws spawn exactly six under full ones.
    public const float WeakDrop = 12f;

    // A moving player's own velocity is added to the release, scaled. Every
    // recorded jump throw comes out at the same 218.8 u/s of lift through this
    // factor, and every running throw straight along the player's facing.
    public const float PlayerVelocityScale = 1.25f;

    // A right click still throws, at 30% of a full one; both buttons at 65%.
    public const float MinStrengthScale = 0.3f;

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
        float normalized = NormalizePitch(pitch);
        float slope = normalized < 0f ? PitchRemapSlopeUp : PitchRemapSlopeDown;

        return PitchRemapOffset + (normalized * slope);
    }

    // The inverse, for reading an aim back out of a seed the engine recorded.
    // The horizon maps to the offset, so that is where the two slopes meet.
    public static float UnremapPitch(float remapped)
    {
        float slope = remapped < PitchRemapOffset ? PitchRemapSlopeUp : PitchRemapSlopeDown;

        return (remapped - PitchRemapOffset) / slope;
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
        return FullSpeed;
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
        float drop = WeakDrop * (1f - Math.Clamp(strength, 0f, 1f));

        return new LaunchSeed
        {
            position = eye + (direction * ForwardOffset) - new Vec3(0f, 0f, drop),
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
