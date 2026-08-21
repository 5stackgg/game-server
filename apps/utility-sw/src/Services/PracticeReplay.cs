using System.Globalization;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.EntitySystem;
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
    // Traced far enough to reach real geometry: a crosshair placement is only
    // exact if it sits on something in the world, so the ring goes wherever the
    // aim ray actually lands.
    private const float AimTraceRange = 4096f;

    // A standing hull is 32 wide and 72 tall, so anything past ~48 units along
    // the aim is outside it whichever way the player is facing.
    private const float AimTraceStartOffset = 48f;

    // Only used when the ray leaves the map without hitting anything -- aiming
    // up over open ground has no surface to mark, and a ring hung out at the
    // full trace length would be a speck against the skybox.
    // Deliberately far. A crosshair marker hung close to the player shifts
    // across the skyline with every step, so standing a few units off the spot
    // aims you somewhere else entirely. Far away it behaves like the horizon:
    // the direction is what matters and small stance errors stop mattering.
    private const float AimFallbackRange = 2400f;

    // A jump tops out around 54 units, so anything further below a recorded
    // position is a different floor and must not be snapped to.
    private const float GroundSnapRange = 96f;

    // Thinner than the ghost line at 1.6: a marker is an outline, and a ring
    // is a dozen overlapping segments whose glow compounds into a blob at
    // anything heavier.
    private const float MarkerWidth = 0.6f;


    // Roughly where the grenade sits in a player's hand, so it reads as "this
    // is what you throw from here" rather than as litter on the floor.
    // Side by side when one spot wants more than one kind of grenade.
    private const float UtilityModelSpacing = 16f;

    // Above standing eye height (64) on purpose. .load stands the player ON
    // the ring, and at chest height the grenade is inside their camera.
    private const float UtilityModelHeight = 80f;

    // The aim reticle is the one marker that is not a place the utility goes,
    // so it never wears the utility's colour.
    private static readonly Color AimColor = new Color(255, 235, 120, 255);

    // The library layer: every lineup's stance ring, landing ring, name and
    // grenade model. Shared on purpose -- everyone on the server should see
    // where the lineups are.
    private readonly List<CEnvBeam> _markerBeams = new();
    private readonly List<CPointWorldText> _markerTexts = new();
    private readonly List<CPhysicsProp> _markerProps = new();

    // The selection layer: the crosshair and labels for whichever lineup ONE
    // player has focused. Kept per player and transmit-blocked from everybody
    // else, because two people practising at once were otherwise wiping each
    // other's aim marker every time either of them moved.
    private class Selection
    {
        public readonly List<CEnvBeam> Beams = new();
        public readonly List<CPointWorldText> Texts = new();

        // The crosshairs, kept per throw so they can be recoloured as the
        // player moves the mouse instead of being torn down and redrawn.
        public readonly List<Aim> Aims = new();
    }

    private class Aim
    {
        public LineupRecord Lineup = null!;
        public readonly List<CEnvBeam> Beams = new();
        public CPointWorldText? Label;
        public float Miss = -1f;
    }

    // The reticle currently being drawn, so its beams can be collected apart
    // from the rest of the selection.
    private Aim? _aimInto;

    private readonly Dictionary<ulong, Selection> _selections = new();

    // Which list the drawing helpers append to. Null means the shared layer.
    private Selection? _drawingInto;

    // Which of the drawn throws is the one the player is looking toward. Every
    // throw off a spot is drawn -- you cannot choose between options you cannot
    // see -- and this one is drawn heavier so it stands out from its siblings.
    private LineupRecord? _focused;

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

    // The whole library for a player, so loading one lineup still draws the
    // rest. Supplied by the plugin, which owns the library.
    public Func<ulong, IReadOnlyList<LineupRecord>> All { get; set; } =
        _ => Array.Empty<LineupRecord>();

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

            // Everything on the map, with this one in focus.
            IReadOnlyList<LineupRecord> everything = All(player.SteamID);

            IReadOnlyList<LineupRecord> library =
                everything.Count > 0 ? everything : new[] { lineup };

            // Whatever else is throwable from this spot comes up with it.
            List<LineupRecord> here = SpotAt(library, standing);

            if (!here.Any(entry => entry.client_id == lineup.client_id))
            {
                here.Add(lineup);
            }

            ShowLibrary(library);
            ShowSelection(player, here, standing);
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
        IReadOnlyList<LineupRecord> everything = All(player.SteamID);

        var standing = new Vec3(feet.X, feet.Y, feet.Z);
        IReadOnlyList<LineupRecord> library =
            everything.Count > 0 ? everything : new[] { lineup };

        List<LineupRecord> here = SpotAt(library, standing);

        if (!here.Any(entry => entry.client_id == lineup.client_id))
        {
            here.Add(lineup);
        }

        ShowLibrary(library);
        ShowSelection(player, here, standing);
    }

    // Every lineup on the map at once. Loading one and cycling with .next hides
    // the thing a practice server is for: seeing where all the smokes go and
    // walking between them. The focused one gets the full treatment; the rest
    // stay as a stance ring and a landing ring so a map full of them is still
    // readable and still cheap.
    // How close a player has to be to a stance ring to be "in" it. A little
    // wider than the ring itself so stepping onto the marker counts.
    public const float SpotRadius = 40f;

    // How far above or below a spot still counts as the same place to stand.
    public const float SpotHeight = 72f;

    // Every lineup on the map, plus the full crosshair treatment for the ones
    // the player can actually throw from where they are standing. One spot
    // often has several throws off it, and the whole point of standing there is
    // to see all of them at once.
    public void ShowAllMarkers(
        IEnumerable<LineupRecord> lineups,
        IReadOnlyCollection<LineupRecord> active,
        Vec3 stance
    )
    {
        ShowLibrary(lineups);
        ShowSelection(null, active, stance);
    }

    // The library layer. Every lineup gets the same quiet treatment -- no
    // exclusions, because a lineup one player has focused is still just a ring
    // to everyone else.
    public void ShowLibrary(IEnumerable<LineupRecord> lineups)
    {
        ClearSharedMarkers();

        _drawingInto = null;

        foreach (LineupRecord lineup in lineups)
        {
            Color type = ColorFor(lineup.utility_type);
            Vec3 feet = Grounded(lineup.release.feet_position);

            // Seven beams where there used to be twenty-one. A map holds
            // hundreds of these at once, so the resting state has to be the
            // cheapest thing on screen as well as the quietest -- and no
            // connecting line at rest, which is what turned a busy map into a
            // cat's cradle.
            Chevron(feet, lineup.release.yaw, 13f, AmberDim, MarkerWidth);
            AddMarkerBeam(
                new Vec3(feet.x, feet.y, feet.z + 2f),
                new Vec3(feet.x, feet.y, feet.z + 9f),
                AmberDim,
                MarkerWidth
            );

            Diamond(lineup.detonation_position, 22f, type, MarkerWidth);

            Label(new Vec3(feet.x, feet.y, feet.z + 14f), lineup.name, Amber);
        }

        ShowSpotUtility(lineups);
    }

    // What to bring, not which throw to make. A model belongs to the SPOT: two
    // smokes thrown from one position want ONE smoke floating over it, or the
    // spot reads as six grenades rather than one place to stand. A spot holding
    // a smoke and a flash still shows both, because that is a real choice about
    // what to equip.
    private void ShowSpotUtility(IEnumerable<LineupRecord> lineups)
    {
        List<(float x, float y, float z, List<string> types)> spots =
            PracticeLineupUtility.UtilityBySpot(
                lineups.Select(lineup =>
                {
                    Vec3 feet = Grounded(lineup.release.feet_position);

                    return (feet.x, feet.y, feet.z, lineup.utility_type);
                }),
                SpotRadius,
                SpotHeight
            );

        foreach ((float x, float y, float z, List<string> types) spot in spots)
        {
            for (int index = 0; index < spot.types.Count; index += 1)
            {
                // Centred row, so a single grenade sits over the middle of the
                // ring and two straddle it rather than one sitting off to a side.
                float offset = (index - ((spot.types.Count - 1) / 2f)) * UtilityModelSpacing;

                UtilityModel(spot.types[index], new Vec3(spot.x + offset, spot.y, spot.z));
            }
        }
    }

    // One player's focused lineups: the big ring, the STAND label and the aim
    // crosshair. Hidden from every other viewer, so nobody else's movement can
    // take it away. A null owner draws into the shared layer, which is only for
    // the single-player paths that predate the split.
    public void ShowSelection(
        IPlayer? owner,
        IReadOnlyCollection<LineupRecord> active,
        Vec3 stance,
        LineupRecord? focused = null
    )
    {
        _focused = focused;

        if (owner == null)
        {
            _drawingInto = null;

            foreach (LineupRecord lineup in active)
            {
                ShowMarkers(lineup, stance);
            }

            return;
        }

        ClearSelection(owner.SteamID);

        if (active.Count == 0)
        {
            return;
        }

        var selection = new Selection();

        _selections[owner.SteamID] = selection;
        _drawingInto = selection;

        try
        {
            foreach (LineupRecord lineup in active)
            {
                ShowMarkers(lineup, stance);
            }
        }
        finally
        {
            _drawingInto = null;
        }

        ApplySelectionVisibility();
    }

    // A selection belongs to one viewer, so it is blocked for everybody else.
    private void ApplySelectionVisibility()
    {
        foreach (IPlayer viewer in _core.PlayerManager.GetAllPlayers())
        {
            if (viewer == null || !viewer.IsValid || viewer.IsFakeClient)
            {
                continue;
            }

            foreach ((ulong owner, Selection selection) in _selections)
            {
                bool hidden = owner != viewer.SteamID;

                foreach (CEnvBeam beam in selection.Beams)
                {
                    if (beam.IsValid)
                    {
                        viewer.ShouldBlockTransmitEntity((int)beam.Index, hidden);
                    }
                }

                foreach (CPointWorldText text in selection.Texts)
                {
                    if (text.IsValid)
                    {
                        viewer.ShouldBlockTransmitEntity((int)text.Index, hidden);
                    }
                }
            }
        }
    }

    public void ClearSelectionFor(ulong steamId)
    {
        ClearSelection(steamId);
    }

    private void ClearSelection(ulong steamId)
    {
        if (!_selections.TryGetValue(steamId, out Selection? selection))
        {
            return;
        }

        // Entity indices are reused, so a standing block has to be lifted
        // BEFORE the entity goes away or it lands on whatever takes its slot.
        foreach (IPlayer viewer in _core.PlayerManager.GetAllPlayers())
        {
            if (viewer == null || !viewer.IsValid || viewer.IsFakeClient)
            {
                continue;
            }

            foreach (CEnvBeam beam in selection.Beams)
            {
                if (beam.IsValid)
                {
                    viewer.ShouldBlockTransmitEntity((int)beam.Index, false);
                }
            }

            foreach (CPointWorldText text in selection.Texts)
            {
                if (text.IsValid)
                {
                    viewer.ShouldBlockTransmitEntity((int)text.Index, false);
                }
            }
        }

        foreach (CEnvBeam beam in selection.Beams)
        {
            if (beam.IsValid)
            {
                beam.Despawn();
            }
        }

        foreach (CPointWorldText text in selection.Texts)
        {
            if (text.IsValid)
            {
                text.Despawn();
            }
        }

        _selections.Remove(steamId);
    }

    // The lineups throwable from where this player is standing, which is what
    // decides whose crosshairs get drawn.
    public static List<LineupRecord> SpotAt(
        IEnumerable<LineupRecord> lineups,
        Vec3 at
    )
    {
        return lineups
            .Where(lineup =>
            {
                Vec3 feet = lineup.release.feet_position;

                return new Vec3(feet.x - at.x, feet.y - at.y, 0f).LengthXY()
                        <= SpotRadius
                    && Math.Abs(feet.z - at.z) <= SpotHeight;
            })
            .ToList();
    }

    private void ShowMarkers(LineupRecord lineup, Vec3 stance)
    {
        // Deliberately not gated behind the ghost preview: a preview is an
        // optional extra, but where to stand and where to point IS the lineup.
        // There is no useful state where a loaded lineup shows neither.
        Color color = ColorFor(lineup.utility_type);
        Vec3 landing = lineup.detonation_position;

        // The stance is a GATE turned to face the throw: step into it, look out
        // of the open end. Amber, because it is about the player rather than
        // the grenade, and oriented, because "stand in this circle" leaves you
        // to guess the one thing that is hardest to guess.
        Gate(stance, lineup.release.yaw, 26f, Amber, MarkerWidth);
        Chevron(stance, lineup.release.yaw, 15f, Amber, MarkerWidth);

        // Ties the grenade floating overhead to the ground it belongs to, so a
        // spot with a model reads as one object rather than two.
        Dashed(
            new Vec3(stance.x, stance.y, stance.z + 4f),
            new Vec3(stance.x, stance.y, stance.z + UtilityModelHeight - 6f),
            AmberDim,
            MarkerWidth,
            4
        );

        Label(
            new Vec3(stance.x, stance.y, stance.z + 14f),
            $"{lineup.name}\n{Tracked("stand")}",
            Amber
        );

        // The flight is a plan, so it is dashed, and it only appears for the
        // throw you are actually on -- every lineup drawing its own line is
        // what made a spot with three throws unreadable.
        Dashed(new Vec3(stance.x, stance.y, stance.z + 40f), landing, color, 0.3f, 7);

        Diamond(landing, 30f, color, MarkerWidth);
        Label(
            new Vec3(landing.x, landing.y, landing.z + 16f),
            Tracked(lineup.utility_type),
            color
        );

        AimReticle(lineup, stance, lineup.name);
    }

    // Where to point. The aim ray is traced until it hits something, so the
    // reticle lands ON the surface being aimed at rather than hanging in the
    // air short of it -- for an arcing smoke the crosshair sits well above the
    // landing spot, so distance-to-landing was never the right answer.
    private void AimReticle(LineupRecord lineup, Vec3 stance, string label)
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

        // Started clear of the player's own hull. Markers are drawn while
        // somebody is standing on the spot, and a trace beginning inside a
        // player stops on that player -- which is why the crosshair landed a
        // stride away instead of on the wall it is aimed at.
        var from = new Vector(
            eye.x + dir.x * AimTraceStartOffset,
            eye.y + dir.y * AimTraceStartOffset,
            eye.z + dir.z * AimTraceStartOffset
        );
        var to = new Vector(
            eye.x + dir.x * AimTraceRange,
            eye.y + dir.y * AimTraceRange,
            eye.z + dir.z * AimTraceRange
        );

        Vec3 hit;

        try
        {
            var trace = _core.Trace.TraceShapeLine(from, to, null);

            _logger.LogInformation(
                "aim trace for {name}: hit={hit} at {x},{y},{z} ({dist} units out)",
                lineup.name,
                trace.DidHit,
                trace.EndPos.X.ToString("0.#"),
                trace.EndPos.Y.ToString("0.#"),
                trace.EndPos.Z.ToString("0.#"),
                new Vec3(
                    trace.EndPos.X - eye.x,
                    trace.EndPos.Y - eye.y,
                    trace.EndPos.Z - eye.z
                ).Length().ToString("0.#")
            );

            hit = trace.DidHit
                ? new Vec3(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z)
                : new Vec3(
                    eye.x + dir.x * AimFallbackRange,
                    eye.y + dir.y * AimFallbackRange,
                    eye.z + dir.z * AimFallbackRange
                );
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to trace a lineup's aim");
            hit = new Vec3(
                eye.x + dir.x * AimFallbackRange,
                eye.y + dir.y * AimFallbackRange,
                eye.z + dir.z * AimFallbackRange
            );
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
        // Tighter than a "look over there" marker: this is a point to cover
        // with the crosshair, so it subtends a few degrees and no more.
        float size = Math.Clamp(away * 0.045f, 9f, 110f);

        // Deliberately not the utility's colour: this is the only marker that
        // is not a place the utility goes, and it has to separate from the
        // stance and landing rings at a glance.
        // Thin lines vanish at range, so the reticle's weight grows with
        // distance the same way its size does. The ground rings never need
        // this: you are always standing on them.
        float weight = Math.Clamp(away * 0.0018f, MarkerWidth, 2.2f);

        // Every throw off the spot is drawn at the same size and weight. Which
        // one you are on is said in COLOUR, not in scale: a smaller crosshair
        // reads as "further away", which is exactly the wrong thing to say
        // about a point you are being asked to cover precisely.
        var aim = new Aim { Lineup = lineup };

        _aimInto = aim;

        try
        {
            Reticle(center, dir, size, MissColor(1f), weight);
        }
        finally
        {
            _aimInto = null;
        }

        // Named at the crosshair itself: several throws off one spot are only
        // useful if you can tell which crosshair belongs to which.
        aim.Label = Label(
            new Vec3(center.x, center.y, center.z + size + 8f),
            label,
            MissColor(1f),
            eye
        );

        _drawingInto?.Aims.Add(aim);
    }

    // Red at a glance, green when the throw is on. Amber through the middle so
    // the last fraction of a degree still has somewhere to go -- a hard
    // two-colour switch gives no sense of getting warmer.
    private static Color MissColor(float miss)
    {
        miss = Math.Clamp(miss, 0f, 1f);

        return new Color(
            (int)(60f + (195f * miss)),
            (int)(230f - (190f * miss)),
            (int)(90f * (1f - miss)),
            255
        );
    }

    // Called as the player moves, not as they walk onto a spot: the crosshairs
    // are already drawn, and all that changes is how wrong each one is.
    public void TintAim(IPlayer player, float eyeYaw, float eyePitch)
    {
        if (!_selections.TryGetValue(player.SteamID, out Selection? selection))
        {
            return;
        }

        foreach (Aim aim in selection.Aims)
        {
            float miss = PracticeLineupUtility.AimMiss(
                PracticeLineupUtility.AimError(
                    eyeYaw,
                    eyePitch,
                    aim.Lineup.release.yaw,
                    aim.Lineup.release.pitch
                ),
                aim.Lineup.aim_tolerance
            );

            // Beams are networked on change, so only send one when the colour
            // would actually differ. At sixteen updates a second across every
            // throw on a spot, repainting unconditionally is real traffic.
            if (Math.Abs(miss - aim.Miss) < 0.02f)
            {
                continue;
            }

            aim.Miss = miss;

            Color color = MissColor(miss);

            foreach (CEnvBeam beam in aim.Beams)
            {
                if (beam.IsValid)
                {
                    beam.Render = color;
                }
            }

            if (aim.Label != null && aim.Label.IsValid)
            {
                aim.Label.Color = color;
            }
        }
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

        // A crosshair, not an area. The four arms stop short of the middle so
        // the gap they leave IS the aim point -- a ring or a box tells you
        // roughly where to look, and roughly is what this is meant to replace.
        float gap = size * 0.12f;
        float arm = size * 0.62f;

        AddMarkerBeam(Corner(gap, 0), Corner(arm, 0), color, width);
        AddMarkerBeam(Corner(-gap, 0), Corner(-arm, 0), color, width);
        AddMarkerBeam(Corner(0, gap), Corner(0, arm), color, width);
        AddMarkerBeam(Corner(0, -gap), Corner(0, -arm), color, width);

        // The point itself: a dot small enough that covering it with the
        // crosshair means covering the exact spot the throw was aimed at.
        float dot = Math.Max(size * 0.03f, 0.6f);

        AddMarkerBeam(Corner(-dot, 0), Corner(dot, 0), color, width * 1.6f);
        AddMarkerBeam(Corner(0, -dot), Corner(0, dot), color, width * 1.6f);

        // Corner brackets rather than a ring, so the crosshair speaks the same
        // language as the gate on the floor -- and eight beams instead of
        // sixteen, on the marker a spot draws once per throw.
        float edge = size;
        float bracket = size * 0.34f;

        foreach (int horizontal in new[] { 1, -1 })
        {
            foreach (int vertical in new[] { 1, -1 })
            {
                float x = edge * horizontal;
                float y = edge * vertical;

                AddMarkerBeam(
                    Corner(x, y),
                    Corner(x - (bracket * horizontal), y),
                    color,
                    width * 0.6f
                );
                AddMarkerBeam(
                    Corner(x, y),
                    Corner(x, y - (bracket * vertical)),
                    color,
                    width * 0.6f
                );
            }
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

    // The grenade itself, floating over the spot at about eye height: a ring
    // says where to stand, and this says what to throw from it without reading
    // a colour off a beam.
    // The panel's --tac-amber ramp, straight off assets/css/tailwind.css.
    // Amber is the colour of YOU in this scheme -- where to stand, which way to
    // face, where to point. The utility's own colour is reserved for the half
    // that is about the grenade: where it lands and what it is. Everything
    // being type-coloured is what made a busy map unreadable.
    private static readonly Color Amber = new Color(249, 158, 47, 255);
    private static readonly Color AmberDim = new Color(203, 117, 11, 255);

    // FSOLID_NOT_SOLID.
    private const byte NotSolid = 4;

    // Flat forward/right for a yaw, so a marker can be built facing the throw
    // instead of facing whatever way the map's axes happen to run.
    private static (Vec3 forward, Vec3 right) Bearing(float yaw)
    {
        double radians = yaw * Math.PI / 180d;
        var forward = new Vec3((float)Math.Cos(radians), (float)Math.Sin(radians), 0f);

        return (forward, new Vec3(forward.y, -forward.x, 0f));
    }

    private static Vec3 Offset(Vec3 at, Vec3 forward, float along, Vec3 right, float across)
    {
        return new Vec3(
            at.x + (forward.x * along) + (right.x * across),
            at.y + (forward.y * along) + (right.y * across),
            at.z
        );
    }

    // A caret on the floor pointing down the throw. Two beams against a ring's
    // ten, and it says the thing a ring cannot: which way to face once you are
    // standing on it.
    private void Chevron(Vec3 at, float yaw, float size, Color color, float width)
    {
        (Vec3 forward, Vec3 right) = Bearing(yaw);

        Vec3 tip = Offset(at, forward, size, right, 0f);

        AddMarkerBeam(Offset(at, forward, -size * 0.4f, right, -size * 0.8f), tip, color, width);
        AddMarkerBeam(Offset(at, forward, -size * 0.4f, right, size * 0.8f), tip, color, width);
    }

    // Four beams, and deliberately not a circle: the landing marker and the
    // stance marker must never be mistaken for each other at a glance.
    private void Diamond(Vec3 at, float radius, Color color, float width)
    {
        var north = new Vec3(at.x, at.y + radius, at.z);
        var east = new Vec3(at.x + radius, at.y, at.z);
        var south = new Vec3(at.x, at.y - radius, at.z);
        var west = new Vec3(at.x - radius, at.y, at.z);

        AddMarkerBeam(north, east, color, width);
        AddMarkerBeam(east, south, color, width);
        AddMarkerBeam(south, west, color, width);
        AddMarkerBeam(west, north, color, width);
    }

    // Corner brackets on a square turned to face the throw, with the two
    // corners on the throwing side cut back so it reads as a gate you step
    // into and shoot out of, rather than a box you are stood in.
    private void Gate(Vec3 at, float yaw, float half, Color color, float width)
    {
        (Vec3 forward, Vec3 right) = Bearing(yaw);

        foreach (int alongSign in new[] { 1, -1 })
        {
            foreach (int acrossSign in new[] { 1, -1 })
            {
                Vec3 corner = Offset(at, forward, half * alongSign, right, half * acrossSign);
                float reach = half * (alongSign > 0 ? 0.28f : 0.46f);

                AddMarkerBeam(
                    corner,
                    Offset(corner, forward, -reach * alongSign, right, 0f),
                    color,
                    width
                );
                AddMarkerBeam(
                    corner,
                    Offset(corner, forward, 0f, right, -reach * acrossSign),
                    color,
                    width
                );
            }
        }
    }

    // A flight is a plan, not an object, so it is drawn as one.
    private void Dashed(Vec3 from, Vec3 to, Color color, float width, int dashes)
    {
        for (int index = 0; index < dashes; index += 1)
        {
            float start = index / (float)dashes;
            float end = start + (0.55f / dashes);

            AddMarkerBeam(Lerp(from, to, start), Lerp(from, to, end), color, width);
        }
    }

    private static Vec3 Lerp(Vec3 from, Vec3 to, float t)
    {
        return new Vec3(
            from.x + ((to.x - from.x) * t),
            from.y + ((to.y - from.y) * t),
            from.z + ((to.z - from.z) * t)
        );
    }

    // The panel sets every caption in mono uppercase on wide tracking, and
    // point_worldtext has no letter-spacing, so the spacing goes in the string.
    private static string Tracked(string text)
    {
        return string.Join(" ", text.ToUpperInvariant().ToCharArray());
    }

    // Asks the engine what each grenade actually looks like, by spawning one of
    // each weapon under the world for a single tick and reading the model back
    // off it. Two problems in one: the path is right by construction rather
    // than by guesswork, and it is precached by construction too -- the engine
    // precaches every weapon at level init, which our own precache list, built
    // from paths we invented, cannot promise. Without this nothing rendered
    // until somebody threw the grenade, which is what taught it the path.
    public void LearnUtilityModels()
    {
        foreach (KeyValuePair<string, string> pair in PracticeLineupUtility.UtilityWeapons())
        {
            try
            {
                CBaseModelEntity probe =
                    _core.EntitySystem.CreateEntityByDesignerName<CBaseModelEntity>(pair.Value);

                if (!probe.IsValid)
                {
                    continue;
                }

                var keys = new CEntityKeyValues();

                keys.SetString("origin", "0 0 -16384");
                keys.SetString("solid", "0");

                probe.DispatchSpawn(keys);

                PracticeLineupUtility.LearnUtilityModel(pair.Key, probe.GetModel());

                probe.Despawn();
            }
            catch (Exception error)
            {
                _logger.LogWarning(
                    error,
                    "unable to read the world model for {weapon}",
                    pair.Value
                );
            }
        }
    }

    private void UtilityModel(string utilityType, Vec3 at)
    {
        string? model = PracticeLineupUtility.ModelForUtilityType(utilityType);

        if (model == null)
        {
            return;
        }

        try
        {
            // A physics prop, not a dynamic one. Grenade models carry propdata,
            // and CS2 deletes them off a prop_dynamic on sight -- "which has
            // propdata which means that it be used on a prop_physics" -- so the
            // engine's own answer is the class to use. prop_dynamic_override
            // does NOT bypass that check here the way it did in Source 1.
            CPhysicsProp prop =
                _core.EntitySystem.CreateEntityByDesignerName<CPhysicsProp>(
                    "prop_physics_override"
                );

            if (!prop.IsValid)
            {
                return;
            }

            // The model arrives as a spawn KEYVALUE, never through SetModel.
            // Both orderings of SetModel are wrong: after DispatchSpawn the
            // entity is already networked without a model and stays the ERROR
            // model, and before it the entity is still in the staging list,
            // which trips the SetupModel assertion in skeletoninstance.cpp.
            var keys = new CEntityKeyValues();
            var origin = new Vector(at.x, at.y, at.z + UtilityModelHeight);

            keys.SetString("model", model);
            keys.SetString("solid", "0");
            keys.SetString(
                "origin",
                $"{origin.X.ToString(CultureInfo.InvariantCulture)} "
                    + $"{origin.Y.ToString(CultureInfo.InvariantCulture)} "
                    + $"{origin.Z.ToString(CultureInfo.InvariantCulture)}"
            );

            prop.DispatchSpawn(keys);

            prop.Teleport(origin, new QAngle(0, 0, 0), new Vector(0, 0, 0));

            // Otherwise it is a physics object: it falls off the marker, and a
            // player can shoot it across the map.
            prop.AcceptInput<string>("DisableMotion", "", null, null, 0);

            // The model floats at chest height ON the spot a player is being
            // told to stand, so anything short of completely intangible traps
            // them inside it. The "solid" keyvalue alone does not survive
            // prop_physics building its own VPhysics on spawn.
            prop.Collision.SolidType = SolidType_t.SOLID_NONE;
            prop.Collision.SolidFlags = NotSolid;
            prop.Collision.CollisionGroup = (byte)CollisionGroup.Nonphysical;
            prop.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.Nonphysical;
            prop.Collision.CollisionAttribute.InteractsAs = 0;
            prop.Collision.CollisionAttribute.InteractsWith = 0;
            prop.CollisionRulesChanged();

            _markerProps.Add(prop);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to show a lineup's utility model");
        }
    }

    private void AddMarkerBeam(Vec3 start, Vec3 end, Color color, float width)
    {
        CEnvBeam? beam = CreateBeam(start, end, color, width);

        if (beam == null)
        {
            return;
        }

        _aimInto?.Beams.Add(beam);

        if (_drawingInto != null)
        {
            _drawingInto.Beams.Add(beam);
        }
        else
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

    // facing: where the text should read from, normally the spot the player is
    // standing on. Passing null keeps the auto-reorient, which is right for a
    // label lying on the floor and wrong for one on a wall.
    private CPointWorldText? Label(Vec3 at, string text, Color color, Vec3? facing = null)
    {
        try
        {
            CPointWorldText label =
                _core.EntitySystem.CreateEntityByDesignerName<CPointWorldText>(
                    "point_worldtext"
                );

            if (!label.IsValid)
            {
                return null;
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
            var angle = new QAngle(0, 0, 0);

            if (facing == null)
            {
                // Spins to face whoever is reading it. Fine on the floor.
                label.ReorientMode = PointWorldTextReorientMode_t
                    .POINT_WORLD_TEXT_REORIENT_AROUND_UP;
            }
            else
            {
                // Aimed by hand instead. Auto-reorient was leaving wall labels
                // rolled onto their side -- text running bottom to top -- so
                // the angle is set explicitly and the roll pinned flat.
                float dx = facing.Value.x - at.x;
                float dy = facing.Value.y - at.y;

                float yaw = (float)(Math.Atan2(dy, dx) * 180.0 / Math.PI);

                // point_worldtext reads along its own +X, so it has to be
                // turned to face the reader rather than away from them.
                angle = new QAngle(0, yaw + 180f, 0);

                label.ReorientMode = PointWorldTextReorientMode_t
                    .POINT_WORLD_TEXT_REORIENT_NONE;
            }

            label.Teleport(
                new Vector(at.x, at.y, at.z),
                angle,
                new Vector(0, 0, 0)
            );

            label.DispatchSpawn();

            if (_drawingInto != null)
            {
                _drawingInto.Texts.Add(label);
            }
            else
            {
                _markerTexts.Add(label);
            }

            return label;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to place a lineup marker");

            return null;
        }
    }

    // For a map change only. The entities died with the map, so their handles
    // are stale -- and a stale handle can be recycled into a NEW entity, which
    // makes despawning it actively harmful. Drop the references instead.
    public void ForgetMarkers()
    {
        _markerBeams.Clear();
        _markerTexts.Clear();
        _markerProps.Clear();
        _selections.Clear();
        _drawingInto = null;
    }

    // The library layer only. The selection layer belongs to individual players
    // and outlives a library redraw.
    private void ClearSharedMarkers()
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

        foreach (CPhysicsProp prop in _markerProps)
        {
            if (prop.IsValid)
            {
                prop.Despawn();
            }
        }

        _markerBeams.Clear();
        _markerTexts.Clear();
        _markerProps.Clear();
    }

    public void ClearMarkers()
    {
        foreach (ulong steamId in _selections.Keys.ToList())
        {
            ClearSelection(steamId);
        }

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

        foreach (CPhysicsProp prop in _markerProps)
        {
            if (prop.IsValid)
            {
                prop.Despawn();
            }
        }

        _markerBeams.Clear();
        _markerTexts.Clear();
        _markerProps.Clear();
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
