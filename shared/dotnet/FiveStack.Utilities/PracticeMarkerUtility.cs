using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// One place to stand, and every lineup thrown from it.
public class LineupSpot
{
    public required string Key { get; init; }
    public required Vec3 At { get; init; }
    public required List<LineupRecord> Lineups { get; init; }

    public List<string> Types =>
        Lineups.Select(lineup => lineup.utility_type).Distinct(StringComparer.Ordinal).ToList();
}

// A line on the floor, in a spot's own frame: along is the way the throw
// faces, across is to its right.
public readonly record struct MarkerSegment(float Along1, float Across1, float Along2, float Across2);

public record RunUp(
    IReadOnlyList<string> Keys,
    bool Walk,
    bool Crouch,
    bool Jump,
    int Milliseconds,
    bool Capped,
    Vec3 From
);

public static class PracticeMarkerUtility
{
    // The gaps between the two numbers of each pair are what stop the marker
    // flickering between stages when the crosshair sits on a boundary.
    public const float NearBelowDegrees = 8f;
    public const float FindAboveDegrees = 9.5f;
    public const float OnBelowDegrees = 1.5f;
    public const float NearAboveDegrees = 2.2f;

    public const float FinderDegrees = 3f;
    public const float FinderLegDegrees = 1.1f;
    public const float TickFromDegrees = 1.7f;
    public const float TickToDegrees = 2.5f;

    // Wider than a player's own crosshair, so the two never draw on each other.
    public const float RingDegrees = 1.2f;
    public const int RingSegments = 16;

    // The point itself, in the middle of the ring: a couple of pixels, small
    // enough to sit in the gap of a crosshair instead of under its arms.
    public const float DotDegrees = 0.14f;

    // How heavy the lines are, in degrees like everything else. A weight in
    // units is too heavy for a wall close to the stance: each piece of the
    // ring comes out wider than it is long, and the ring draws as a blot.
    public const float LineDegrees = 0.1f;

    // The plain ring round each of the other throws off the spot.
    public const float OtherRingDegrees = 1f;
    public const int OtherRingSegments = 8;

    // Two aims closer than this would draw one ring inside the other.
    public const float CrowdedDegrees = RingDegrees + OtherRingDegrees;

    public static eAimStage AimStage(eAimStage current, float errorDegrees)
    {
        switch (current)
        {
            case eAimStage.Find:
                return errorDegrees < NearBelowDegrees ? eAimStage.Near : eAimStage.Find;
            case eAimStage.Near:
                if (errorDegrees > FindAboveDegrees)
                {
                    return eAimStage.Find;
                }

                return errorDegrees < OnBelowDegrees ? eAimStage.On : eAimStage.Near;
            default:
                return errorDegrees > NearAboveDegrees ? eAimStage.Near : eAimStage.On;
        }
    }

    // The stage a marker is born in, with nothing to be sticky about yet.
    public static eAimStage AimStage(float errorDegrees)
    {
        if (errorDegrees < OnBelowDegrees)
        {
            return eAimStage.On;
        }

        return errorDegrees < NearBelowDegrees ? eAimStage.Near : eAimStage.Find;
    }

    // How big something has to be at this distance to cover this much of the view.
    public static float Subtend(float distance, float degrees)
    {
        return distance * MathF.Tan(degrees * MathF.PI / 180f);
    }

    // The angle between two points, seen from the eye.
    public static float Apart(Vec3 eye, Vec3 a, Vec3 b)
    {
        float cos = (a - eye).Normalized().Dot((b - eye).Normalized());

        return MathF.Acos(Math.Clamp(cos, -1f, 1f)) * 180f / MathF.PI;
    }

    // Which aims off one spot get a ring, given which is the lead (or -1).
    // An aim inside a ring already drawn would put a second ring inside the
    // first, and two rings round one point read as one broken marker rather
    // than as two throws. The wheel still steps between them.
    public static bool[] Ringed(Vec3 eye, IReadOnlyList<Vec3> aims, int lead)
    {
        var ringed = new bool[aims.Count];
        var drawn = new List<(Vec3 At, float Radius)>();

        if (lead >= 0)
        {
            ringed[lead] = true;
            drawn.Add((aims[lead], RingDegrees));
        }

        for (int index = 0; index < aims.Count; index++)
        {
            if (index == lead)
            {
                continue;
            }

            Vec3 aim = aims[index];

            if (drawn.Any(ring => Apart(eye, ring.At, aim) < ring.Radius + OtherRingDegrees))
            {
                continue;
            }

            ringed[index] = true;
            drawn.Add((aim, OtherRingDegrees));
        }

        return ringed;
    }

    // The crosshair on two throws aimed at one point cannot say which is
    // meant, and the nearer by a hair flips as the hand shakes. The one
    // already loaded stays; the wheel steps to the other.
    public static LineupRecord Chosen(LineupRecord nearest, LineupRecord? loaded)
    {
        if (loaded == null || loaded.client_id == nearest.client_id)
        {
            return nearest;
        }

        float apart = PracticeLineupUtility.AimError(
            nearest.release.yaw,
            nearest.release.pitch,
            loaded.release.yaw,
            loaded.release.pitch
        );

        return apart < CrowdedDegrees ? loaded : nearest;
    }

    public static List<MarkerSegment> Ring(float radius, int segments)
    {
        var ring = new List<MarkerSegment>(segments);

        for (int index = 0; index < segments; index++)
        {
            double a = index * 2 * Math.PI / segments;
            double b = (index + 1) * 2 * Math.PI / segments;

            ring.Add(
                new MarkerSegment(
                    (float)(Math.Cos(a) * radius),
                    (float)(Math.Sin(a) * radius),
                    (float)(Math.Cos(b) * radius),
                    (float)(Math.Sin(b) * radius)
                )
            );
        }

        return ring;
    }

    public const float FacingFrom = 13f;
    public const float FacingTo = 31f;

    // A line ahead of the toes ending in an arrowhead: which way to face.
    public static List<MarkerSegment> Facing()
    {
        return new List<MarkerSegment>
        {
            new MarkerSegment(FacingFrom, 0f, FacingTo, 0f),
            new MarkerSegment(FacingTo - 7f, 4.6f, FacingTo, 0f),
            new MarkerSegment(FacingTo - 7f, -4.6f, FacingTo, 0f),
        };
    }

    public const float LaneHalfWidth = 11f;
    public const float LaneChevronEvery = 30f;
    public const int LaneMaxChevrons = 6;

    // Rails from the stance to where the grenade leaves the hand, chevrons
    // along them, and a bar across the end.
    public static List<MarkerSegment> Lane(float length)
    {
        var lane = new List<MarkerSegment>();
        float start = FacingTo + 6f;

        if (length <= start + 12f)
        {
            return lane;
        }

        lane.Add(new MarkerSegment(start, -LaneHalfWidth, length, -LaneHalfWidth));
        lane.Add(new MarkerSegment(start, LaneHalfWidth, length, LaneHalfWidth));
        lane.Add(new MarkerSegment(length, -LaneHalfWidth - 6f, length, LaneHalfWidth + 6f));

        int chevrons = Math.Min(LaneMaxChevrons, (int)((length - start) / LaneChevronEvery));

        for (int index = 1; index <= chevrons; index++)
        {
            float tip = start + (index * (length - start) / (chevrons + 1));

            lane.Add(new MarkerSegment(tip - 6f, 5.5f, tip, 0f));
            lane.Add(new MarkerSegment(tip - 6f, -5.5f, tip, 0f));
        }

        return lane;
    }

    // Stances close enough to be one place to stand. By distance, not a grid:
    // two throws either side of a grid line are still one spot.
    public static List<LineupSpot> Spots(
        IEnumerable<LineupRecord> lineups,
        float radius,
        float height
    )
    {
        var spots = new List<LineupSpot>();

        foreach (LineupRecord lineup in lineups)
        {
            Vec3 feet = lineup.release.feet_position;
            LineupSpot? found = null;

            foreach (LineupSpot spot in spots)
            {
                if (
                    new Vec3(spot.At.x - feet.x, spot.At.y - feet.y, 0f).LengthXY() <= radius
                    && Math.Abs(spot.At.z - feet.z) <= height
                )
                {
                    found = spot;
                    break;
                }
            }

            if (found != null)
            {
                found.Lineups.Add(lineup);
                continue;
            }

            spots.Add(
                new LineupSpot
                {
                    Key = string.IsNullOrEmpty(lineup.id) ? lineup.client_id : lineup.id!,
                    At = feet,
                    Lineups = new List<LineupRecord> { lineup },
                }
            );
        }

        return spots;
    }

    // Degrees the target sits off the way the player is facing. Source yaw
    // grows anticlockwise, so positive is to the player's left.
    public static float Bearing(float eyeYaw, Vec3 from, Vec3 to)
    {
        float toward = MathF.Atan2(to.y - from.y, to.x - from.x) * 180f / MathF.PI;

        return HudAimGrid.Delta(toward, eyeYaw);
    }

    // Which cell of the ribbon a bearing lands in, left to right, or -1 when it
    // is off either end.
    public static int CompassColumn(float bearing, float halfSpanDegrees, int columns)
    {
        if (columns < 2 || Math.Abs(bearing) > halfSpanDegrees)
        {
            return -1;
        }

        float across = (halfSpanDegrees - bearing) / (2f * halfSpanDegrees);

        return Math.Clamp((int)MathF.Round(across * (columns - 1)), 0, columns - 1);
    }

    private const uint InJump = 1 << 1;
    private const uint InDuck = 1 << 2;
    private const uint InForward = 1 << 3;
    private const uint InBack = 1 << 4;
    private const uint InMoveLeft = 1 << 9;
    private const uint InMoveRight = 1 << 10;
    private const uint InWalk = 1 << 16;

    private const float HeldShare = 0.5f;
    private const int JumpLeadMilliseconds = 500;

    private static readonly (string key, uint bit, string opposite)[] MoveKeys =
    {
        ("W", InForward, "S"),
        ("A", InMoveLeft, "D"),
        ("S", InBack, "W"),
        ("D", InMoveRight, "A"),
    };

    // The recorded run-up read as keys, the same way the web panel reads it:
    // a key counts when it was down for at least half the samples, and a pair
    // of opposites cancels. Null when no key was held long enough to be the
    // instruction.
    public static RunUp? RunUpOf(IReadOnlyList<ApproachSample>? approach)
    {
        if (approach == null || approach.Count < 2)
        {
            return null;
        }

        List<ApproachSample> samples = approach.OrderBy(sample => sample.t).ToList();

        bool Held(uint bit)
        {
            return samples.Count(sample => (sample.buttons & bit) != 0)
                >= samples.Count * HeldShare;
        }

        List<(string key, uint bit, string opposite)> down = MoveKeys
            .Where(entry => Held(entry.bit))
            .ToList();

        List<(string key, uint bit, string opposite)> kept = down
            .Where(entry => !down.Any(other => other.key == entry.opposite))
            .ToList();

        if (kept.Count == 0)
        {
            return null;
        }

        uint mask = kept.Aggregate(0u, (all, entry) => all | entry.bit);
        int first = samples.FindIndex(sample => (sample.buttons & mask) != 0);
        ApproachSample release = samples[^1];

        bool jumped =
            samples.Any(sample =>
                (sample.buttons & InJump) != 0 && sample.t >= release.t - JumpLeadMilliseconds
            )
            || (!release.on_ground && samples.Any(sample => (sample.buttons & InJump) != 0));

        return new RunUp(
            kept.Select(entry => entry.key).ToList(),
            Held(InWalk),
            Held(InDuck),
            jumped,
            Math.Max(0, release.t - samples[first].t),
            first == 0 && samples.Count >= ApproachUtility.MaxSamples,
            samples[first].pos
        );
    }

    public static string RunUpLine(RunUp runUp)
    {
        var keys = new List<string>();

        if (runUp.Walk)
        {
            keys.Add("Shift");
        }

        if (runUp.Crouch)
        {
            keys.Add("Ctrl");
        }

        keys.AddRange(runUp.Keys);

        string seconds = (runUp.Milliseconds / 1000f).ToString(
            "0.0",
            System.Globalization.CultureInfo.InvariantCulture
        );

        return $"Hold {string.Join(" + ", keys)} {seconds}{(runUp.Capped ? "+" : "")}s first";
    }
}
