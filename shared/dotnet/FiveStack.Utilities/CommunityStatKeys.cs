namespace FiveStack.Utilities;

// The detailed community stats by the names the panel stores them under, which
// are the match stats' names wherever the match side has the same stat.
public static class CommunityStatKeys
{
    public const string Shots = "shots";
    public const string Hits = "hits";
    public const string HitsHead = "hits_head";
    public const string HitsNeck = "hits_neck";
    public const string HitsChest = "hits_chest";
    public const string HitsStomach = "hits_stomach";
    public const string HitsLeftArm = "hits_left_arm";
    public const string HitsRightArm = "hits_right_arm";
    public const string HitsLeftLeg = "hits_left_leg";
    public const string HitsRightLeg = "hits_right_leg";
    public const string DamageTaken = "damage_taken";
    public const string TeamDamage = "team_damage";
    public const string HeDamage = "he_damage";
    public const string MolotovDamage = "molotov_damage";
    public const string FlashAssists = "flash_assists";
    public const string EnemiesFlashed = "enemies_flashed";
    public const string TeamFlashed = "team_flashed";
    public const string FlashDurationMs = "flash_duration_ms";
    public const string KnifeKills = "knife_kills";
    public const string ZeusKills = "zeus_kills";
    public const string HeKills = "he_kills";
    public const string MolotovKills = "molotov_kills";
    public const string NoScopeKills = "no_scope_kills";
    public const string ThruSmokeKills = "thru_smoke_kills";
    public const string ThruWallKills = "thru_wall_kills";
    public const string BlindedKills = "blinded_kills";
    public const string InAirKills = "in_air_kills";
    public const string TeamKills = "team_kills";
    public const string Suicides = "suicides";
    public const string TradeKills = "trade_kills";
    public const string BombPlants = "bomb_plants";
    public const string BombDefuses = "bomb_defuses";
    public const string HostageRescues = "hostage_rescues";

    // The best killstreak is a maximum, not a running total like the rest.
    public const string BestStreak = "best_streak";

    public static readonly IReadOnlyList<string> All =
    [
        Shots,
        Hits,
        HitsHead,
        HitsNeck,
        HitsChest,
        HitsStomach,
        HitsLeftArm,
        HitsRightArm,
        HitsLeftLeg,
        HitsRightLeg,
        DamageTaken,
        TeamDamage,
        HeDamage,
        MolotovDamage,
        FlashAssists,
        EnemiesFlashed,
        TeamFlashed,
        FlashDurationMs,
        KnifeKills,
        ZeusKills,
        HeKills,
        MolotovKills,
        NoScopeKills,
        ThruSmokeKills,
        ThruWallKills,
        BlindedKills,
        InAirKills,
        TeamKills,
        Suicides,
        TradeKills,
        BombPlants,
        BombDefuses,
        HostageRescues,
        BestStreak,
    ];

    // CS2's hitgroup numbers. Generic (0) and gear (10) are hits in no group.
    public static string? HitGroup(int hitGroup)
    {
        return hitGroup switch
        {
            1 => HitsHead,
            2 => HitsChest,
            3 => HitsStomach,
            4 => HitsLeftArm,
            5 => HitsRightArm,
            6 => HitsLeftLeg,
            7 => HitsRightLeg,
            8 => HitsNeck,
            _ => null,
        };
    }
}
