using System.Globalization;
using System.Text;
using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.EntitySystem;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Trace;

namespace UtilityPractice;

// What one player sees in the world: a glowing grenade floating over every
// spot that passes their filters, footprints on the floor near one, and the
// aim marker for the throw they are on. Every player has their own set, hidden
// from everybody else, which is what lets two people filter, clear and line up
// different things on one server.
public partial class PracticeReplay
{
    private sealed class MarkerSet
    {
        public readonly List<CEnvBeam> Beams = new();
        public readonly List<CPhysicsProp> Props = new();
    }

    // One place to stand: where one or more of a spot's throws were made from,
    // to the unit. It is drawn there once and never moved.
    private sealed class Stand
    {
        public required Vec3 At;
        public readonly List<LineupRecord> Lineups = new();
        public bool On;

        // The throw in focus is made from here.
        public bool Lead;

        // The throw the dot is drawn for: it sits where that throw's
        // crosshair lands looking down, which depends on the way it faces.
        public required LineupRecord Dot;
        public readonly MarkerSet Set = new();
    }

    // A spot is every throw within a few strides of each other, and they were
    // not all made from the same square foot of floor: each place gets a ring
    // of its own.
    private sealed class Pad
    {
        public required LineupSpot Spot;
        public readonly List<Stand> Stands = new();

        // Which way the throw in focus faces, at the place it is made from.
        // The one part of a pad that is ever drawn somewhere else, because it
        // belongs to the throw and not to the floor.
        public readonly MarkerSet Arrow = new();
        public Stand? ArrowAt;
        public float ArrowYaw;
    }

    private sealed class AimMark
    {
        public required LineupRecord Lineup;

        // The throw the player is on gets the whole marker; the others off the
        // same spot are a plain ring, so the choice is visible without four
        // finders stacked across the skyline.
        public required bool Full;
        public required Vec3 Center;
        public required Vec3 Forward;
        public required float Away;
        public eAimStage Stage;
        public bool Lined;
        public float Visibility = 1f;
        public readonly MarkerSet Set = new();
    }

    private sealed class PlayerWorld
    {
        public string Signature = "";
        public List<LineupSpot> Spots = new();
        public readonly Dictionary<string, Vec3> Floors = new();
        public readonly MarkerSet Beacons = new();
        public readonly Dictionary<string, Pad> Pads = new();
        public readonly List<AimMark> Aims = new();

        // The landing mark and the run-up lane of the throw in focus.
        public readonly MarkerSet Focus = new();
        public string? FocusSpot;
        public LineupRecord? FocusLineup;

        // Set by .clear and held until the player asks for something again.
        public bool Hidden;
    }

    private readonly Dictionary<ulong, PlayerWorld> _worlds = new();

    // Who each marker belongs to, and which players have been told to skip the
    // ones that are not theirs.
    private readonly Dictionary<uint, ulong> _owners = new();
    private readonly Dictionary<int, ulong> _viewers = new();

    private static readonly Color Green = new Color(60, 230, 90, 255);

    private const float PadLift = 1.5f;
    private const float PadWidth = 0.7f;
    // Thirty-two thin strokes are a circle; ten thick ones were a nut. The
    // ring is only sixteen units across, so a stroke as wide as the other
    // markers' is most of the way to filling it in.
    private const int PadRingSegments = 32;
    private const float PadRingWidth = 0.3f;
    private const float PadArrowWidth = 0.45f;

    // A beam as long as it is wide is a square, which at this size is a dot.
    private const float PadDotUnits = 0.5f;

    // Two throws share a ring only when they were made from the same place.
    // Anything further apart than this is a different place to stand, however
    // close, and gets a ring of its own: a ring a stride from where a throw
    // was really made is a ring that makes it miss.
    private const float StandSameUnits = 1f;

    // Further apart than a step, so walking along the edge does not draw and
    // remove the same footprints over and over.
    private const float PadShowUnits = 420f;
    private const float PadHideUnits = 480f;
    private const float PadReachHeight = 160f;
    private const int MaxPads = 3;

    // How far up a floor is searched for a water surface to draw on.
    private const float WaterProbe = 40f;

    /// <summary>
    /// The lineups a player's world is drawn from: their library after their
    /// own filters. Supplied by the plugin, which owns both.
    /// </summary>
    public Func<ulong, IReadOnlyList<LineupRecord>> Shown { get; set; } =
        _ => Array.Empty<LineupRecord>();

    private PlayerWorld WorldFor(ulong steamId)
    {
        if (!_worlds.TryGetValue(steamId, out PlayerWorld? world))
        {
            world = new PlayerWorld();
            _worlds[steamId] = world;
        }

        return world;
    }

    public IReadOnlyList<LineupSpot> SpotsFor(ulong steamId)
    {
        return _worlds.TryGetValue(steamId, out PlayerWorld? world) && !world.Hidden
            ? world.Spots
            : Array.Empty<LineupSpot>();
    }

    public bool IsHidden(ulong steamId)
    {
        return _worlds.TryGetValue(steamId, out PlayerWorld? world) && world.Hidden;
    }

    // ── ownership ────────────────────────────────────────────────────────

    private IEnumerable<IPlayer> Humans()
    {
        foreach (IPlayer player in _core.PlayerManager.GetAllPlayers())
        {
            if (player != null && player.IsValid && !player.IsFakeClient)
            {
                yield return player;
            }
        }
    }

    private void Own(CEntityInstance entity, ulong owner)
    {
        if (!TransmitBlocking)
        {
            return;
        }

        _owners[entity.Index] = owner;

        foreach (IPlayer viewer in Humans())
        {
            if (viewer.SteamID != owner)
            {
                viewer.ShouldBlockTransmitEntity((int)entity.Index, true);
            }
        }
    }

    // Before the despawn, never after: the engine hands a freed index to the
    // next entity it makes, and a block left standing would hide that one.
    private void Release(CEntityInstance entity)
    {
        if (!_owners.Remove(entity.Index))
        {
            return;
        }

        foreach (IPlayer viewer in Humans())
        {
            viewer.ShouldBlockTransmitEntity((int)entity.Index, false);
        }
    }

    /// <summary>
    /// Brings whoever has joined since the last call in line with what is
    /// already drawn. A marker hides itself from the players present when it
    /// is made; somebody who connects afterwards has been told nothing.
    /// </summary>
    public void SyncViewers()
    {
        if (!TransmitBlocking)
        {
            return;
        }

        var present = new HashSet<int>();

        foreach (IPlayer viewer in Humans())
        {
            present.Add(viewer.PlayerID);

            if (_viewers.TryGetValue(viewer.PlayerID, out ulong known) && known == viewer.SteamID)
            {
                continue;
            }

            _viewers[viewer.PlayerID] = viewer.SteamID;
            viewer.ClearTransmitEntityBlocks();

            foreach ((uint index, ulong owner) in _owners)
            {
                if (owner != viewer.SteamID)
                {
                    viewer.ShouldBlockTransmitEntity((int)index, true);
                }
            }
        }

        foreach (int gone in _viewers.Keys.Where(id => !present.Contains(id)).ToList())
        {
            _viewers.Remove(gone);
        }
    }

    // A slot that empties and fills again inside one second is a new client
    // that has been told nothing, whatever the slot remembers.
    public void ForgetViewer(int playerId)
    {
        _viewers.Remove(playerId);
    }

    // ── drawing ──────────────────────────────────────────────────────────

    private void Beam(
        MarkerSet into,
        ulong? owner,
        Vec3 start,
        Vec3 end,
        Color color,
        float width
    )
    {
        CEnvBeam? beam = CreateBeam(start, end, color, width);

        if (beam == null)
        {
            return;
        }

        into.Beams.Add(beam);

        if (owner != null)
        {
            Own(beam, owner.Value);
        }
    }

    // Lines given in a spot's own frame, laid on the floor facing its throw.
    private void Segments(
        MarkerSet into,
        ulong? owner,
        Vec3 at,
        float yaw,
        float z,
        IEnumerable<MarkerSegment> segments,
        Color color,
        float width
    )
    {
        (Vec3 forward, Vec3 right) = Bearing(yaw);

        foreach (MarkerSegment segment in segments)
        {
            Beam(
                into,
                owner,
                new Vec3(
                    at.x + (forward.x * segment.Along1) + (right.x * segment.Across1),
                    at.y + (forward.y * segment.Along1) + (right.y * segment.Across1),
                    z
                ),
                new Vec3(
                    at.x + (forward.x * segment.Along2) + (right.x * segment.Across2),
                    at.y + (forward.y * segment.Along2) + (right.y * segment.Across2),
                    z
                ),
                color,
                width
            );
        }
    }

    // Four beams, and deliberately not a circle: the landing marker and the
    // stance marker must never be mistaken for each other at a glance.
    private void Diamond(MarkerSet into, ulong? owner, Vec3 at, float radius, Color color, float width)
    {
        var north = new Vec3(at.x, at.y + radius, at.z);
        var east = new Vec3(at.x + radius, at.y, at.z);
        var south = new Vec3(at.x, at.y - radius, at.z);
        var west = new Vec3(at.x - radius, at.y, at.z);

        Beam(into, owner, north, east, color, width);
        Beam(into, owner, east, south, color, width);
        Beam(into, owner, south, west, color, width);
        Beam(into, owner, west, north, color, width);
    }

    // The grenade itself, floating over the spot: the glow says where, and
    // this says what to bring.
    private void Model(MarkerSet into, ulong? owner, string utilityType, Vec3 at, Color? glow)
    {
        if (!DrawModels || !Sane(at))
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

            // The model floats ON the spot a player is being told to stand, so
            // anything short of completely intangible traps them inside it.
            // The "solid" keyvalue alone does not survive prop_physics building
            // its own VPhysics on spawn.
            prop.Collision.SolidType = SolidType_t.SOLID_NONE;
            prop.Collision.SolidFlags = NotSolid;
            prop.Collision.CollisionGroup = (byte)CollisionGroup.Nonphysical;
            prop.Collision.CollisionAttribute.CollisionGroup = (byte)CollisionGroup.Nonphysical;
            prop.Collision.CollisionAttribute.InteractsAs = 0;
            prop.Collision.CollisionAttribute.InteractsWith = 0;
            prop.CollisionRulesChanged();

            // Set on the spawn tick so it rides the entity's first snapshot.
            // Type 3 is the through-walls outline; team -1 shows it to
            // everyone the entity itself is sent to.
            prop.Glow.GlowType = 3;
            prop.Glow.GlowColorOverride = glow ?? ColorFor(utilityType);
            prop.Glow.GlowRange = UtilityGlowRange;
            prop.Glow.GlowRangeMin = 0;
            prop.Glow.GlowTeam = -1;

            // Without this the outline only exists through walls: the moment
            // the model is actually on screen the glow is culled, which reads
            // as "it vanishes when I look at it".
            prop.Glow.EligibleForScreenHighlight = true;
            prop.Glow.Glowing = true;

            into.Props.Add(prop);

            if (owner != null)
            {
                Own(prop, owner.Value);
            }
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to show a lineup's utility model");
        }
    }

    private void Despawn(MarkerSet set)
    {
        foreach (CEnvBeam beam in set.Beams)
        {
            if (beam.IsValid)
            {
                Release(beam);
                beam.Despawn();
            }
        }

        foreach (CPhysicsProp prop in set.Props)
        {
            if (prop.IsValid)
            {
                Release(prop);
                prop.Despawn();
            }
        }

        Forget(set);
    }

    private static void Forget(MarkerSet set)
    {
        set.Beams.Clear();
        set.Props.Clear();
    }

    // ── the world: every spot that passes the player's filters ───────────

    // Geometry is part of it, not just identity: a lineup that was edited
    // keeps its id and has to redraw, and a refresh that changed nothing must
    // not.
    private string WorldSignature(IReadOnlyList<LineupRecord> lineups)
    {
        var builder = new StringBuilder(lineups.Count * 48);

        foreach (LineupRecord lineup in lineups)
        {
            Vec3 feet = lineup.release.feet_position;

            builder
                .Append(lineup.client_id)
                .Append(':')
                .Append(lineup.utility_type)
                .Append(':')
                .Append((int)feet.x).Append(',').Append((int)feet.y).Append(',').Append((int)feet.z)
                .Append(':')
                .Append((int)lineup.release.yaw)
                .Append('|');
        }

        // An execute recolours the spots it uses without changing which
        // lineups are drawn.
        if (LibraryRestriction != null)
        {
            builder.Append("execute");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Draw a player's spots, or leave them alone when nothing about them has
    /// changed. Called every second as well as on demand, so it has to be free
    /// when there is nothing to do: redrawing despawns and respawns every
    /// model that player can see.
    /// </summary>
    public void ShowWorld(IPlayer owner, IReadOnlyList<LineupRecord> lineups)
    {
        if (!DrawMarkers)
        {
            return;
        }

        PlayerWorld world = WorldFor(owner.SteamID);

        if (world.Hidden)
        {
            return;
        }

        IReadOnlyList<LineupRecord> all = Restricted(lineups);
        List<LineupRecord> drawn = all.Take(MaxLibraryDrawn).ToList();
        string signature = WorldSignature(drawn);

        if (signature == world.Signature)
        {
            return;
        }

        world.Signature = signature;

        if (drawn.Count < all.Count)
        {
            // Never silently: a capped map reads as "this is everything".
            _logger.LogWarning(
                "library draw capped at {drawn} of {total} lineups",
                drawn.Count,
                all.Count
            );
        }

        Despawn(world.Beacons);

        foreach (Pad pad in world.Pads.Values)
        {
            DespawnPad(pad);
        }

        world.Pads.Clear();
        world.Floors.Clear();
        world.Spots = PracticeMarkerUtility.Spots(drawn, SpotRadius, SpotHeight);

        if (!DrawBeacons)
        {
            return;
        }

        foreach (LineupSpot spot in world.Spots)
        {
            Vec3 floor = Grounded(spot.At);
            List<string> types = spot.Types;

            world.Floors[spot.Key] = floor;

            // What to bring, not which throw to make: two smokes from one
            // position want ONE smoke floating over it, and a smoke plus a
            // flash still shows both.
            for (int index = 0; index < types.Count; index++)
            {
                float offset = (index - ((types.Count - 1) / 2f)) * UtilityModelSpacing;

                Model(
                    world.Beacons,
                    owner.SteamID,
                    types[index],
                    new Vec3(floor.x + offset, floor.y, floor.z),
                    StepGlow(spot, types[index])
                );
            }
        }
    }

    // In an execute the step's colour beats the utility's own: telling the
    // four smokes apart is the whole job while one is running.
    private Color? StepGlow(LineupSpot spot, string utilityType)
    {
        if (LibraryRestriction == null)
        {
            return null;
        }

        foreach (LineupRecord lineup in spot.Lineups)
        {
            if (lineup.utility_type != utilityType)
            {
                continue;
            }

            PracticeStepColors.StepColor? step = StepColorFor(lineup.client_id);

            if (step != null)
            {
                return Rgb(step.Value);
            }
        }

        return null;
    }

    // A lineup the player has just been put on or has just saved: their world,
    // with this throw in focus whether or not their filters would show it.
    private void ShowAround(IPlayer player, LineupRecord lineup, Vec3 standing)
    {
        WorldFor(player.SteamID).Hidden = false;

        IReadOnlyList<LineupRecord> shown = Shown(player.SteamID);
        IReadOnlyList<LineupRecord> library = shown.Any(entry =>
            entry.client_id == lineup.client_id
        )
            ? shown
            : shown.Append(lineup).ToList();

        // Whatever else is throwable from this spot comes up with it.
        List<LineupRecord> here = SpotAt(library, standing);

        if (!here.Any(entry => entry.client_id == lineup.client_id))
        {
            here.Add(lineup);
        }

        ShowWorld(player, library);
        ShowSelection(player, here, standing, lineup);
    }

    // ── stand rings: where to put your feet, and which way to face ───────

    private static float Flat(Vec3 a, Vec3 b)
    {
        return new Vec3(a.x - b.x, a.y - b.y, 0f).LengthXY();
    }

    /// <summary>
    /// Keeps stand rings on the spots a player is near. Walking pace: this adds
    /// and removes whole markers, so it runs on the spot watcher's clock, not
    /// the aim's.
    /// </summary>
    public void WatchSpots(IPlayer owner, Vec3 feet)
    {
        if (!DrawMarkers || !DrawBeacons)
        {
            return;
        }

        if (!_worlds.TryGetValue(owner.SteamID, out PlayerWorld? world) || world.Hidden)
        {
            return;
        }

        var wanted = new List<(LineupSpot spot, Vec3 floor, float away)>();

        foreach (LineupSpot spot in world.Spots)
        {
            if (!world.Floors.TryGetValue(spot.Key, out Vec3 floor))
            {
                continue;
            }

            float away = Flat(floor, feet);
            float reach = world.Pads.ContainsKey(spot.Key) ? PadHideUnits : PadShowUnits;

            if (away <= reach && Math.Abs(floor.z - feet.z) <= PadReachHeight)
            {
                wanted.Add((spot, floor, away));
            }
        }

        List<(LineupSpot spot, Vec3 floor, float away)> kept = wanted
            .OrderBy(entry => entry.away)
            .Take(MaxPads)
            .ToList();

        foreach (string key in world.Pads.Keys.ToList())
        {
            if (kept.Any(entry => entry.spot.Key == key))
            {
                continue;
            }

            DespawnPad(world.Pads[key]);
            world.Pads.Remove(key);
        }

        foreach ((LineupSpot spot, Vec3 floor, float _) in kept)
        {
            if (world.Pads.ContainsKey(spot.Key))
            {
                continue;
            }

            var pad = new Pad { Spot = spot };

            world.Pads[spot.Key] = pad;
            DrawPad(owner.SteamID, pad);
            LeadPad(owner.SteamID, pad, world.FocusSpot == spot.Key ? world.FocusLineup : null);
        }
    }

    private void DrawPad(ulong owner, Pad pad)
    {
        foreach (LineupRecord lineup in pad.Spot.Lineups)
        {
            // Exactly where the throw was made: the ring is the eight units
            // that count as on the spot, and the dot is what goes under the
            // crosshair looking straight down, facing the arrow.
            Vec3 feet = Grounded(lineup.release.feet_position);
            Stand? stand = pad.Stands.FirstOrDefault(known =>
                (known.At - feet).Length() <= StandSameUnits
            );

            if (stand == null)
            {
                stand = new Stand { At = feet, Dot = lineup };
                pad.Stands.Add(stand);
            }

            stand.Lineups.Add(lineup);
        }

        foreach (Stand stand in pad.Stands)
        {
            DrawStand(stand, owner);
        }
    }

    private static Color StandColor(Stand stand)
    {
        return stand.On ? Green : stand.Lead ? Amber : AmberDim;
    }

    private void DrawStand(Stand stand, ulong owner)
    {
        Vec3 at = stand.At;
        float z = PadZ(at);
        Color color = StandColor(stand);

        Segments(
            stand.Set,
            owner,
            at,
            0f,
            z,
            PracticeMarkerUtility.Ring(PracticeLineupUtility.StanceToleranceUnits, PadRingSegments),
            color,
            PadRingWidth
        );
        Segments(
            stand.Set,
            owner,
            PracticeLineupUtility.LookDownPoint(stand.Dot, at),
            0f,
            z,
            new[] { new MarkerSegment(-PadDotUnits, 0f, PadDotUnits, 0f) },
            color,
            PadDotUnits
        );
    }

    // Says which of a pad's places the throw in focus is made from: that ring
    // goes bright and gets the arrow. No ring moves. A spot with one throw has
    // nothing to choose between, so its one place leads whether or not
    // anything is in focus.
    private void LeadPad(ulong owner, Pad pad, LineupRecord? focus)
    {
        LineupRecord? lead =
            focus != null && pad.Spot.Lineups.Any(lineup => lineup.client_id == focus.client_id)
                ? focus
                : pad.Spot.Lineups.Count == 1 ? pad.Spot.Lineups[0]
                : null;

        Stand? leading =
            lead == null
                ? null
                : pad.Stands.FirstOrDefault(stand =>
                    stand.Lineups.Any(lineup => lineup.client_id == lead.client_id)
                );

        foreach (Stand stand in pad.Stands)
        {
            bool leads = stand == leading;

            // Two throws off one place that face different ways put the dot
            // in different places, so it follows the one in focus.
            if (leads && lead != null && stand.Dot.client_id != lead.client_id)
            {
                stand.Lead = true;
                stand.Dot = lead;
                Despawn(stand.Set);
                DrawStand(stand, owner);

                continue;
            }

            if (stand.Lead == leads)
            {
                continue;
            }

            stand.Lead = leads;
            Recolour(stand.Set.Beams, StandColor(stand));
        }

        float yaw = lead?.release.yaw ?? 0f;

        if (
            pad.ArrowAt == leading
            && (leading == null || PracticeLineupUtility.AngleGap(pad.ArrowYaw, yaw) <= 1f)
        )
        {
            return;
        }

        Despawn(pad.Arrow);
        pad.ArrowAt = leading;
        pad.ArrowYaw = yaw;

        if (leading == null)
        {
            return;
        }

        Segments(
            pad.Arrow,
            owner,
            leading.At,
            yaw,
            PadZ(leading.At),
            PracticeMarkerUtility.Facing(),
            StandColor(leading),
            PadArrowWidth
        );
    }

    private void DespawnPad(Pad pad)
    {
        Despawn(pad.Arrow);
        pad.ArrowAt = null;

        foreach (Stand stand in pad.Stands)
        {
            Despawn(stand.Set);
        }

        pad.Stands.Clear();
    }

    // On the floor, unless the floor is under water: lines drawn below a
    // surface are swallowed by it, which is why the old ring floated at knee
    // height everywhere instead.
    private float PadZ(Vec3 floor)
    {
        try
        {
            var parameters = new TraceParams();

            parameters.InteractWith = MaskTrace.Water;

            var trace = _core.Trace.TraceShapeLine(
                new Vector(floor.x, floor.y, floor.z + WaterProbe),
                new Vector(floor.x, floor.y, floor.z),
                parameters
            );

            if (trace.DidHit && !trace.StartInSolid && trace.EndPos.Z > floor.z + PadLift)
            {
                return trace.EndPos.Z + PadLift;
            }
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to look for water over a spot");
        }

        return floor.z + PadLift;
    }

    // ── the selection: the throws off the spot the player is on ──────────

    /// <summary>
    /// One player's focused lineups: the aim marker for the throw they are on,
    /// a numbered ring for each other throw off the same spot, where it lands,
    /// and the lane to run down if it is thrown on the move.
    /// </summary>
    public void ShowSelection(
        IPlayer owner,
        IReadOnlyCollection<LineupRecord> active,
        Vec3 stance,
        LineupRecord? focused = null
    )
    {
        if (!DrawMarkers)
        {
            return;
        }

        PlayerWorld world = WorldFor(owner.SteamID);

        ClearFocus(world);

        if (world.Hidden || active.Count == 0)
        {
            foreach (Pad pad in world.Pads.Values)
            {
                LeadPad(owner.SteamID, pad, null);
            }

            return;
        }

        // Numbered the same way every time, so "2" is the same throw whichever
        // way the player walked in.
        List<LineupRecord> ordered = active
            .OrderBy(lineup => lineup.name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(lineup => lineup.client_id, StringComparer.Ordinal)
            .ToList();

        // The RECORDED spot, never where the player happens to be standing.
        Vec3 spot = Grounded(ordered[0].release.feet_position);

        // An aim marker only means anything seen from its own spot: the same
        // angles from anywhere else point at a different piece of wall.
        bool onSpot = Flat(spot, stance) <= SpotRadius && Math.Abs(spot.z - stance.z) <= SpotHeight;

        LineupRecord? lead =
            focused != null
                ? ordered.FirstOrDefault(lineup => lineup.client_id == focused.client_id)
                : null;

        if (lead == null && ordered.Count == 1)
        {
            lead = ordered[0];
        }

        float visibility = AimVisibility(owner.SteamID);
        CCSPlayerPawn? viewing = owner.PlayerPawn;
        QAngle? eyes = viewing != null && viewing.IsValid ? viewing.EyeAngles : null;

        if (onSpot)
        {
            List<(Vec3 center, Vec3 forward, float away)> aims = ordered.Select(AimPoint).ToList();
            int leadAt = lead != null ? ordered.IndexOf(lead) : -1;
            (Vec3 center, Vec3 forward, float away) seen = aims[Math.Max(leadAt, 0)];
            bool[] ringed = PracticeMarkerUtility.Ringed(
                seen.center - (seen.forward * seen.away),
                aims.Select(aim => aim.center).ToList(),
                leadAt
            );

            for (int index = 0; index < ordered.Count; index++)
            {
                if (!ringed[index])
                {
                    continue;
                }

                LineupRecord lineup = ordered[index];
                (Vec3 center, Vec3 forward, float away) = aims[index];

                var mark = new AimMark
                {
                    Lineup = lineup,
                    Full = lineup == lead,
                    Center = center,
                    Forward = forward,
                    Away = away,
                    Visibility = visibility,
                };

                // Born in the stage and colour it should already be, so a
                // marker drawn while the player is ALREADY on the angle does
                // not come up as a finder and correct itself a tick later.
                if (mark.Full && eyes != null && viewing != null)
                {
                    float error = AimErrorFrom(lineup, EyeOf(viewing), eyes.Value.Y, eyes.Value.X);

                    mark.Stage = PracticeMarkerUtility.AimStage(error);
                    mark.Lined = PracticeLineupUtility.AimMiss(error, lineup.aim_tolerance) <= 0f;
                }

                DrawAim(owner.SteamID, mark);
                world.Aims.Add(mark);
            }
        }

        if (lead != null)
        {
            Color color = ColorFor(lead.utility_type);
            Vec3 landing = lead.detonation_position;

            Diamond(world.Focus, owner.SteamID, landing, 30f, color, MarkerWidth);

            if (onSpot)
            {
                DrawLane(world, owner.SteamID, lead, Grounded(lead.release.feet_position));
            }
        }

        LineupSpot? home = world.Spots.FirstOrDefault(candidate =>
            candidate.Lineups.Any(lineup => lineup.client_id == ordered[0].client_id)
        );

        world.FocusSpot = home?.Key;
        world.FocusLineup = lead;

        foreach (Pad pad in world.Pads.Values)
        {
            LeadPad(owner.SteamID, pad, pad.Spot == home ? lead : null);
        }
    }

    private void ClearFocus(PlayerWorld world)
    {
        Despawn(world.Focus);

        foreach (AimMark mark in world.Aims)
        {
            Despawn(mark.Set);
        }

        world.Aims.Clear();
        world.FocusSpot = null;
        world.FocusLineup = null;
    }

    // Where a throw is aimed: the point its aim ray lands on, the way the ray
    // runs, and how far that is from the eye it was aimed from.
    //
    // From the eye of somebody on THIS LINEUP'S spot, in the stance it was
    // lined up in -- never from wherever the player happens to be, and never
    // from a standing eye for a throw made crouched. The point is a fact about
    // the lineup and the map: it is found once, drawn there, and does not move
    // when the player does. Anywhere but the exact spot it is the wrong thing
    // to put a crosshair on, which is what the ring on the floor is for.
    // Where the crosshair has to be for this throw, from this eye: the angles
    // to the point its marker is drawn at. The marker is fixed where the
    // recorded angles meet the wall from the recorded eye, so "lined up" has to
    // mean on the marker. Measured against the recorded angles instead, it
    // disagrees with the marker by however far this eye is from that one --
    // on a close wall, enough to say lined up with the crosshair off the
    // marker and not with it on.
    public (float yaw, float pitch) AimAnglesFrom(LineupRecord lineup, Vec3 eye)
    {
        (Vec3 center, _, _) = AimPoint(lineup);

        return PracticeLineupUtility.AnglesTo(eye, center);
    }

    public float AimErrorFrom(LineupRecord lineup, Vec3 eye, float eyeYaw, float eyePitch)
    {
        (float yaw, float pitch) = AimAnglesFrom(lineup, eye);

        return PracticeLineupUtility.AimError(eyeYaw, eyePitch, yaw, pitch);
    }

    public static Vec3 EyeOf(CCSPlayerPawn pawn)
    {
        Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);

        return new Vec3(origin.X, origin.Y, origin.Z + pawn.ViewOffset.Z.Value);
    }

    private (Vec3 center, Vec3 forward, float away) AimPoint(LineupRecord lineup)
    {
        Vec3 spot = Grounded(lineup.release.feet_position);

        var eye = new Vec3(spot.x, spot.y, spot.z + PracticeLineupUtility.AimEyeHeight(lineup));

        double yaw = lineup.release.yaw * Math.PI / 180.0;
        double pitch = lineup.release.pitch * Math.PI / 180.0;
        float flat = (float)Math.Cos(pitch);

        // CS2 pitch is negative looking up, so the sign flips here.
        var dir = new Vec3(
            (float)(Math.Cos(yaw) * flat),
            (float)(Math.Sin(yaw) * flat),
            (float)(-Math.Sin(pitch))
        );

        var fallback = new Vec3(
            eye.x + dir.x * AimFallbackRange,
            eye.y + dir.y * AimFallbackRange,
            eye.z + dir.z * AimFallbackRange
        );

        // Traced once per lineup and remembered: the wall a throw points at is
        // a static fact of the map, and the cache outlives whatever transient
        // -- a body, a thrown grenade -- might wander through the ray later.
        if (!_aimHits.TryGetValue(lineup.client_id, out Vec3 hit))
        {
            try
            {
                var trace = _core.Trace.TraceShapeLine(
                    new Vector(eye.x, eye.y, eye.z),
                    new Vector(
                        eye.x + dir.x * AimTraceRange,
                        eye.y + dir.y * AimTraceRange,
                        eye.z + dir.z * AimTraceRange
                    ),
                    SkipMarkers()
                );

                hit = trace.DidHit
                    ? new Vec3(trace.EndPos.X, trace.EndPos.Y, trace.EndPos.Z)
                    : fallback;
            }
            catch (Exception error)
            {
                _logger.LogError(error, "unable to trace a lineup's aim");
                hit = fallback;
            }

            _aimHits[lineup.client_id] = hit;
        }

        // Pulled back off the surface so the marker does not z-fight with the
        // wall it is drawn on.
        var center = new Vec3(hit.x - dir.x * 2f, hit.y - dir.y * 2f, hit.z - dir.z * 2f);

        return (center, dir, (center - eye).Length());
    }

    private static Color AimColor(AimMark mark)
    {
        Color color = mark.Full ? (mark.Lined ? Green : Amber) : AmberDim;

        return Faded(color, mark.Visibility);
    }

    // Everything is sized in degrees of the player's view, so the marker looks
    // the same from the stance whether the wall is ten units away or two
    // thousand. How much of it is drawn is the stage: the finder and its name
    // from far off, ticks once it is close, and then only the ring -- which is
    // wider than a crosshair and has nothing in the middle, so the thing being
    // lined up on is never covered.
    private void DrawAim(ulong owner, AimMark mark)
    {
        // Nothing to draw rather than something invisible: a marker switched
        // off should not still be costing entities, and the last rep of a
        // drill is meant to have none in it at all.
        if (mark.Visibility <= 0f)
        {
            return;
        }

        Vec3 right = Cross(mark.Forward, new Vec3(0, 0, 1));

        // Looking straight up or down leaves no horizon to take "right" from.
        if (right.Length() < 0.001f)
        {
            right = new Vec3(1, 0, 0);
        }

        right = Normalize(right);

        Vec3 up = Normalize(Cross(right, mark.Forward));
        Vec3 center = mark.Center;

        Vec3 Point(float x, float y) =>
            new Vec3(
                center.x + right.x * x + up.x * y,
                center.y + right.y * x + up.y * y,
                center.z + right.z * x + up.z * y
            );

        float Units(float degrees) => PracticeMarkerUtility.Subtend(mark.Away, degrees);

        float width = Units(PracticeMarkerUtility.LineDegrees);
        Color color = AimColor(mark);

        void Ring(float radius, int segments)
        {
            foreach (MarkerSegment segment in PracticeMarkerUtility.Ring(radius, segments))
            {
                Beam(
                    mark.Set,
                    owner,
                    Point(segment.Along1, segment.Across1),
                    Point(segment.Along2, segment.Across2),
                    color,
                    width * 0.6f
                );
            }
        }

        // The other throws off this spot: a plain ring each, so the choice is
        // visible. No number and no name -- text in the world is drawn by a
        // different entity that will not face the player, and the bar already
        // says which throw is in hand.
        if (!mark.Full)
        {
            Ring(Units(PracticeMarkerUtility.OtherRingDegrees), PracticeMarkerUtility.OtherRingSegments);

            return;
        }

        Ring(Units(PracticeMarkerUtility.RingDegrees), PracticeMarkerUtility.RingSegments);

        // A beam as long as it is wide is a square, which at this size is a
        // dot. The ring says roughly where; this is what the crosshair goes on.
        // A beam comes out twice as thick as the width it is given, so a dot
        // given its own length as width draws as two pixels stacked.
        float dot = Units(PracticeMarkerUtility.DotDegrees) / 2f;

        Beam(mark.Set, owner, Point(-dot, 0f), Point(dot, 0f), color, dot);

        if (mark.Stage == eAimStage.On)
        {
            return;
        }

        float from = Units(PracticeMarkerUtility.TickFromDegrees);
        float to = Units(PracticeMarkerUtility.TickToDegrees);

        foreach ((float x, float y) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
        {
            Beam(mark.Set, owner, Point(x * from, y * from), Point(x * to, y * to), color, width);
        }

        if (mark.Stage == eAimStage.Near)
        {
            return;
        }

        float edge = Units(PracticeMarkerUtility.FinderDegrees);
        float leg = Units(PracticeMarkerUtility.FinderLegDegrees);

        foreach (int horizontal in new[] { 1, -1 })
        {
            foreach (int vertical in new[] { 1, -1 })
            {
                float x = edge * horizontal;
                float y = edge * vertical;

                Beam(mark.Set, owner, Point(x, y), Point(x - (leg * horizontal), y), color, width);
                Beam(mark.Set, owner, Point(x, y), Point(x, y - (leg * vertical)), color, width);
            }
        }
    }

    // A throw made on the move starts somewhere and is let go somewhere else.
    // The footprints mark the first; this draws the way to the second.
    private void DrawLane(PlayerWorld world, ulong owner, LineupRecord lineup, Vec3 spot)
    {
        RunUp? runUp = PracticeMarkerUtility.RunUpOf(lineup.approach);

        if (runUp == null)
        {
            return;
        }

        Vec3 release = lineup.approach.OrderBy(sample => sample.t).Last().pos;
        float length = Flat(spot, release);
        List<MarkerSegment> lane = PracticeMarkerUtility.Lane(length);

        if (lane.Count == 0)
        {
            return;
        }

        float yaw = (float)(Math.Atan2(release.y - spot.y, release.x - spot.x) * 180.0 / Math.PI);
        float z = PadZ(spot);

        Segments(world.Focus, owner, spot, yaw, z, lane, Amber, PadWidth);
    }

    /// <summary>
    /// Called as the player moves, not as they walk onto a spot: the markers
    /// are already drawn, and all that changes is how right they are. The
    /// footprints go green inside the spot, and the aim marker sheds what it
    /// no longer needs as the crosshair closes in.
    /// </summary>
    public void TintAim(IPlayer player, float eyeYaw, float eyePitch, Vec3 feet, Vec3 eye)
    {
        if (!DrawMarkers)
        {
            return;
        }

        if (!_worlds.TryGetValue(player.SteamID, out PlayerWorld? world) || world.Hidden)
        {
            return;
        }

        foreach (Pad pad in world.Pads.Values)
        {
            foreach (Stand stand in pad.Stands)
            {
                bool on = Flat(stand.At, feet) <= PracticeLineupUtility.StanceToleranceUnits;

                if (on == stand.On)
                {
                    continue;
                }

                stand.On = on;
                Recolour(stand.Set.Beams, StandColor(stand));

                if (stand == pad.ArrowAt)
                {
                    Recolour(pad.Arrow.Beams, StandColor(stand));
                }
            }
        }

        float visibility = AimVisibility(player.SteamID);

        foreach (AimMark mark in world.Aims)
        {
            bool lined = mark.Lined;
            eAimStage stage = mark.Stage;

            if (mark.Full)
            {
                float error = AimErrorFrom(mark.Lineup, eye, eyeYaw, eyePitch);

                stage = PracticeMarkerUtility.AimStage(mark.Stage, error);
                lined = PracticeLineupUtility.AimMiss(error, mark.Lineup.aim_tolerance) <= 0f;
            }

            bool redraw = stage != mark.Stage || (visibility > 0f) != (mark.Visibility > 0f);
            bool recolour = lined != mark.Lined || Math.Abs(visibility - mark.Visibility) >= 0.01f;

            mark.Stage = stage;
            mark.Lined = lined;
            mark.Visibility = visibility;

            if (redraw)
            {
                Despawn(mark.Set);
                DrawAim(player.SteamID, mark);
            }
            else if (recolour)
            {
                Recolour(mark.Set.Beams, AimColor(mark));
            }
        }
    }

    // ── clearing ─────────────────────────────────────────────────────────

    private void DespawnWorld(PlayerWorld world)
    {
        ClearFocus(world);
        Despawn(world.Beacons);

        foreach (Pad pad in world.Pads.Values)
        {
            DespawnPad(pad);
        }

        world.Pads.Clear();
        world.Floors.Clear();
        world.Spots = new List<LineupSpot>();
        world.Signature = "";
    }

    /// <summary>
    /// Takes one player's markers out of the world and keeps them out until
    /// that player asks for something again. Nobody else's are touched.
    /// </summary>
    public void HideWorld(ulong steamId)
    {
        PlayerWorld world = WorldFor(steamId);

        DespawnWorld(world);
        world.Hidden = true;
    }

    public void Unhide(ulong steamId)
    {
        if (_worlds.TryGetValue(steamId, out PlayerWorld? world))
        {
            world.Hidden = false;
        }
    }

    // A player's markers are theirs: leaving them behind leaks the entities
    // and leaves standing transmit blocks pointing at indices the engine will
    // hand to something else.
    public void ForgetPlayer(ulong steamId)
    {
        if (!_worlds.Remove(steamId, out PlayerWorld? world))
        {
            return;
        }

        DespawnWorld(world);
    }

    private void ClearWorlds()
    {
        foreach (PlayerWorld world in _worlds.Values)
        {
            DespawnWorld(world);
        }
    }

    // The entities are already gone: swept by tag, or dead with the map.
    private void ForgetWorlds()
    {
        _worlds.Clear();
        _owners.Clear();
        _viewers.Clear();
        Forget(_spawnBoxes);

        if (!TransmitBlocking)
        {
            return;
        }

        try
        {
            _core.PlayerManager.ClearAllBlockedTransmitEntities();
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to clear transmit blocks");
        }
    }
}
