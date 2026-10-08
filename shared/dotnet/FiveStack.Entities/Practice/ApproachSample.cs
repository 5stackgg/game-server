namespace FiveStack.Entities.Practice;

public struct ApproachSample
{
    // Milliseconds relative to the release tick: the release sample is 0 and
    // everything before it is negative.
    public int t { get; set; }

    public Vec3 pos { get; set; }
    public Vec3 vel { get; set; }

    public float pitch { get; set; }
    public float yaw { get; set; }

    // The low 32 bits of ButtonStates[0], the IN_* bits held on that tick.
    public uint buttons { get; set; }

    public bool on_ground { get; set; }
    public bool ducked { get; set; }
}
