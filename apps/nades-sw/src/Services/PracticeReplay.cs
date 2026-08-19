using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace NadePractice;

// Puts a player back where a lineup was thrown from, and draws the line the
// grenade took so they can see the throw before they make it.
public class PracticeReplay
{
    private const float GhostSeconds = 8f;
    private const float GhostWidth = 1.6f;

    // A simplified line is a few dozen points; a long one is strided rather
    // than spawning an entity per segment.
    private const int MaxGhostSegments = 32;

    private const float MarkerHeight = 42f;

    private const float BloomWidth = 1.1f;

    // The landing point is where the grenade came to rest, so a smoke emitted
    // exactly there starts inside the floor. A hand's height above it falls
    // back onto the same spot.
    private const float BloomSmokeLift = 8f;

    // One beam per occupied voxel is thousands of entities for a single smoke.
    // The outline is contoured down to fit this, and a server full of people
    // previewing at once is capped again on top of it.
    private const int MaxBloomBeams = 48;
    private const int MaxBloomBeamsTotal = 240;

    private readonly ISwiftlyCore _core;
    private readonly NadesConfig _config;
    private readonly PracticeRecorder _recorder;
    private readonly ILogger<PracticeReplay> _logger;

    private enum GhostKind
    {
        Line,
        Bloom,
    }

    private class Ghost
    {
        public required ulong OwnerSteamId;
        public required GhostKind Kind;
        public required DateTime ExpiresAt;
        public required List<CEnvBeam> Beams;
    }

    private readonly List<Ghost> _ghosts = new List<Ghost>();

    // A real smoke emitted at the landing point: the only preview with perfect
    // fidelity, because it is the same cloud the throw would make.
    private readonly Dictionary<ulong, CSmokeGrenadeProjectile> _bloomSmoke = new();

    // Ghost projectiles the plugin has in the air, so their detonation can be
    // announced. A grenade nobody threw is invisible to everything outside this
    // process -- no demo event names it as a throw, and nothing else knows it
    // exists -- which is why the plugin has to say so itself.
    private class GhostThrow
    {
        public required ulong OwnerSteamId;
        public required string UtilityType;
        public required string ClientId;
        public required string? LineupId;
        public required DateTime ExpiresAt;
    }

    private const float GhostThrowSeconds = 30f;

    private readonly Dictionary<uint, GhostThrow> _ghostThrows = new();

    public PracticeReplay(
        ISwiftlyCore core,
        NadesConfig config,
        PracticeRecorder recorder,
        ILogger<PracticeReplay> logger
    )
    {
        _core = core;
        _config = config;
        _recorder = recorder;
        _logger = logger;
    }

    // Wired by the plugin rather than injected: PracticeSystem already depends
    // on this service, so asking for it back would close the cycle.
    public Func<ulong, bool> IsSolo { get; set; } = _ => true;

    // A capture client watching a lineup wants the throw and not the plugin's
    // drawing of it: beams in frame are our overlay filmed instead of the map.
    public Func<ulong, bool> WantsGhosts { get; set; } = _ => true;

    public void Load(IPlayer player, LineupRecord lineup)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return;
        }

        Vec3 feet = lineup.release.feet_position;
        var position = new Vector(feet.x, feet.y, feet.z);
        var angles = new QAngle(lineup.release.pitch, lineup.release.yaw, 0);

        player.Teleport(position, angles, new Vector(0, 0, 0));

        // A single application is not enough: the client re-predicts from the
        // command it had in flight and snaps the view back.
        ReapplyAngles(player, angles, 2);

        GiveUtility(player, lineup.utility_type);

        player.SendCenter(Describe(lineup));
    }

    // Tier 1 preview: the recorded line, drawn as beam segments with a marker
    // where it lands.
    public void ShowGhost(IPlayer player, LineupRecord lineup)
    {
        if (!_config.GhostPreview || !WantsGhosts(player.SteamID))
        {
            return;
        }

        ClearKind(player.SteamID, GhostKind.Line);

        Color color = ColorFor(lineup.utility_type);
        var beams = new List<CEnvBeam>();

        // A lineup fetched from the panel arrives without its flight path, and
        // the marker alone is the honest answer until the path has been
        // fetched: a straight beam to the landing spot is a wrong line, not a
        // missing one.
        List<Vec3> points = GhostPoints(lineup);

        for (int index = 0; index < points.Count - 1; index++)
        {
            CEnvBeam? beam = CreateBeam(points[index], points[index + 1], color, GhostWidth);
            if (beam != null)
            {
                beams.Add(beam);
            }
        }

        Vec3 landing = lineup.detonation_position;
        CEnvBeam? marker = CreateBeam(
            landing,
            new Vec3(landing.x, landing.y, landing.z + MarkerHeight),
            color,
            GhostWidth * 2f
        );

        if (marker != null)
        {
            beams.Add(marker);
        }

        if (beams.Count == 0)
        {
            return;
        }

        _ghosts.Add(
            new Ghost
            {
                OwnerSteamId = player.SteamID,
                Kind = GhostKind.Line,
                ExpiresAt = DateTime.UtcNow.AddSeconds(GhostSeconds),
                Beams = beams,
            }
        );

        ApplyVisibility();
    }

    // The measured bloom, outlined where it would actually sit. Answers how
    // many beams it took: zero when the panel has no measurement for this
    // lineup, which is a normal answer and not a failure.
    public int ShowBloom(IPlayer player, LineupRecord lineup)
    {
        ClearKind(player.SteamID, GhostKind.Bloom);

        if (!_config.GhostPreview || !WantsGhosts(player.SteamID))
        {
            return 0;
        }

        int budget = Math.Min(MaxBloomBeams, MaxBloomBeamsTotal - BloomBeamCount());

        if (budget <= 0)
        {
            return 0;
        }

        List<BloomSegment> outline = SmokeVolumeUtility.Outline(
            lineup.smoke_volume,
            new SmokeOutlineOptions { MaxSegments = budget }
        );

        if (outline.Count == 0)
        {
            return 0;
        }

        Color color = ColorFor(lineup.utility_type);
        var beams = new List<CEnvBeam>();

        foreach (BloomSegment segment in outline)
        {
            CEnvBeam? beam = CreateBeam(segment.a, segment.b, color, BloomWidth);

            if (beam != null)
            {
                beams.Add(beam);
            }
        }

        if (beams.Count == 0)
        {
            return 0;
        }

        // Held until it is toggled off rather than expiring: a player lining a
        // throw up is looking at it for as long as that takes.
        _ghosts.Add(
            new Ghost
            {
                OwnerSteamId = player.SteamID,
                Kind = GhostKind.Bloom,
                ExpiresAt = DateTime.MaxValue,
                Beams = beams,
            }
        );

        ApplyVisibility();

        return beams.Count;
    }

    // The outline is a drawing of the measurement; this is the measurement's
    // subject. Only Swiftly can emit one, so only Swiftly offers it.
    public bool ShowBloomSmoke(IPlayer player, LineupRecord lineup)
    {
        ClearBloomSmoke(player.SteamID);

        if (lineup.utility_type != "Smoke")
        {
            return false;
        }

        CBasePlayerPawn? pawn = player.Pawn;

        if (pawn == null || !pawn.IsValid)
        {
            return false;
        }

        Vec3 landing = lineup.detonation_position;

        _recorder.Emitting = true;

        try
        {
            CSmokeGrenadeProjectile smoke = _core.Game.EmitSmokeGrenade(
                new Vector(landing.x, landing.y, landing.z + BloomSmokeLift),
                new QAngle(0, 0, 0),
                new Vector(0, 0, 0),
                player.Controller.Team,
                pawn
            );

            if (!smoke.IsValid)
            {
                return false;
            }

            // Belt and braces with the flag above: whichever way round the
            // engine raises entity creation, this grenade is not a lineup.
            _recorder.Forget(smoke.Index);

            _bloomSmoke[player.SteamID] = smoke;
            ApplyVisibility();

            return true;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to emit a bloom preview smoke");
            return false;
        }
        finally
        {
            _recorder.Emitting = false;
        }
    }

    public void ClearBloomSmoke(ulong steamId)
    {
        if (!_bloomSmoke.Remove(steamId, out CSmokeGrenadeProjectile? smoke))
        {
            return;
        }

        if (smoke.IsValid)
        {
            Unblock(smoke.Index);
            smoke.Despawn();
        }
    }

    // Tier 2: a real grenade, launched from the physics seed the engine gave
    // us at record time rather than from the player's eye angles, so it lands
    // where the recorded one did instead of near it.
    public void ThrowGhostProjectile(IPlayer player, LineupRecord lineup)
    {
        if (!_config.GhostProjectile)
        {
            return;
        }

        CBasePlayerPawn? pawn = player.Pawn;

        if (pawn == null || !pawn.IsValid)
        {
            return;
        }

        // Both halves or nothing. A zeroed seed would launch the grenade out of
        // the map origin, and a seed the panel did not call exact belongs to a
        // path fitted to a demo rather than to a throw the engine performed --
        // practise toward that one, never replay it.
        if (!lineup.IsExactlyReplayable())
        {
            return;
        }

        Vec3 seedPosition = lineup.initial_position;
        Vec3 seedVelocity = lineup.initial_velocity;

        var position = new Vector(seedPosition.x, seedPosition.y, seedPosition.z);
        var velocity = new Vector(seedVelocity.x, seedVelocity.y, seedVelocity.z);

        (float pitch, float yaw) = TrajectoryUtility.AnglesFromVelocity(seedVelocity);
        var angles = new QAngle(pitch, yaw, 0);

        Team team = player.Controller.Team;

        _recorder.Emitting = true;

        try
        {
            CBaseCSGrenadeProjectile projectile;

            switch (lineup.utility_type)
            {
                case "Smoke":
                    projectile = _core.Game.EmitSmokeGrenade(
                        position,
                        angles,
                        velocity,
                        team,
                        pawn
                    );
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

            if (projectile.IsValid)
            {
                _recorder.Forget(projectile.Index);

                _ghostThrows[projectile.Index] = new GhostThrow
                {
                    OwnerSteamId = player.SteamID,
                    UtilityType = lineup.utility_type,
                    ClientId = lineup.client_id,
                    LineupId = lineup.id,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(GhostThrowSeconds),
                };
            }
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to replay {utility}", lineup.utility_type);
        }
        finally
        {
            _recorder.Emitting = false;
        }
    }

    // Answers the console line for a ghost the plugin emitted, or null when the
    // detonation belongs to somebody's real throw.
    public string? GhostDetonated(uint entityIndex, Vec3 position)
    {
        return _ghostThrows.Remove(entityIndex, out GhostThrow? ghost)
            ? Announce(ghost, position)
            : null;
    }

    // EventMolotovDetonate carries no entity index, so a ghost molotov is
    // matched by its owner. The plugin only ever has one ghost in the air per
    // player, so there is nothing to disambiguate between.
    public string? GhostMolotovDetonated(ulong steamId, Vec3 position)
    {
        foreach ((uint index, GhostThrow ghost) in _ghostThrows)
        {
            if (ghost.OwnerSteamId == steamId && ghost.UtilityType == "Molotov")
            {
                _ghostThrows.Remove(index);
                return Announce(ghost, position);
            }
        }

        return null;
    }

    private static string Announce(GhostThrow ghost, Vec3 position)
    {
        return PracticeSignalUtility.GhostDetonatedLine(
            ghost.UtilityType,
            position,
            ghost.ClientId,
            ghost.LineupId,
            ghost.OwnerSteamId
        );
    }

    public void ClearBloom(ulong steamId)
    {
        ClearKind(steamId, GhostKind.Bloom);
        ClearBloomSmoke(steamId);
    }

    public void ClearGhosts(ulong steamId)
    {
        for (int index = _ghosts.Count - 1; index >= 0; index--)
        {
            if (_ghosts[index].OwnerSteamId != steamId)
            {
                continue;
            }

            Kill(_ghosts[index]);
            _ghosts.RemoveAt(index);
        }

        ClearBloomSmoke(steamId);
    }

    public void ClearAll()
    {
        foreach (Ghost ghost in _ghosts)
        {
            Kill(ghost);
        }

        _ghosts.Clear();
        _ghostThrows.Clear();

        foreach (ulong steamId in _bloomSmoke.Keys.ToList())
        {
            ClearBloomSmoke(steamId);
        }
    }

    private void ClearKind(ulong steamId, GhostKind kind)
    {
        for (int index = _ghosts.Count - 1; index >= 0; index--)
        {
            if (_ghosts[index].OwnerSteamId != steamId || _ghosts[index].Kind != kind)
            {
                continue;
            }

            Kill(_ghosts[index]);
            _ghosts.RemoveAt(index);
        }
    }

    private int BloomBeamCount()
    {
        return _ghosts
            .Where(ghost => ghost.Kind == GhostKind.Bloom)
            .Sum(ghost => ghost.Beams.Count);
    }

    public void Sweep()
    {
        DateTime now = DateTime.UtcNow;

        // A ghost that never reported a detonation is a projectile that was
        // removed some other way. Its entry is dropped rather than left to
        // shadow whatever lands on that index next.
        foreach ((uint index, GhostThrow ghost) in _ghostThrows.ToList())
        {
            if (ghost.ExpiresAt <= now)
            {
                _ghostThrows.Remove(index);
            }
        }

        foreach ((ulong owner, CSmokeGrenadeProjectile smoke) in _bloomSmoke.ToList())
        {
            if (!smoke.IsValid)
            {
                _bloomSmoke.Remove(owner);
            }
        }

        for (int index = _ghosts.Count - 1; index >= 0; index--)
        {
            if (_ghosts[index].ExpiresAt > now)
            {
                continue;
            }

            Kill(_ghosts[index]);
            _ghosts.RemoveAt(index);
        }
    }

    // Swiftly's transmit blocking is a standing per-player list rather than a
    // per-frame callback, so it is recomputed whenever the set of beams or of
    // solo players changes.
    public void ApplyVisibility()
    {
        foreach (IPlayer viewer in _core.PlayerManager.GetAllPlayers())
        {
            if (viewer == null || !viewer.IsValid || viewer.IsFakeClient)
            {
                continue;
            }

            bool viewerIsSolo = IsSolo(viewer.SteamID);
            bool viewerWantsGhosts = WantsGhosts(viewer.SteamID);

            foreach (Ghost ghost in _ghosts)
            {
                bool hidden =
                    !viewerWantsGhosts
                    || (
                        ghost.OwnerSteamId != viewer.SteamID
                        && (viewerIsSolo || IsSolo(ghost.OwnerSteamId))
                    );

                foreach (CEnvBeam beam in ghost.Beams)
                {
                    if (beam.IsValid)
                    {
                        viewer.ShouldBlockTransmitEntity((int)beam.Index, hidden);
                    }
                }
            }

            foreach ((ulong owner, CSmokeGrenadeProjectile smoke) in _bloomSmoke)
            {
                if (!smoke.IsValid)
                {
                    continue;
                }

                viewer.ShouldBlockTransmitEntity(
                    (int)smoke.Index,
                    !viewerWantsGhosts
                        || (owner != viewer.SteamID && (viewerIsSolo || IsSolo(owner)))
                );
            }
        }
    }

    public static string Describe(LineupRecord lineup)
    {
        string name = string.IsNullOrEmpty(lineup.name) ? "unnamed" : lineup.name;
        string strength = string.IsNullOrEmpty(lineup.strength) ? "" : $" / {lineup.strength}";

        return $"{name}\n{lineup.utility_type} - {lineup.technique}{strength}";
    }

    private void GiveUtility(IPlayer player, string utilityType)
    {
        string? weapon = PracticeLineupUtility.WeaponForUtilityType(utilityType);
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (weapon == null || pawn == null || !pawn.IsValid)
        {
            return;
        }

        if (!HasWeapon(pawn, weapon))
        {
            pawn.ItemServices?.GiveItem(weapon);
        }

        pawn.WeaponServices?.SelectWeaponByDesignerName(weapon);
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

    private void ReapplyAngles(IPlayer player, QAngle angles, int frames)
    {
        if (frames <= 0)
        {
            return;
        }

        _core.Scheduler.NextTick(() =>
        {
            if (!player.IsValid)
            {
                return;
            }

            player.Teleport(null, angles, new Vector(0, 0, 0));
            ReapplyAngles(player, angles, frames - 1);
        });
    }

    // Empty when the path is unknown; ShowGhost still draws the marker.
    private static List<Vec3> GhostPoints(LineupRecord lineup)
    {
        var points = new List<Vec3>();

        if (lineup.trajectory.Count == 0)
        {
            return points;
        }

        int stride = Math.Max(
            1,
            (int)Math.Ceiling(lineup.trajectory.Count / (double)MaxGhostSegments)
        );

        // The seed is where the grenade actually left the hand. Drawing the line
        // from it only asks that the point be real, not that the throw be
        // reproducible, so this is the looser of the two questions.
        if (lineup.HasPhysicsSeed())
        {
            points.Add(lineup.initial_position);
        }

        for (int index = 0; index < lineup.trajectory.Count; index++)
        {
            // Bounces are where the line changes direction, so they survive
            // striding.
            if (index % stride == 0 || lineup.trajectory[index].bounce)
            {
                points.Add(lineup.trajectory[index].p);
            }
        }

        points.Add(lineup.detonation_position);

        return points;
    }

    private CEnvBeam? CreateBeam(Vec3 start, Vec3 end, Color color, float width)
    {
        try
        {
            CEnvBeam beam = _core.EntitySystem.CreateEntityByDesignerName<CEnvBeam>("env_beam");

            if (!beam.IsValid)
            {
                return null;
            }

            beam.Render = color;
            beam.Width = width;

            beam.Teleport(
                new Vector(start.x, start.y, start.z),
                new QAngle(0, 0, 0),
                new Vector(0, 0, 0)
            );

            beam.EndPos = new Vector(end.x, end.y, end.z);

            beam.DispatchSpawn();

            return beam;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to draw a lineup preview");
            return null;
        }
    }

    private void Kill(Ghost ghost)
    {
        foreach (CEnvBeam beam in ghost.Beams)
        {
            if (!beam.IsValid)
            {
                continue;
            }

            Unblock(beam.Index);
            beam.Despawn();
        }
    }

    // Entity indices are reused, so a standing block has to be lifted before
    // the entity goes away or it will hide whatever lands on that index next.
    private void Unblock(uint index)
    {
        foreach (IPlayer viewer in _core.PlayerManager.GetAllPlayers())
        {
            if (viewer != null && viewer.IsValid)
            {
                viewer.ShouldBlockTransmitEntity((int)index, false);
            }
        }
    }

    private static Color ColorFor(string utilityType)
    {
        switch (utilityType)
        {
            case "Smoke":
                return new Color(220, 220, 220, 255);
            case "Flash":
                return new Color(120, 180, 255, 255);
            case "HighExplosive":
                return new Color(255, 90, 90, 255);
            case "Molotov":
                return new Color(255, 150, 40, 255);
            default:
                return new Color(200, 120, 255, 255);
        }
    }
}
