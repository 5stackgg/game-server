using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// Reload, while a grenade of yours is in the air, rides along behind it: the
// landing is the half of a throw the thrower never gets to see, and walking
// round the corner to look takes longer than the smoke lasts.
//
// The camera is the render director's chase shot without the film-making. It
// runs along the path the grenade has already flown, a fixed distance behind,
// so it can only ever be somewhere the grenade has been -- which is how it
// stays out of walls without knowing where any wall is.
public partial class UtilityPracticePlugin
{
    private class Flight
    {
        public required CBaseCSGrenadeProjectile Projectile;
        public required uint Index;
        public required string Designer;
        public required int Since;
        public required Vec3 From;
        public Vec3 At;
        public int Still;
        public int Tick = -1;
        public bool Over;
    }

    private class Follow
    {
        public required Flight Flight;
        public required CDynamicProp Camera;
        public required uint HudWas;
        public required int Pressed;
        public readonly List<Vec3> Path = new();
        public readonly List<float> Arc = new();
        public Vec3? CameraAt;
        public float Rise;
        public bool KeyDown = true;
        public int OverTick = -1;
    }

    // The newest grenade each player has in the air, real or rethrown.
    private readonly Dictionary<ulong, Flight> _flying = new();
    private readonly Dictionary<ulong, Follow> _following = new();

    // The same bit the render director sets: the crosshair is the player's,
    // and in a view that is not theirs it is just a mark on the picture.
    private const uint FollowHideCrosshair = 1 << 8;

    // Less than this in a tick, for this many ticks running, is a grenade that
    // has stopped. The top of a lob is slower than that for three ticks at
    // most, so the two cannot be confused.
    private const float RestUnitsPerTick = 0.25f;
    private const int RestTicks = 8;

    // A smoke's projectile outlives its flight by the whole life of the smoke.
    private const int FlightMaxTicks = 12 * 64;

    // Tapped rather than held, the view comes home by itself this long after
    // the grenade is done: enough to see a smoke open or a fire catch.
    private const int FollowLingerTicks = 96;

    // The release that ends a held follow can be lost the same way the one
    // that ends a zoom can, so nothing rides along for longer than this.
    private const int FollowMaxTicks = 20 * 64;

    // Once it has landed the camera backs away to here, or a smoke would open
    // around it and the picture would be the inside of a cloud.
    private const float FollowLandedDistance = 300f;
    private const float FollowBackOffSeconds = 0.6f;

    private const float FollowDt = 1f / RenderDirectorUtility.TickRate;

    // Called a tick after a projectile is created, when its thrower is set.
    private void TrackFlight(CEntityInstance entity)
    {
        if (_config.RenderMode || !(entity.DesignerName ?? "").EndsWith("_projectile"))
        {
            return;
        }

        try
        {
            CBaseCSGrenadeProjectile projectile = entity.As<CBaseCSGrenadeProjectile>();
            CBaseEntity? thrower = projectile.Thrower.Value;

            if (thrower == null || !thrower.IsValid)
            {
                return;
            }

            IPlayer? player = Core.PlayerManager.GetPlayerFromPawn(thrower.As<CBasePlayerPawn>());

            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                return;
            }

            Vector from = projectile.InitialPosition;
            Vector at = projectile.AbsOrigin ?? from;

            string designer = entity.DesignerName ?? "";

            TrackLive(player, projectile.Index, designer);

            _flying[player.SteamID] = new Flight
            {
                Projectile = projectile,
                Index = projectile.Index,
                Designer = designer,
                Since = _aimTick,
                From = new Vec3(from.X, from.Y, from.Z),
                At = new Vec3(at.X, at.Y, at.Z),
            };
        }
        catch
        {
            // A projectile that cannot be read is one nobody can ride.
        }
    }

    private bool HasFlight(ulong steamId)
    {
        return _flying.ContainsKey(steamId);
    }

    private bool IsFollowing(ulong steamId)
    {
        return _following.ContainsKey(steamId);
    }

    // Every game tick, not every HUD frame: the camera is an entity the client
    // blends between ticks, and one moved four ticks at a time steps.
    private void FollowTick()
    {
        if (_flying.Count > 0)
        {
            foreach ((ulong steamId, Flight flight) in _flying.ToList())
            {
                Advance(flight);

                if (flight.Over)
                {
                    _flying.Remove(steamId);
                }
            }
        }

        if (_following.Count == 0)
        {
            return;
        }

        foreach ((ulong steamId, Follow follow) in _following.ToList())
        {
            try
            {
                Ride(steamId, follow);
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "the follow camera failed for {steam}", steamId);
                EndFollow(steamId);
            }
        }
    }

    // Once a tick, whoever asks: a flight being ridden is asked twice, by the
    // list of what is in the air and by the camera riding it.
    private void Advance(Flight flight)
    {
        if (flight.Over || flight.Tick == _aimTick)
        {
            return;
        }

        flight.Tick = _aimTick;

        CBaseCSGrenadeProjectile projectile = flight.Projectile;
        Vector? origin = null;

        try
        {
            if (projectile.IsValid && projectile.Index == flight.Index)
            {
                origin = projectile.AbsOrigin;
            }
        }
        catch
        {
            origin = null;
        }

        if (origin == null || _aimTick - flight.Since > FlightMaxTicks)
        {
            flight.Over = true;

            return;
        }

        var at = new Vec3(origin.Value.X, origin.Value.Y, origin.Value.Z);

        flight.Still = (at - flight.At).Length() < RestUnitsPerTick ? flight.Still + 1 : 0;
        flight.At = at;

        if (flight.Still >= RestTicks)
        {
            flight.Over = true;
        }
    }

    private bool StartFollow(IPlayer player)
    {
        ulong steamId = player.SteamID;

        if (!_flying.TryGetValue(steamId, out Flight? flight) || flight.Over)
        {
            return false;
        }

        CCSPlayerPawn? pawn = player.PlayerPawn;
        CCSPlayer_CameraServices? services = pawn?.CameraServices;

        if (pawn == null || !pawn.IsValid || services == null)
        {
            return false;
        }

        CDynamicProp? camera = _director.SpawnCamera(pawn);

        if (camera == null)
        {
            return false;
        }

        var follow = new Follow
        {
            Flight = flight,
            Camera = camera,
            HudWas = pawn.HideHUD,
            Pressed = _aimTick,
        };

        // What was flown before the key went down is not on record, and a
        // straight line from the hand is close enough: the camera only ever
        // uses the last stretch of it.
        follow.Path.Add(flight.From);
        follow.Arc.Add(0f);
        Extend(follow, flight.At);

        _following[steamId] = follow;

        Unzoom(player);
        Place(follow);

        services.ViewEntity = Core.EntitySystem.GetRefEHandle<CBaseEntity>(camera);
        services.ViewEntityUpdated();

        pawn.HideHUD = follow.HudWas | FollowHideCrosshair;
        pawn.HideHUDUpdated();

        return true;
    }

    private void Ride(ulong steamId, Follow follow)
    {
        IPlayer? player = _system.Find(steamId);
        CCSPlayerPawn? pawn = player?.PlayerPawn;

        if (
            player == null
            || !player.IsValid
            || pawn == null
            || !pawn.IsValid
            || !follow.Camera.IsValid
            || _aimTick - follow.Pressed > FollowMaxTicks
        )
        {
            EndFollow(steamId);

            return;
        }

        Flight flight = follow.Flight;

        // A second grenade thrown takes the first one's place in the air, and
        // the first is still this camera's to finish watching.
        Advance(flight);
        Extend(follow, flight.At);

        if (flight.Over && follow.OverTick < 0)
        {
            follow.OverTick = _aimTick;
        }

        if (!follow.KeyDown && follow.OverTick >= 0 && _aimTick - follow.OverTick > FollowLingerTicks)
        {
            EndFollow(steamId);

            return;
        }

        Place(follow);
    }

    private static void Extend(Follow follow, Vec3 at)
    {
        float step = (at - follow.Path[^1]).Length();

        if (step < 1f)
        {
            return;
        }

        follow.Path.Add(at);
        follow.Arc.Add(follow.Arc[^1] + step);
    }

    private void Place(Follow follow)
    {
        float behind = RenderDirectorUtility.ChaseDistance;

        if (follow.OverTick >= 0)
        {
            float since = (_aimTick - follow.OverTick) * FollowDt;

            behind +=
                (FollowLandedDistance - RenderDirectorUtility.ChaseDistance)
                * RenderDirectorUtility.Ease(since / FollowBackOffSeconds);
        }

        Vec3 onPath = RenderDirectorUtility.PointAlong(
            follow.Path,
            follow.Arc,
            MathF.Max(0f, follow.Arc[^1] - behind)
        );
        Vec3 lifted = _replay.CameraClear(
            onPath,
            onPath + new Vec3(0f, 0f, RenderDirectorUtility.ChaseHeight)
        );

        follow.Rise = RenderDirectorUtility
            .Approach(
                new Vec3(0f, 0f, follow.Rise),
                new Vec3(0f, 0f, MathF.Max(0f, lifted.z - onPath.z)),
                FollowDt,
                RenderDirectorUtility.ChaseRiseHalfLife
            )
            .z;

        Vec3 eye = onPath + new Vec3(0f, 0f, follow.Rise);
        (float pitch, float yaw) = RenderDirectorUtility.LookAt(eye, follow.Flight.At);
        CDynamicProp camera = follow.Camera;

        // The same trick the director's camera uses: a teleport tells the
        // client not to blend from the last position, which is right for the
        // cut away from the eyes and wrong for every tick after it.
        bool cut =
            follow.CameraAt == null
            || (eye - follow.CameraAt.Value).Length() > RenderDirectorUtility.CameraCutDistance;
        byte frame = camera.InterpolationFrame;

        camera.Teleport(new Vector(eye.x, eye.y, eye.z), new QAngle(pitch, yaw, 0), new Vector(0, 0, 0));
        follow.CameraAt = eye;

        if (!cut && camera.InterpolationFrame != frame)
        {
            camera.InterpolationFrame = frame;
            camera.InterpolationFrameUpdated();
        }
    }

    private void EndFollow(ulong steamId)
    {
        if (!_following.Remove(steamId, out Follow? follow))
        {
            return;
        }

        try
        {
            CCSPlayerPawn? pawn = _system.Find(steamId)?.PlayerPawn;

            if (pawn != null && pawn.IsValid)
            {
                CCSPlayer_CameraServices? services = pawn.CameraServices;

                if (services != null)
                {
                    services.ViewEntity = CHandle<CBaseEntity>.Invalid;
                    services.ViewEntityUpdated();
                }

                pawn.HideHUD = follow.HudWas;
                pawn.HideHUDUpdated();
            }
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to hand the view back to {steam}", steamId);
        }

        try
        {
            if (follow.Camera.IsValid)
            {
                follow.Camera.Despawn();
            }
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to remove the follow camera of {steam}", steamId);
        }
    }

    // A reload that left somebody looking through a camera would leave them
    // there: the next instance has no record that the view was ever taken.
    private void EndFollowEveryone()
    {
        foreach (ulong steamId in _following.Keys.ToList())
        {
            EndFollow(steamId);
        }

        _flying.Clear();
    }
}
