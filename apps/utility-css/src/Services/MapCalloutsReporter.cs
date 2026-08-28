using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace UtilityPractice;

// The map's own names for its areas, read off the entities the engine resolves
// them from. This is what fills `player_kills.attacker_location` in a match, and
// with the boxes attached the panel can draw them on the radar and name a throw
// by where it lands and where it was thrown from.
//
// Only ever a fallback. The published extract covers the official pool and wins
// on the API side; this is how a workshop map -- which nothing offline has ever
// opened -- gets any callouts at all.
public class MapCalloutsReporter
{
    // A place volume thinner than this on either horizontal axis is a trigger
    // somebody tied to the class by accident, not an area anyone calls out.
    private const float MinExtent = 8f;

    private readonly UtilityApiClient _api;
    private readonly ILogger<MapCalloutsReporter> _logger;

    private string _reported = string.Empty;
    private List<MapCalloutPayload> _cached = new List<MapCalloutPayload>();

    public MapCalloutsReporter(UtilityApiClient api, ILogger<MapCalloutsReporter> logger)
    {
        _api = api;
        _logger = logger;
    }

    public void Reset()
    {
        _reported = string.Empty;
        _cached = new List<MapCalloutPayload>();
    }

    /// <summary>
    /// The level's callouts, walked once per map and held. Anything naming a
    /// point on screen reads this rather than calling <see cref="Collect"/> --
    /// resolving a marker is a per-frame question and walking the entity list
    /// is not a per-frame answer.
    /// </summary>
    public IReadOnlyList<MapCalloutPayload> Callouts => _cached;

    /// <summary>
    /// What the map calls this point, already humanised. Empty when the map has
    /// no callouts or nothing is near enough to name.
    /// </summary>
    public string Label(Vec3 point)
    {
        return CalloutLookup.ResolveLabel(point, _cached);
    }

    /// <summary>
    /// Reads every env_cs_place in the level. Safe to call from a map-load
    /// listener; it only touches entities the game already made.
    /// </summary>
    public List<MapCalloutPayload> Collect()
    {
        var byName = new Dictionary<string, MapCalloutPayload>(StringComparer.OrdinalIgnoreCase);

        foreach (
            CBaseEntity entity in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(
                "env_cs_place"
            )
        )
        {
            if (!entity.IsValid)
            {
                continue;
            }

            string name;

            try
            {
                // CCSPlace carries exactly one field, m_name, and it is the
                // string the HUD shows. Read through the schema rather than a
                // generated wrapper so a CounterStrikeSharp version that has
                // not generated the class still compiles.
                name = Schema.GetString(entity.Handle, "CCSPlace", "m_name") ?? string.Empty;
            }
            catch (Exception error)
            {
                _logger.LogWarning(error, "unable to read a place name");
                continue;
            }

            name = name.Trim();

            if (name.Length == 0)
            {
                continue;
            }

            // The volume is a model, so its extents are relative to the entity
            // and have to be lifted into world space before anything can ask
            // whether a grenade is inside one.
            var origin = entity.AbsOrigin;
            var mins = entity.Collision?.Mins;
            var maxs = entity.Collision?.Maxs;

            if (origin == null || mins == null || maxs == null)
            {
                continue;
            }

            var box = new MapCalloutBox
            {
                min = new[] { mins.X + origin.X, mins.Y + origin.Y, mins.Z + origin.Z },
                max = new[] { maxs.X + origin.X, maxs.Y + origin.Y, maxs.Z + origin.Z },
            };

            if (
                box.max[0] - box.min[0] < MinExtent
                || box.max[1] - box.min[1] < MinExtent
            )
            {
                continue;
            }

            if (!byName.TryGetValue(name, out MapCalloutPayload? callout))
            {
                callout = new MapCalloutPayload { name = name };
                byName[name] = callout;
            }

            callout.boxes.Add(box);
        }

        return byName.Values.ToList();
    }

    /// <summary>
    /// Reports the level's callouts once per map. Nothing is retried: the next
    /// map load reports again, and a level nobody opens again needs nothing.
    /// </summary>
    public void Report(string mapName)
    {
        if (string.IsNullOrEmpty(mapName) || _reported == mapName)
        {
            return;
        }

        List<MapCalloutPayload> callouts;

        try
        {
            callouts = Collect();
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to collect the map's callouts");
            return;
        }

        if (callouts.Count == 0)
        {
            return;
        }

        _cached = callouts;
        _reported = mapName;
        _ = Task.Run(async () => await _api.Callouts(mapName, callouts));
    }
}
