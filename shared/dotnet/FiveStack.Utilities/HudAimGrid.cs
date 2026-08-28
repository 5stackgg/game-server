namespace FiveStack.Utilities;

// Turns a crosshair's yaw/pitch error into the one-of-N cell the aim dot sits
// in. Panorama cannot be handed a number to position by, so the travel a
// dialog variable would have carried is a class group instead.
//
// Everything is measured in multiples of the lineup's OWN tolerance, not in
// degrees. A fixed degree span made the drawn tolerance box a decoration: it
// was the same size for a 0.1 degree lineup and a 2 degree one, so "is the dot
// in the box" answered nothing. Normalising by tolerance means the box is the
// tolerance, on every lineup, and landing the dot inside it is the whole
// instruction.
public static class HudAimGrid
{
    // Where one tolerance lands, as a fraction of the distance from centre to
    // edge. The stylesheet draws the box at exactly this half-extent.
    public const float ToleranceExtent = 0.5f;

    // Past this many tolerances the dot is pinned to the edge; it only has to
    // say "miles off, that way".
    public const float PinAt = 16f;

    public static int Column(float eyeYaw, float targetYaw, float tolerance, int columns)
    {
        return Cell(Delta(targetYaw, eyeYaw), tolerance, columns);
    }

    public static int Row(float eyePitch, float targetPitch, float tolerance, int rows)
    {
        return Cell(Delta(eyePitch, targetPitch), tolerance, rows);
    }

    // Shortest way round, so 359 and 1 are two degrees apart rather than 358.
    public static float Delta(float from, float to)
    {
        float delta = (from - to) % 360f;

        if (delta > 180f)
        {
            delta -= 360f;
        }

        if (delta < -180f)
        {
            delta += 360f;
        }

        return delta;
    }

    // -1..1, where +/-ToleranceExtent is exactly one tolerance out.
    public static float Offset(float degrees, float tolerance)
    {
        if (tolerance <= 0f)
        {
            tolerance = PracticeLineupUtility.DefaultAimTolerance;
        }

        float ratio = degrees / tolerance;
        float magnitude = Math.Abs(ratio);
        float sign = Math.Sign(ratio);

        // Linear inside the tolerance, because that is the range the player is
        // actually working in and it wants every pixel it can get. Outside it,
        // 1/sqrt falls away to the edge without ever quite reaching it.
        if (magnitude <= 1f)
        {
            return sign * magnitude * ToleranceExtent;
        }

        // Normalised so PinAt tolerances lands exactly on the frame edge rather
        // than somewhere short of it.
        float curve = 1f - 1f / MathF.Sqrt(Math.Min(magnitude, PinAt));
        float full = 1f - 1f / MathF.Sqrt(PinAt);

        return sign * (ToleranceExtent + (1f - ToleranceExtent) * (curve / full));
    }

    // The box and dot give fine-grained feedback once you know what they are;
    // this says the same thing in words so you never have to work it out. Axes
    // are only named when they are the part actually out, so a throw that only
    // needs a nudge left does not also nag about pitch.
    public static string Direction(
        float eyeYaw,
        float eyePitch,
        float targetYaw,
        float targetPitch,
        float tolerance
    )
    {
        if (tolerance <= 0f)
        {
            tolerance = PracticeLineupUtility.DefaultAimTolerance;
        }

        float yaw = Delta(targetYaw, eyeYaw);
        float pitch = Delta(eyePitch, targetPitch);

        var parts = new List<string>();

        if (Math.Abs(yaw) > tolerance)
        {
            parts.Add(yaw > 0f ? "RIGHT" : "LEFT");
        }

        if (Math.Abs(pitch) > tolerance)
        {
            parts.Add(pitch > 0f ? "UP" : "DOWN");
        }

        if (parts.Count == 0)
        {
            return "LINED UP - THROW IT";
        }

        return "LOOK " + string.Join(" AND ", parts);
    }

    private static int Cell(float degrees, float tolerance, int count)
    {
        if (count < 2)
        {
            return 0;
        }

        float offset = Math.Clamp(Offset(degrees, tolerance), -1f, 1f);

        return (int)MathF.Round((offset + 1f) / 2f * (count - 1));
    }
}
