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
            { "Smoke", "models/weapons/w_eq_smokegrenade_dropped.vmdl" },
            { "Flash", "models/weapons/w_eq_flashbang_dropped.vmdl" },
            { "HighExplosive", "models/weapons/w_eq_fraggrenade_dropped.vmdl" },
            { "Molotov", "models/weapons/w_eq_molotov_dropped.vmdl" },
            { "Decoy", "models/weapons/w_eq_decoy_dropped.vmdl" },
        };

    // Everything that has to be in the map's precache list. A model the server
    // did not precache renders as the ERROR model, and precache only runs at
    // map load -- so this list has to be handed over from the precache hook,
    // not at the moment something wants to draw one.
    public static IEnumerable<string> AllUtilityModels()
    {
        return UtilityToModel.Values;
    }

    // Learned at runtime from a real projectile. The hard-coded table below is
    // a starting guess carried over from CS:GO's naming, and a wrong path shows
    // as the ERROR model -- so the first time anybody actually throws a smoke,
    // the engine's own answer replaces the guess for good.
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
