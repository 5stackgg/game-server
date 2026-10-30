using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// Valve's overview frame, the one CS2's own radar images are drawn in: pos_x and
// pos_y are the world point at the image's top left corner and scale is world
// units per pixel of a 1024 square. metadata.json stores that as resolution =
// scale and offset = (-pos_x, 1024 * scale - pos_y).
public class RadarLevel
{
    public string Name { get; set; } = "";
    public float MaxZ { get; set; }
}

public class RadarCalibration
{
    public float Resolution { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public List<RadarLevel> Levels { get; set; } = new List<RadarLevel>();
}

public static class RadarProjection
{
    public const float Pixels = 1024f;

    public static string NormalizeMapName(string? name)
    {
        string trimmed = (name ?? "").Trim().ToLowerInvariant();

        return trimmed.EndsWith("_night", StringComparison.Ordinal)
            ? trimmed[..^"_night".Length]
            : trimmed;
    }

    // A level is a floor CS2 draws on an image of its own, in the same frame as
    // the main one, for anything below its maxZ. Null is the main image.
    public static string? Level(float z, RadarCalibration calibration)
    {
        RadarLevel? below = null;

        foreach (RadarLevel level in calibration.Levels)
        {
            if (z < level.MaxZ && (below == null || level.MaxZ < below.MaxZ))
            {
                below = level;
            }
        }

        return below?.Name;
    }

    public static (float X, float Y) Project(Vec3 point, RadarCalibration calibration)
    {
        return (
            (point.x + calibration.OffsetX) / calibration.Resolution,
            Pixels - (point.y + calibration.OffsetY) / calibration.Resolution
        );
    }

    // 0..1 across the image, which is what a one-of-N class grid needs. Points
    // off the image are clamped rather than dropped: a marker pinned to the edge
    // still says which way to look.
    public static (float X, float Y) Normalized(Vec3 point, RadarCalibration calibration)
    {
        (float x, float y) = Project(point, calibration);

        return (Math.Clamp(x / Pixels, 0f, 1f), Math.Clamp(y / Pixels, 0f, 1f));
    }

    public static (int Column, int Row) Cell(
        Vec3 point,
        RadarCalibration calibration,
        int columns,
        int rows
    )
    {
        (float x, float y) = Normalized(point, calibration);

        return (
            (int)MathF.Round(x * (columns - 1)),
            (int)MathF.Round(y * (rows - 1))
        );
    }
}
