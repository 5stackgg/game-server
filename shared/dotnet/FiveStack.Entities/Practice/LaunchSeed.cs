namespace FiveStack.Entities.Practice;

// What the engine is handed to start a grenade: the point it appears at and the
// velocity it leaves with. The same pair LineupRecord stores as
// initial_position / initial_velocity, which is why a solved throw is exactly
// replayable without anything else being saved.
public struct LaunchSeed
{
    public Vec3 position { get; set; }
    public Vec3 velocity { get; set; }

    // Unit throw direction and the speed along it, kept apart so calibration
    // can fault the aim and the strength separately -- a wrong direction and a
    // wrong speed are different bugs with different fixes.
    public Vec3 direction { get; set; }
    public float speed { get; set; }
}
