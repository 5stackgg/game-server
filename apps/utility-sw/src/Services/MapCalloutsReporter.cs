using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.SchemaDefinitions;

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

    private const string PlaceClass = "env_cs_place";

    private readonly ISwiftlyCore _core;
    private readonly UtilityApiClient _api;
    private readonly ILogger<MapCalloutsReporter> _logger;

    // Report is asked on the per-second tick, never from the map-load handler:
    // MapCalloutsWalk says when a walk is the map's answer, and it is asked again
    // until one is -- otherwise the map stays unnamed for the whole session,
    // silently: no HUD kicker, no auto-named save, and nothing ever POSTed for the
    // workshop maps this endpoint exists for.
    private readonly MapCalloutsWalk _walk = new MapCalloutsWalk();
    private List<MapCalloutPayload> _cached = new List<MapCalloutPayload>();

    public MapCalloutsReporter(
        ISwiftlyCore core,
        UtilityApiClient api,
        ILogger<MapCalloutsReporter> logger
    )
    {
        _core = core;
        _api = api;
        _logger = logger;
    }

    public void MapLoaded()
    {
        _walk.MapLoaded();
        _cached = new List<MapCalloutPayload>();
    }

    public void OnSpawned(CEntityInstance entity)
    {
        if (entity.DesignerName == PlaceClass)
        {
            _walk.Spawned(entity.Index);
        }
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
    /// Reads the level's env_cs_place entities -- after a map load, only the
    /// ones that spawned since.
    /// </summary>
    public List<MapCalloutPayload> Collect()
    {
        var byName = new Dictionary<string, MapCalloutPayload>(StringComparer.OrdinalIgnoreCase);

        foreach (
            CCSPlace place in _core.EntitySystem.GetAllEntitiesByDesignerName<CCSPlace>(
                PlaceClass
            )
        )
        {
            if (!place.IsValid || !_walk.Counts(place.Index))
            {
                continue;
            }

            string name = (place.Name ?? string.Empty).Trim();

            if (name.Length == 0)
            {
                continue;
            }

            // The volume is a model, so its extents are relative to the entity
            // and have to be lifted into world space before anything can ask
            // whether a grenade is inside one.
            // AbsOrigin is a nullable VALUE type here, not a reference: a null
            // check narrows nothing, the value has to be taken out.
            if (place.AbsOrigin is not { } origin)
            {
                continue;
            }

            var mins = place.Collision.Mins;
            var maxs = place.Collision.Maxs;

            var box = new MapCalloutBox
            {
                min = new[] { mins.X + origin.X, mins.Y + origin.Y, mins.Z + origin.Z },
                max = new[] { maxs.X + origin.X, maxs.Y + origin.Y, maxs.Z + origin.Z },
            };

            if (box.max[0] - box.min[0] < MinExtent || box.max[1] - box.min[1] < MinExtent)
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
    /// Reports the level's callouts once per map. Safe to call every tick: it
    /// stops the moment it has an answer, and gives up after
    /// MapCalloutsWalk.MaxAttempts so a map that genuinely has no places is not
    /// walked for ever.
    /// </summary>
    public void Report(string? mapName)
    {
        if (!_walk.Begin(mapName))
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
        _walk.Answered(mapName);
        _logger.LogInformation("reporting {count} callout(s) for {map}", callouts.Count, mapName);
        _ = Task.Run(async () => await _api.Callouts(mapName, callouts));
    }
}
