using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

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

    private readonly ISwiftlyCore _core;
    private readonly UtilityConfig _config;
    private readonly PracticeReplay _replay;
    private readonly ILogger<PracticeSystem> _logger;

    private readonly Dictionary<ulong, PracticeState> _states = new();
    private readonly List<(ulong steamId, string weapon)> _regive = new();

    // Who a solve made invulnerable who had not asked for it, so the flag is
    // handed back rather than left on.
    private readonly HashSet<ulong> _shielded = new();

    public PracticeSystem(
        ISwiftlyCore core,
        UtilityConfig config,
        PracticeReplay replay,
        ILogger<PracticeSystem> logger
    )
    {
        _core = core;
        _config = config;
        _replay = replay;
        _logger = logger;
    }

    // Wired by the plugin rather than injected, the same way the replay's solo
    // check is: the solver already depends on this service's siblings, and
    // asking for it back would close the cycle.
    public Func<bool> SolveRunning { get; set; } = () => false;

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
        _shielded.Remove(steamId);
        _replay.ClearGhosts(steamId);
        _regive.RemoveAll(pending => pending.steamId == steamId);
    }

    public void Reset()
    {
        _states.Clear();
        _regive.Clear();
        _shielded.Clear();
        _replay.ClearAll();
    }

    // The shared second: re-assert the flags the engine keeps resetting, show
    // whoever is timing themselves how long they have been at it, and retire
    // expired previews.
    public void Tick()
    {
        bool solving = SolveRunning();

        foreach (IPlayer player in _core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                continue;
            }

            CCSPlayerPawn? pawn = player.PlayerPawn;

            if (pawn == null || !pawn.IsValid || !player.IsAlive)
            {
                continue;
            }

            if (!_states.TryGetValue(player.SteamID, out PracticeState? state))
            {
                Shield(player.SteamID, pawn, solving);
                continue;
            }

            ApplyFlags(pawn, state, solving);

            if (state.TimerStartedAt != null)
            {
                double elapsed = (DateTime.UtcNow - state.TimerStartedAt.Value).TotalSeconds;
                player.SendCenter($"{elapsed:0.0}s");
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
            IPlayer? player = Find(steamId);
            CCSPlayerPawn? pawn = player?.PlayerPawn;

            if (player == null || !player.IsAlive || pawn == null || !pawn.IsValid)
            {
                continue;
            }

            if (HasWeapon(pawn, weapon))
            {
                continue;
            }

            pawn.ItemServices?.GiveItem(weapon);
        }
    }

    public IPlayer? Find(ulong steamId)
    {
        foreach (IPlayer player in _core.PlayerManager.GetAllPlayers())
        {
            if (player != null && player.IsValid && player.SteamID == steamId)
            {
                return player;
            }
        }

        return null;
    }

    public List<ulong> ConnectedSteamIds()
    {
        return _core
            .PlayerManager.GetAllPlayers()
            .Where(player => player != null && player.IsValid && !player.IsFakeClient)
            .Select(player => player.SteamID)
            .ToList();
    }

    public bool SavePosition(IPlayer player, string name)
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

    public static ThrowSnapshot? Where(IPlayer player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;
        Vector? origin = pawn?.AbsOrigin;

        if (pawn == null || origin == null)
        {
            return null;
        }

        return new ThrowSnapshot
        {
            feet_position = new Vec3(origin.Value.X, origin.Value.Y, origin.Value.Z),
            pitch = pawn.EyeAngles.X,
            yaw = pawn.EyeAngles.Y,
        };
    }

    public static void TeleportTo(IPlayer player, ThrowSnapshot position)
    {
        player.Teleport(
            new Vector(
                position.feet_position.x,
                position.feet_position.y,
                position.feet_position.z
            ),
            new QAngle(position.pitch, position.yaw, 0),
            new Vector(0, 0, 0)
        );
    }

    public List<ThrowSnapshot> SpawnPoints()
    {
        var spawns = new List<ThrowSnapshot>();

        foreach (
            string designer in new[] { "info_player_terrorist", "info_player_counterterrorist" }
        )
        {
            foreach (
                CBaseEntity spawn in _core.EntitySystem.GetAllEntitiesByDesignerName<CBaseEntity>(
                    designer
                )
            )
            {
                Vector? origin = spawn.AbsOrigin;

                if (origin == null)
                {
                    continue;
                }

                spawns.Add(
                    new ThrowSnapshot
                    {
                        feet_position = new Vec3(
                            origin.Value.X,
                            origin.Value.Y,
                            origin.Value.Z
                        ),
                        yaw = spawn.AbsRotation?.Y ?? 0f,
                    }
                );
            }
        }

        return spawns;
    }

    // Somebody who has never run a practice command still gets caught by a
    // solve's HE and molotovs. Only players this shielded are ever handed back
    // to the engine's own answer, so a flag somebody else owns is left alone.
    private void Shield(ulong steamId, CCSPlayerPawn pawn, bool solving)
    {
        if (solving)
        {
            if (pawn.TakesDamage)
            {
                pawn.TakesDamage = false;
                _shielded.Add(steamId);
            }

            return;
        }

        if (_shielded.Remove(steamId) && !pawn.TakesDamage)
        {
            pawn.TakesDamage = true;
        }
    }

    // Re-asserted every second because respawning resets both. Only a move
    // type this plugin set is ever undone: forcing MOVETYPE_WALK on everyone
    // would break ladders and spectating for players who never asked for it.
    private static void ApplyFlags(CCSPlayerPawn pawn, PracticeState state, bool solving)
    {
        if (state.Noclip && pawn.MoveType != MoveType_t.MOVETYPE_NOCLIP)
        {
            SetMoveType(pawn, MoveType_t.MOVETYPE_NOCLIP);
        }
        else if (!state.Noclip && pawn.MoveType == MoveType_t.MOVETYPE_NOCLIP)
        {
            SetMoveType(pawn, MoveType_t.MOVETYPE_WALK);
        }

        // A solve throws hundreds of live grenades at a point somebody may be
        // standing near. Being blown up by the tool you asked for help from is
        // not a practice outcome.
        bool takesDamage = !state.God && !solving;

        if (pawn.TakesDamage != takesDamage)
        {
            pawn.TakesDamage = takesDamage;
        }
    }

    private static void SetMoveType(CCSPlayerPawn pawn, MoveType_t moveType)
    {
        pawn.MoveType = moveType;
        // m_MoveType alone is cosmetic; the engine reads m_nActualMoveType.
        pawn.ActualMoveType = moveType;
        pawn.MoveTypeUpdated();
    }

    private static bool HasWeapon(CCSPlayerPawn pawn, string designerName)
    {
        CPlayer_WeaponServices? weapons = pawn.WeaponServices;

        if (weapons == null)
        {
            return false;
        }

        foreach (CBasePlayerWeapon weapon in weapons.MyValidWeapons)
        {
            if (weapon.DesignerName == designerName)
            {
                return true;
            }
        }

        return false;
    }
}
