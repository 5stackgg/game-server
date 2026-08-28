using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// A straight port of web/composables/useRadarProjection.ts. The panel and the
// in-game HUD draw the same lineups onto the same radar images, so they have to
// agree to the pixel -- a lineup that lands on Window on the site and on Palace
// in game is worse than no map at all. Any change here belongs in both.
public class RadarSplit
{
    public float BoundsTop { get; set; }
    public float BoundsBottom { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
}

public class RadarCalibration
{
    public float Resolution { get; set; }
    public float OffsetX { get; set; }
    public float OffsetY { get; set; }
    public List<RadarSplit> Splits { get; set; } = new List<RadarSplit>();
}

public static class RadarProjection
{
    // The web renders into a 1024 square regardless of the source image size,
    // and the calibration is expressed against that, not against the png.
    public const float Canvas = 1024f;
    public const float Pixels = 1024f;

    public static string NormalizeMapName(string? name)
    {
        string trimmed = (name ?? "").Trim().ToLowerInvariant();

        return trimmed.EndsWith("_night", StringComparison.Ordinal)
            ? trimmed[..^"_night".Length]
            : trimmed;
    }

    // Nuke and Vertigo stack two playable levels on one image; a point inside a
    // split's height band shifts by a percentage of the image, which is what
    // puts the lower level on its own half.
    public static (float X, float Y) Split(float z, IReadOnlyList<RadarSplit>? splits)
    {
        if (splits == null)
        {
            return (0f, 0f);
        }

        foreach (RadarSplit split in splits)
        {
            if (z > split.BoundsBottom && z < split.BoundsTop)
            {
                return (split.OffsetX, split.OffsetY);
            }
        }

        return (0f, 0f);
    }

    public static (float X, float Y) Project(Vec3 point, RadarCalibration calibration)
    {
        (float dx, float dy) = Split(point.z, calibration.Splits);

        float gameX = point.x + calibration.OffsetX;
        float gameY = point.y + calibration.OffsetY;

        float pixelX = gameX / calibration.Resolution + dx / 100f * Pixels;
        float pixelYFromBottom = gameY / calibration.Resolution + dy / 100f * Pixels;

        return (pixelX * (Canvas / Pixels), Canvas - pixelYFromBottom * (Canvas / Pixels));
    }

    // 0..1 across the image, which is what a one-of-N class grid needs. Points
    // off the image are clamped rather than dropped: a marker pinned to the edge
    // still says which way to look.
    public static (float X, float Y) Normalized(Vec3 point, RadarCalibration calibration)
    {
        (float x, float y) = Project(point, calibration);

        return (Math.Clamp(x / Canvas, 0f, 1f), Math.Clamp(y / Canvas, 0f, 1f));
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
