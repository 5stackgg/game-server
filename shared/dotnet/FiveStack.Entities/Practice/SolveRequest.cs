namespace FiveStack.Entities.Practice;

// One question for the solver: land this kind of utility on this point, thrown
// from here.
public class SolveRequest
{
    public string map { get; set; } = "";
    public string utility_type { get; set; } = "Smoke";
    public string side { get; set; } = "TERRORIST";
    public string name { get; set; } = "";

    public Vec3 target { get; set; }

    // Where the grenade leaves from and where the player stands to do it. Both
    // are carried because a lineup has to tell a human where to stand, and the
    // eye is what the throw is actually computed from.
    public Vec3 eye { get; set; }
    public Vec3 feet { get; set; }

    public float tolerance { get; set; }
    public int max_grenades { get; set; }
    public int batch_size { get; set; }
    public float max_seconds { get; set; }

    // The strength buckets calibration cleared. Empty is not "search them all",
    // it is "nothing is safe to search".
    public List<string> strengths { get; set; } = new List<string>();

    public string requested_by { get; set; } = "";
}
