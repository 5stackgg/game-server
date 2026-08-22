using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace UtilityPractice;

public class PracticeState
{
    public LineupRecord? Loaded { get; set; }

    // The last query's matches, so .next and .prev walk them in place.
    public List<LineupRecord> Results { get; } = new List<LineupRecord>();
    public int Index { get; set; } = -1;

    public Dictionary<string, ThrowSnapshot> Positions { get; } =
        new Dictionary<string, ThrowSnapshot>(StringComparer.OrdinalIgnoreCase);

    public bool Noclip { get; set; }
    public bool God { get; set; }

    // On by default: a lineup preview is one player's working note, not
    // something the rest of the server asked to look at.
    public bool Solo { get; set; } = true;

    // Off by default: the bloom outline is dozens of entities, and a player who
    // has not asked for it should not be paying for it.
    public bool Bloom { get; set; }

    // On by default, because a person loading a lineup wants to see the line.
    // A capture client turns it off: beams drawn over the map end up in the
    // clip instead of the throw.
    public bool Ghosts { get; set; } = true;

    // Lineups this player has already been told are not exact. Said once per
    // lineup: a warning repeated on every .rethrow is a warning nobody reads.
    public HashSet<string> WarnedInexact { get; } = new HashSet<string>();

    public DateTime? TimerStartedAt { get; set; }
}

// Per-player practice state, plus the one repeating job the whole plugin
// shares. One timer iterating players, never a timer per player: a practice
// server with ten people on it would otherwise be running ten of everything.
public class PracticeSystem
{
    private const int MaxSavedPositions = 32;

    private readonly UtilityConfig _config;
    private readonly PracticeReplay _replay;
    private readonly ILogger<PracticeSystem> _logger;

    private readonly Dictionary<ulong, PracticeState> _states = new();
    private readonly List<(ulong steamId, string weapon)> _regive = new();

    public PracticeSystem(
        UtilityConfig config,
        PracticeReplay replay,
        ILogger<PracticeSystem> logger
    )
    {
        _config = config;
        _replay = replay;
        _logger = logger;
    }

    public PracticeState StateFor(ulong steamId)
    {
        if (!_states.TryGetValue(steamId, out PracticeState? state))
        {
            state = new PracticeState();
            _states[steamId] = state;
        }

        return state;
    }

    // Defaults to true for a player with no state yet, so a preview is never
    // broadcast to the server on the strength of a missing dictionary entry.
    public bool IsSolo(ulong steamId)
    {
        return !_states.TryGetValue(steamId, out PracticeState? state) || state.Solo;
    }

    // Defaults to true for a player with no state yet, matching the config
    // default rather than silently disabling previews for everybody.
    public bool WantsGhosts(ulong steamId)
    {
        return !_states.TryGetValue(steamId, out PracticeState? state) || state.Ghosts;
    }

    public void Forget(ulong steamId)
    {
        _states.Remove(steamId);
        _replay.ClearGhosts(steamId);
        _regive.RemoveAll(pending => pending.steamId == steamId);
    }

    public void Reset()
    {
        _states.Clear();
        _regive.Clear();
        _replay.ClearAll();
    }

    // The shared second: re-assert the flags the engine keeps resetting, show
    // whoever is timing themselves how long they have been at it, and retire
    // expired previews.
    public void Tick()
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot)
            {
                continue;
            }

            if (!_states.TryGetValue(player.SteamID, out PracticeState? state))
            {
                continue;
            }

            CCSPlayerPawn? pawn = player.PlayerPawn.Value;

            if (pawn == null || !pawn.IsValid || !player.PawnIsAlive)
            {
                continue;
            }

            ApplyFlags(pawn, state);

            if (state.TimerStartedAt != null)
            {
                double elapsed = (DateTime.UtcNow - state.TimerStartedAt.Value).TotalSeconds;
                player.PrintToCenter($"{elapsed:0.0}s");
            }
        }

        _replay.Sweep();
    }

    // Called on the release edge, so a thrown grenade is in the player's hand
    // again a tick or two later.
    public void OnThrown(ulong steamId, string utilityType)
    {
        if (!_config.InfiniteUtility)
        {
            return;
        }

        string? weapon = PracticeLineupUtility.WeaponForUtilityType(utilityType);

        if (weapon == null)
        {
            return;
        }

        _regive.Add((steamId, weapon));
    }

    // sv_infinite_ammo also freezes the throw animation and the pin, which
    // makes every recorded release strength wrong; handing the grenade back is
    // the only version that leaves the throw itself alone.
    public void RefillUtility()
    {
        if (_regive.Count == 0)
        {
            return;
        }

        var pending = _regive.ToList();
        _regive.Clear();

        foreach ((ulong steamId, string weapon) in pending)
        {
            CCSPlayerController? player = Utilities.GetPlayerFromSteamId(steamId);

            if (player == null || !player.IsValid || !player.PawnIsAlive)
            {
                continue;
            }

            if (HasWeapon(player, weapon))
            {
                continue;
            }

            player.GiveNamedItem(weapon);
        }
    }

    // The whole bag, every time somebody is alive without it. A practice
    // server that makes you buy your utility before every throw is a practice
    // server nobody uses; mp_maxmoney only helps if you remember to go and buy.
    private static readonly string[] Loadout = new[]
    {
        "weapon_smokegrenade",
        "weapon_flashbang",
        "weapon_hegrenade",
        "weapon_molotov",
        "weapon_incgrenade",
        "weapon_decoy",
    };

    public void GiveUtility(CCSPlayerController player)
    {
        if (!player.IsValid || !player.PawnIsAlive)
        {
            return;
        }

        bool isCt = player.Team == CsTeam.CounterTerrorist;

        foreach (string weapon in Loadout)
        {
            // One firebomb per side, and it is not the same one.
            if (weapon == "weapon_molotov" && isCt)
            {
                continue;
            }

            if (weapon == "weapon_incgrenade" && !isCt)
            {
                continue;
            }

            if (HasWeapon(player, weapon))
            {
                continue;
            }

            player.GiveNamedItem(weapon);
        }
    }

    public List<ulong> ConnectedSteamIds()
    {
        return Utilities
            .GetPlayers()
            .Where(player => player.IsValid && !player.IsBot)
            .Select(player => player.SteamID)
            .ToList();
    }

    public bool SavePosition(CCSPlayerController player, string name)
    {
        PracticeState state = StateFor(player.SteamID);

        if (
            state.Positions.Count >= MaxSavedPositions
            && !state.Positions.ContainsKey(name)
        )
        {
            return false;
        }

        ThrowSnapshot? here = Where(player);

        if (here == null)
        {
            return false;
        }

        state.Positions[name] = here;
        return true;
    }

    public static ThrowSnapshot? Where(CCSPlayerController player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;
        Vector? origin = pawn?.AbsOrigin;

        if (pawn == null || origin == null)
        {
            return null;
        }

        return new ThrowSnapshot
        {
            feet_position = new Vec3(origin.X, origin.Y, origin.Z),
            pitch = pawn.EyeAngles.X,
            yaw = pawn.EyeAngles.Y,
        };
    }

    public static void TeleportTo(CCSPlayerController player, ThrowSnapshot position)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;

        if (pawn == null || !pawn.IsValid)
        {
            return;
        }

        // Yaw only for the body. A pawn's rotation is which way it faces, and
        // a lineup's pitch is where the player is LOOKING -- feeding -63 into
        // the body lies the model on its back. The aim goes on the eyes below.
        pawn.Teleport(
            new Vector(
                position.feet_position.x,
                position.feet_position.y,
                position.feet_position.z
            ),
            new QAngle(0, position.yaw, 0),
            new Vector(0, 0, 0)
        );

        pawn.EyeAngles.X = position.pitch;
        pawn.EyeAngles.Y = position.yaw;
        pawn.EyeAngles.Z = 0;
    }

    public static List<ThrowSnapshot> SpawnPoints()
    {
        var spawns = new List<ThrowSnapshot>();

        foreach (string designer in new[] { "info_player_terrorist", "info_player_counterterrorist" })
        {
            foreach (CBaseEntity spawn in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(designer))
            {
                Vector? origin = spawn.AbsOrigin;

                if (origin == null)
                {
                    continue;
                }

                spawns.Add(
                    new ThrowSnapshot
                    {
                        feet_position = new Vec3(origin.X, origin.Y, origin.Z),
                        yaw = spawn.AbsRotation?.Y ?? 0f,
                    }
                );
            }
        }

        return spawns;
    }

    // Re-asserted every second because respawning resets both. Only a move
    // type this plugin set is ever undone: forcing MOVETYPE_WALK on everyone
    // would break ladders and spectating for players who never asked for it.
    private static void ApplyFlags(CCSPlayerPawn pawn, PracticeState state)
    {
        if (state.Noclip && pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP)
        {
            SetMoveType(pawn, MoveType_t.MOVETYPE_NOCLIP);
        }
        else if (!state.Noclip && pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP)
        {
            SetMoveType(pawn, MoveType_t.MOVETYPE_WALK);
        }

        bool takesDamage = !state.God;

        if (pawn.TakesDamage != takesDamage)
        {
            pawn.TakesDamage = takesDamage;
        }
    }

    private static void SetMoveType(CCSPlayerPawn pawn, MoveType_t moveType)
    {
        pawn.MoveType = moveType;
        // m_MoveType alone is cosmetic; the engine reads m_nActualMoveType.
        Schema.SetSchemaValue(pawn.Handle, "CBaseEntity", "m_nActualMoveType", (byte)moveType);
        Utilities.SetStateChanged(pawn, "CBaseEntity", "m_MoveType");
    }

    private static bool HasWeapon(CCSPlayerController player, string designerName)
    {
        CPlayer_WeaponServices? weapons = player.PlayerPawn.Value?.WeaponServices;

        if (weapons == null)
        {
            return false;
        }

        foreach (var handle in weapons.MyWeapons)
        {
            if (handle.Value?.DesignerName == designerName)
            {
                return true;
            }
        }

        return false;
    }
}
