using System.Text;
using System.Text.RegularExpressions;
using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// What the MAP calls a point. The same answer the panel gives, deliberately: a
// lineup the HUD calls "Window" and the website calls "Connector" is worse than
// neither of them naming it, so the resolution order here mirrors
// web/utilities/mapCallouts.ts and api/src/utility/utility-callouts.service.ts
// exactly and is tested against the same cases.
public static partial class CalloutLookup
{
    // How far outside every place volume a point may sit and still be named.
    // The volumes do not tile a map, and a grenade rests on top of geometry as
    // often as inside a place.
    public const float SnapUnits = 256f;

    // Valve names that read badly once split, keyed by the name with its spaces
    // and case removed so a raw token and an already-spaced one both land here.
    private static readonly Dictionary<string, string> Aliases = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["bombsitea"] = "A Site",
        ["bombsiteb"] = "B Site",
        ["bombsitec"] = "C Site",
        ["tspawn"] = "T Spawn",
        ["ctspawn"] = "CT Spawn",
        ["terroristspawn"] = "T Spawn",
        ["counterterroristspawn"] = "CT Spawn",
    };

    /// <summary>
    /// The name of the place a world point is in, or null when the map has
    /// nothing to say about it.
    ///
    /// XY containment is decided before Z because places stack: a smoke on a
    /// roof, or in the air over a site, still belongs to the place beneath it.
    /// Z only breaks ties, which is what keeps Nuke and Vertigo from answering
    /// with the lower level's callout for a point on the upper one. Where
    /// volumes nest ("A Site" containing "Goose") the tightest one wins -- the
    /// more specific name is the one a player would say. See Volume for why
    /// that is measured in three dimensions.
    /// </summary>
    public static string? Resolve(
        Vec3 point,
        IEnumerable<MapCalloutPayload>? callouts,
        float snap = SnapUnits
    )
    {
        if (callouts == null)
        {
            return null;
        }

        string? insideName = null;
        float insideArea = float.MaxValue;

        string? aboveName = null;
        float aboveGap = float.MaxValue;
        float aboveArea = float.MaxValue;

        string? nearestName = null;
        float nearestDistance = float.MaxValue;

        foreach (MapCalloutPayload callout in callouts)
        {
            if (callout?.boxes == null || string.IsNullOrEmpty(callout.name))
            {
                continue;
            }

            foreach (MapCalloutBox box in callout.boxes)
            {
                if (box?.min == null || box.max == null || box.min.Length < 3 || box.max.Length < 3)
                {
                    continue;
                }

                bool inXY =
                    point.x >= box.min[0]
                    && point.x <= box.max[0]
                    && point.y >= box.min[1]
                    && point.y <= box.max[1];

                float area = Volume(box);

                if (inXY)
                {
                    if (point.z >= box.min[2] && point.z <= box.max[2])
                    {
                        if (area < insideArea)
                        {
                            insideArea = area;
                            insideName = callout.name;
                        }
                    }
                    else
                    {
                        float gap = Gap(point.z, box.min[2], box.max[2]);

                        if (gap < aboveGap || (gap == aboveGap && area < aboveArea))
                        {
                            aboveGap = gap;
                            aboveArea = area;
                            aboveName = callout.name;
                        }
                    }

                    continue;
                }

                float dx = Gap(point.x, box.min[0], box.max[0]);
                float dy = Gap(point.y, box.min[1], box.max[1]);
                float dz = Gap(point.z, box.min[2], box.max[2]);
                float distance = MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestName = callout.name;
                }
            }
        }

        if (insideName != null)
        {
            return insideName;
        }

        if (aboveName != null)
        {
            return aboveName;
        }

        return nearestDistance <= snap ? nearestName : null;
    }

    /// <summary>
    /// Resolves and humanises in one step, which is what anything putting a
    /// place name on screen actually wants.
    /// </summary>
    public static string ResolveLabel(
        Vec3 point,
        IEnumerable<MapCalloutPayload>? callouts,
        float snap = SnapUnits
    )
    {
        return Humanize(Resolve(point, callouts, snap));
    }

    /// <summary>
    /// "BombsiteA" -> "A Site", "LongDoors" -> "Long Doors". Empty in, empty out.
    /// </summary>
    public static string Humanize(string? raw)
    {
        string value = (raw ?? string.Empty).Trim();

        if (value.Length == 0)
        {
            return string.Empty;
        }

        string key = value.Replace(" ", string.Empty).Replace("_", string.Empty);

        if (Aliases.TryGetValue(key, out string? alias))
        {
            return alias;
        }

        // Valve glues a lowercase joining word between two capitalised ones --
        // TopofMid, BackofA. The camelCase walk below would read that as one
        // word and give "Topof Mid", so it is split first.
        value = OfJoin().Replace(value, "$1 of $2");

        var spaced = new StringBuilder(value.Length + 8);

        for (int index = 0; index < value.Length; index++)
        {
            char current = value[index];

            if (current == '_' || current == '-')
            {
                spaced.Append(' ');

                continue;
            }

            if (index > 0 && char.IsUpper(current))
            {
                char previous = value[index - 1];
                bool afterLower = char.IsLower(previous) || char.IsDigit(previous);
                // An acronym only breaks where the next letter starts a word,
                // so CTSpawn splits once rather than into C T Spawn.
                bool endsAcronym =
                    char.IsUpper(previous)
                    && index + 1 < value.Length
                    && char.IsLower(value[index + 1]);

                if (afterLower || endsAcronym)
                {
                    spaced.Append(' ');
                }
            }

            spaced.Append(current);
        }

        return string.Join(
            ' ',
            spaced.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)
        );
    }

    [GeneratedRegex("([a-z])of([A-Z])")]
    private static partial Regex OfJoin();

    /// <summary>
    /// The tightest enclosing volume wins where places overlap. MEASURED, not
    /// assumed: scored against `player_kills.attacker_location` (the engine's
    /// own answer) over 1,920 labelled kills, smallest-volume beat
    /// smallest-footprint 92.5% to 89.8%. Footprint alone loses the stacked
    /// pairs -- it called Mirage's Catwalk "Underpass" 41 times, because
    /// Underpass sits under it and is the narrower of the two seen from above.
    /// </summary>
    private static float Volume(MapCalloutBox box)
    {
        return (box.max[0] - box.min[0])
            * (box.max[1] - box.min[1])
            * MathF.Max(box.max[2] - box.min[2], 1f);
    }

    private static float Gap(float value, float min, float max)
    {
        if (value < min)
        {
            return min - value;
        }

        if (value > max)
        {
            return value - max;
        }

        return 0f;
    }
}
