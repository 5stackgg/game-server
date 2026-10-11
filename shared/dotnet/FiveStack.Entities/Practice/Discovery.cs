namespace FiveStack.Entities.Practice;

// A lineup discovery the panel hands a render server to throw for real: the
// spot it wants a grenade on and the throws a flight model proposed for it.
public class DiscoveryJob
{
    public string id { get; set; } = "";
    public string map { get; set; } = "";
    public string utility_type { get; set; } = "Smoke";
    public Vec3 target { get; set; }
    public float radius { get; set; }
    public List<DiscoveryCandidate> candidates { get; set; } = new List<DiscoveryCandidate>();

    // Bounds on the whole job, so a long list of candidates cannot hold the
    // server for longer than a discovery is worth.
    public int max_grenades { get; set; } = 400;
    public int max_seconds { get; set; } = 240;
}

// One proposed throw: where to stand, what to do and where to aim.
public class DiscoveryCandidate
{
    public string result_id { get; set; } = "";
    public Vec3 stand { get; set; }

    // "", forward, back, left or right; walk or run; and the jump throw bind.
    public string direction { get; set; } = "";
    public string gait { get; set; } = "";
    public bool jump_throw { get; set; }

    public string strength { get; set; } = "Full";
    public float pitch { get; set; }
    public float yaw { get; set; }
}

// What the server found when it threw a candidate.
public class DiscoveryOutcome
{
    public string result_id { get; set; } = "";

    // verified: a real grenade from this aim lands within the radius.
    // missed: neither the proposal nor anything near it did.
    public string status { get; set; } = "missed";

    // The aim the throw was verified with, which refinement may have moved
    // off the proposal, and the seed that aim throws.
    public float pitch { get; set; }
    public float yaw { get; set; }
    public Vec3 initial_position { get; set; }
    public Vec3 initial_velocity { get; set; }

    public Vec3 landing { get; set; }
    public float distance { get; set; }
    public int bounces { get; set; }
    public int flight_time_ms { get; set; }

    // How far the landing moves for a quarter of a degree of aim error
    // either way, measured by throwing it: the lower, the easier.
    public float forgiveness { get; set; }

    public int grenades { get; set; }
    public List<Vec3> path { get; set; } = new List<Vec3>();
}
