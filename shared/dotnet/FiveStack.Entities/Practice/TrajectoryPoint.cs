namespace FiveStack.Entities.Practice;

public class TrajectoryPoint
{
    public Vec3 p { get; set; }
    public int t { get; set; }

    // Sampling is lossy, but a bounce is where the path changes direction, so
    // simplification must never drop one.
    public bool bounce { get; set; }
}
