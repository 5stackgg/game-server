using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// Name tables and lookup helpers shared by both plugin runtimes.
public static class PracticeLineupUtility
{
    // designer name of the projectile entity -> e_utility_types value
    private static readonly Dictionary<string, string> ProjectileToUtility =
        new()
        {
            { "smokegrenade_projectile", "Smoke" },
            { "flashbang_projectile", "Flash" },
            { "hegrenade_projectile", "HighExplosive" },
            { "molotov_projectile", "Molotov" },
            { "incendiarygrenade_projectile", "Molotov" },
            { "decoy_projectile", "Decoy" },
        };

    // e_utility_types value -> the item a player is given to reproduce it
    private static readonly Dictionary<string, string> UtilityToWeapon =
        new()
        {
            { "Smoke", "weapon_smokegrenade" },
            { "Flash", "weapon_flashbang" },
            { "HighExplosive", "weapon_hegrenade" },
            { "Molotov", "weapon_molotov" },
            { "Decoy", "weapon_decoy" },
        };

    // e_utility_types value -> the world model shown floating over a lineup's
    // stance, so a player can see WHAT to throw before walking to the spot.
    private static readonly Dictionary<string, string> UtilityToModel =
        new()
        {
            { "Smoke", "weapons/models/grenade/smokegrenade/weapon_smokegrenade.vmdl" },
            { "Flash", "weapons/models/grenade/flashbang/weapon_flashbang.vmdl" },
            { "HighExplosive", "weapons/models/grenade/hegrenade/weapon_hegrenade.vmdl" },
            { "Molotov", "weapons/models/grenade/molotov/weapon_molotov.vmdl" },
            { "Decoy", "weapons/models/grenade/decoy/weapon_decoy.vmdl" },
        };

    // Everything that has to be in the map's precache list. A model the server
    // did not precache renders as the ERROR model, and precache only runs at
    // map load -- so this list has to be handed over from the precache hook,
    // not at the moment something wants to draw one.
    public static IEnumerable<string> AllUtilityModels()
    {
        return UtilityToModel.Values;
    }

    // The engine's own answer, harvested at map load from a throwaway weapon
    // entity of each type and topped up from any projectile that actually
    // flies. The table above is only a fallback: CS2 renamed these out of
    // CS:GO's models/weapons/w_eq_* scheme, and a path that is wrong by one
    // character renders as the ERROR model with no other complaint.
    private static readonly Dictionary<string, string> LearnedModels = new();

    public static void LearnUtilityModel(string utilityType, string? model)
    {
        if (string.IsNullOrEmpty(model) || !model.EndsWith(".vmdl"))
        {
            return;
        }

        LearnedModels[utilityType] = model;
    }

    public static string? ModelForUtilityType(string utilityType)
    {
        if (LearnedModels.TryGetValue(utilityType, out string? learned))
        {
            return learned;
        }

        return UtilityToModel.TryGetValue(utilityType, out string? model)
            ? model
            : null;
    }

    private static readonly HashSet<string> GrenadeWeapons =
        new()
        {
            "weapon_smokegrenade",
            "weapon_flashbang",
            "weapon_hegrenade",
            "weapon_molotov",
            "weapon_incgrenade",
            "weapon_decoy",
        };

    public static string? UtilityTypeForProjectile(string designerName)
    {
        return ProjectileToUtility.TryGetValue(designerName, out string? type) ? type : null;
    }

    // Shortest way round the circle between two angles.
    public static float AngleGap(float a, float b)
    {
        float gap = Math.Abs(a - b) % 360f;

        return gap > 180f ? 360f - gap : gap;
    }

    // How wrong the crosshair is for this throw, in degrees: the worse of the
    // two axes, because being dead on the yaw does not help if the pitch is off.
    public static float AimError(float eyeYaw, float eyePitch, float yaw, float pitch)
    {
        return Math.Max(AngleGap(eyeYaw, yaw), AngleGap(eyePitch, pitch));
    }

    // 0 when the crosshair is inside the lineup's tolerance, 1 when it is a
    // long way outside, and a ramp between the two. What the reticle's colour
    // is a picture of: green means throw it, red means keep looking.
    public static float AimMiss(float error, float tolerance)
    {
        if (tolerance <= 0f)
        {
            tolerance = DefaultAimTolerance;
        }

        if (error <= tolerance)
        {
            return 0f;
        }

        // Red at the point where the crosshair is nowhere near, not at some
        // multiple of a tolerance that may itself be tiny -- otherwise a 0.1
        // degree lineup would read fully red one degree off, which is where
        // almost every attempt starts.
        float span = Math.Max(AimMissSpanDegrees - tolerance, 0.01f);

        return Math.Clamp((error - tolerance) / span, 0f, 1f);
    }

    // The same green-to-red idea as the aim, for where the player's feet are.
    // Position and angle are the two halves of a lineup and a player has no way
    // to tell which one they have wrong, so both say so the same way.
    public static float StanceMiss(float distance)
    {
        if (distance <= StanceToleranceUnits)
        {
            return 0f;
        }

        return Math.Clamp(
            (distance - StanceToleranceUnits) / (StanceMissSpanUnits - StanceToleranceUnits),
            0f,
            1f
        );
    }

    // Close enough to stand. Deliberately far tighter than SpotRadius, which
    // asks "are these the same place" -- this asks "are you ON it".
    public const float StanceToleranceUnits = 8f;

    // Fully red this far from the recorded spot.
    public const float StanceMissSpanUnits = 48f;

    // A lineup that never said how precise it is.
    public const float DefaultAimTolerance = 0.35f;

    // Fully red this far off the recorded angle.
    public const float AimMissSpanDegrees = 6f;

    // Groups stance positions that are close enough to be the same place to
    // stand, and reports the distinct kinds of grenade thrown from each. What
    // floats over a spot answers "what do I bring here" -- so two smokes from
    // one position are one smoke, and a smoke plus a flash are two.
    public static List<(float x, float y, float z, List<string> types)> UtilityBySpot(
        IEnumerable<(float x, float y, float z, string utilityType)> throws,
        float radius,
        float height
    )
    {
        var spots = new List<(float x, float y, float z, List<string> types)>();

        foreach ((float x, float y, float z, string utilityType) throwFrom in throws)
        {
            List<string>? types = null;

            foreach ((float x, float y, float z, List<string> types) spot in spots)
            {
                float dx = spot.x - throwFrom.x;
                float dy = spot.y - throwFrom.y;

                if (
                    Math.Sqrt((dx * dx) + (dy * dy)) <= radius
                    && Math.Abs(spot.z - throwFrom.z) <= height
                )
                {
                    types = spot.types;
                    break;
                }
            }

            if (types == null)
            {
                types = new List<string>();
                spots.Add((throwFrom.x, throwFrom.y, throwFrom.z, types));
            }

            if (!types.Contains(throwFrom.utilityType))
            {
                types.Add(throwFrom.utilityType);
            }
        }

        return spots;
    }

    // Type paired with the weapon that carries its model, for the map-load
    // harvest. Spawning the real weapon is the only way to get a path that is
    // certainly correct AND certainly precached -- our own precache list can
    // only ever contain paths we guessed.
    public static IEnumerable<KeyValuePair<string, string>> UtilityWeapons()
    {
        return UtilityToWeapon;
    }

    public static string? WeaponForUtilityType(string utilityType)
    {
        return UtilityToWeapon.TryGetValue(utilityType, out string? weapon) ? weapon : null;
    }

    public static bool IsGrenadeWeapon(string designerName)
    {
        return GrenadeWeapons.Contains(designerName);
    }

    // What the grenade in a player's hand would record as, so a command that
    // takes no utility argument can read the intent off the loadout instead of
    // guessing at a smoke. weapon_incgrenade has no entry in the table above --
    // it is the CT molotov, and it is a Molotov lineup either way.
    public static string? UtilityTypeForWeapon(string designerName)
    {
        if (designerName == "weapon_incgrenade")
        {
            return "Molotov";
        }

        foreach ((string utilityType, string weapon) in UtilityToWeapon)
        {
            if (weapon == designerName)
            {
                return utilityType;
            }
        }

        return null;
    }

    // The API's spelling is the one that counts, and the mismatch that matters
    // is "HE": the demo parser emits it, the panel's enum does not have it, and
    // a wrong value here stores a lineup nobody can find rather than failing.
    private static readonly Dictionary<string, string> UtilityTypeAliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "HE", "HighExplosive" },
            { "HEGrenade", "HighExplosive" },
            { "HighExplosive", "HighExplosive" },
            { "Smoke", "Smoke" },
            { "SmokeGrenade", "Smoke" },
            { "Flash", "Flash" },
            { "Flashbang", "Flash" },
            { "Molotov", "Molotov" },
            { "Incendiary", "Molotov" },
            { "Decoy", "Decoy" },
        };

    public static string NormalizeUtilityType(string utilityType)
    {
        return UtilityTypeAliases.TryGetValue(utilityType, out string? normalized)
            ? normalized
            : utilityType;
    }

    // Everything a query could have meant, nearest first, so .next and .prev
    // walk the same set the player was thinking of. Resolve picks one out of
    // this; it does not narrow it further.
    /// <summary>
    /// Exact lookup, for a load the panel asked for rather than one a player
    /// typed. `.load` matches names loosely because a human is guessing at one;
    /// a panel already knows exactly which lineup it means, and picking a
    /// near-miss there would stand somebody on the wrong throw without saying
    /// so.
    ///
    /// Both keys are checked: a saved lineup is addressed by its panel `id`,
    /// and a scratch throw sent for a test has only the plugin-side
    /// `client_id`.
    /// </summary>
    public static LineupRecord? ById(IEnumerable<LineupRecord> lineups, string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return lineups.FirstOrDefault(lineup =>
            string.Equals(lineup.id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(lineup.client_id, id, StringComparison.OrdinalIgnoreCase)
        );
    }

    public static List<LineupRecord> Filter(
        IEnumerable<LineupRecord> lineups,
        string query,
        Vec3? near = null
    )
    {
        IEnumerable<LineupRecord> matches = lineups;

        if (!string.IsNullOrWhiteSpace(query))
        {
            matches = matches.Where(lineup =>
                lineup.name.Contains(query, StringComparison.OrdinalIgnoreCase)
            );
        }

        if (near == null)
        {
            return matches.ToList();
        }

        Vec3 from = near.Value;

        return matches
            .OrderBy(lineup => (lineup.release.feet_position - from).Length())
            .ToList();
    }

    // Exact name, then unique prefix, then nearest to the player. Resolution
    // order matters: a player who typed the whole name should never be handed
    // something else because they happen to be standing next to another lineup.
    public static LineupRecord? Resolve(
        IEnumerable<LineupRecord> lineups,
        string query,
        Vec3? near = null
    )
    {
        var candidates = lineups.ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            LineupRecord? exact = candidates.FirstOrDefault(lineup =>
                string.Equals(lineup.name, query, StringComparison.OrdinalIgnoreCase)
            );
            if (exact != null)
            {
                return exact;
            }

            var prefixed = candidates
                .Where(lineup =>
                    lineup.name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (prefixed.Count == 1)
            {
                return prefixed[0];
            }

            var contained = candidates
                .Where(lineup =>
                    lineup.name.Contains(query, StringComparison.OrdinalIgnoreCase)
                )
                .ToList();
            if (contained.Count == 1)
            {
                return contained[0];
            }

            candidates = prefixed.Count > 0 ? prefixed : contained;
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        if (near == null)
        {
            return candidates[0];
        }

        Vec3 from = near.Value;
        return candidates
            .OrderBy(lineup => (lineup.release.feet_position - from).Length())
            .First();
    }
}
