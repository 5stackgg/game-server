using FiveStack.Enums;

namespace FiveStack.Entities.Practice;

// One recorded lineup, as it crosses the wire to the panel. Field names match
// the API's ingest contract exactly.
public class LineupRecord
{
    public string? id { get; set; }

    // Plugin-generated, so a retry after a timeout cannot create a duplicate.
    public string client_id { get; set; } = "";

    public string map { get; set; } = "";
    public string? workshop_map_id { get; set; }
    public string name { get; set; } = "";

    public string utility_type { get; set; } = nameof(eUtilityType.Smoke);
    public string side { get; set; } = "TERRORIST";
    public string visibility { get; set; } = nameof(eLineupVisibility.Private);
    public string author_steam_id { get; set; } = "";

    public ThrowSnapshot release { get; set; } = new ThrowSnapshot();

    // The engine's own physics seed, straight off the projectile. Replaying
    // from these reproduces the throw exactly; the eye angles above only
    // approximate it, because the release adds the player's own velocity.
    public Vec3 initial_position { get; set; }
    public Vec3 initial_velocity { get; set; }

    public Vec3 detonation_position { get; set; }
    public int bounces { get; set; }
    public float flight_time { get; set; }

    public string technique { get; set; } = nameof(eThrowTechnique.Stationary);
    public string? strength { get; set; }

    // How close the crosshair has to be, in degrees, before this throw counts
    // as lined up. Per lineup: a tight one wants a green zone you can only
    // reach deliberately, a forgiving one wants a green zone you can find at a
    // glance. Zero or missing means fall back to the plugin's own default.
    public float aim_tolerance { get; set; }

    // The author's write-up, when there is one. The plugin only ever says that
    // it EXISTS -- a paragraph does not belong in centre text.
    public string? description { get; set; }

    // The panel's word for how this lineup was arrived at: exact, derived or
    // low. Null for a lineup recorded in this session, which has not been
    // through the panel yet, and for anything an older panel returned.
    public string? confidence { get; set; }

    public List<TrajectoryPoint> trajectory { get; set; } = new List<TrajectoryPoint>();

    // The measured bloom, when the panel has one. Arrives with the trajectory
    // artifact rather than with the library row, and is absent for everything
    // that is not a smoke.
    public SmokeVolume? smoke_volume { get; set; }

    // A trajectory only replays under the physics it was recorded with.
    public int recorded_tickrate { get; set; }
    public string plugin_runtime { get; set; } = "";
    public string plugin_version { get; set; } = "";

    public const string Exact = "exact";

    // m_vInitialVelocity is never zero for a grenade that was actually thrown,
    // so this is what separates a lineup with a physics seed from one without.
    public bool HasPhysicsSeed()
    {
        return initial_velocity.Length() > 0f;
    }

    // Whether the engine can be handed this seed and reproduce the throw.
    //
    // A seed is not the same statement as an exact lineup, even where the two
    // travel together today: the panel stamps a plugin-recorded lineup "exact"
    // whether or not it captured a seed, and a mined lineup that later acquires
    // one is still a path fitted to a demo. Both halves are required, and an
    // absent confidence counts as no -- an older panel does not send the field
    // at all, and defaulting that to exact would put the bug back on precisely
    // the deployments least able to spot it.
    public bool IsExactlyReplayable()
    {
        return HasPhysicsSeed()
            && string.Equals(confidence, Exact, StringComparison.OrdinalIgnoreCase);
    }

    // True only where the panel said so: an unknown provenance is not the same
    // as a bad one, and warning about every lineup an older panel returns would
    // teach a player to ignore the warning that matters.
    public bool IsKnownInexact()
    {
        return !string.IsNullOrEmpty(confidence)
            && !string.Equals(confidence, Exact, StringComparison.OrdinalIgnoreCase);
    }
}
