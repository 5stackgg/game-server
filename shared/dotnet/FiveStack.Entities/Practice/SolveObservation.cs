namespace FiveStack.Entities.Practice;

// What the server did with a candidate. This is the only measurement in the
// solver: no model produced it, a real grenade did.
public class SolveObservation
{
    public SolveCandidate candidate { get; set; }
    public Vec3 landing { get; set; }

    // Distance from the requested target. float.MaxValue for a grenade that
    // never reported, so a lost projectile can never win.
    public float distance { get; set; } = float.MaxValue;

    public bool landed { get; set; }
    public int bounces { get; set; }
}
