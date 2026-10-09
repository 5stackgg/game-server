using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.EntitySystem;
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

    // FSOLID_NOT_SOLID.
    private const byte NotSolid = 4;

    private const uint HideCrosshair = 1 << 8;
    private const uint HideRadar = 1 << 12;

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

        // Ticks since go, -1 before it. Every line after go carries it, so the
        // pod can place a still by when the beat happened rather than by when
        // the line reached its console log.
        public int GoTicks = -1;
        public string? StagedLine;

        public CDynamicProp? Camera;
        public Vec3? CameraAt;
        public float Fov = RenderDirectorUtility.DefaultFov;
        public float ZoomFrom = RenderDirectorUtility.DefaultFov;
        public float ZoomTo = RenderDirectorUtility.DefaultFov;
        public int ZoomTick = -1;
        public Vec3 Eye;
        public Vec3 Look;
        public Vec3 DetachEye;
        public Vec3 DetachLook;
        public float Rise;
        public Vec3 BloomEye;
        public Vec3 BloomLook;

        public uint ProjectileIndex;
        public CBaseCSGrenadeProjectile? Projectile;
        public Vec3 ProjectileAt;
        public readonly List<Vec3> Path = new();
        public readonly List<float> PathArc = new();
        public Vec3 Direction = new Vec3(1f, 0f, 0f);
        public bool Detached;
        public int DetachTick;
        public bool Settled;
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

        // The pod re-sends a stage it has not heard back about, and the library
        // fetch behind the first one can land after the retry was sent. The
        // second answer must not throw away a take already under way.
        Take? current = _take;

        if (
            current != null
            && current.Beat != eRenderBeat.Wrap
            && current.SteamId == steamId
            && string.Equals(current.Lineup.id, lineupId, StringComparison.OrdinalIgnoreCase)
        )
        {
            if (current.Beat == eRenderBeat.Staged && current.StagedLine != null)
            {
                Say(player, current.StagedLine);
            }

            return;
        }

        if (current != null && current.Beat != eRenderBeat.Wrap)
        {
            Fail(current, player, "superseded");
        }
        else
        {
            Reset(null);
        }

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

        // Spawned on the spot first, then placed exactly: the spawn is what
        // squares the body up, staging is what puts the eyes on the line.
        _system.RespawnAt(
            player,
            RenderDirectorUtility.StageAt(lineup.release.feet_position, lineup.approach),
            lineup.release.yaw
        );

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

    // Named, so a go meant for one lineup can never start another's take.
    public void Go(IPlayer player, string lineupId)
    {
        Take? take = _take;

        if (
            take == null
            || take.SteamId != player.SteamID
            || take.Beat != eRenderBeat.Staged
            || !string.Equals(take.Lineup.id, lineupId, StringComparison.OrdinalIgnoreCase)
        )
        {
            Say(player, RenderDirectorUtility.Line("error", ("lineup", lineupId), ("reason", "not_staged")));
            return;
        }

        take.GoTicks = 0;
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

        if (take.GoTicks >= 0)
        {
            take.GoTicks++;
        }

        StepZoom(take, pawn);

        switch (take.Beat)
        {
            case eRenderBeat.Staging:
                Staging(take, player, pawn);
                break;
            case eRenderBeat.Stance:
                Still(take, player, RenderDirectorUtility.StanceStillAt, "stance");
                Next(take, RenderDirectorUtility.StanceSeconds, eRenderBeat.StanceEyes);
                break;
            case eRenderBeat.StanceEyes:
                Still(take, player, RenderDirectorUtility.StanceEyesStillAt, "stance_eyes");
                Next(take, RenderDirectorUtility.StanceEyesSeconds, eRenderBeat.Tilt);
                break;
            case eRenderBeat.Tilt:
                Tilt(take);
                Next(take, RenderDirectorUtility.TiltSeconds, eRenderBeat.Aim);
                break;
            case eRenderBeat.Aim:
                Still(take, player, RenderDirectorUtility.AimStillAt, "aim");
                Next(take, RenderDirectorUtility.AimSeconds, eRenderBeat.Pin);
                break;
            case eRenderBeat.Pin:
                Still(take, player, RenderDirectorUtility.PinStillAt, "aim_pin");
                Next(take, RenderDirectorUtility.PinSeconds, eRenderBeat.AimClose);
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
            case eRenderBeat.Wrap:
                FrameBloom(take);

                if (take.Tick >= RenderDirectorUtility.Ticks(RenderDirectorUtility.WrapSeconds))
                {
                    Reset(null);
                }
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
        QAngle body = pawn.AbsRotation ?? new QAngle(0, 0, 0);
        var at = new Vec3(origin.X, origin.Y, origin.Z);
        float drift = (at - take.Feet).LengthXY();
        bool aimed = RenderDirectorUtility.AimMatches(
            eyes.X,
            eyes.Y,
            take.Lineup.release.pitch,
            take.Lineup.release.yaw
        );
        bool upright = RenderDirectorUtility.StandsUpright(body.X, body.Z);

        if (drift <= RenderDirectorUtility.StagedPositionTolerance && aimed && upright)
        {
            take.Beat = eRenderBeat.Staged;
            take.Tick = 0;

            // Where the pawn came to rest, not where staging aimed for: every
            // camera placed off the feet has to match the eyes it cuts to.
            take.Feet = at;

            // Looking level while the body is on screen. The aim goes back
            // on at the tilt, when the camera is in the eyes.
            _replay.Repoint(player, 0f, take.Lineup.release.yaw);

            if (take.Camera == null || !take.Camera.IsValid)
            {
                take.Camera = SpawnCamera(pawn);
            }

            // Already on the opening shot: the pod records a moment before it
            // says go, and that moment used to be the aim from the eyes.
            FrameSpot(take, pawn);

            take.StagedLine = Tell(
                take,
                player,
                "staged",
                ("utility", take.Lineup.utility_type),
                ("x", at.x),
                ("y", at.y),
                ("z", at.z),
                ("dz", at.z - take.Lineup.release.feet_position.z),
                ("pitch", eyes.X),
                ("yaw", eyes.Y),
                ("lean", body.X)
            );
            return;
        }

        if (take.Attempts >= StageAttempts)
        {
            Fail(
                take,
                player,
                !aimed ? "aim_drift" : (upright ? "position_drift" : "body_lean"),
                ("drift", drift),
                ("pitch", eyes.X),
                ("yaw", eyes.Y),
                ("lean", body.X)
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
                if (!FrameSpot(take, pawn))
                {
                    Fail(take, player, "no_camera");
                    return;
                }

                Shot(take, player, "stance", "camera");
                break;
            case eRenderBeat.StanceEyes:
                (take.Eye, take.Look) = RenderDirectorUtility.EyesShot(
                    Head(take),
                    RenderDirectorUtility.StanceEyesPitch,
                    take.Lineup.release.yaw
                );
                MoveCamera(take);
                Shot(take, player, "stance_eyes", "camera");
                break;
            case eRenderBeat.Tilt:
                // Behind the camera, so the cut back into the eyes lands on a
                // view that is already there. The pin comes out now so cs2's
                // lineup reticle is up by the pulled-pin still.
                _replay.Repoint(player, take.Lineup.release.pitch, take.Lineup.release.yaw);
                Shot(take, player, "tilt", "camera");
                Tell(
                    take,
                    player,
                    "pin",
                    ("utility", take.Lineup.utility_type),
                    ("strength", take.Lineup.strength)
                );
                break;
            case eRenderBeat.Aim:
                ViewThroughEyes(take, pawn);
                _replay.Repoint(player, take.Lineup.release.pitch, take.Lineup.release.yaw);
                Shot(take, player, "aim", "eyes");
                break;
            case eRenderBeat.Pin:
                Shot(take, player, "pin", "eyes");
                break;
            case eRenderBeat.AimClose:
                ZoomTo(take, RenderDirectorUtility.AimCloseFov);
                Shot(take, player, "aim_close", "eyes");
                break;
            case eRenderBeat.Throw:
                ZoomTo(take, RenderDirectorUtility.DefaultFov);
                Shot(take, player, "throw", "eyes");
                Tell(
                    take,
                    player,
                    "act",
                    ("utility", take.Lineup.utility_type),
                    ("technique", take.Lineup.technique),
                    ("strength", take.Lineup.strength),
                    ("jump_bind", take.Lineup.release.jump_throw)
                );
                break;
            case eRenderBeat.Follow:
                Shot(take, player, "follow", "eyes");
                break;
            case eRenderBeat.Bloom:
                Shot(take, player, "bloom", "camera");
                break;
        }
    }

    private bool FrameSpot(Take take, CCSPlayerPawn pawn)
    {
        Vec3 above = take.Feet + new Vec3(0f, 0f, RenderDirectorUtility.AboveHead);
        float bestReach = -1f;

        foreach (
            (Vec3 eye, Vec3 look) in RenderDirectorUtility.SpotCandidates(
                take.Feet,
                take.Lineup.release.yaw
            )
        )
        {
            Vec3 clear = _replay.CameraClear(above, eye);
            float reach = (clear - above).Length();

            if (reach > bestReach)
            {
                bestReach = reach;
                take.Eye = clear;
                take.Look = look;
            }

            if (reach >= (eye - above).Length() * 0.97f)
            {
                break;
            }
        }

        return ViewThroughCamera(take, pawn);
    }

    private void Tilt(Take take)
    {
        float s = take.Tick / (float)RenderDirectorUtility.Ticks(RenderDirectorUtility.TiltSeconds);

        (take.Eye, take.Look) = RenderDirectorUtility.TiltShot(
            Head(take),
            take.Lineup.release.pitch,
            take.Lineup.release.yaw,
            s
        );
        MoveCamera(take);
    }

    private static Vec3 Head(Take take)
    {
        return take.Feet + new Vec3(0f, 0f, RenderDirectorUtility.StandingEyeHeight);
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

        // An aim still is only worth having if the eyes were on the lineup's
        // line when it was taken, so it says how far off they were. The angles
        // alone are not enough: a pawn tipped back looks along the right line
        // from the wrong place, which is what eye_off measures.
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (kind.StartsWith("aim", StringComparison.Ordinal) && pawn != null && pawn.IsValid)
        {
            QAngle eyes = pawn.EyeAngles;
            QAngle body = pawn.AbsRotation ?? new QAngle(0, 0, 0);

            Tell(
                take,
                player,
                "still",
                ("kind", kind),
                ("dpitch", RenderDirectorUtility.AngleDelta(eyes.X, take.Lineup.release.pitch)),
                ("dyaw", RenderDirectorUtility.AngleDelta(eyes.Y, take.Lineup.release.yaw)),
                ("eye_off", RenderDirectorUtility.LeanEyeDrift(body.X))
            );
            return;
        }

        Tell(take, player, "still", ("kind", kind));
    }

    /// <summary>
    /// The pod's throw just made a projectile. Its flight is replaced with the
    /// recorded one, so what is filmed is the lineup and not however close the
    /// pod's key presses came to it. Answers whether the projectile was ours.
    /// </summary>
    public bool ClaimProjectile(CEntityInstance entity)
    {
        Take? take = _take;

        // The close-up as well as the throw beat: when the act line is slow to
        // reach the pod it throws on its own clock, which can land a moment
        // before the throw beat begins.
        if (take == null || (take.Beat != eRenderBeat.Throw && take.Beat != eRenderBeat.AimClose))
        {
            return false;
        }

        if (
            !RenderDirectorUtility.SameUtility(
                PracticeLineupUtility.UtilityTypeForProjectile(entity.DesignerName ?? ""),
                take.Lineup.utility_type
            )
        )
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
        take.Path.Clear();
        take.PathArc.Clear();
        take.Path.Add(seedAt);
        take.PathArc.Add(0f);
        take.Direction = RenderDirectorUtility.ChaseDirection(seedVelocity, take.Direction);
        take.Detached = false;

        // The close-up also accepts the throw, so the zoom may still be in.
        if (take.ZoomTo != RenderDirectorUtility.DefaultFov)
        {
            ZoomTo(take, RenderDirectorUtility.DefaultFov);
        }

        Tell(
            take,
            player,
            "thrown",
            ("utility", take.Lineup.utility_type),
            ("release_drift", drift)
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

        if (!take.Settled)
        {
            take.PathArc.Add(take.PathArc[^1] + (at - take.Path[^1]).Length());
            take.Path.Add(at);
        }
        take.Direction = RenderDirectorUtility.Turn(
            take.Direction,
            RenderDirectorUtility.ChaseDirection(velocity, take.Direction),
            Dt
        );

        if (!take.Detached)
        {
            if (take.Tick < RenderDirectorUtility.Ticks(RenderDirectorUtility.DetachSeconds))
            {
                return;
            }

            // The camera starts exactly where the eyes are and pulls away from
            // there, so the cut into the chase reads as the view letting go.
            // The real view, not the lineup's: a jump throw has just landed.
            Vector eyeOrigin = pawn.AbsOrigin ?? new Vector(take.Feet.x, take.Feet.y, take.Feet.z);
            float viewZ = pawn.ViewOffset.Z.Value;
            float eyeHeight = viewZ > 1f ? viewZ : RenderDirectorUtility.StandingEyeHeight;
            QAngle eyes = pawn.EyeAngles;
            take.Eye = new Vec3(eyeOrigin.X, eyeOrigin.Y, eyeOrigin.Z + eyeHeight);
            take.Look = take.Eye + RenderDirectorUtility.Forward(eyes.X, eyes.Y) * 200f;
            take.Rise = 0f;
            take.DetachEye = take.Eye;
            take.DetachLook = take.Look;
            take.DetachTick = take.Tick;

            if (!ViewThroughCamera(take, pawn))
            {
                Fail(take, player, "no_camera");
                return;
            }

            take.Detached = true;
            Shot(take, player, "follow", "camera");
        }

        // From the first bounce the grenade is rolling or settling, and a camera
        // that keeps chasing its bearing scrapes along floors and walls. It stops
        // where it is and only turns to keep the grenade in frame.
        if (!take.Settled && projectile != null && projectile.IsValid && projectile.Bounces > 0)
        {
            take.Settled = true;
        }

        if (take.Settled)
        {
            take.Look = RenderDirectorUtility.Approach(take.Look, at, Dt, SettledLookHalfLife);
            MoveCamera(take);

            if (take.Tick > RenderDirectorUtility.Ticks(RenderDirectorUtility.FollowMaxSeconds))
            {
                Landed(take, player, at);
            }
            return;
        }

        float since = (take.Tick - take.DetachTick) * Dt;
        Vec3 onPath = RenderDirectorUtility.PointAlong(
            take.Path,
            take.PathArc,
            RenderDirectorUtility.ChaseArc(take.PathArc[^1], since)
        );
        float rise =
            RenderDirectorUtility.ChaseHeight
            * RenderDirectorUtility.Ease(since / RenderDirectorUtility.ChaseCatchUpSeconds);
        Vec3 cleared = _replay.CameraClear(onPath, onPath + new Vec3(0f, 0f, rise));
        take.Rise = RenderDirectorUtility
            .Approach(
                new Vec3(0f, 0f, take.Rise),
                new Vec3(0f, 0f, MathF.Max(0f, cleared.z - onPath.z)),
                Dt,
                RenderDirectorUtility.ChaseRiseHalfLife
            )
            .z;
        Vec3 chase = onPath + new Vec3(0f, 0f, take.Rise);
        float handOff = RenderDirectorUtility.Ease(since / RenderDirectorUtility.DetachHandOffSeconds);

        take.Eye = RenderDirectorUtility.Lerp(take.DetachEye, chase, handOff);
        take.Look = RenderDirectorUtility.Lerp(take.DetachLook, at, handOff);
        MoveCamera(take);

        if (take.Tick > RenderDirectorUtility.Ticks(RenderDirectorUtility.FollowMaxSeconds))
        {
            Landed(take, player, at);
        }
    }

    private const float SettledLookHalfLife = 0.12f;

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
        (take.BloomEye, take.BloomLook) = BloomVantage(at, take);

        Tell(
            take,
            player,
            "detonated",
            ("utility", take.Lineup.utility_type),
            ("x", at.x),
            ("y", at.y),
            ("z", at.z)
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
        FrameBloom(take);

        Still(take, player, RenderDirectorUtility.LandingStillSeconds(take.Lineup.utility_type), "landing");

        if (take.Tick >= RenderDirectorUtility.Ticks(RenderDirectorUtility.BloomHoldSeconds(take.Lineup.utility_type)))
        {
            Tell(take, player, "done");
            take.Beat = eRenderBeat.Wrap;
            take.Tick = 0;
        }
    }

    private void FrameBloom(Take take)
    {
        take.Eye = RenderDirectorUtility.Approach(take.Eye, take.BloomEye, Dt, BloomEyeHalfLife);
        take.Look = RenderDirectorUtility.Approach(take.Look, take.BloomLook, Dt, BloomLookHalfLife);
        MoveCamera(take);
    }

    // The first vantage a trace says sees the cloud unobstructed; failing
    // that, whichever got furthest before something was in the way.
    private (Vec3 eye, Vec3 look) BloomVantage(Vec3 landing, Take take)
    {
        (Vec3 eye, Vec3 look) best = default;
        float bestReach = -1f;

        foreach ((Vec3 eye, Vec3 look) in RenderDirectorUtility.BloomCandidates(landing, take.Feet, take.Direction))
        {
            Vec3 clear = _replay.CameraClear(look, eye);
            float reach = (clear - look).Length();
            float want = (eye - look).Length();

            if (reach >= want * 0.97f)
            {
                return (clear, look);
            }

            if (reach > bestReach)
            {
                bestReach = reach;
                best = (clear, look);
            }
        }

        return best;
    }

    private bool ViewThroughCamera(Take take, CCSPlayerPawn pawn)
    {
        if (take.Camera == null || !take.Camera.IsValid)
        {
            take.Camera = SpawnCamera(pawn);
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

    private static void ZoomTo(Take take, float fov)
    {
        take.ZoomFrom = take.Fov;
        take.ZoomTo = fov;
        take.ZoomTick = 0;
    }

    // Eased a tick at a time: a snap from the close-up's narrow field of view
    // back to the default read as a jump cut right as the throw began.
    private static void StepZoom(Take take, CCSPlayerPawn pawn)
    {
        if (take.ZoomTick < 0)
        {
            return;
        }

        take.ZoomTick++;
        float s = RenderDirectorUtility.Ease(
            take.ZoomTick / (float)RenderDirectorUtility.Ticks(RenderDirectorUtility.ZoomSeconds)
        );

        take.Fov = take.ZoomFrom + ((take.ZoomTo - take.ZoomFrom) * s);

        if (s >= 1f)
        {
            take.ZoomTick = -1;
        }

        bool home = s >= 1f && take.ZoomTo >= RenderDirectorUtility.DefaultFov;
        Zoom(pawn, home ? 0 : (int)MathF.Round(take.Fov));
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

    private CDynamicProp? SpawnCamera(CCSPlayerPawn pawn)
    {
        try
        {
            CDynamicProp camera = _core.EntitySystem.CreateEntityByDesignerName<CDynamicProp>("prop_dynamic");

            if (!camera.IsValid)
            {
                return null;
            }

            CEntityKeyValues keys = PracticeReplay.MarkerKeys();
            keys.SetString("solid", "0");

            // The client never interpolates an entity with no model, so a
            // model-less camera steps once per tick and beats against the
            // 60fps capture. The thrower's own model is always precached; it
            // is never drawn (kRenderNone below).
            string? model = ModelOf(pawn);

            if (!string.IsNullOrEmpty(model))
            {
                keys.SetString("model", model);
            }

            camera.DispatchSpawn(keys);

            // A prop with no model is the giant ERROR model, centred on the
            // camera: drawn, it hung across the aim shot; solid, the chase
            // grenade landed on it and popped in mid-air.
            camera.Collision.SolidType = SolidType_t.SOLID_NONE;
            camera.Collision.SolidFlags = NotSolid;
            camera.Collision.CollisionGroup = (byte)CollisionGroup.Nonphysical;
            camera.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.Nonphysical;
            camera.Collision.CollisionAttribute.InteractsAs = 0;
            camera.Collision.CollisionAttribute.InteractsWith = 0;
            camera.CollisionRulesChanged();

            camera.RenderMode = RenderMode_t.kRenderNone;
            camera.RenderModeUpdated();
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

    private static string? ModelOf(CCSPlayerPawn pawn)
    {
        try
        {
            return pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName;
        }
        catch
        {
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

        // A teleport bumps the interpolation frame, which tells the client not
        // to blend from the last position; the camera moves every tick, so that
        // would make it step. Handing the old frame back keeps it blending --
        // but only for a move, never a cut: blended, the first placement slid
        // in from wherever the camera was spawned.
        bool cut =
            take.CameraAt == null
            || (take.Eye - take.CameraAt.Value).Length() > RenderDirectorUtility.CameraCutDistance;
        byte frame = camera.InterpolationFrame;

        camera.Teleport(
            new Vector(take.Eye.x, take.Eye.y, take.Eye.z),
            new QAngle(pitch, yaw, 0),
            new Vector(0, 0, 0)
        );

        take.CameraAt = take.Eye;

        if (!cut && camera.InterpolationFrame != frame)
        {
            camera.InterpolationFrame = frame;
            camera.InterpolationFrameUpdated();
        }
    }

    private void Fail(Take take, IPlayer player, string reason, params (string key, object? value)[] detail)
    {
        if (_take == take)
        {
            Reset(null);
        }

        var fields = new List<(string key, object? value)> { ("reason", reason) };
        fields.AddRange(detail);

        Tell(take, player, "error", fields.ToArray());
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

    private void Shot(Take take, IPlayer player, string name, string view)
    {
        Tell(take, player, "shot", ("name", name), ("view", view));
    }

    // Every line about a take names its lineup, so a pod never acts on a line
    // meant for the lineup before, and carries t (ms since go) once filming.
    private string Tell(Take take, IPlayer player, string eventName, params (string key, object? value)[] fields)
    {
        var all = new List<(string key, object? value)> { ("lineup", take.Lineup.id) };

        if (take.GoTicks >= 0)
        {
            all.Add(("t", RenderDirectorUtility.Milliseconds(take.GoTicks)));
        }

        all.AddRange(fields);

        string line = RenderDirectorUtility.Line(eventName, all.ToArray());
        Say(player, line);

        return line;
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
