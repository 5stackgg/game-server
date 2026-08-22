namespace FiveStack.Entities.Practice;

// Player state at the release tick. This is the half a human has to reproduce:
// where to stand, where to look, what to hold.
public class ThrowSnapshot
{
    public Vec3 feet_position { get; set; }

    // AbsOrigin + ViewOffset. Where the projectile actually leaves from, which
    // is not the same as where the player is standing.
    public Vec3 eye_position { get; set; }

    public float pitch { get; set; }
    public float yaw { get; set; }

    public Vec3 velocity { get; set; }
    public float speed { get; set; }

    public bool on_ground { get; set; }
    public bool ducked { get; set; }
    public bool walking { get; set; }

    // Stored raw as well as bucketed. If the Full/Half/Drop thresholds turn out
    // wrong they can be re-derived without re-recording anything.
    public float throw_strength_raw { get; set; }
    public bool jump_throw { get; set; }

    public uint buttons { get; set; }
    public int tick { get; set; }
}
