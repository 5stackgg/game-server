using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

public readonly record struct ApproachFrame(int Tick, ApproachSample Sample);

public static class ApproachUtility
{
    public const int TickRate = 64;

    // Two seconds at 64Hz, which is also how much the recorder keeps per player.
    public const int MaxSamples = 128;

    // sv_maxvelocity is 3500, about 55 units a tick. Further than this between
    // two ticks is a teleport, and a run-up does not start before one.
    public const float MaxStepUnits = 64f;

    public static void Push(Queue<ApproachFrame> buffer, int tick, ApproachSample sample)
    {
        buffer.Enqueue(new ApproachFrame(tick, sample));

        while (buffer.Count > MaxSamples)
        {
            buffer.Dequeue();
        }
    }

    // anchorTick is the last tick the player stood settled. Null, or older than
    // the buffer, means the whole buffer was run-up.
    public static List<ApproachSample> Cut(
        IEnumerable<ApproachFrame> frames,
        int? anchorTick,
        int releaseTick
    )
    {
        var window = new List<ApproachFrame>();

        if (anchorTick != null && anchorTick.Value >= releaseTick)
        {
            return new List<ApproachSample>();
        }

        foreach (ApproachFrame frame in frames)
        {
            if (frame.Tick > releaseTick)
            {
                continue;
            }

            if (anchorTick != null && frame.Tick < anchorTick.Value)
            {
                continue;
            }

            // A NaN would make the whole save unserializable, not just this.
            if (!IsFinite(frame.Sample))
            {
                return new List<ApproachSample>();
            }

            if (
                window.Count > 0
                && (frame.Sample.pos - window[^1].Sample.pos).Length() > MaxStepUnits
            )
            {
                window.Clear();
            }

            window.Add(frame);
        }

        if (window.Count > MaxSamples)
        {
            window.RemoveRange(0, window.Count - MaxSamples);
        }

        // The release on its own is not a run-up; the snapshot already holds it.
        if (window.Count < 2)
        {
            return new List<ApproachSample>();
        }

        return window
            .Select(frame => frame.Sample with { t = Milliseconds(frame.Tick - releaseTick) })
            .ToList();
    }

    public static int Milliseconds(int ticks)
    {
        return (int)MathF.Round(ticks * 1000f / TickRate, MidpointRounding.AwayFromZero);
    }

    private static bool IsFinite(ApproachSample sample)
    {
        return float.IsFinite(sample.pos.x)
            && float.IsFinite(sample.pos.y)
            && float.IsFinite(sample.pos.z)
            && float.IsFinite(sample.vel.x)
            && float.IsFinite(sample.vel.y)
            && float.IsFinite(sample.vel.z)
            && float.IsFinite(sample.pitch)
            && float.IsFinite(sample.yaw);
    }
}
