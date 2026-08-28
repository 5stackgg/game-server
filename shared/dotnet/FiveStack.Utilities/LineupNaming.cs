using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// What the map itself would call a throw: "Window Smoke from T Spawn".
//
// A mirror of UtilityCalloutsService.autoName in the api, deliberately, for the
// same reason CalloutLookup is duplicated rather than fetched -- a name the HUD
// shows the moment you save and the name the website shows for the same throw
// have to be the same string. Any change here belongs in both.
public static class LineupNaming
{
    // The api's TYPE_LABELS. Only HighExplosive differs from its enum name.
    public static string TypeLabel(string? utilityType)
    {
        if (
            string.Equals(
                utilityType,
                nameof(eUtilityType.HighExplosive),
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return "HE";
        }

        return string.IsNullOrWhiteSpace(utilityType) ? "" : utilityType!;
    }

    /// <summary>
    /// Empty when the map has no callouts near either end, which is what keeps
    /// the caller's own fallback in play rather than replacing it with a name
    /// that says nothing.
    /// </summary>
    public static string Auto(
        string? utilityType,
        Vec3 origin,
        Vec3 landing,
        IReadOnlyList<MapCalloutPayload>? callouts
    )
    {
        if (callouts == null || callouts.Count == 0)
        {
            return "";
        }

        string from = CalloutLookup.ResolveLabel(origin, callouts);
        string to = CalloutLookup.ResolveLabel(landing, callouts);
        string type = TypeLabel(utilityType);

        if (to.Length > 0 && from.Length > 0)
        {
            // Thrown from the place it lands in: "from Window" onto Window says
            // nothing, so it collapses to one name.
            return to == from ? $"{to} {type}" : $"{to} {type} from {from}";
        }

        if (to.Length > 0)
        {
            return $"{to} {type}";
        }

        if (from.Length > 0)
        {
            return $"{type} from {from}";
        }

        return "";
    }
}
