namespace FiveStack.Entities.Practice;

// One run-up sample in the API's spelling. Flat rather than nested Vec3s, like
// UtilityPathPoint: the panel validates and stores it field by field.
public class UtilityApproachPoint
{
    public int? t { get; set; }

    public float? x { get; set; }
    public float? y { get; set; }
    public float? z { get; set; }

    public float? vx { get; set; }
    public float? vy { get; set; }
    public float? vz { get; set; }

    public float? pitch { get; set; }
    public float? yaw { get; set; }

    public uint? buttons { get; set; }
    public bool? on_ground { get; set; }
    public bool? ducked { get; set; }

    public static UtilityApproachPoint From(ApproachSample sample)
    {
        return new UtilityApproachPoint
        {
            t = sample.t,
            x = sample.pos.x,
            y = sample.pos.y,
            z = sample.pos.z,
            vx = sample.vel.x,
            vy = sample.vel.y,
            vz = sample.vel.z,
            pitch = sample.pitch,
            yaw = sample.yaw,
            buttons = sample.buttons,
            on_ground = sample.on_ground,
            ducked = sample.ducked,
        };
    }

    public ApproachSample? ToSample()
    {
        if (
            t == null
            || x == null
            || y == null
            || z == null
            || vx == null
            || vy == null
            || vz == null
            || pitch == null
            || yaw == null
            || buttons == null
            || on_ground == null
            || ducked == null
        )
        {
            return null;
        }

        return new ApproachSample
        {
            t = t.Value,
            pos = new Vec3(x.Value, y.Value, z.Value),
            vel = new Vec3(vx.Value, vy.Value, vz.Value),
            pitch = pitch.Value,
            yaw = yaw.Value,
            buttons = buttons.Value,
            on_ground = on_ground.Value,
            ducked = ducked.Value,
        };
    }

    // Whole or nothing: a run-up with a hole in it would be acted out with a
    // zeroed sample in the middle, which is a teleport to the world origin.
    public static List<ApproachSample> ToSamples(List<UtilityApproachPoint>? points)
    {
        var samples = new List<ApproachSample>();

        if (points == null)
        {
            return samples;
        }

        foreach (UtilityApproachPoint? point in points)
        {
            ApproachSample? sample = point?.ToSample();

            if (sample == null)
            {
                return new List<ApproachSample>();
            }

            samples.Add(sample.Value);
        }

        return samples;
    }
}
