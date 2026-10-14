using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// Throws a real grenade from an exact launch seed, for anything that tests a
// throw by making the engine fly it: the solver and lineup discovery.
public class GrenadeEmitter
{
    private readonly ISwiftlyCore _core;
    private readonly PracticeRecorder _recorder;
    private readonly ILogger<GrenadeEmitter> _logger;

    public GrenadeEmitter(ISwiftlyCore core, PracticeRecorder recorder, ILogger<GrenadeEmitter> logger)
    {
        _core = core;
        _recorder = recorder;
        _logger = logger;
    }

    // An emitted grenade needs a live pawn to own it. The preferred player if
    // they are alive, else anyone who is; bots only when asked, because a
    // practice server's bots stand where somebody put them to be flashed.
    public IPlayer? Owner(ulong preferred, bool allowBots)
    {
        IPlayer? fallback = null;

        foreach (IPlayer player in _core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || !player.IsAlive)
            {
                continue;
            }

            if (player.IsFakeClient && !allowBots)
            {
                continue;
            }

            CBasePlayerPawn? pawn = player.Pawn;

            if (pawn == null || !pawn.IsValid)
            {
                continue;
            }

            if (player.SteamID == preferred)
            {
                return player;
            }

            fallback ??= player;
        }

        return fallback;
    }

    public uint? Emit(string utilityType, LaunchSeed seed, IPlayer owner)
    {
        CBasePlayerPawn? pawn = owner.Pawn;

        if (pawn == null || !pawn.IsValid)
        {
            return null;
        }

        var position = new Vector(seed.position.x, seed.position.y, seed.position.z);
        var velocity = new Vector(seed.velocity.x, seed.velocity.y, seed.velocity.z);

        (float pitch, float yaw) = TrajectoryUtility.AnglesFromVelocity(seed.velocity);
        var angles = new QAngle(pitch, yaw, 0);

        Team team = owner.Controller.Team;

        // The recorder's own guard against counting a grenade the plugin threw
        // as a lineup somebody made or an attempt somebody took. A solve or a
        // discovery emits hundreds, so this is the difference between that and
        // a library full of rubbish.
        _recorder.Emitting = true;

        try
        {
            CBaseCSGrenadeProjectile projectile;

            switch (utilityType)
            {
                case "Smoke":
                    projectile = _core.Game.EmitSmokeGrenade(position, angles, velocity, team, pawn);
                    break;
                case "Flash":
                    projectile = _core.Game.EmitFlashbang(position, angles, velocity, pawn);
                    break;
                case "HighExplosive":
                    projectile = _core.Game.EmitHEGrenade(position, angles, velocity, pawn);
                    break;
                case "Molotov":
                    projectile = _core.Game.EmitMolotov(position, angles, velocity, team, pawn);
                    break;
                default:
                    projectile = _core.Game.EmitDecoy(position, angles, velocity, pawn);
                    break;
            }

            if (!projectile.IsValid)
            {
                return null;
            }

            _recorder.Forget(projectile.Index);

            return projectile.Index;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to emit a {utility}", utilityType);
            return null;
        }
        finally
        {
            _recorder.Emitting = false;
        }
    }

    // Deferred a tick: this is usually called from inside the projectile's own
    // detonate event.
    public void Retire(uint index, Action? after = null)
    {
        _core.Scheduler.NextTick(() =>
        {
            CBaseCSGrenadeProjectile? projectile = ProjectileAt(index);

            if (projectile != null && projectile.IsValid)
            {
                projectile.Despawn();
            }

            after?.Invoke();
        });
    }

    public CBaseCSGrenadeProjectile? ProjectileAt(uint index)
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
}
