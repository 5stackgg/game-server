using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// When a jump throw let go of the grenade, counted from take-off. Its aim can
// be perfect and it still miss: the grenade keeps the jump's upward speed, and
// that is a different number every millisecond after the feet leave the
// ground.
public static class PracticeThrowTiming
{
    private const uint InJump = 1 << 1;

    // Samples are a tick apart, so anything closer than a tick is the same.
    public const int SameMilliseconds = 16;

    // Milliseconds from take-off to release. A jump bind lets go on the tick it
    // jumps, still on the ground: that is a release at take-off, not a throw
    // made standing. Null for a throw made standing, or one whose take-off is
    // older than the samples kept.
    public static int? TakeoffToRelease(IReadOnlyList<ApproachSample>? approach)
    {
        if (approach == null || approach.Count == 0)
        {
            return null;
        }

        List<ApproachSample> samples = approach.OrderBy(sample => sample.t).ToList();
        ApproachSample release = samples[^1];

        if (release.on_ground)
        {
            return (release.buttons & InJump) != 0 ? 0 : null;
        }

        int grounded = samples.FindLastIndex(sample => sample.on_ground);

        if (grounded < 0)
        {
            return null;
        }

        return release.t - samples[grounded + 1].t;
    }

    // When the lineup let go after take-off. The library is sent without its
    // run-up samples, so a lineup thrown with the jump bind -- which lets go
    // on the tick it jumps -- is 0 without them, and so is any other jump
    // throw with none on record: letting go at take-off is what a jump throw
    // is.
    public static int? LineupTakeoffToRelease(LineupRecord lineup)
    {
        if (lineup.release.jump_throw)
        {
            return 0;
        }

        return TakeoffToRelease(lineup.approach) ?? (PracticeThrowKeys.For(lineup).Jump ? 0 : null);
    }

    // When this throw let go after take-off. Its run-up is cut from the last
    // tick the player stood still, which for a jump from a standstill is the
    // release itself, leaving no take-off in it; the engine's own jump-throw
    // flag says it let go at take-off.
    public static int? ThrowTakeoffToRelease(LineupRecord thrown)
    {
        return TakeoffToRelease(thrown.approach) ?? (thrown.release.jump_throw ? 0 : null);
    }

    // How much later than the lineup this throw let go; negative is earlier.
    // Null when either was not a jump throw.
    public static int? ReleaseOffset(LineupRecord thrown, LineupRecord lineup)
    {
        int? mine = ThrowTakeoffToRelease(thrown);
        int? theirs = LineupTakeoffToRelease(lineup);

        return mine == null || theirs == null ? null : mine - theirs;
    }

    public static string? Describe(int? offset)
    {
        if (offset is not int ms)
        {
            return null;
        }

        if (Math.Abs(ms) < SameMilliseconds)
        {
            return "on time";
        }

        return ms > 0 ? $"{ms}ms late" : $"{-ms}ms early";
    }
}
