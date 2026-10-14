using System.Globalization;
using System.Text;
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
public partial class PracticeReplay
{

    // A simplified line is a few dozen points; a long one is strided rather
    // than spawning an entity per segment.

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
        Trail,
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

    // Brushes you can walk through. A trigger is an invisible volume with no
    // surface to aim at, but the trace treated one as a wall -- which is why
    // the crosshair for the Ancient InstaMid smokes from CT landed in mid-air
    // well short of the wall, sitting on the edge of the CT buy zone. Named
    // explicitly rather than by a "trigger_" prefix alone because func_buyzone,
    // func_bomb_target and func_hostage_rescue are the same kind of thing
    // wearing a different name.
    private static readonly HashSet<string> TracePassThrough = new()
    {
        "func_buyzone",
        "func_bomb_target",
        "func_hostage_rescue",
        "func_nav_blocker",
        "trigger_multiple",
        "trigger_once",
        "trigger_push",
        "trigger_hurt",
        "trigger_teleport",
        "trigger_soundscape",
        "trigger_look",
        "trigger_proximity",
    };

    // A camera is never stopped by a body: the director traces from the
    // thrower's own head, and a trace that hit that head pinned the camera there.
    private static TraceParams SkipMarkersAndPlayers()
    {
        TraceParams parameters = SkipMarkers();
        Func<CEntityInstance, bool>? markers = parameters.ShouldHitEntity;

        parameters.ShouldHitEntity = entity =>
            entity.DesignerName != "player" && (markers == null || markers(entity));

        return parameters;
    }

    private static TraceParams SkipMarkers()
    {
        var parameters = new TraceParams();

        parameters.IterateEntities = true;
        parameters.ShouldHitEntity = entity =>
        {
            string designer = entity.DesignerName ?? "";

            // Our own entities are never a surface: the render camera's prop
            // carries a huge collision box, and a trace that stopped on it pulled
            // the chase camera into the grenade.
            if (entity.Entity?.Name == MarkerTag)
            {
                return false;
            }

            // Held weapons follow players through rays, and projectiles are
            // wherever somebody last threw one.
            return !TraceInvisible.Contains(designer)
                && !TracePassThrough.Contains(designer)
                && !designer.StartsWith("trigger_")
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

    // How far away the model's outline stays visible. Across the map on
    // purpose: the glowing grenade is how a player finds a lineup from the far
    // side of it, and each player's layer holds only what passes their own
    // filters.
    private const int UtilityGlowRange = 6000;

    // Roughly where the grenade sits in a player's hand, so it reads as "this
    // is what you throw from here" rather than as litter on the floor.
    // Side by side when one spot wants more than one kind of grenade.
    private const float UtilityModelSpacing = 16f;

    // Above standing eye height (64) on purpose. .load stands the player ON
    // the ring, and at chest height the grenade is inside their camera.
    public const float UtilityModelHeight = 80f;

    // Spawn boxes belong to nobody and are redrawn by nothing: a toggle the
    // player asked for must not blink out because somebody ran .next.
    private readonly MarkerSet _spawnBoxes = new();

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

    // Where each lineup's aim ray lands, traced once. client_id keyed;
    // dropped on map change with everything else.
    private readonly Dictionary<string, Vec3> _aimHits = new();

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
        public required string Name;
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

    // Whether loading a lineup should still announce it on centre text. False
    // for a player whose HUD panel is up: the panel already names the lineup and
    // holds it there, so the centre line is the same fact said twice, once in a
    // channel that fades.
    public Func<ulong, bool> AnnouncesLoad { get; set; } = _ => true;

    /// <summary>
    /// Narrows the library layer to a named set of lineups, IN ORDER.
    ///
    /// Starting an execute drew every smoke on the map, because a playbook step
    /// goes through the same Load as a typed .load and Load draws the whole
    /// library. That buries the four throws the execute is actually about in a
    /// hundred that it is not. Null means the whole library, which is the
    /// resting state.
    ///
    /// Ordered rather than a set because the position IS the colour: an execute
    /// puts several grenades up at once and drawn in the utility's own colour
    /// they are all the same white, so nothing on the ground says which one
    /// landed where.
    /// </summary>
    public IReadOnlyList<string>? LibraryRestriction { get; set; }

    /// <summary>
    /// The colour a lineup is wearing in the running execute, or null when it
    /// is not in one. Position in LibraryRestriction decides it.
    /// </summary>
    public PracticeStepColors.StepColor? StepColorFor(string clientId)
    {
        IReadOnlyList<string>? order = LibraryRestriction;

        if (order == null)
        {
            return null;
        }

        for (int index = 0; index < order.Count; index++)
        {
            if (string.Equals(order[index], clientId, StringComparison.OrdinalIgnoreCase))
            {
                return PracticeStepColors.For(index);
            }
        }

        return null;
    }

    private static Color Rgb(PracticeStepColors.StepColor step)
    {
        return new Color((int)step.R, (int)step.G, (int)step.B, 255);
    }

    /// <summary>
    /// How visible this player's aim marker should be, 1 down to 0. Wired by
    /// the plugin like Shown, because what it depends on -- a per-player
    /// switch and how far into a drill they are -- is not the replay layer's
    /// business. 0 means draw nothing at all.
    /// </summary>
    public Func<ulong, float> AimVisibility { get; set; } = _ => 1f;

    private static Color Faded(Color color, float visibility)
    {
        // Beams render bright against the world, so fading one is scaling it
        // toward black.
        float amount = Math.Clamp(visibility, 0f, 1f);

        return new Color(
            (int)(color.R * amount),
            (int)(color.G * amount),
            (int)(color.B * amount),
            255
        );
    }

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

    /// <summary>
    /// Our own coloured flight trail. OFF, because the engine already draws one
    /// and ITS trail time is what keeps the practice camera up after the
    /// grenade lands -- suppressing it to make room for this took away the
    /// ability to watch a smoke bloom, which is worth more than a coloured arc.
    /// The throw's colour still reaches the player on the cloud itself, on the
    /// markers and on the panel. Flip this to true to get the coloured arc
    /// back, and set the engine's trailtime to 0 in the same change or there
    /// will be two arcs down every flight.
    /// </summary>
    public const bool DrawTrail = false;

    // REAL projectiles: EmitSmokeGrenade / EmitFlashbang / EmitHEGrenade /
    // EmitMolotov, plus the bloom's live smoke.
    public const bool EmitGrenades = true;

    // The grenade over every spot, and the footprints on the floor near one.
    public const bool DrawBeacons = true;

    // Hiding one player's markers from everybody else. This is the call that
    // crashed clients on SwiftlyS2 1.4.4; with it off everyone sees everyone's
    // markers and nothing else changes.
    public const bool TransmitBlocking = true;

    // So the server log says exactly which of these is live. Without it there
    // is no way to tell a switch that is off from a build that never deployed.
    public static string SwitchState()
    {
        return $"markers={DrawMarkers} models={DrawModels} "
            + $"bloom={DrawBloom} grenades={EmitGrenades} "
            + $"beacons={DrawBeacons} transmit={TransmitBlocking}";
    }

    public void Load(IPlayer player, LineupRecord lineup)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        Vector? wasAt = pawn?.AbsOrigin;
        _logger.LogInformation(
            "[nade-render] Load: steam={steam} fake={fake} alive={alive} team={team} pawnValid={valid} at={x},{y},{z}",
            player.SteamID,
            player.IsFakeClient,
            player.IsAlive,
            player.Controller?.Team,
            pawn != null && pawn.IsValid,
            wasAt?.X,
            wasAt?.Y,
            wasAt?.Z
        );

        if (pawn == null || !pawn.IsValid)
        {
            _logger.LogWarning(
                "[nade-render] Load ABORTED: no valid pawn to teleport (player is in team-select / dead / spectating) — the throw still emits from the seed but the CLIENT NEVER MOVES"
            );
            return;
        }

        // The same floor the stance marker is drawn on, so .load puts the
        // player standing on the ring rather than dropping into it.
        Vec3 feet = Standable(Grounded(lineup.release.feet_position));
        var position = new Vector(feet.x, feet.y, feet.z);

        // Yaw for the body, pitch for the eyes, and never the two together: a
        // pawn's rotation is which way it faces, so a lineup's -63 pitch fed
        // into it lies the player on their back.
        var facing = new QAngle(0, lineup.release.yaw, 0);
        var aim = new QAngle(lineup.release.pitch, lineup.release.yaw, 0);

        _logger.LogInformation(
            "[nade-render] Load: teleporting {steam} to {x},{y},{z} yaw={yaw} pitch={pitch}",
            player.SteamID,
            position.X,
            position.Y,
            position.Z,
            lineup.release.yaw,
            lineup.release.pitch
        );

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

                // A tick after the teleport, is the pawn actually where we put
                // it? If this reads far from the target the teleport did not
                // stick -- a round reset / respawn yanked it back to spawn, or
                // client prediction rubber-banded it -- which is the render's
                // "player never moved".
                double dx = landed.X - feet.x;
                double dy = landed.Y - feet.y;
                double dz = landed.Z - feet.z;
                double drift = System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
                _logger.LogInformation(
                    "[nade-render] Load settled: steam={steam} at={x},{y},{z} target={tx},{ty},{tz} drift={drift} alive={alive}",
                    player.SteamID,
                    landed.X,
                    landed.Y,
                    landed.Z,
                    feet.x,
                    feet.y,
                    feet.z,
                    drift,
                    player.IsAlive
                );
            }
            else
            {
                _logger.LogWarning(
                    "[nade-render] Load settled: steam={steam} has NO valid pawn a tick after teleport (respawned/died?)",
                    player.SteamID
                );
            }

            ShowAround(player, lineup, standing);
        });

        if (AnnouncesLoad(player.SteamID))
        {
            player.SendCenter(Describe(lineup));
        }
    }

    // A square the size of a player, flat on the floor: the space a spawn
    // actually takes up. White, because amber here means a throw -- where to
    // stand for one, where to aim it -- and a spawn is neither. Four beams
    // each, so a map's worth is cheap, and the count is capped anyway.
    private const float SpawnBoxHalfWidth = 16f;
    private const int MaxSpawnsDrawn = 32;

    private static readonly Color SpawnWhite = new Color(240, 240, 240, 255);

    public bool SpawnsShown => _spawnBoxes.Beams.Count > 0;

    public void ShowSpawns(IReadOnlyList<ThrowSnapshot> spawns)
    {
        ClearSpawns();

        if (!DrawMarkers)
        {
            return;
        }

        // Square to the map, not to the way the spawn faces: a player's hull
        // is, and which way a spawn points is not something anybody practising
        // utility cares about.
        (float x, float y)[] corners =
        {
            (-SpawnBoxHalfWidth, -SpawnBoxHalfWidth),
            (SpawnBoxHalfWidth, -SpawnBoxHalfWidth),
            (SpawnBoxHalfWidth, SpawnBoxHalfWidth),
            (-SpawnBoxHalfWidth, SpawnBoxHalfWidth),
        };

        foreach (ThrowSnapshot spawn in spawns.Take(MaxSpawnsDrawn))
        {
            Vec3 feet = Grounded(spawn.feet_position);

            // The footprints' height, for the footprints' reason: on the
            // water at Ancient T spawn the floor is under the surface, and
            // anything drawn on it is not seen.
            float z = PadZ(feet);

            for (int corner = 0; corner < corners.Length; corner++)
            {
                (float x1, float y1) = corners[corner];
                (float x2, float y2) = corners[(corner + 1) % corners.Length];

                Beam(
                    _spawnBoxes,
                    null,
                    new Vec3(feet.x + x1, feet.y + y1, z),
                    new Vec3(feet.x + x2, feet.y + y2, z),
                    SpawnWhite,
                    PadWidth
                );
            }
        }
    }

    public void ClearSpawns()
    {
        Despawn(_spawnBoxes);
    }

    private IReadOnlyList<LineupRecord> Restricted(IReadOnlyList<LineupRecord> lineups)
    {
        IReadOnlyList<string>? only = LibraryRestriction;

        if (only == null || only.Count == 0)
        {
            return lineups;
        }

        // Drawn in the execute's own order, so the colours run 1..n the way the
        // steps do rather than in whatever order the library came back in.
        return only.Select(id =>
                lineups.FirstOrDefault(lineup =>
                    string.Equals(lineup.client_id, id, StringComparison.OrdinalIgnoreCase)
                )
            )
            .OfType<LineupRecord>()
            .ToList();
    }

    /// <summary>
    /// Put a player on a lineup's stance without reloading the lineup.
    ///
    /// Walking into a circle tells you where to stand but not exactly where,
    /// and "exactly" is the whole point of a stance -- so being near one and
    /// pressing use finishes the walk. Deliberately not a Load: the lineup is
    /// already the one in hand, and re-giving the grenade mid-run-up would take
    /// a thrown one back.
    /// </summary>
    public bool StandOn(IPlayer player, LineupRecord lineup)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return false;
        }

        Vec3 feet = Standable(Grounded(lineup.release.feet_position));

        if (!Sane(feet))
        {
            return false;
        }

        var position = new Vector(feet.x, feet.y, feet.z);
        var facing = new QAngle(0, lineup.release.yaw, 0);
        var aim = new QAngle(lineup.release.pitch, lineup.release.yaw, 0);

        player.Teleport(position, facing, new Vector(0, 0, 0));
        pawn.EyeAngles = aim;

        // The client re-predicts from the command it had in flight and snaps
        // the view back, so once is not enough.
        ReapplyAngles(player, facing, aim, 2);

        return true;
    }

    /// <summary>
    /// Stand a render's thrower on the lineup with its exact aim, and draw
    /// nothing. Unlike StandOn the client's view is turned as well: the camera
    /// is filming through these eyes, so the view itself has to be on the
    /// line, not just the server's idea of where the pawn looks.
    /// </summary>
    public Vec3? Stage(IPlayer player, LineupRecord lineup)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return null;
        }

        Vec3 feet = Standable(
            Grounded(RenderDirectorUtility.StageAt(lineup.release.feet_position, lineup.approach))
        );

        if (!Sane(feet))
        {
            return null;
        }

        var facing = new QAngle(0, lineup.release.yaw, 0);

        player.Teleport(new Vector(feet.x, feet.y, feet.z), facing, new Vector(0, 0, 0));
        Repoint(player, lineup.release.pitch, lineup.release.yaw);
        ReapplyEyes(player, lineup.release.pitch, lineup.release.yaw, 2);

        GiveUtility(player, lineup.utility_type);

        return feet;
    }

    // Re-points a staged thrower without moving them; the aim drifts if
    // anything nudged the view between staging and the shot.
    public void Repoint(IPlayer player, float pitch, float yaw)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            return;
        }

        player.ExecuteCommand(RenderDirectorUtility.EyesCommand(pitch, yaw));
        pawn.EyeAngles = new QAngle(pitch, yaw, 0);
    }

    // The client re-predicts from the command it had in flight and snaps the
    // view back, so once is not enough.
    private void ReapplyEyes(IPlayer player, float pitch, float yaw, int frames)
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

            Repoint(player, pitch, yaw);
            ReapplyEyes(player, pitch, yaw, frames - 1);
        });
    }

    // Where a camera can sit on the line from its subject without being inside
    // a wall.
    public Vec3 CameraClear(Vec3 subject, Vec3 eye)
    {
        try
        {
            var trace = _core.Trace.TraceShapeLine(
                new Vector(subject.x, subject.y, subject.z),
                new Vector(eye.x, eye.y, eye.z),
                SkipMarkersAndPlayers()
            );

            Vec3? hit = trace.DidHit
                ? new Vec3(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z)
                : null;

            return RenderDirectorUtility.Unobstructed(subject, eye, hit, CameraWallMargin);
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to trace a render camera");
            return eye;
        }
    }

    private const float CameraWallMargin = 12f;

    // A point this close to where the trace stopped is the thing that was
    // being looked for, not something in front of it.
    private const float SeenWithinUnits = 24f;

    /// <summary>
    /// Whether a straight line from an eye reaches a point with none of the
    /// map in the way. A marker behind a wall is not on the player's screen,
    /// and treating the crosshair passing over where it would be as looking at
    /// it puts a lineup on the HUD that nothing in view explains.
    /// </summary>
    public bool Sees(Vec3 eye, Vec3 point)
    {
        try
        {
            var trace = _core.Trace.TraceShapeLine(
                new Vector(eye.x, eye.y, eye.z),
                new Vector(point.x, point.y, point.z),
                SkipMarkersAndPlayers()
            );

            if (!trace.DidHit)
            {
                return true;
            }

            return (new Vec3(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z) - point).Length()
                <= SeenWithinUnits;
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to trace a line of sight");

            // Unknown is treated as visible: hiding a lineup the player can
            // see is the worse of the two mistakes.
            return true;
        }
    }

    public static CEntityKeyValues MarkerKeys()
    {
        return Tagged();
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

                if (IsSolo(player.SteamID))
                {
                    Own(beam, player.SteamID);
                }
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
    // force is .rethrow asking by name. The switch below is what a render pod
    // sets to have every load throw itself; a player typing the command has
    // already said which throw they want, so it is not the switch's to refuse.
    public void ThrowGhostProjectile(IPlayer player, LineupRecord lineup, bool force = false)
    {
        _logger.LogInformation(
            "[nade-render] ThrowGhostProjectile: emitGrenades={emit} ghostProjectile={ghost} forced={force} exactlyReplayable={exact} hasSeed={seed} confidence={conf}",
            EmitGrenades,
            _config.GhostProjectile,
            force,
            lineup.IsExactlyReplayable(),
            lineup.HasPhysicsSeed(),
            lineup.confidence
        );

        if (!EmitGrenades)
        {
            _logger.LogInformation("[nade-render] skip: EmitGrenades off");
            return;
        }

        if (!_config.GhostProjectile && !force)
        {
            _logger.LogInformation("[nade-render] skip: GhostProjectile off");
            return;
        }

        CBasePlayerPawn? pawn = player.Pawn;

        if (pawn == null || !pawn.IsValid)
        {
            _logger.LogInformation("[nade-render] skip: pawn invalid");
            return;
        }

        // Both halves or nothing. A zeroed seed would launch the grenade out of
        // the map origin, and a seed the panel did not call exact belongs to a
        // path fitted to a demo rather than to a throw the engine performed --
        // practise toward that one, never replay it.
        if (!lineup.IsExactlyReplayable())
        {
            _logger.LogInformation("[nade-render] skip: not exactly replayable");
            return;
        }
        _logger.LogInformation("[nade-render] EMITTING {type} from seed", lineup.utility_type);

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
                    Name = DrillUtility.Name(lineup),
                    UtilityType = lineup.utility_type,
                    ClientId = lineup.client_id,
                    LineupId = lineup.id,
                    ExpiresAt = DateTime.UtcNow.AddSeconds(GhostThrowSeconds),
                };

                uint index = projectile.Index;

                _core.Scheduler.Delay(BlockableAfterTicks, () => MakeBlockable(projectile, index));
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

    // A rethrow is how a player stands in a throw's way to see whether a body
    // blocks it. The engine's grenade passes through every player and skips
    // its owner, so a few ticks out -- clear of whoever stands on the spot it
    // was thrown from -- it is given the player layers and no owner. The
    // thrower stays, which is what says whose throw it was.
    private const int BlockableAfterTicks = 4;

    // player, csgo_team1, csgo_team2.
    private const ulong PlayerLayers = (1UL << 18) | (1UL << 31) | (1UL << 32);

    private void MakeBlockable(CBaseCSGrenadeProjectile projectile, uint index)
    {
        try
        {
            if (!projectile.IsValid || projectile.Index != index)
            {
                return;
            }

            VPhysicsCollisionAttribute_t attribute = projectile.Collision.CollisionAttribute;

            attribute.InteractsWith |= PlayerLayers;
            attribute.InteractsWithUpdated();
            projectile.OwnerEntity = CHandle<CBaseEntity>.Invalid;
            projectile.OwnerEntityUpdated();
            projectile.CollisionRulesChanged();
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to make a rethrown grenade blockable");
        }
    }

    // The name of the lineup a ghost in the air was thrown from, or null for a
    // projectile that is somebody's own throw.
    public string? GhostName(uint entityIndex)
    {
        return _ghostThrows.TryGetValue(entityIndex, out GhostThrow? ghost) ? ghost.Name : null;
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

            if (_ghosts[index].Kind == GhostKind.Trail)
            {
                _trails.Remove(_ghosts[index].OwnerSteamId);
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

        ClearWorlds();

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

    // Public so a drill can re-arm a player for another rep without moving
    // them: the point of a repeat is throwing the same lineup again, and being
    // teleported back onto the spot each time takes the walk-up away.
    public void GiveUtility(IPlayer player, string utilityType)
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

        Vec3 standable = Standable(landing);
        var position = new Vector(standable.x, standable.y, standable.z);
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

        ShowAround(player, lineup, new Vec3(feet.X, feet.Y, feet.Z));
    }

    // How close a player has to be to a stance ring to be "in" it. A little
    // wider than the ring itself so stepping onto the marker counts.
    public const float SpotRadius = 40f;

    // How far above or below a spot still counts as the same place to stand.
    public const float SpotHeight = 72f;

    // The most lineups a player's layer will draw. The panel serves up to 500,
    // and drawn in full that is a server crash delivered by popularity rather
    // than by any bug. The API returns newest first, so what survives the cap
    // is the newest.
    private const int MaxLibraryDrawn = 150;

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

    // Where a player would be STANDING at this spot. Lineups recorded before
    // the recorder learned to keep the standstill hold the release origin,
    // which for a jump throw is a jump height up in the air -- and a marker
    // floating at head height is not somewhere anyone can stand.
    // Where to PUT a player, as opposed to where to draw a mark.
    //
    // Grounded traces a line, and a line finds the floor between things a
    // player hull cannot fit between -- the rubble at Ancient ruins is the
    // reported case, where the trace lands in a gap and the pawn arrives
    // wedged in the stones. Dropping them from just above instead lets the
    // engine resolve the standing position itself, which it does correctly and
    // a trace here cannot. Markers keep using the true floor: a reticle
    // hovering above the ground for this reason would be a different bug.
    private static Vec3 Standable(Vec3 grounded)
    {
        return new Vec3(grounded.x, grounded.y, grounded.z + TeleportClearance);
    }

    // Where a player standing at this point actually rests. Swept with the
    // player's own hull, not a line down the middle: on a stair edge or a kerb
    // the hull sits on the higher step while the centre of it hangs over the
    // lower one, and a line trace stood the thrower ~5u too low -- low enough
    // to put the crosshair on the wrong thing in a lineup aimed past a wall.
    private Vec3 Grounded(Vec3 position)
    {
        try
        {
            // Started above the point on purpose: a trace that begins flush
            // against a surface can report no hit at all, which is exactly the
            // case for a lineup already standing on the floor.
            var from = new Vector(position.x, position.y, position.z + HullLift);
            var to = new Vector(
                position.x,
                position.y,
                position.z - GroundSnapRange
            );
            var hull = new BBox_t
            {
                Mins = new Vector(-PlayerHalfWidth, -PlayerHalfWidth, 0f),
                Maxs = new Vector(PlayerHalfWidth, PlayerHalfWidth, PlayerHeight),
            };

            var trace = _core.Trace.TracePlayerBBox(from, to, hull, SkipMarkersAndPlayers());

            if (trace.StartInSolid || !trace.DidHit)
            {
                return GroundedOnCentre(position);
            }

            float drop = position.z - trace.EndPos.Z;

            // A stance recorded standing is already where the hull rests; only
            // one recorded in the air (or under a floor) is moved.
            if (MathF.Abs(drop) <= HullRestTolerance)
            {
                return position;
            }

            _logger.LogInformation(
                "lineup stance moved {drop} units onto where a player rests",
                drop
            );

            return new Vec3(position.x, position.y, trace.EndPos.Z);
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to find the floor under a lineup");
            return GroundedOnCentre(position);
        }
    }

    private const float HullLift = 8f;
    private const float HullRestTolerance = 1f;
    private const float PlayerHalfWidth = 16f;
    private const float PlayerHeight = 72f;

    private Vec3 GroundedOnCentre(Vec3 position)
    {
        try
        {
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

    // Enough to clear an uneven floor without being a noticeable drop.
    private const float TeleportClearance = 4f;

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
        "prop_dynamic",
    };

    private static CEntityKeyValues Tagged()
    {
        var keys = new CEntityKeyValues();

        keys.SetString("targetname", MarkerTag);

        return keys;
    }

    // Live utility, as opposed to the preview drawn over it. Smokes and fires
    // are what stands between two attempts at the same lineup, and anything
    // still in the air goes with them: a rethrow answered by the grenade you
    // are trying to replace is not an answer.
    private static readonly string[] ThrownUtilityClasses =
    {
        "smokegrenade_projectile",
        "flashbang_projectile",
        "hegrenade_projectile",
        "molotov_projectile",
        "decoy_projectile",
        "inferno",
    };

    public int ClearThrownUtility()
    {
        int cleared = 0;

        foreach (string designer in ThrownUtilityClasses)
        {
            try
            {
                foreach (CBaseEntity entity in EntitiesOf(designer))
                {
                    if (!entity.IsValid)
                    {
                        continue;
                    }

                    // Taken out of the world by hand, so nothing will ever
                    // report its detonation: the bookkeeping goes with it or it
                    // sits in the table until the reaper times it out.
                    _ghostThrows.Remove(entity.Index);

                    entity.Despawn();
                    cleared += 1;
                }
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "unable to clear {designer}", designer);
            }
        }

        // The bloom preview is a real smoke, so it went with the rest. Its
        // handles have to go too: a dead one can be recycled into a new entity,
        // and despawning THAT is worse than leaving the entry behind.
        _bloomSmoke.Clear();

        return cleared;
    }

    // Despawns every marker in the world, ours or a previous instance's, then
    // forgets the handles. Safe to call when there is nothing to find.
    // A copy, never the entity system's own enumeration: despawning inside
    // that throws on the next step, so a loop over it removes one entity and
    // stops. That is how a sweep left a world full of the last instance's
    // beams, and how clearing utility took one smoke and left the rest.
    private List<CBaseEntity> EntitiesOf(string designer)
    {
        return _core.EntitySystem.GetAllEntitiesByDesignerName<CBaseEntity>(designer).ToList();
    }

    public int SweepMarkers()
    {
        // The aim trace is memoised per lineup for the life of the map, so a
        // point that was cached while the trace still stopped on trigger
        // brushes would outlive the fix for it. A sweep is the one moment the
        // world is known to be empty of ours, so it is where that is dropped.
        _aimHits.Clear();

        int swept = 0;

        foreach (string designer in MarkerClasses)
        {
            try
            {
                foreach (CBaseEntity entity in EntitiesOf(designer))
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

    // For a map change only. The entities died with the map, so their handles
    // are stale -- and a stale handle can be recycled into a NEW entity, which
    // makes despawning it actively harmful. Drop the references instead.
    public void ForgetMarkers()
    {
        _aimHits.Clear();
        ForgetWorlds();
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

    // How long a coloured trail stays up. Matches the engine trail it replaces
    // (sv_grenade_trajectory_prac_trailtime), so turning ours on does not also
    // silently change how long you have to walk over and look at it.
    private const float TrailSeconds = 10f;

    private const float TrailWidth = 0.5f;

    // The recorder samples at 32Hz, which down a three second arc is ~96 beams
    // for ONE grenade -- five players rehearsing would be thousands of live
    // entities, which is a server falling over because the feature works. A
    // grenade flies a smooth parabola, so a beam every so many units is the
    // same curve for a fraction of the cost, and a stuck or rolling grenade
    // stops adding to it entirely.
    private const float TrailStepUnits = 26f;

    // Backstop for a grenade that travels a very long way.
    private const int MaxTrailBeams = 48;

    private readonly Dictionary<ulong, (Ghost ghost, Vec3 last)> _trails = new();

    /// <summary>
    /// One sampled point of a live grenade, in the colour that throw was
    /// promised.
    ///
    /// The engine's practice trail is coloured by the thrower's TEAM, so on a
    /// practice server -- where everybody is usually on the same side -- every
    /// arc looks the same and the colour says nothing about which throw it
    /// belonged to. Which colour a given throw gets is the caller's business:
    /// its step in a running execute, otherwise the next one off the player's
    /// own cycle.
    /// </summary>
    public void TrailPoint(ulong steamId, PracticeStepColors.StepColor step, Vec3 at)
    {
        if (!DrawMarkers || !DrawTrail)
        {
            return;
        }

        if (!_trails.TryGetValue(steamId, out (Ghost ghost, Vec3 last) trail))
        {
            var started = new Ghost
            {
                OwnerSteamId = steamId,
                Kind = GhostKind.Trail,
                ExpiresAt = DateTime.UtcNow.AddSeconds(TrailSeconds),
                Beams = new List<CEnvBeam>(),
            };

            _ghosts.Add(started);
            _trails[steamId] = (started, at);

            return;
        }

        Vec3 moved = new Vec3(at.x - trail.last.x, at.y - trail.last.y, at.z - trail.last.z);

        if (moved.Length() < TrailStepUnits)
        {
            // Not far enough to be worth an entity, and the arc is unchanged.
            // The cursor deliberately stays put so the next beam spans the
            // whole gap rather than starting from a point never drawn.
            return;
        }

        if (trail.ghost.Beams.Count < MaxTrailBeams)
        {
            CEnvBeam? beam = CreateBeam(trail.last, at, Rgb(step), TrailWidth);

            if (beam != null)
            {
                trail.ghost.Beams.Add(beam);
            }
        }

        // Pushed out as the grenade flies, so the whole arc fades together from
        // the moment it lands rather than the start of it disappearing while
        // the smoke is still in the air.
        trail.ghost.ExpiresAt = DateTime.UtcNow.AddSeconds(TrailSeconds);

        _trails[steamId] = (trail.ghost, at);
    }

    /// <summary>
    /// Tint the smoke cloud itself, so the thing a player is actually looking
    /// at is the thing that carries the colour.
    ///
    /// A trail says where a grenade went; the bloom is what it DID, and after
    /// four of them are up the trails have faded and all that is left on the
    /// map is four identical grey clouds. Set at creation, before the grenade
    /// has landed: the colour is read when the smoke starts blooming, so there
    /// is nothing to change afterwards.
    /// </summary>
    public bool TintSmoke(CEntityInstance entity, PracticeStepColors.StepColor step)
    {
        if (!DrawMarkers)
        {
            return false;
        }

        try
        {
            CSmokeGrenadeProjectile smoke = entity.As<CSmokeGrenadeProjectile>();

            if (!smoke.IsValid)
            {
                return false;
            }

            smoke.SmokeColor = new Vector(step.R, step.G, step.B);

            // The schema write alone does not reach clients -- the same rule
            // the beams follow, where a bare colour assignment never networks.
            smoke.SmokeColorUpdated();

            return true;
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to tint a smoke");
            return false;
        }
    }

    /// <summary>The grenade is gone; the next one starts a new arc.</summary>
    public void TrailEnded(ulong steamId)
    {
        _trails.Remove(steamId);
    }

    private void Kill(Ghost ghost)
    {
        foreach (CEnvBeam beam in ghost.Beams)
        {
            if (!beam.IsValid)
            {
                continue;
            }

            Release(beam);
            beam.Despawn();
        }
    }

    // The web panel's type colours, so a smoke is the same colour on the
    // website, on the minimap and in the world.
    private static Color ColorFor(string utilityType)
    {
        switch (utilityType)
        {
            case "Smoke":
                return new Color(50, 214, 224, 255);
            case "Flash":
                return new Color(255, 210, 26, 255);
            case "HighExplosive":
                return new Color(255, 59, 59, 255);
            case "Molotov":
                return new Color(255, 106, 26, 255);
            default:
                return new Color(102, 221, 85, 255);
        }
    }
}
