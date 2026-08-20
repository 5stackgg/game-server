using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

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

    // One beam per occupied voxel is thousands of entities for a single smoke.
    // The outline is contoured down to fit this, and a server full of people
    // previewing at once is capped again on top of it.
    private const int MaxBloomBeams = 48;
    private const int MaxBloomBeamsTotal = 240;

    private readonly UtilityConfig _config;
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

    public PracticeReplay(UtilityConfig config, ILogger<PracticeReplay> logger)
    {
        _config = config;
        _logger = logger;
    }

    // Wired by the plugin rather than injected: PracticeSystem already depends
    // on this service, so asking for it back would close the cycle.
    public Func<ulong, bool> WantsGhosts { get; set; } = _ => true;

    public void Load(CCSPlayerController player, LineupRecord lineup)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn.Value;

        if (pawn == null || !pawn.IsValid)
        {
            return;
        }

        Vec3 feet = lineup.release.feet_position;
        var position = new Vector(feet.x, feet.y, feet.z);
        var angles = new QAngle(lineup.release.pitch, lineup.release.yaw, 0);

        pawn.Teleport(position, angles, new Vector(0, 0, 0));

        // A single application is not enough: the client re-predicts from the
        // command it had in flight and snaps the view back.
        ReapplyAngles(player, angles, 2);

        GiveUtility(player, lineup.utility_type);

        player.PrintToCenter(Describe(lineup));
    }

    // Tier 1 preview: the recorded line, drawn as beam segments with a marker
    // where it lands.
    public void ShowGhost(CCSPlayerController player, LineupRecord lineup)
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

        GhostsChanged();
        _ghosts.Add(
            new Ghost
            {
                OwnerSteamId = player.SteamID,
                Kind = GhostKind.Line,
                ExpiresAt = DateTime.UtcNow.AddSeconds(GhostSeconds),
                Beams = beams,
            }
        );
    }

    // The measured bloom, outlined where it would actually sit. Answers how
    // many beams it took: zero when the panel has no measurement for this
    // lineup, which is a normal answer and not a failure.
    public int ShowBloom(CCSPlayerController player, LineupRecord lineup)
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
        GhostsChanged();
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

    public void ClearBloom(ulong steamId)
    {
        ClearKind(steamId, GhostKind.Bloom);
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
            GhostsChanged();
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
            GhostsChanged();
        }
    }

    private int BloomBeamCount()
    {
        return _ghosts
            .Where(ghost => ghost.Kind == GhostKind.Bloom)
            .Sum(ghost => ghost.Beams.Count);
    }

    public void ClearAll()
    {
        foreach (Ghost ghost in _ghosts)
        {
            Kill(ghost);
        }

        _ghosts.Clear();
        GhostsChanged();
    }

    public void Sweep()
    {
        DateTime now = DateTime.UtcNow;

        for (int index = _ghosts.Count - 1; index >= 0; index--)
        {
            if (_ghosts[index].ExpiresAt > now)
            {
                continue;
            }

            Kill(_ghosts[index]);
            _ghosts.RemoveAt(index);
            GhostsChanged();
        }
    }

    public bool HasGhosts => _ghosts.Count > 0;

    // Beams are the only entities this plugin spawns, so a transmit filter
    // built from this list can never hide anything else by accident.
    //
    // Cached, because the caller is CheckTransmit and that runs every frame:
    // rebuilding this per frame allocates a list per frame forever. Invalidated
    // by every path that adds or removes a ghost.
    private (uint index, ulong owner)[]? _ghostIndexes;

    public IReadOnlyList<(uint index, ulong owner)> GhostEntities()
    {
        if (_ghostIndexes != null)
        {
            return _ghostIndexes;
        }

        var indexes = new List<(uint index, ulong owner)>();

        foreach (Ghost ghost in _ghosts)
        {
            foreach (CEnvBeam beam in ghost.Beams)
            {
                if (beam.IsValid)
                {
                    indexes.Add((beam.Index, ghost.OwnerSteamId));
                }
            }
        }

        _ghostIndexes = indexes.ToArray();
        return _ghostIndexes;
    }

    // Every mutation of _ghosts goes through here, so the cache cannot outlive
    // the set it describes.
    private void GhostsChanged()
    {
        _ghostIndexes = null;
    }

    public static string Describe(LineupRecord lineup)
    {
        string name = string.IsNullOrEmpty(lineup.name) ? "unnamed" : lineup.name;
        string strength = string.IsNullOrEmpty(lineup.strength) ? "" : $" / {lineup.strength}";

        return $"{name}\n{lineup.utility_type} - {lineup.technique}{strength}";
    }

    private void GiveUtility(CCSPlayerController player, string utilityType)
    {
        string? weapon = PracticeLineupUtility.WeaponForUtilityType(utilityType);

        if (weapon == null)
        {
            return;
        }

        if (!HasWeapon(player, weapon))
        {
            player.GiveNamedItem(weapon);
        }

        // There is no server-side "select this weapon" in CounterStrikeSharp,
        // so the switch goes through the client.
        player.ExecuteClientCommand($"use {weapon}");
    }

    private static bool HasWeapon(CCSPlayerController player, string designerName)
    {
        CPlayer_WeaponServices? weapons = player.PlayerPawn.Value?.WeaponServices;

        if (weapons == null)
        {
            return false;
        }

        foreach (var handle in weapons.MyWeapons)
        {
            if (handle.Value?.DesignerName == designerName)
            {
                return true;
            }
        }

        return false;
    }

    private static void ReapplyAngles(CCSPlayerController player, QAngle angles, int frames)
    {
        if (frames <= 0)
        {
            return;
        }

        Server.NextFrame(() =>
        {
            CCSPlayerPawn? pawn = player.IsValid ? player.PlayerPawn.Value : null;

            if (pawn == null || !pawn.IsValid)
            {
                return;
            }

            pawn.Teleport(null, angles, new Vector(0, 0, 0));
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

        int stride = Math.Max(1, (int)Math.Ceiling(lineup.trajectory.Count / (double)MaxGhostSegments));

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
            CEnvBeam? beam = Utilities.CreateEntityByName<CEnvBeam>("env_beam");

            if (beam == null || !beam.IsValid)
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

            beam.EndPos.X = end.x;
            beam.EndPos.Y = end.y;
            beam.EndPos.Z = end.z;
            Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");

            beam.DispatchSpawn();

            return beam;
        }
        catch (Exception error)
        {
            _logger.LogError(error, "unable to draw a lineup preview");
            return null;
        }
    }

    private static void Kill(Ghost ghost)
    {
        foreach (CEnvBeam beam in ghost.Beams)
        {
            if (beam.IsValid)
            {
                beam.Remove();
            }
        }
    }

    private static Color ColorFor(string utilityType)
    {
        switch (utilityType)
        {
            case "Smoke":
                return Color.FromArgb(255, 220, 220, 220);
            case "Flash":
                return Color.FromArgb(255, 120, 180, 255);
            case "HighExplosive":
                return Color.FromArgb(255, 255, 90, 90);
            case "Molotov":
                return Color.FromArgb(255, 255, 150, 40);
            default:
                return Color.FromArgb(255, 200, 120, 255);
        }
    }
}
