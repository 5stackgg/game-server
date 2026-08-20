using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

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
    private readonly UtilityConfig _config;
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

    // In-world markers for the lineup currently loaded. Deliberately NOT part
    // of _ghosts: ghosts are filtered per viewer, and a marker is meant to be
    // seen by everyone on the server.
    // How far in front of the stance the reticle hangs. Deliberately short: an
    // aim ray traced to the far side of the map climbs thousands of units on
    // the upward pitch a smoke needs, and puts the marker on a ceiling nobody
    // is looking at. A player lines a crosshair up against something a few
    // metres away, so that is where the target goes -- unless a wall is nearer,
    // in which case it lands on the wall.
    private const float AimTraceRange = 420f;

    // A jump tops out around 54 units, so anything further below a recorded
    // position is a different floor and must not be snapped to.
    private const float GroundSnapRange = 96f;

    // Thinner than the ghost line at 1.6: a marker is an outline, and a ring
    // is a dozen overlapping segments whose glow compounds into a blob at
    // anything heavier.
    private const float MarkerWidth = 0.6f;

    // The aim reticle is the one marker that is not a place the utility goes,
    // so it never wears the utility's colour.
    private static readonly Color AimColor = new Color(255, 235, 120, 255);

    private readonly List<CEnvBeam> _markerBeams = new();
    private readonly List<CPointWorldText> _markerTexts = new();

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
        UtilityConfig config,
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

        // The same floor the stance marker is drawn on, so .load puts the
        // player standing on the ring rather than dropping into it.
        Vec3 feet = Grounded(lineup.release.feet_position);
        var position = new Vector(feet.x, feet.y, feet.z);

        // Yaw for the body, pitch for the eyes, and never the two together: a
        // pawn's rotation is which way it faces, so a lineup's -63 pitch fed
        // into it lies the player on their back.
        var facing = new QAngle(0, lineup.release.yaw, 0);
        var aim = new QAngle(lineup.release.pitch, lineup.release.yaw, 0);

        player.Teleport(position, facing, new Vector(0, 0, 0));
        pawn.EyeAngles = aim;

        // A single application is not enough: the client re-predicts from the
        // command it had in flight and snaps the view back.
        ReapplyAngles(player, facing, aim, 2);

        GiveUtility(player, lineup.utility_type);

        // Drawn a tick later, from where the player ACTUALLY ended up. The
        // engine resolves the floor by standing them on it, which beats any
        // trace we could run: a stored origin can be a jump height off (an
        // editor-authored lineup has no way to know the floor) and the marker
        // still lands under their feet.
        _core.Scheduler.NextTick(() =>
        {
            if (!player.IsValid)
            {
                return;
            }

            CCSPlayerPawn? settled = player.PlayerPawn;
            Vec3 standing = feet;

            if (settled != null && settled.IsValid)
            {
                Vector landed = settled.AbsOrigin ?? new Vector(feet.x, feet.y, feet.z);
                standing = new Vec3(landed.X, landed.Y, landed.Z);
            }

            ShowMarkers(lineup, standing);
        });

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

        ClearMarkers();

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

    // What to actually do once the crosshair is on the reticle. Everything the
    // lineup knows about the throw, in the order a player performs it.
    public static string ThrowHint(LineupRecord lineup)
    {
        string movement = lineup.technique switch
        {
            "Jump" => "JUMP THROW",
            "Run" => "RUN AND THROW",
            "RunJump" => "RUN + JUMP THROW",
            "Crouch" => "CROUCH THROW",
            "CrouchJump" => "CROUCH + JUMP THROW",
            "Walk" => "WALK AND THROW",
            _ => "STAND STILL",
        };

        string click = lineup.strength switch
        {
            "Half" => "LEFT + RIGHT CLICK",
            "Drop" => "RIGHT CLICK",
            "Full" => "LEFT CLICK",
            _ => "LEFT CLICK",
        };

        string bind = lineup.release.jump_throw ? " (jump-throw bind)" : "";

        return $"LINED UP\n{movement} - {click}{bind}";
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

    private void ReapplyAngles(IPlayer player, QAngle facing, QAngle aim, int frames)
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

            player.Teleport(null, facing, new Vector(0, 0, 0));

            CCSPlayerPawn? pawn = player.PlayerPawn;

            if (pawn != null && pawn.IsValid)
            {
                pawn.EyeAngles = aim;
            }

            ReapplyAngles(player, facing, aim, frames - 1);
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

    // Valve's own guides mark three things per lineup -- where you stand, what
    // you look at, and where it lands -- and that split is the right one, so
    // these mirror it. It is drawn from entities the server owns rather than
    // the annotation system, which is client-side and cannot be driven from
    // here at all.
    // The stance is passed in rather than traced again: by the time markers are
    // drawn the player is standing on the spot, and a downward trace from
    // inside their own hull hits them instead of the floor. Load works it out
    // before the teleport, while the spot is still empty.
    // Markers for a lineup the player is already standing on, with no teleport:
    // used straight after .save, where moving them would be pointless.
    public void ShowMarkersFor(IPlayer player, LineupRecord lineup)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return;
        }

        Vector feet = pawn.AbsOrigin ?? new Vector(0, 0, 0);

        ShowMarkers(lineup, new Vec3(feet.X, feet.Y, feet.Z));
    }

    private void ShowMarkers(LineupRecord lineup, Vec3 stance)
    {
        ClearMarkers();

        if (!_config.GhostPreview)
        {
            return;
        }

        Color color = ColorFor(lineup.utility_type);
        Vec3 landing = lineup.detonation_position;

        Ring(stance, 24f, color, MarkerWidth);
        Label(
            new Vec3(stance.x, stance.y, stance.z + 12f),
            $"STAND\n{lineup.name}",
            color
        );

        Ring(landing, 34f, color, MarkerWidth);
        Label(
            new Vec3(landing.x, landing.y, landing.z + 16f),
            lineup.utility_type.ToUpperInvariant(),
            color
        );

        AimReticle(lineup, stance);
    }

    // Where to point. The aim ray is traced until it hits something, so the
    // reticle lands ON the surface being aimed at rather than hanging in the
    // air short of it -- for an arcing smoke the crosshair sits well above the
    // landing spot, so distance-to-landing was never the right answer.
    private void AimReticle(LineupRecord lineup, Vec3 stance)
    {
        // From where the player's eye WILL BE after .load, not from where the
        // grenade left the hand. Those stopped being the same point when the
        // recorder started storing the standstill as feet_position: a run- or
        // jump-throw releases a whole run-up away from where it is set up, and
        // tracing from the release point puts the reticle that far off from
        // what the player sees standing on the marker.
        var eye = new Vec3(
            stance.x,
            stance.y,
            stance.z + PracticeSolverUtility.StandingEyeHeight
        );

        double yaw = lineup.release.yaw * Math.PI / 180.0;
        double pitch = lineup.release.pitch * Math.PI / 180.0;
        float flat = (float)Math.Cos(pitch);

        // CS2 pitch is negative looking up, so the sign flips here.
        var dir = new Vec3(
            (float)(Math.Cos(yaw) * flat),
            (float)(Math.Sin(yaw) * flat),
            (float)(-Math.Sin(pitch))
        );

        var from = new Vector(eye.x, eye.y, eye.z);
        var to = new Vector(
            eye.x + dir.x * AimTraceRange,
            eye.y + dir.y * AimTraceRange,
            eye.z + dir.z * AimTraceRange
        );

        Vec3 hit;

        try
        {
            var trace = _core.Trace.TraceShapeLine(from, to, null);

            hit = trace.DidHit
                ? new Vec3(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z)
                : new Vec3(to.X, to.Y, to.Z);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to trace a lineup's aim");
            hit = new Vec3(to.X, to.Y, to.Z);
        }

        // Pulled back off the surface so the reticle does not z-fight with the
        // wall it is drawn on.
        var center = new Vec3(
            hit.x - dir.x * 2f,
            hit.y - dir.y * 2f,
            hit.z - dir.z * 2f
        );

        float away = new Vec3(center.x - eye.x, center.y - eye.y, center.z - eye.z).Length();

        // Sized by distance so it looks the same from the stance whether the
        // wall is ten units away or two thousand.
        float size = Math.Clamp(away * 0.09f, 18f, 60f);

        // Deliberately not the utility's colour: this is the only marker that
        // is not a place the utility goes, and it has to separate from the
        // stance and landing rings at a glance.
        // Thin lines vanish at range, so the reticle's weight grows with
        // distance the same way its size does. The ground rings never need
        // this: you are always standing on them.
        float weight = Math.Clamp(away * 0.0018f, MarkerWidth, 2.2f);

        Reticle(center, dir, size, AimColor, weight);
    }

    // A box with a ring inside it, drawn in the plane facing back down the aim
    // ray so it reads as something to line a crosshair up with.
    private void Reticle(Vec3 center, Vec3 forward, float size, Color color, float width)
    {
        Vec3 right = Cross(forward, new Vec3(0, 0, 1));

        // Looking straight up or down leaves no horizon to take "right" from.
        if (right.Length() < 0.001f)
        {
            right = new Vec3(1, 0, 0);
        }

        right = Normalize(right);

        Vec3 up = Normalize(Cross(right, forward));

        Vec3 Corner(float x, float y) =>
            new Vec3(
                center.x + right.x * x + up.x * y,
                center.y + right.y * x + up.y * y,
                center.z + right.z * x + up.z * y
            );

        Vec3 topLeft = Corner(-size, size);
        Vec3 topRight = Corner(size, size);
        Vec3 bottomRight = Corner(size, -size);
        Vec3 bottomLeft = Corner(-size, -size);

        AddMarkerBeam(topLeft, topRight, color, width);
        AddMarkerBeam(topRight, bottomRight, color, width);
        AddMarkerBeam(bottomRight, bottomLeft, color, width);
        AddMarkerBeam(bottomLeft, topLeft, color, width);

        const int Segments = 20;
        float radius = size * 0.45f;

        for (int index = 0; index < Segments; index++)
        {
            double a = index * 2 * Math.PI / Segments;
            double b = (index + 1) * 2 * Math.PI / Segments;

            AddMarkerBeam(
                Corner((float)Math.Cos(a) * radius, (float)Math.Sin(a) * radius),
                Corner((float)Math.Cos(b) * radius, (float)Math.Sin(b) * radius),
                color,
                width
            );
        }
    }

    // Where a player would be STANDING at this spot. Lineups recorded before
    // the recorder learned to keep the standstill hold the release origin,
    // which for a jump throw is a jump height up in the air -- and a marker
    // floating at head height is not somewhere anyone can stand.
    private Vec3 Grounded(Vec3 position)
    {
        try
        {
            // Started above the point on purpose: a trace that begins flush
            // against a surface can report no hit at all, which is exactly the
            // case for a lineup already standing on the floor.
            var from = new Vector(position.x, position.y, position.z + 8f);
            var to = new Vector(
                position.x,
                position.y,
                position.z - GroundSnapRange
            );

            var trace = _core.Trace.TraceShapeLine(from, to, null);

            if (!trace.DidHit)
            {
                _logger.LogWarning(
                    "lineup stance at {x},{y},{z} found no floor within {range} units",
                    position.x,
                    position.y,
                    position.z,
                    GroundSnapRange
                );
                return position;
            }

            float drop = position.z - trace.EndPos.Z;

            if (drop > 1f)
            {
                _logger.LogInformation(
                    "lineup stance lowered {drop} units onto the floor (recorded airborne)",
                    drop
                );
            }

            return new Vec3(position.x, position.y, trace.EndPos.Z);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to find the floor under a lineup");
            return position;
        }
    }

    private void AddMarkerBeam(Vec3 start, Vec3 end, Color color, float width)
    {
        CEnvBeam? beam = CreateBeam(start, end, color, width);

        if (beam != null)
        {
            _markerBeams.Add(beam);
        }
    }

    private static Vec3 Cross(Vec3 a, Vec3 b)
    {
        return new Vec3(
            a.y * b.z - a.z * b.y,
            a.z * b.x - a.x * b.z,
            a.x * b.y - a.y * b.x
        );
    }

    private static Vec3 Normalize(Vec3 v)
    {
        float length = v.Length();

        return length < 0.0001f ? v : new Vec3(v.x / length, v.y / length, v.z / length);
    }

    private void Ring(Vec3 center, float radius, Color color, float width)
    {
        const int Segments = 20;

        for (int index = 0; index < Segments; index++)
        {
            double a = index * 2 * Math.PI / Segments;
            double b = (index + 1) * 2 * Math.PI / Segments;

            CEnvBeam? beam = CreateBeam(
                new Vec3(
                    center.x + (float)(Math.Cos(a) * radius),
                    center.y + (float)(Math.Sin(a) * radius),
                    center.z + 2f
                ),
                new Vec3(
                    center.x + (float)(Math.Cos(b) * radius),
                    center.y + (float)(Math.Sin(b) * radius),
                    center.z + 2f
                ),
                color,
                width
            );

            if (beam != null)
            {
                _markerBeams.Add(beam);
            }
        }
    }

    private void Label(Vec3 at, string text, Color color)
    {
        try
        {
            CPointWorldText label =
                _core.EntitySystem.CreateEntityByDesignerName<CPointWorldText>(
                    "point_worldtext"
                );

            if (!label.IsValid)
            {
                return;
            }

            label.MessageText = text;
            label.Color = color;
            label.FontSize = 60;
            label.FontName = "Arial Black";
            label.Fullbright = true;
            label.WorldUnitsPerPx = 0.15f;
            label.Enabled = true;
            label.JustifyHorizontal = PointWorldTextJustifyHorizontal_t
                .POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            label.JustifyVertical = PointWorldTextJustifyVertical_t
                .POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;
            // Always readable, wherever the reader is standing.
            label.ReorientMode = PointWorldTextReorientMode_t
                .POINT_WORLD_TEXT_REORIENT_AROUND_UP;

            label.Teleport(
                new Vector(at.x, at.y, at.z),
                new QAngle(0, 0, 0),
                new Vector(0, 0, 0)
            );

            label.DispatchSpawn();

            _markerTexts.Add(label);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to place a lineup marker");
        }
    }

    public void ClearMarkers()
    {
        foreach (CEnvBeam beam in _markerBeams)
        {
            if (beam.IsValid)
            {
                beam.Despawn();
            }
        }

        foreach (CPointWorldText label in _markerTexts)
        {
            if (label.IsValid)
            {
                label.Despawn();
            }
        }

        _markerBeams.Clear();
        _markerTexts.Clear();
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
