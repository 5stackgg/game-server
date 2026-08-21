using System.Globalization;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;

namespace UtilityPractice;

// Puts a player back where a lineup was thrown from, and draws the line the
// grenade took so they can see the throw before they make it.
public class PracticeReplay
{

    // A simplified line is a few dozen points; a long one is strided rather
    // than spawning an entity per segment.

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

    // Players catch line traces, and both marker traces suffered for it: the
    // aim trace was offset forward to clear the thrower's hull, which broke
    // walls nearer than the offset and still let any OTHER body in the ray
    // catch it -- the crosshair drew wherever somebody was standing and only
    // corrected on the next redraw. Skipping pawns in the filter kills the
    // whole class instead of one case of it.
    // Bodies wander through rays, and every marker this plugin draws sits
    // exactly where its rays go -- the floating grenade model lives ~16u above
    // the stance eye, dead in the path of any steep upward throw, which is
    // where "the crosshair is suddenly low and close" came from. None of these
    // is ever the wall being aimed at.
    private static readonly HashSet<string> TraceInvisible = new()
    {
        "player",
        "prop_physics_override",
        "env_beam",
        "point_worldtext",
    };

    private static TraceParams SkipMarkers()
    {
        var parameters = new TraceParams();

        parameters.IterateEntities = true;
        parameters.ShouldHitEntity = entity =>
        {
            string designer = entity.DesignerName ?? "";

            // Held weapons follow players through rays, and projectiles are
            // wherever somebody last threw one.
            return !TraceInvisible.Contains(designer)
                && !designer.StartsWith("weapon_")
                && !designer.EndsWith("_projectile");
        };

        return parameters;
    }


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

    // How far away the model's outline stays visible. Bounded: across the whole
    // map every spot glowing through every wall is noise, not guidance.
    private const int UtilityGlowRange = 1500;

    // Roughly where the grenade sits in a player's hand, so it reads as "this
    // is what you throw from here" rather than as litter on the floor.
    // Side by side when one spot wants more than one kind of grenade.
    private const float UtilityModelSpacing = 16f;

    // Above standing eye height (64) on purpose. .load stands the player ON
    // the ring, and at chest height the grenade is inside their camera.
    public const float UtilityModelHeight = 80f;

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

        // The gate and its tether, which belong to the SPOT rather than to any
        // one throw off it, and are recoloured by where the player is standing.
        public readonly List<CEnvBeam> Stance = new();

        public Vec3 At;
        public int Bucket = -1;
    }

    private class Aim
    {
        public LineupRecord Lineup = null!;
        public readonly List<CEnvBeam> Beams = new();
        public int Bucket = -1;
    }

    // The lined-up crosshair: barely-there green. Beams render bright against
    // the world, so a dark colour is how a beam whispers.
    private static readonly Color AimSettled = new Color(18, 52, 26, 255);

    // What .tintest settled: a "Color" INPUT reaches clients, where assigning
    // Render on a live beam moves the value on the server and nowhere else.
    // This is why colour changes are one input per beam instead of the
    // despawn-and-respawn machinery that used to live here.
    private static void Recolour(List<CEnvBeam> beams, Color color)
    {
        foreach (CEnvBeam beam in beams)
        {
            if (beam.IsValid)
            {
                beam.AcceptInput<string>(
                    "Color",
                    $"{color.R} {color.G} {color.B}",
                    null,
                    null,
                    0
                );
            }
        }
    }

    // Coarse on purpose: this decides how often a crosshair is torn down and
    // rebuilt, and the eye cannot separate neighbouring shades anyway.
    private const int MissBuckets = 5;

    private static int BucketFor(float miss)
    {
        return PracticeLineupUtility.MissBucket(miss, MissBuckets);
    }

    private static Color ColorForBucket(int bucket)
    {
        return MissColor(bucket / (float)(MissBuckets - 1));
    }

    // The reticle currently being drawn, so its beams can be collected apart
    // from the rest of the selection.
    private Aim? _aimInto;

    // Where each lineup's aim ray lands, traced once. client_id keyed;
    // dropped on map change with everything else.
    private readonly Dictionary<string, Vec3> _aimHits = new();

    // Same, for the spot furniture.
    private List<CEnvBeam>? _stanceInto;

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

    // ---------------------------------------------------------------------
    // CRASH BISECT. Every one of these makes the plugin touch the engine for
    // something other than moving a player, and all of them are off. A fresh
    // instance should stay up with the plugin still recording, saving, loading
    // and teleporting -- it just draws nothing and spawns nothing.
    //
    // Turn ONE back on, redeploy, play until it either crashes or clearly does
    // not, and that names the culprit. Turning two on at once wastes the run.
    // ---------------------------------------------------------------------

    // Beams and world text: rings/gates, labels, connectors, the flight line.
    public const bool DrawMarkers = true;

    // prop_physics_override grenade models floating over a spot, and the
    // collision clearing that follows them.
    public const bool DrawModels = true;

    // The measured bloom outline.
    public const bool DrawBloom = true;

    // REAL projectiles: EmitSmokeGrenade / EmitFlashbang / EmitHEGrenade /
    // EmitMolotov, plus the bloom's live smoke.
    public const bool EmitGrenades = true;

    // So the server log says exactly which of these is live. Without it there
    // is no way to tell a switch that is off from a build that never deployed.
    public static string SwitchState()
    {
        return $"markers={DrawMarkers} models={DrawModels} "
            + $"bloom={DrawBloom} grenades={EmitGrenades}";
    }

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

    // The measured bloom, outlined where it would actually sit. Answers how
    // many beams it took: zero when the panel has no measurement for this
    // lineup, which is a normal answer and not a failure.
    public int ShowBloom(IPlayer player, LineupRecord lineup)
    {
        if (!DrawBloom)
        {
            return 0;
        }

        ClearKind(player.SteamID, GhostKind.Bloom);

        if (!_config.GhostPreview)
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

        return beams.Count;
    }

    // The outline is a drawing of the measurement; this is the measurement's
    // subject. Only Swiftly can emit one, so only Swiftly offers it.
    public bool ShowBloomSmoke(IPlayer player, LineupRecord lineup)
    {
        if (!EmitGrenades)
        {
            return false;
        }

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
            smoke.Despawn();
        }
    }

    // Tier 2: a real grenade, launched from the physics seed the engine gave
    // us at record time rather than from the player's eye angles, so it lands
    // where the recorded one did instead of near it.
    public void ThrowGhostProjectile(IPlayer player, LineupRecord lineup)
    {
        if (!EmitGrenades)
        {
            return;
        }

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

    // What to actually do once the crosshair is on the reticle. Everything the
    // lineup knows about the throw, in the order a player performs it.
    public static string ThrowHint(LineupRecord lineup)
    {
        string bind = lineup.release.jump_throw ? " + JUMP-THROW BIND" : "";

        // No LINED UP banner: the crosshair fading to nothing already says it,
        // and this line has a standing job on the card rather than appearing
        // only at the moment of success.
        return $"{lineup.utility_type.ToUpperInvariant()} - "
            + $"{PracticeLineupUtility.TechniqueLabel(lineup.technique)} - "
            + $"{PracticeLineupUtility.StrengthLabel(lineup.strength)}{bind}";
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

    // Valve's own guides mark three things per lineup -- where you stand, what
    // you look at, and where it lands -- and that split is the right one, so
    // these mirror it. It is drawn from entities the server owns rather than
    // the annotation system, which is client-side and cannot be driven from
    // here at all.
    // The stance is passed in rather than traced again: by the time markers are
    // drawn the player is standing on the spot, and a downward trace from
    // inside their own hull hits them instead of the floor. Load works it out
    // before the teleport, while the spot is still empty.
    // Stands the player where the utility lands, looking back down the throw.
    //
    // Grounded, because a detonation is a point in the air and teleporting into
    // it drops the player out of it -- the useful place to inspect a smoke from
    // is the floor underneath it. Facing back toward the stance because the
    // question at the landing end is always "where did this come from".
    public bool JumpToLanding(IPlayer player, LineupRecord lineup)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return false;
        }

        Vec3 landing = Grounded(lineup.detonation_position);
        Vec3 stance = lineup.release.feet_position;

        float yaw = (float)(
            Math.Atan2(stance.y - landing.y, stance.x - landing.x) * 180.0 / Math.PI
        );

        var position = new Vector(landing.x, landing.y, landing.z);
        var facing = new QAngle(0, yaw, 0);

        player.Teleport(position, facing, new Vector(0, 0, 0));
        pawn.EyeAngles = facing;

        // The client re-predicts from the command it had in flight and snaps
        // the view back, so once is not enough.
        ReapplyAngles(player, facing, facing, 2);

        return true;
    }

    // Markers for a lineup the player is already standing on, with no teleport:
    // used straight after .save, where moving them would be pointless.
    public void ShowMarkersFor(IPlayer player, LineupRecord lineup)
    {
        if (!DrawMarkers)
        {
            return;
        }

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
    // The most lineups the resting layer will draw. Each costs ~8 networked
    // entities (7 beams + a label) plus a prop per spot, and the panel serves
    // up to 500 -- drawn in full that is thousands of edicts, which is a server
    // crash delivered by popularity rather than by any bug. The API returns
    // newest first, so what survives the cap is the newest.
    private const int MaxLibraryDrawn = 150;

    public void ShowLibrary(IEnumerable<LineupRecord> lineups)
    {
        if (!DrawMarkers)
        {
            return;
        }

        ClearSharedMarkers();

        _drawingInto = null;

        List<LineupRecord> all = lineups.ToList();
        List<LineupRecord> drawn = all.Take(MaxLibraryDrawn).ToList();

        if (drawn.Count < all.Count)
        {
            // Never silently: a capped map reads as "this is everything".
            _logger.LogWarning(
                "library draw capped at {drawn} of {total} lineups",
                drawn.Count,
                all.Count
            );
        }

        lineups = drawn;

        foreach (LineupRecord lineup in lineups)
        {
            Color type = ColorFor(lineup.utility_type);
            Vec3 feet = Grounded(lineup.release.feet_position);

            // Seven beams where there used to be twenty-one. A map holds
            // hundreds of these at once, so the resting state has to be the
            // cheapest thing on screen as well as the quietest -- and no
            // connecting line at rest, which is what turned a busy map into a
            // cat's cradle.
            // No name on the ground and no post: the chevron says which way,
            // the glowing model says what and where, and the name arrives in
            // centre text when the player points at the grenade.
            Needle(feet, lineup.release.yaw, 13f, AmberDim, MarkerWidth);
            Diamond(lineup.detonation_position, 22f, type, MarkerWidth);
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
        if (!DrawMarkers)
        {
            return;
        }

        _focused = focused;

        if (owner == null)
        {
            _drawingInto = null;

            if (active.Count > 0)
            {
                ShowStance(Grounded(active.First().release.feet_position));
            }

            foreach (LineupRecord lineup in active)
            {
                ShowMarkers(lineup);
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

        // The gate marks the RECORDED spot, never where the player happens to
        // be standing -- SpotWatch passes the player's own position, and a gate
        // drawn under their feet can never tell them they are off it.
        selection.At = Grounded(active.First().release.feet_position);

        try
        {
            _stanceInto = selection.Stance;

            try
            {
                ShowStance(selection.At);
            }
            finally
            {
                _stanceInto = null;
            }

            foreach (LineupRecord lineup in active)
            {
                ShowMarkers(lineup);
            }
        }
        finally
        {
            _drawingInto = null;
        }

    }

    // The gate answers the other half of the question. A player who is off the
    // angle and a player who is off the spot both see "not yet" -- in the same
    // colours, on the marker that is actually wrong.
    private void TintStance(Selection selection, Vec3 feet)
    {
        int bucket = BucketFor(
            PracticeLineupUtility.StanceMiss(
                new Vec3(selection.At.x - feet.x, selection.At.y - feet.y, 0f).LengthXY()
            )
        );

        if (bucket == selection.Bucket)
        {
            return;
        }

        selection.Bucket = bucket;
        Recolour(selection.Stance, ColorForBucket(bucket));
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

    // Everything about the PLACE rather than any throw off it. Drawn once per
    // selection: calling this per lineup stacked identical gates on each other
    // and, worse, piled the labels into an unreadable smear.
    // Feet are aimed the same way a crosshair is, so the spot gets the same
    // instrument: a circle flat on the floor with four arms stopping short of
    // the middle -- the gap IS where the feet go -- the whole thing tinted
    // green/red by how close the player is standing. This replaces a bracket
    // gate and a vertical pillar that said "here" from afar but nothing about
    // how close you were once you arrived.
    private void GroundReticle(Vec3 at, Color color)
    {
        float z = at.z + 1.5f;

        for (int index = 0; index < StanceRingSegments; index++)
        {
            double a = index * 2 * Math.PI / StanceRingSegments;
            double b = (index + 1) * 2 * Math.PI / StanceRingSegments;

            AddMarkerBeam(
                new Vec3(
                    at.x + (float)(Math.Cos(a) * StanceRingRadius),
                    at.y + (float)(Math.Sin(a) * StanceRingRadius),
                    z
                ),
                new Vec3(
                    at.x + (float)(Math.Cos(b) * StanceRingRadius),
                    at.y + (float)(Math.Sin(b) * StanceRingRadius),
                    z
                ),
                color,
                StanceWidth
            );
        }

        // The same grammar as the reticle in the air, at the same proportions:
        // four arms stopping short of the middle so the gap IS the point, and
        // a dot small enough that standing over it means standing on the exact
        // spot. Finer than the ring around it -- the ring is for finding, the
        // crosshair is for placing.
        float gap = StanceRingRadius * 0.12f;
        float arm = StanceRingRadius * 0.62f;

        foreach ((float x, float y) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
        {
            AddMarkerBeam(
                new Vec3(at.x + (x * gap), at.y + (y * gap), z),
                new Vec3(at.x + (x * arm), at.y + (y * arm), z),
                color,
                StanceCrossWidth
            );
        }

        float dot = Math.Max(StanceRingRadius * 0.03f, 0.6f);

        AddMarkerBeam(
            new Vec3(at.x - dot, at.y, z),
            new Vec3(at.x + dot, at.y, z),
            color,
            StanceCrossWidth * 1.6f
        );
        AddMarkerBeam(
            new Vec3(at.x, at.y - dot, z),
            new Vec3(at.x, at.y + dot, z),
            color,
            StanceCrossWidth * 1.6f
        );
    }

    private void ShowStance(Vec3 stance)
    {
        GroundReticle(stance, ColorForBucket(MissBuckets - 1));
    }

    private void ShowMarkers(LineupRecord lineup)
    {
        // Deliberately not gated behind the ghost preview: a preview is an
        // optional extra, but where to stand and where to point IS the lineup.
        // There is no useful state where a loaded lineup shows neither.
        Color color = ColorFor(lineup.utility_type);
        Vec3 landing = lineup.detonation_position;

        Diamond(landing, 30f, color, MarkerWidth);
        Label(
            new Vec3(landing.x, landing.y, landing.z + 16f),
            PracticeLineupUtility.Tracked(lineup.utility_type),
            color
        );

        AimReticle(lineup, lineup.name);
    }

    // Where to point. The aim ray is traced until it hits something, so the
    // reticle lands ON the surface being aimed at rather than hanging in the
    // air short of it -- for an arcing smoke the crosshair sits well above the
    // landing spot, so distance-to-landing was never the right answer.
    private void AimReticle(LineupRecord lineup, string label)
    {
        // From the eye of somebody standing on THIS LINEUP'S spot -- never from
        // wherever the player happens to be. The caller's stance is the live
        // player position, and tracing the same angles from a different origin
        // lands on a different piece of wall: that is the whole reason the
        // crosshair "sometimes" appeared in the right place. The point a throw
        // is aimed at is a fact about the lineup and the map, so nothing about
        // the viewer may enter into it.
        //
        // Not the release point either: a run- or jump-throw leaves the hand a
        // whole run-up away from where it is set up.
        Vec3 spot = Grounded(lineup.release.feet_position);

        var eye = new Vec3(
            spot.x,
            spot.y,
            spot.z + PracticeSolverUtility.StandingEyeHeight
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

        // Traced once per lineup and remembered: the wall a throw points at is
        // a static fact of the map, so a reticle that lands in two different
        // places across two redraws is always wrong at least once. The cache
        // also outlives whatever transient -- a body, a thrown grenade -- might
        // wander through the ray on a later redraw.
        if (!_aimHits.TryGetValue(lineup.client_id, out Vec3 hit))
        {
            try
            {
                var trace = _core.Trace.TraceShapeLine(from, to, SkipMarkers());

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

            _aimHits[lineup.client_id] = hit;
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
            Reticle(center, dir, size, ColorForBucket(MissBuckets - 1), weight);
        }
        finally
        {
            _aimInto = null;
        }

        // Named at the crosshair itself: several throws off one spot are only
        // useful if you can tell which crosshair belongs to which.
        // Amber, and never repainted: the label names the throw, the beams
        // carry the miss signal, and splitting the jobs means the label's own
        // colour networking never becomes a question.
        Label(new Vec3(center.x, center.y, center.z + size + 8f), label, Amber);

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

    // Called as the player moves, not as they walk onto a spot: the markers are
    // already drawn, and all that changes is how wrong they are.
    public void TintAim(IPlayer player, float eyeYaw, float eyePitch, Vec3 feet)
    {
        if (!DrawMarkers)
        {
            return;
        }

        if (!_selections.TryGetValue(player.SteamID, out Selection? selection))
        {
            return;
        }

        TintStance(selection, feet);

        foreach (Aim aim in selection.Aims)
        {
            int bucket = BucketFor(
                PracticeLineupUtility.AimMiss(
                    PracticeLineupUtility.AimError(
                        eyeYaw,
                        eyePitch,
                        aim.Lineup.release.yaw,
                        aim.Lineup.release.pitch
                    ),
                    aim.Lineup.aim_tolerance
                )
            );

            if (bucket == aim.Bucket)
            {
                continue;
            }

            aim.Bucket = bucket;

            // On the angle the crosshair has done its job, and full-strength
            // beams would now be sitting exactly where the player needs to see
            // the world. Faded to a whisper rather than removed: it stays a
            // reference point if they drift, without costing them the view.
            Recolour(
                aim.Beams,
                bucket == 0 ? AimSettled : ColorForBucket(bucket)
            );
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

            var trace = _core.Trace.TraceShapeLine(from, to, SkipMarkers());

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

    private const float StanceWidth = 1.6f;

    private const float StanceCrossWidth = 0.9f;

    // A circle a player fits inside, with the crosshair gap at its centre.
    private const float StanceRingRadius = 22f;
    private const int StanceRingSegments = 14;

    // Legible without being architecture. These labels sit on the spot they
    // name, at arm's length, not across the map.
    private const int LabelFontSize = 34;
    private const float LabelUnitsPerPx = 0.06f;

    // Stamped on every entity this plugin spawns. Entities outlive the plugin
    // instance that made them: a hot reload drops all our references while the
    // beams stay in the world, so the ONLY way a fresh instance can find its
    // predecessor's litter is a mark it can read back off the world itself.
    private const string MarkerTag = "5stack_utility_marker";

    // The classes we spawn. Maps author their own env_beams and props, which is
    // exactly why the sweep matches on the tag as well as the class.
    private static readonly string[] MarkerClasses =
    {
        "env_beam",
        "point_worldtext",
        "prop_physics_override",
    };

    private static CEntityKeyValues Tagged()
    {
        var keys = new CEntityKeyValues();

        keys.SetString("targetname", MarkerTag);

        return keys;
    }

    // Despawns every marker in the world, ours or a previous instance's, then
    // forgets the handles. Safe to call when there is nothing to find.
    public int SweepMarkers()
    {
        int swept = 0;

        foreach (string designer in MarkerClasses)
        {
            try
            {
                foreach (
                    CBaseEntity entity in _core.EntitySystem.GetAllEntitiesByDesignerName<CBaseEntity>(
                        designer
                    )
                )
                {
                    if (!entity.IsValid || entity.Entity?.Name != MarkerTag)
                    {
                        continue;
                    }

                    entity.Despawn();
                    swept += 1;
                }
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "unable to sweep {designer} markers", designer);
            }
        }

        ForgetMarkers();

        return swept;
    }

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

    // A needle, not a wedge: the shaft lies exactly on the throw's yaw, so a
    // player standing on the spot can set their crosshair BY it the same way
    // the reticle in the air is set. The fat chevron this replaces pointed
    // "roughly there", which stops being useful the moment feet are down and
    // precision becomes the whole question.
    private void Needle(Vec3 at, float yaw, float size, Color color, float width)
    {
        (Vec3 forward, Vec3 right) = Bearing(yaw);
        float z = at.z + 1.5f;

        Vec3 At(float along, float across)
        {
            return new Vec3(
                at.x + (forward.x * along) + (right.x * across),
                at.y + (forward.y * along) + (right.y * across),
                z
            );
        }

        Vec3 tip = At(size * 2.2f, 0f);

        AddMarkerBeam(At(size * 0.5f, 0f), tip, color, width);
        AddMarkerBeam(At(size * 1.75f, size * 0.35f), tip, color, width);
        AddMarkerBeam(At(size * 1.75f, -size * 0.35f), tip, color, width);
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

    private void UtilityModel(string utilityType, Vec3 at)
    {
        if (!DrawModels)
        {
            return;
        }

        if (!Sane(at))
        {
            return;
        }

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

            keys.SetString("targetname", MarkerTag);
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

            // Through-wall findability, on the model itself rather than a
            // separate marker: the grenade IS the sign for "there is a lineup
            // here", so it carries its own outline. Set on the spawn tick so it
            // rides the entity's first snapshot -- the same networking caution
            // that forced the beam rebuilds. Type 3 is the through-walls
            // outline; team -1 shows it to everyone.
            prop.Glow.GlowType = 3;
            prop.Glow.GlowColorOverride = ColorFor(utilityType);
            prop.Glow.GlowRange = UtilityGlowRange;
            prop.Glow.GlowRangeMin = 0;
            prop.Glow.GlowTeam = -1;

            // Without this the outline only exists through walls: the moment
            // the model is actually on screen the glow is culled, which reads
            // as "it vanishes when I look at it".
            prop.Glow.EligibleForScreenHighlight = true;
            prop.Glow.Glowing = true;

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
        _stanceInto?.Add(beam);

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
    private CPointWorldText? Label(Vec3 at, string text, Color color)
    {
        if (!Sane(at))
        {
            return null;
        }

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
            label.FontName = "Arial Black";
            label.Fullbright = true;
            label.Enabled = true;
            label.JustifyHorizontal = PointWorldTextJustifyHorizontal_t
                .POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
            label.JustifyVertical = PointWorldTextJustifyVertical_t
                .POINT_WORLD_TEXT_JUSTIFY_VERTICAL_CENTER;

            // Every label spins to face whoever is reading it, and is spawned
            // with no angle of its own. Aiming one by hand is what produced
            // text lying on its side and mirrored: point_worldtext draws in its
            // own flat plane, so any hand-set angle is a plane you end up
            // reading edge-on or from behind. There is no orientation worth
            // computing here -- the engine already knows where the reader is.
            label.ReorientMode = PointWorldTextReorientMode_t
                .POINT_WORLD_TEXT_REORIENT_AROUND_UP;

            // Small, because these sit ON the thing they name rather than
            // across the map from it. 60px at 0.15 units/px was roughly two
            // metres of lettering standing in a doorway.
            label.FontSize = LabelFontSize;
            label.WorldUnitsPerPx = LabelUnitsPerPx;

            var angle = new QAngle(0, 0, 0);

            label.Teleport(
                new Vector(at.x, at.y, at.z),
                angle,
                new Vector(0, 0, 0)
            );

            label.DispatchSpawn(Tagged());

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
        _aimHits.Clear();
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

    // A NaN or an infinity reaching Teleport takes the whole server down inside
    // native code, where the try/catch below cannot see it -- so coordinates are
    // checked before the engine ever sees them, not after it faults.
    private static bool Sane(Vec3 point)
    {
        return float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
    }

    private CEnvBeam? CreateBeam(Vec3 start, Vec3 end, Color color, float width)
    {
        if (!Sane(start) || !Sane(end))
        {
            _logger.LogWarning(
                "refusing to draw a beam at {sx},{sy},{sz} -> {ex},{ey},{ez}",
                start.x,
                start.y,
                start.z,
                end.x,
                end.y,
                end.z
            );

            return null;
        }

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

            beam.DispatchSpawn(Tagged());

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

            beam.Despawn();
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
