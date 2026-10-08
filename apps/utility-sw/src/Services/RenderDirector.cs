using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

/// <summary>
/// Directs one lineup preview for the render pod connected as a player.
///
/// The pod only films: it stages a lineup by id, starts capturing, says go, and
/// presses the throw when told. Every beat after that -- the stance shot, the
/// aim, the close-up, the throw, the chase and the bloom -- is timed here on
/// the game tick, and announced to the pod as a line in its own console.
/// </summary>
public class RenderDirector
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger<RenderDirector> _logger;
    private readonly PracticeReplay _replay;
    private readonly PracticeLibrary _library;
    private readonly PracticeSystem _system;

    private const float Dt = 1f / RenderDirectorUtility.TickRate;

    // How long the pawn gets to settle before staging is measured, and how
    // many times the teleport is re-sent before the pod is told it failed.
    private static readonly int SettleTicks = RenderDirectorUtility.Ticks(0.25f);
    private const int StageAttempts = 4;

    private const uint HideCrosshair = 1 << 8;
    private const uint HideRadar = 1 << 12;

    private const float ChaseEyeHalfLife = 0.12f;
    private const float ChaseLookHalfLife = 0.05f;
    private const float BloomEyeHalfLife = 0.45f;
    private const float BloomLookHalfLife = 0.3f;

    private sealed class Take
    {
        public ulong SteamId;
        public LineupRecord Lineup = null!;
        public eRenderBeat Beat;
        public int Tick;
        public int Attempts;
        public Vec3 Feet;
        public bool StillSent;

        public CDynamicProp? Camera;
        public Vec3 Eye;
        public Vec3 Look;

        public uint ProjectileIndex;
        public CBaseCSGrenadeProjectile? Projectile;
        public Vec3 ProjectileAt;
        public Vec3 Direction = new Vec3(1f, 0f, 0f);
        public bool Detached;
        public Vec3? Landing;
    }

    private Take? _take;

    public RenderDirector(
        ISwiftlyCore core,
        ILogger<RenderDirector> logger,
        PracticeReplay replay,
        PracticeLibrary library,
        PracticeSystem system
    )
    {
        _core = core;
        _logger = logger;
        _replay = replay;
        _library = library;
        _system = system;
    }

    public bool Busy => _take != null && _take.Beat != eRenderBeat.Staged;

    public void Stage(IPlayer player, string lineupId, bool refreshed = false)
    {
        ulong steamId = player.SteamID;

        // The render library is the batch the panel queued, and the pod stages
        // each lineup moments after the previous one finished -- read it fresh
        // rather than trust whatever was cached when the pod joined.
        if (!refreshed)
        {
            _library.Refresh(
                steamId,
                _ =>
                {
                    IPlayer? still = _system.Find(steamId);

                    if (still != null && still.IsValid)
                    {
                        Stage(still, lineupId, refreshed: true);
                    }
                }
            );
            return;
        }

        Reset(null);

        LineupRecord? lineup = PracticeLineupUtility.ById(_library.For(steamId), lineupId);

        if (lineup == null)
        {
            Say(player, RenderDirectorUtility.Line("error", ("reason", "unknown_lineup"), ("lineup", lineupId)));
            return;
        }

        if (!lineup.HasPhysicsSeed())
        {
            Say(player, RenderDirectorUtility.Line("error", ("reason", "no_seed"), ("lineup", lineupId)));
            return;
        }

        Vec3? feet = _replay.Stage(player, lineup);

        if (feet == null)
        {
            Say(player, RenderDirectorUtility.Line("error", ("reason", "no_pawn"), ("lineup", lineupId)));
            return;
        }

        _take = new Take
        {
            SteamId = steamId,
            Lineup = lineup,
            Beat = eRenderBeat.Staging,
            Feet = feet.Value,
            Attempts = 1,
        };
    }

    public void Go(IPlayer player)
    {
        Take? take = _take;

        if (take == null || take.SteamId != player.SteamID || take.Beat != eRenderBeat.Staged)
        {
            Say(player, RenderDirectorUtility.Line("error", ("reason", "not_staged")));
            return;
        }

        Enter(take, eRenderBeat.Stance);
    }

    public void OnTick()
    {
        Take? take = _take;

        if (take == null)
        {
            return;
        }

        IPlayer? player = _system.Find(take.SteamId);
        CCSPlayerPawn? pawn = player?.PlayerPawn;

        if (player == null || !player.IsValid || pawn == null || !pawn.IsValid)
        {
            _logger.LogWarning("render take for {steam} lost its pawn", take.SteamId);
            Reset(null);
            return;
        }

        take.Tick++;

        switch (take.Beat)
        {
            case eRenderBeat.Staging:
                Staging(take, player, pawn);
                break;
            case eRenderBeat.Stance:
                Still(take, player, RenderDirectorUtility.StanceStillAt, "stance");
                Next(take, RenderDirectorUtility.StanceSeconds, eRenderBeat.Aim);
                break;
            case eRenderBeat.Aim:
                Still(take, player, RenderDirectorUtility.AimStillAt, "aim");
                Next(take, RenderDirectorUtility.AimSeconds, eRenderBeat.AimClose);
                break;
            case eRenderBeat.AimClose:
                Still(take, player, RenderDirectorUtility.AimCloseStillAt, "aim_close");
                Next(take, RenderDirectorUtility.AimCloseSeconds, eRenderBeat.Throw);
                break;
            case eRenderBeat.Throw:
                if (take.Tick > RenderDirectorUtility.Ticks(RenderDirectorUtility.ThrowTimeoutSeconds))
                {
                    Fail(take, player, "no_throw");
                }
                break;
            case eRenderBeat.Follow:
                Follow(take, player, pawn);
                break;
            case eRenderBeat.Bloom:
                Bloom(take, player);
                break;
        }
    }

    private void Staging(Take take, IPlayer player, CCSPlayerPawn pawn)
    {
        if (take.Tick < SettleTicks)
        {
            return;
        }

        Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
        QAngle eyes = pawn.EyeAngles;
        var at = new Vec3(origin.X, origin.Y, origin.Z);
        float drift = (at - take.Feet).LengthXY();
        bool aimed = RenderDirectorUtility.AimMatches(
            eyes.X,
            eyes.Y,
            take.Lineup.release.pitch,
            take.Lineup.release.yaw
        );

        if (drift <= RenderDirectorUtility.StagedPositionTolerance && aimed)
        {
            take.Beat = eRenderBeat.Staged;
            take.Tick = 0;

            Say(
                player,
                RenderDirectorUtility.Line(
                    "staged",
                    ("lineup", take.Lineup.id),
                    ("utility", take.Lineup.utility_type),
                    ("x", at.x),
                    ("y", at.y),
                    ("z", at.z),
                    ("pitch", eyes.X),
                    ("yaw", eyes.Y)
                )
            );
            return;
        }

        if (take.Attempts >= StageAttempts)
        {
            Fail(
                take,
                player,
                aimed ? "position_drift" : "aim_drift",
                ("drift", drift),
                ("pitch", eyes.X),
                ("yaw", eyes.Y)
            );
            return;
        }

        take.Attempts++;
        take.Tick = 0;

        Vec3? feet = _replay.Stage(player, take.Lineup);

        if (feet != null)
        {
            take.Feet = feet.Value;
        }
    }

    private void Enter(Take take, eRenderBeat beat)
    {
        IPlayer? player = _system.Find(take.SteamId);
        CCSPlayerPawn? pawn = player?.PlayerPawn;

        if (player == null || pawn == null || !pawn.IsValid)
        {
            Reset(null);
            return;
        }

        take.Beat = beat;
        take.Tick = 0;
        take.StillSent = false;

        switch (beat)
        {
            case eRenderBeat.Stance:
            {
                (Vec3 eye, Vec3 look) = RenderDirectorUtility.StanceShot(take.Feet, take.Lineup.release.yaw);
                Vec3 head = take.Feet + new Vec3(0f, 0f, RenderDirectorUtility.StandingEyeHeight);

                take.Eye = _replay.CameraClear(head, eye);
                take.Look = look;

                if (!ViewThroughCamera(take, pawn))
                {
                    Fail(take, player, "no_camera");
                    return;
                }

                Shot(player, "stance", "camera");
                break;
            }
            case eRenderBeat.Aim:
                ViewThroughEyes(take, pawn);
                _replay.Repoint(player, take.Lineup.release.pitch, take.Lineup.release.yaw);
                Shot(player, "aim", "eyes");
                break;
            case eRenderBeat.AimClose:
                Zoom(pawn, RenderDirectorUtility.AimCloseFov);
                Shot(player, "aim_close", "eyes");
                break;
            case eRenderBeat.Throw:
                Zoom(pawn, 0);
                Shot(player, "throw", "eyes");
                Say(
                    player,
                    RenderDirectorUtility.Line(
                        "act",
                        ("lineup", take.Lineup.id),
                        ("utility", take.Lineup.utility_type),
                        ("technique", take.Lineup.technique),
                        ("strength", take.Lineup.strength),
                        ("jump_bind", take.Lineup.release.jump_throw)
                    )
                );
                break;
            case eRenderBeat.Follow:
                Shot(player, "follow", "eyes");
                break;
            case eRenderBeat.Bloom:
                Shot(player, "bloom", "camera");
                break;
        }
    }

    private void Next(Take take, float seconds, eRenderBeat next)
    {
        if (take.Tick >= RenderDirectorUtility.Ticks(seconds))
        {
            Enter(take, next);
        }
    }

    private void Still(Take take, IPlayer player, float at, string kind)
    {
        if (take.StillSent || take.Tick < RenderDirectorUtility.Ticks(at))
        {
            return;
        }

        take.StillSent = true;
        Say(player, RenderDirectorUtility.Line("still", ("kind", kind), ("lineup", take.Lineup.id)));
    }

    /// <summary>
    /// The pod's throw just made a projectile. Its flight is replaced with the
    /// recorded one, so what is filmed is the lineup and not however close the
    /// pod's key presses came to it. Answers whether the projectile was ours.
    /// </summary>
    public bool ClaimProjectile(CEntityInstance entity)
    {
        Take? take = _take;

        if (take == null || take.Beat != eRenderBeat.Throw)
        {
            return false;
        }

        if (PracticeLineupUtility.UtilityTypeForProjectile(entity.DesignerName ?? "") == null)
        {
            return false;
        }

        CBaseCSGrenadeProjectile projectile;

        try
        {
            projectile = entity.As<CBaseCSGrenadeProjectile>();
        }
        catch
        {
            return false;
        }

        CBaseEntity? thrower = projectile.Thrower.Value;
        IPlayer? player =
            thrower == null ? null : _core.PlayerManager.GetPlayerFromPawn(thrower.As<CBasePlayerPawn>());

        if (player == null || !player.IsValid || player.SteamID != take.SteamId)
        {
            return false;
        }

        Vector released = projectile.InitialPosition;
        Vec3 seedAt = take.Lineup.initial_position;
        Vec3 seedVelocity = take.Lineup.initial_velocity;
        (float pitch, float yaw) = TrajectoryUtility.AnglesFromVelocity(seedVelocity);
        float drift = (new Vec3(released.X, released.Y, released.Z) - seedAt).Length();

        projectile.Teleport(
            new Vector(seedAt.x, seedAt.y, seedAt.z),
            new QAngle(pitch, yaw, 0),
            new Vector(seedVelocity.x, seedVelocity.y, seedVelocity.z)
        );

        take.Projectile = projectile;
        take.ProjectileIndex = projectile.Index;
        take.ProjectileAt = seedAt;
        take.Direction = RenderDirectorUtility.ChaseDirection(seedVelocity, take.Direction);
        take.Detached = false;

        Say(
            player,
            RenderDirectorUtility.Line(
                "thrown",
                ("lineup", take.Lineup.id),
                ("utility", take.Lineup.utility_type),
                ("release_drift", drift)
            )
        );

        Enter(take, eRenderBeat.Follow);

        return true;
    }

    private void Follow(Take take, IPlayer player, CCSPlayerPawn pawn)
    {
        CBaseCSGrenadeProjectile? projectile = take.Projectile;
        Vec3 at = take.ProjectileAt;

        if (projectile != null && projectile.IsValid && projectile.AbsOrigin is Vector origin)
        {
            at = new Vec3(origin.X, origin.Y, origin.Z);
        }

        Vec3 velocity = (at - take.ProjectileAt) * RenderDirectorUtility.TickRate;
        take.ProjectileAt = at;
        take.Direction = RenderDirectorUtility.ChaseDirection(velocity, take.Direction);

        if (!take.Detached)
        {
            if (take.Tick < RenderDirectorUtility.Ticks(RenderDirectorUtility.DetachSeconds))
            {
                return;
            }

            // The camera starts exactly where the eyes are and pulls away from
            // there, so the cut into the chase reads as the view letting go.
            Vector eyeOrigin = pawn.AbsOrigin ?? new Vector(take.Feet.x, take.Feet.y, take.Feet.z);
            take.Eye = new Vec3(eyeOrigin.X, eyeOrigin.Y, eyeOrigin.Z + RenderDirectorUtility.StandingEyeHeight);
            take.Look = take.Eye + RenderDirectorUtility.Forward(take.Lineup.release.pitch, take.Lineup.release.yaw) * 200f;

            if (!ViewThroughCamera(take, pawn))
            {
                Fail(take, player, "no_camera");
                return;
            }

            take.Detached = true;
            Shot(player, "follow", "camera");
        }

        Vec3 target = _replay.CameraClear(at, RenderDirectorUtility.ChaseEye(at, take.Direction));

        take.Eye = RenderDirectorUtility.Approach(take.Eye, target, Dt, ChaseEyeHalfLife);
        take.Look = RenderDirectorUtility.Approach(take.Look, at, Dt, ChaseLookHalfLife);
        MoveCamera(take);

        if (take.Tick > RenderDirectorUtility.Ticks(RenderDirectorUtility.FollowMaxSeconds))
        {
            Landed(take, player, at);
        }
    }

    public void OnDetonated(uint entityIndex, Vec3 at)
    {
        Take? take = _take;

        if (take == null || take.Beat != eRenderBeat.Follow || take.ProjectileIndex != entityIndex)
        {
            return;
        }

        IPlayer? player = _system.Find(take.SteamId);

        if (player != null && player.IsValid)
        {
            Landed(take, player, at);
        }
    }

    // EventMolotovDetonate names only the thrower; the take's own throw is the
    // only one this player has in the air.
    public bool OnMolotovDetonated(ulong steamId, Vec3 at)
    {
        Take? take = _take;

        if (take == null || take.Beat != eRenderBeat.Follow || take.SteamId != steamId)
        {
            return false;
        }

        IPlayer? player = _system.Find(steamId);

        if (player != null && player.IsValid)
        {
            Landed(take, player, at);
        }

        return true;
    }

    private void Landed(Take take, IPlayer player, Vec3 at)
    {
        take.Landing = at;

        Say(
            player,
            RenderDirectorUtility.Line(
                "detonated",
                ("lineup", take.Lineup.id),
                ("utility", take.Lineup.utility_type),
                ("x", at.x),
                ("y", at.y),
                ("z", at.z)
            )
        );

        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (!take.Detached && pawn != null && pawn.IsValid)
        {
            take.Eye = take.Feet + new Vec3(0f, 0f, RenderDirectorUtility.StandingEyeHeight);
            take.Look = at;
            take.Detached = ViewThroughCamera(take, pawn);
        }

        Enter(take, eRenderBeat.Bloom);
    }

    private void Bloom(Take take, IPlayer player)
    {
        Vec3 landing = take.Landing ?? take.ProjectileAt;
        (Vec3 eye, Vec3 look) = RenderDirectorUtility.BloomShot(landing, take.Feet, take.Direction);

        Vec3 target = _replay.CameraClear(look, eye);

        take.Eye = RenderDirectorUtility.Approach(take.Eye, target, Dt, BloomEyeHalfLife);
        take.Look = RenderDirectorUtility.Approach(take.Look, look, Dt, BloomLookHalfLife);
        MoveCamera(take);

        Still(take, player, RenderDirectorUtility.LandingStillSeconds(take.Lineup.utility_type), "landing");

        if (take.Tick >= RenderDirectorUtility.Ticks(RenderDirectorUtility.BloomHoldSeconds(take.Lineup.utility_type)))
        {
            string? lineupId = take.Lineup.id;

            Reset(null);
            Say(player, RenderDirectorUtility.Line("done", ("lineup", lineupId)));
        }
    }

    private bool ViewThroughCamera(Take take, CCSPlayerPawn pawn)
    {
        if (take.Camera == null || !take.Camera.IsValid)
        {
            take.Camera = SpawnCamera();
        }

        CDynamicProp? camera = take.Camera;
        CCSPlayer_CameraServices? services = pawn.CameraServices;

        if (camera == null || services == null)
        {
            return false;
        }

        MoveCamera(take);

        services.ViewEntity = _core.EntitySystem.GetRefEHandle<CBaseEntity>(camera);
        services.ViewEntityUpdated();

        pawn.HideHUD = HideCrosshair | HideRadar;
        pawn.HideHUDUpdated();

        return true;
    }

    private void ViewThroughEyes(Take take, CCSPlayerPawn pawn)
    {
        CCSPlayer_CameraServices? services = pawn.CameraServices;

        if (services != null)
        {
            services.ViewEntity = CHandle<CBaseEntity>.Invalid;
            services.ViewEntityUpdated();
        }

        pawn.HideHUD = HideRadar;
        pawn.HideHUDUpdated();
    }

    private static void Zoom(CCSPlayerPawn pawn, int fov)
    {
        CCSPlayer_CameraServices? services = pawn.CameraServices;

        if (services == null)
        {
            return;
        }

        services.FOV = (uint)fov;
        services.FOVUpdated();
    }

    private CDynamicProp? SpawnCamera()
    {
        try
        {
            CDynamicProp camera = _core.EntitySystem.CreateEntityByDesignerName<CDynamicProp>("prop_dynamic");

            if (!camera.IsValid)
            {
                return null;
            }

            camera.DispatchSpawn(PracticeReplay.MarkerKeys());
            camera.Render = new Color(255, 255, 255, 0);
            camera.RenderUpdated();

            return camera;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to spawn the render camera");
            return null;
        }
    }

    private static void MoveCamera(Take take)
    {
        CDynamicProp? camera = take.Camera;

        if (camera == null || !camera.IsValid)
        {
            return;
        }

        (float pitch, float yaw) = RenderDirectorUtility.LookAt(take.Eye, take.Look);

        camera.Teleport(
            new Vector(take.Eye.x, take.Eye.y, take.Eye.z),
            new QAngle(pitch, yaw, 0),
            new Vector(0, 0, 0)
        );
    }

    private void Fail(Take take, IPlayer player, string reason, params (string key, object? value)[] detail)
    {
        string? lineupId = take.Lineup.id;

        Reset(null);

        var fields = new List<(string key, object? value)> { ("reason", reason), ("lineup", lineupId) };
        fields.AddRange(detail);

        Say(player, RenderDirectorUtility.Line("error", fields.ToArray()));
    }

    /// <summary>
    /// Hands the view back and forgets the take. Safe with nothing in progress.
    /// </summary>
    public void Reset(string? reason)
    {
        Take? take = _take;
        _take = null;

        if (take == null)
        {
            return;
        }

        IPlayer? player = _system.Find(take.SteamId);
        CCSPlayerPawn? pawn = player?.PlayerPawn;

        if (pawn != null && pawn.IsValid)
        {
            ViewThroughEyes(take, pawn);
            pawn.HideHUD = 0;
            pawn.HideHUDUpdated();
            Zoom(pawn, 0);
        }

        if (take.Camera != null && take.Camera.IsValid)
        {
            take.Camera.Despawn();
        }

        if (reason != null && player != null && player.IsValid)
        {
            Say(player, RenderDirectorUtility.Line("error", ("reason", reason), ("lineup", take.Lineup.id)));
        }
    }

    private void Shot(IPlayer player, string name, string view)
    {
        Say(player, RenderDirectorUtility.Line("shot", ("name", name), ("view", view)));
    }

    private void Say(IPlayer player, string line)
    {
        _logger.LogInformation("{line}", line);

        if (player.IsValid)
        {
            player.SendMessage(MessageType.Console, line + "\n");
        }
    }
}
