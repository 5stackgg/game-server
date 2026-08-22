using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

public class SmokeOutlineOptions
{
    // The hard entity budget. One beam per occupied voxel is thousands of
    // entities for a single smoke, so the outline is contoured and decimated
    // until it fits this rather than being allowed to grow to fit the shape.
    public int MaxSegments { get; set; } = 48;

    // Horizontal contours through the bloom, low to high. Three reads as a
    // shape; more reads as a scribble and costs the budget the rings need.
    public int MaxLevels { get; set; } = 3;

    public int MinDensity { get; set; } = 1;

    // In grid cells. A voxel boundary is a staircase, and tracing it exactly
    // costs one beam per step; a cell and a bit of slop turns a smoke's
    // circumference into a dozen lines instead of a hundred.
    public float Epsilon { get; set; } = 1.25f;

    public int MinLoopCells { get; set; } = 4;
}

// Decoding and outlining of a measured smoke volume. Pure: both runtimes draw
// the result with their own entities, and neither the packing nor the contour
// tracing has anything to do with the game.
public static class SmokeVolumeUtility
{
    // A measured bloom is tens of thousands of cells. Anything past this is not
    // a smoke, and allocating for it on the game thread is how a practice
    // server stalls.
    public const int MaxCells = 1 << 21;

    // den is two cells per byte, low nibble first, over dx*dy*dz cells in the
    // order (k*dy + j)*dx + i. A den shorter than the grid is not an error: the
    // cells it does not cover are clear.
    public static byte[] Decode(SmokeVolume? volume)
    {
        if (volume == null || volume.dx <= 0 || volume.dy <= 0 || volume.dz <= 0)
        {
            return System.Array.Empty<byte>();
        }

        long count = (long)volume.dx * volume.dy * volume.dz;

        if (count > MaxCells)
        {
            return System.Array.Empty<byte>();
        }

        var density = new byte[count];

        // No grid at all means the volume is only its bounding box, and the box
        // is then the honest answer rather than an empty outline.
        if (string.IsNullOrEmpty(volume.den))
        {
            System.Array.Fill(density, (byte)15);
            return density;
        }

        byte[] packed;

        try
        {
            packed = Convert.FromBase64String(volume.den);
        }
        catch (FormatException)
        {
            return density;
        }

        int cells = Math.Min((int)count, packed.Length * 2);

        for (int index = 0; index < cells; index++)
        {
            byte pair = packed[index >> 1];
            density[index] = (index & 1) == 0 ? (byte)(pair & 0x0F) : (byte)(pair >> 4);
        }

        return density;
    }

    public static byte Density(byte[] density, SmokeVolume volume, int i, int j, int k)
    {
        if (
            i < 0
            || j < 0
            || k < 0
            || i >= volume.dx
            || j >= volume.dy
            || k >= volume.dz
        )
        {
            return 0;
        }

        int index = ((k * volume.dy) + j) * volume.dx + i;

        return index < density.Length ? density[index] : (byte)0;
    }

    public static List<BloomSegment> Outline(
        SmokeVolume? volume,
        SmokeOutlineOptions? options = null
    )
    {
        var segments = new List<BloomSegment>();

        if (volume == null)
        {
            return segments;
        }

        options ??= new SmokeOutlineOptions();
        byte[] density = Decode(volume);

        if (density.Length == 0 || options.MaxSegments <= 0)
        {
            return segments;
        }

        List<int> levels = Levels(density, volume, options);

        if (levels.Count == 0)
        {
            return segments;
        }

        var contours = new List<List<List<(int x, int y)>>>();

        foreach (int level in levels)
        {
            List<List<(int x, int y)>> loops = Loops(density, volume, level, options);

            // Longest first, so a budget that only affords one ring per level
            // spends it on the ring that describes the bloom.
            loops.Sort((left, right) => right.Count.CompareTo(left.Count));
            contours.Add(loops);
        }

        int deepest = contours.Max(loops => loops.Count);

        // Round robin rather than level by level: a budget spent entirely on the
        // bottom contour would say nothing about how tall the smoke is.
        for (int pass = 0; pass < deepest && segments.Count < options.MaxSegments; pass++)
        {
            for (int level = 0; level < contours.Count; level++)
            {
                if (pass >= contours[level].Count)
                {
                    continue;
                }

                List<(int x, int y)> loop = contours[level][pass];

                if (segments.Count + loop.Count - 1 > options.MaxSegments)
                {
                    continue;
                }

                Draw(segments, loop, volume, levels[level]);
            }
        }

        return segments;
    }

    private static void Draw(
        List<BloomSegment> segments,
        List<(int x, int y)> loop,
        SmokeVolume volume,
        int level
    )
    {
        float z = volume.oz + ((level + 0.5f) * volume.vs);

        for (int index = 0; index < loop.Count - 1; index++)
        {
            (int x, int y) from = loop[index];
            (int x, int y) to = loop[index + 1];

            if (from == to)
            {
                continue;
            }

            segments.Add(
                new BloomSegment(
                    new Vec3(volume.ox + (from.x * volume.vs), volume.oy + (from.y * volume.vs), z),
                    new Vec3(volume.ox + (to.x * volume.vs), volume.oy + (to.y * volume.vs), z)
                )
            );
        }
    }

    // Evenly spaced through the occupied slices, offset by half a step so the
    // lowest and highest rings are inside the bloom rather than on the one-cell
    // caps at either end of it.
    private static List<int> Levels(
        byte[] density,
        SmokeVolume volume,
        SmokeOutlineOptions options
    )
    {
        var levels = new List<int>();
        int lowest = -1;
        int highest = -1;

        for (int k = 0; k < volume.dz; k++)
        {
            if (!Occupied(density, volume, k, options.MinDensity))
            {
                continue;
            }

            if (lowest < 0)
            {
                lowest = k;
            }

            highest = k;
        }

        if (lowest < 0)
        {
            return levels;
        }

        int span = highest - lowest + 1;
        int count = Math.Clamp(options.MaxLevels, 1, span);

        for (int level = 0; level < count; level++)
        {
            int k = lowest + (int)((level + 0.5f) * span / count);
            k = Math.Clamp(k, lowest, highest);

            if (!levels.Contains(k))
            {
                levels.Add(k);
            }
        }

        return levels;
    }

    private static bool Occupied(
        byte[] density,
        SmokeVolume volume,
        int k,
        int minDensity
    )
    {
        for (int j = 0; j < volume.dy; j++)
        {
            for (int i = 0; i < volume.dx; i++)
            {
                if (Density(density, volume, i, j, k) >= minDensity)
                {
                    return true;
                }
            }
        }

        return false;
    }

    // Closed loops around the occupied cells of one slice, in grid corner
    // coordinates, simplified. Every loop is returned with its first point
    // repeated last, so a caller can draw it without closing it by hand.
    private static List<List<(int x, int y)>> Loops(
        byte[] density,
        SmokeVolume volume,
        int k,
        SmokeOutlineOptions options
    )
    {
        var edges = new List<((int x, int y) a, (int x, int y) b)>();

        for (int j = 0; j < volume.dy; j++)
        {
            for (int i = 0; i < volume.dx; i++)
            {
                if (Density(density, volume, i, j, k) < options.MinDensity)
                {
                    continue;
                }

                // Counter-clockwise around the cell, so every loop keeps the
                // smoke on its left and a hole inside the bloom comes out as
                // its own loop rather than as a stray line.
                if (Density(density, volume, i, j - 1, k) < options.MinDensity)
                {
                    edges.Add(((i, j), (i + 1, j)));
                }

                if (Density(density, volume, i + 1, j, k) < options.MinDensity)
                {
                    edges.Add(((i + 1, j), (i + 1, j + 1)));
                }

                if (Density(density, volume, i, j + 1, k) < options.MinDensity)
                {
                    edges.Add(((i + 1, j + 1), (i, j + 1)));
                }

                if (Density(density, volume, i - 1, j, k) < options.MinDensity)
                {
                    edges.Add(((i, j + 1), (i, j)));
                }
            }
        }

        return Trace(edges, options);
    }

    private static List<List<(int x, int y)>> Trace(
        List<((int x, int y) a, (int x, int y) b)> edges,
        SmokeOutlineOptions options
    )
    {
        var loops = new List<List<(int x, int y)>>();
        var outgoing = new Dictionary<(int x, int y), List<int>>();

        for (int index = 0; index < edges.Count; index++)
        {
            if (!outgoing.TryGetValue(edges[index].a, out List<int>? from))
            {
                from = new List<int>();
                outgoing[edges[index].a] = from;
            }

            from.Add(index);
        }

        var used = new bool[edges.Count];

        for (int index = 0; index < edges.Count; index++)
        {
            if (used[index])
            {
                continue;
            }

            (int x, int y) start = edges[index].a;
            var loop = new List<(int x, int y)> { start };
            int current = index;

            for (int guard = 0; guard <= edges.Count; guard++)
            {
                used[current] = true;
                loop.Add(edges[current].b);

                if (edges[current].b == start)
                {
                    break;
                }

                int next = Next(edges, outgoing, used, current);

                if (next < 0)
                {
                    break;
                }

                current = next;
            }

            List<(int x, int y)>? simplified = Simplify(loop, options);

            if (simplified != null)
            {
                loops.Add(simplified);
            }
        }

        return loops;
    }

    // Straight ahead first, then the tightest right turn. At a corner where two
    // cells only touch diagonally both continuations are legal, and turning
    // splits the pinch into two loops instead of drawing a line through it.
    private static int Next(
        List<((int x, int y) a, (int x, int y) b)> edges,
        Dictionary<(int x, int y), List<int>> outgoing,
        bool[] used,
        int current
    )
    {
        if (!outgoing.TryGetValue(edges[current].b, out List<int>? candidates))
        {
            return -1;
        }

        (int x, int y) direction = (
            edges[current].b.x - edges[current].a.x,
            edges[current].b.y - edges[current].a.y
        );
        (int x, int y) right = (direction.y, -direction.x);

        int straightMatch = -1;
        int rightMatch = -1;
        int any = -1;

        foreach (int candidate in candidates)
        {
            if (used[candidate])
            {
                continue;
            }

            (int x, int y) heading = (
                edges[candidate].b.x - edges[candidate].a.x,
                edges[candidate].b.y - edges[candidate].a.y
            );

            if (heading == direction)
            {
                straightMatch = candidate;
            }
            else if (heading == right)
            {
                rightMatch = candidate;
            }
            else if (any < 0)
            {
                any = candidate;
            }
        }

        if (straightMatch >= 0)
        {
            return straightMatch;
        }

        return rightMatch >= 0 ? rightMatch : any;
    }

    // Null for a loop too small to be worth a beam: a single stray cell of
    // density is measurement noise, not a shape a player can throw at.
    private static List<(int x, int y)>? Simplify(
        List<(int x, int y)> loop,
        SmokeOutlineOptions options
    )
    {
        if (loop.Count < 4 || loop[0] != loop[^1])
        {
            return null;
        }

        if (Math.Abs(Area(loop)) < options.MinLoopCells)
        {
            return null;
        }

        var points = loop.Select(point => new TrajectoryPoint
            {
                p = new Vec3(point.x, point.y, 0f),
            })
            .ToList();

        List<TrajectoryPoint> simplified = TrajectoryUtility.Simplify(points, options.Epsilon);

        if (simplified.Count < 4)
        {
            return null;
        }

        return simplified
            .Select(point => ((int)MathF.Round(point.p.x), (int)MathF.Round(point.p.y)))
            .ToList();
    }

    // Shoelace, in cells. The loop repeats its first point last, which the sum
    // below relies on rather than closing the polygon itself.
    private static float Area(List<(int x, int y)> loop)
    {
        float sum = 0f;

        for (int index = 0; index < loop.Count - 1; index++)
        {
            sum += (loop[index].x * loop[index + 1].y) - (loop[index + 1].x * loop[index].y);
        }

        return sum / 2f;
    }
}
