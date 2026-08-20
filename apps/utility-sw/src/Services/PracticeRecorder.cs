using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// Turns a thrown grenade into a reproducible lineup.
//
// The engine hands us both halves directly, so none of this is guesswork:
// CBaseCSGrenadeProjectile carries m_vInitialPosition/m_vInitialVelocity (the
// physics seed) and CBaseGrenade carries m_hThrower (who owns it). That last
// one is why several players can practise in one server without their throws
// crossing: a projectile names its own owner, so nothing is keyed on "whoever
// threw last".
public class PracticeRecorder
{
    // Guard rails, so a stuck projectile or a spammed throw cannot grow
    // unbounded on a long-lived practice server.
    private const int MaxTrackedProjectiles = 64;
    private const int MaxRawPoints = 2048;
    private const int ForceFinalizeTicks = 64 * 30;
    private const int MaxHistoryPerPlayer = 20;

    // Sample every other tick: 32Hz is well past what a replayed line needs,
    // and bounces are captured exactly regardless via m_nBounces.
    private const int SampleEveryTicks = 2;

    private readonly ISwiftlyCore _core;
    private readonly ILogger<PracticeRecorder> _logger;

    private class ArmedState
    {
        public bool PinPulled;
        public bool Released;
        public ThrowSnapshot? Frozen;
    }

    private class TrackedProjectile
    {
        public required ulong ThrowerSteamId;
        public required string UtilityType;
        public required ThrowSnapshot Release;
        public required int StartTick;
        public Vec3 InitialPosition;
        public Vec3 InitialVelocity;
        public int LastBounces;
        public List<TrajectoryPoint> Raw = new List<TrajectoryPoint>();
    }

    private readonly Dictionary<ulong, ArmedState> _armed = new();
    private readonly Dictionary<ulong, ThrowSnapshot> _pending = new();
    private readonly Dictionary<uint, TrackedProjectile> _tracked = new();
    private readonly Dictionary<ulong, List<LineupRecord>> _history = new();

    private int _tick;

    public PracticeRecorder(ISwiftlyCore core, ILogger<PracticeRecorder> logger)
    {
        _core = core;
        _logger = logger;
    }

    // Raised on the release edge with the thrower and what they threw, so
    // whoever hands the grenade back does not have to re-derive either.
    public event Action<ulong, string>? Thrown;

    // Raised once a throw is over and its landing point is known. This is the
    // only place a completed throw exists, so scoring reads it from here rather
    // than hooking the detonate events a second time and re-deriving the owner.
    public event Action<LineupRecord>? Finalized;

    public IReadOnlyList<LineupRecord> HistoryFor(ulong steamId)
    {
        return _history.TryGetValue(steamId, out var records)
            ? records
            : new List<LineupRecord>();
    }

    public LineupRecord? LastThrow(ulong steamId, int back = 0)
    {
        var records = HistoryFor(steamId);
        int index = records.Count - 1 - back;
        return index >= 0 && index < records.Count ? records[index] : null;
    }

    // Set while the plugin emits a projectile of its own. A preview grenade is
    // not a throw anybody made: recording it would save a lineup nobody threw,
    // and scoring it would count an attempt nobody took.
    public bool Emitting { get; set; }

    public void Forget(uint entityIndex)
    {
        _tracked.Remove(entityIndex);
    }

    public void Reset()
    {
        _armed.Clear();
        _pending.Clear();
        _tracked.Clear();
    }

    // Only does work while something is in flight or someone is holding a
    // pulled pin, so an idle practice server pays one branch per tick.
    public void OnTick()
    {
        _tick++;

        WatchArmedGrenades();
        SampleProjectiles();
    }

    private void WatchArmedGrenades()
    {
        foreach (IPlayer player in _core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                continue;
            }

            CCSPlayerPawn? pawn = player.PlayerPawn;
            CBasePlayerWeapon? active = pawn?.WeaponServices?.ActiveWeapon.Value;

            if (pawn == null || !pawn.IsValid || active == null || !active.IsValid)
            {
                _armed.Remove(player.SteamID);
                continue;
            }

            CBaseCSGrenade? grenade = TryAsGrenade(active);
            if (grenade == null)
            {
                _armed.Remove(player.SteamID);
                continue;
            }

            if (!_armed.TryGetValue(player.SteamID, out ArmedState? state))
            {
                state = new ArmedState();
                _armed[player.SteamID] = state;
            }

            if (grenade.PinPulled)
            {
                state.PinPulled = true;
            }

            // m_fThrowTime going non-zero is the release edge. Freeze the
            // player's state right here: by the time the projectile entity
            // exists they have already started moving again.
            if (state.PinPulled && !state.Released && grenade.ThrowTime.Value > 0)
            {
                state.Released = true;
                state.Frozen = Snapshot(pawn, grenade);
                _pending[player.SteamID] = state.Frozen;
            }
        }
    }

    private static CBaseCSGrenade? TryAsGrenade(CBasePlayerWeapon weapon)
    {
        string designer = weapon.DesignerName ?? "";
        if (!PracticeLineupUtility.IsGrenadeWeapon(designer))
        {
            return null;
        }

        try
        {
            return weapon.As<CBaseCSGrenade>();
        }
        catch
        {
            return null;
        }
    }

    private ThrowSnapshot Snapshot(CCSPlayerPawn pawn, CBaseCSGrenade grenade)
    {
        Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
        Vector velocity = pawn.AbsVelocity;
        QAngle angles = pawn.EyeAngles;

        float eyeZ = origin.Z + pawn.ViewOffset.Z.Value;

        CCSPlayer_MovementServices? movement = pawn.MovementServices;
        bool ducked = movement?.Ducked ?? false;
        uint buttons = 0;
        bool walking = false;

        if (movement != null)
        {
            buttons = (uint)movement.Buttons.ButtonStates[0];
            // IN_SPEED
            walking = (buttons & (1 << 16)) != 0;
        }

        return new ThrowSnapshot
        {
            feet_position = new Vec3(origin.X, origin.Y, origin.Z),
            eye_position = new Vec3(origin.X, origin.Y, eyeZ),
            pitch = angles.X,
            yaw = angles.Y,
            velocity = new Vec3(velocity.X, velocity.Y, velocity.Z),
            speed = new Vec3(velocity.X, velocity.Y, 0f).LengthXY(),
            on_ground = (pawn.Flags & (1 << 0)) != 0,
            ducked = ducked,
            walking = walking,
            throw_strength_raw = grenade.ThrowStrength,
            jump_throw = grenade.JumpThrow,
            buttons = buttons,
            tick = _tick,
        };
    }

    // A projectile appearing is what links a frozen snapshot to a physical
    // grenade. m_hThrower is read off the entity rather than assumed, so two
    // players throwing on the same tick cannot be confused for one another.
    public void OnProjectileCreated(CEntityInstance entity)
    {
        string designer = entity.DesignerName ?? "";
        string? utilityType = PracticeLineupUtility.UtilityTypeForProjectile(designer);

        // Anything that is not a grenade leaves silently: every entity in the
        // map comes through here. Past this line it IS a throw, so a drop is
        // worth saying out loud -- a silently dropped throw is what makes
        // ".save" claim you never threw anything.
        if (utilityType == null)
        {
            return;
        }

        if (Emitting)
        {
            return;
        }

        if (_tracked.Count >= MaxTrackedProjectiles)
        {
            _logger.LogWarning(
                "dropped a {type}: already tracking {count} projectiles",
                utilityType,
                _tracked.Count
            );
            return;
        }

        CBaseCSGrenadeProjectile projectile;
        try
        {
            projectile = entity.As<CBaseCSGrenadeProjectile>();
        }
        catch
        {
            return;
        }

        CBaseEntity? thrower = projectile.Thrower.Value;
        IPlayer? player =
            thrower == null ? null : _core.PlayerManager.GetPlayerFromPawn(thrower.As<CBasePlayerPawn>());

        if (player == null || !player.IsValid)
        {
            _logger.LogWarning(
                "dropped a {type}: no thrower on the projectile yet",
                utilityType
            );
            return;
        }

        if (!_pending.Remove(player.SteamID, out ThrowSnapshot? release))
        {
            // No frozen snapshot: the pin/throw edge was missed (hot reload
            // mid-throw, or a scripted give). Record what is still true rather
            // than dropping the throw entirely.
            release = new ThrowSnapshot { tick = _tick };
        }

        Vector initialPosition = projectile.InitialPosition;
        Vector initialVelocity = projectile.InitialVelocity;

        _tracked[entity.Index] = new TrackedProjectile
        {
            ThrowerSteamId = player.SteamID,
            UtilityType = utilityType,
            Release = release,
            StartTick = _tick,
            InitialPosition = new Vec3(
                initialPosition.X,
                initialPosition.Y,
                initialPosition.Z
            ),
            InitialVelocity = new Vec3(
                initialVelocity.X,
                initialVelocity.Y,
                initialVelocity.Z
            ),
        };

        if (_armed.TryGetValue(player.SteamID, out ArmedState? state))
        {
            state.PinPulled = false;
            state.Released = false;
            state.Frozen = null;
        }

        Thrown?.Invoke(player.SteamID, utilityType);
    }

    private void SampleProjectiles()
    {
        if (_tracked.Count == 0)
        {
            return;
        }

        var expired = new List<uint>();

        foreach ((uint index, TrackedProjectile tracked) in _tracked)
        {
            CBaseCSGrenadeProjectile? projectile = TryProjectileAt(index);

            if (projectile == null || !projectile.IsValid)
            {
                expired.Add(index);
                continue;
            }

            if (_tick - tracked.StartTick > ForceFinalizeTicks)
            {
                expired.Add(index);
                continue;
            }

            Vector? origin = projectile.AbsOrigin;
            if (origin == null || tracked.Raw.Count >= MaxRawPoints)
            {
                continue;
            }

            // A bounce is where the path turns. Sampling can miss it, the
            // counter cannot.
            bool bounced = projectile.Bounces > tracked.LastBounces;
            if (bounced)
            {
                tracked.LastBounces = projectile.Bounces;
            }

            if (bounced || _tick % SampleEveryTicks == 0)
            {
                tracked.Raw.Add(
                    new TrajectoryPoint
                    {
                        p = new Vec3(origin.Value.X, origin.Value.Y, origin.Value.Z),
                        t = _tick,
                        bounce = bounced,
                    }
                );
            }
        }

        foreach (uint index in expired)
        {
            FinalizeByIndex(index, null);
        }
    }

    private CBaseCSGrenadeProjectile? TryProjectileAt(uint index)
    {
        try
        {
            return _core.EntitySystem.GetEntityByIndex<CBaseCSGrenadeProjectile>(index);
        }
        catch
        {
            return null;
        }
    }

    // Called from the detonate handlers, which carry the projectile's entity
    // index for every utility except molotovs.
    public void OnDetonated(uint entityIndex, Vec3 position)
    {
        FinalizeByIndex(entityIndex, position);
    }

    // EventMolotovDetonate carries no entity id, so the thrower is the only
    // handle available.
    public void OnMolotovDetonated(ulong steamId, Vec3 position)
    {
        foreach ((uint index, TrackedProjectile tracked) in _tracked)
        {
            if (tracked.ThrowerSteamId == steamId && tracked.UtilityType == "Molotov")
            {
                FinalizeByIndex(index, position);
                return;
            }
        }
    }

    private void FinalizeByIndex(uint entityIndex, Vec3? detonation)
    {
        if (!_tracked.Remove(entityIndex, out TrackedProjectile? tracked))
        {
            return;
        }

        Vec3 landing =
            detonation
            ?? (
                tracked.Raw.Count > 0
                    ? tracked.Raw[^1].p
                    : tracked.InitialPosition
            );

        var record = new LineupRecord
        {
            client_id = Guid.NewGuid().ToString(),
            utility_type = tracked.UtilityType,
            author_steam_id = tracked.ThrowerSteamId.ToString(),
            release = tracked.Release,
            initial_position = tracked.InitialPosition,
            initial_velocity = tracked.InitialVelocity,
            detonation_position = landing,
            bounces = tracked.LastBounces,
            flight_time = (_tick - tracked.StartTick) / 64f,
            // The plugin watched this throw happen, so it is exact by
            // observation. It is never sent: the panel owns provenance and
            // stamps its own on ingest.
            confidence = LineupRecord.Exact,
            technique = TrajectoryUtility.ClassifyTechnique(tracked.Release).ToString(),
            strength = TrajectoryUtility
                .ClassifyStrength(tracked.Release.throw_strength_raw)
                .ToString(),
            trajectory = TrajectoryUtility.Simplify(tracked.Raw),
            recorded_tickrate = 64,
            plugin_runtime = "swiftlys2",
        };

        if (!_history.TryGetValue(tracked.ThrowerSteamId, out List<LineupRecord>? records))
        {
            records = new List<LineupRecord>();
            _history[tracked.ThrowerSteamId] = records;
        }

        records.Add(record);
        while (records.Count > MaxHistoryPerPlayer)
        {
            records.RemoveAt(0);
        }

        Finalized?.Invoke(record);
    }
}
