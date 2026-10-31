namespace FiveStack.Utilities;

// Weapons by the names the match stats keep in player_kills."with": the event's
// weapon without its weapon_ prefix, except that every knife is one knife and
// a silenced gun is one gun with its silencer on or off.
public static class CommunityWeapons
{
    public const string Knife = "knife";

    // CS2's guns by item definition index. Anything else a player can hold or
    // be hurt by (knives, fists, grenades, the taser, the bomb, the world, and
    // what other modes add) fires no bullets.
    private static readonly Dictionary<int, string> Firearms = new()
    {
        [1] = "deagle",
        [2] = "elite",
        [3] = "fiveseven",
        [4] = "glock",
        [7] = "ak47",
        [8] = "aug",
        [9] = "awp",
        [10] = "famas",
        [11] = "g3sg1",
        [13] = "galilar",
        [14] = "m249",
        [16] = "m4a1",
        [17] = "mac10",
        [19] = "p90",
        [23] = "mp5sd",
        [24] = "ump45",
        [25] = "xm1014",
        [26] = "bizon",
        [27] = "mag7",
        [28] = "negev",
        [29] = "sawedoff",
        [30] = "tec9",
        [32] = "hkp2000",
        [33] = "mp7",
        [34] = "mp9",
        [35] = "nova",
        [36] = "p250",
        [38] = "scar20",
        [39] = "sg556",
        [40] = "ssg08",
        [60] = "m4a1_silencer",
        [61] = "usp_silencer",
        [63] = "cz75a",
        [64] = "revolver",
    };

    private static readonly HashSet<string> FirearmNames = [.. Firearms.Values];

    // Events may name a silenced gun with its silencer off apart, which would
    // split one gun's shots, hits and kills over two keys.
    private const string SilencerOff = "_silencer_off";

    // CS2 builds these guns on the class of another, so a name read from the
    // entity rather than from the item can be the other gun's: the M4A1-S's
    // class is the M4A4's, the USP-S's the P2000's, the CZ75's the P250's, the
    // R8's the Deagle's and the MP5-SD's the MP7's.
    private static readonly HashSet<string> SharedClasses =
    [
        "m4a1",
        "hkp2000",
        "p250",
        "deagle",
        "mp7",
    ];

    public static string Normalize(string? weapon)
    {
        string name = (weapon ?? "").Trim().ToLowerInvariant();

        if (name.StartsWith("weapon_"))
        {
            name = name["weapon_".Length..];
        }

        if (name.EndsWith(SilencerOff))
        {
            name = name[..^"_off".Length];
        }

        return name.StartsWith(Knife) || name == "bayonet" ? Knife : name;
    }

    public static bool IsFirearm(string? weapon)
    {
        return FirearmNames.Contains(Normalize(weapon));
    }

    public static string? FirearmByDefinition(int index)
    {
        return Firearms.GetValueOrDefault(index);
    }

    // Whether the name may stand for another gun, which only the item can tell.
    public static bool SharesAClass(string? weapon)
    {
        return SharedClasses.Contains(Normalize(weapon));
    }

    // The gun on the class of another, by the class's gun.
    private static readonly Dictionary<string, string> OnAClass = new()
    {
        ["m4a1_silencer"] = "m4a1",
        ["usp_silencer"] = "hkp2000",
        ["cz75a"] = "p250",
        ["revolver"] = "deagle",
        ["mp5sd"] = "mp7",
    };

    // The event's gun by the item in hand, which is taken only when it is that
    // gun or the one on its class: a player who switched guns since keeps the
    // event's name.
    public static string InHand(string weapon, int? definitionIndex)
    {
        if (!SharesAClass(weapon) || definitionIndex == null)
        {
            return weapon;
        }

        string? held = FirearmByDefinition(definitionIndex.Value);

        return held != null && OnAClass.GetValueOrDefault(held, held) == Normalize(weapon)
            ? held
            : weapon;
    }

    // A molotov and an incendiary both kill by their fire, the inferno.
    public static string? KillType(string? weapon)
    {
        return Normalize(weapon) switch
        {
            Knife => CommunityStatKeys.KnifeKills,
            "taser" => CommunityStatKeys.ZeusKills,
            "hegrenade" => CommunityStatKeys.HeKills,
            "inferno" or "molotov" => CommunityStatKeys.MolotovKills,
            _ => null,
        };
    }

    public static string? DamageType(string? weapon)
    {
        return Normalize(weapon) switch
        {
            "hegrenade" => CommunityStatKeys.HeDamage,
            "inferno" or "molotov" => CommunityStatKeys.MolotovDamage,
            _ => null,
        };
    }
}
