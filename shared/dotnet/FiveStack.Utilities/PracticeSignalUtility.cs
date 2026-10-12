using System.Globalization;
using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// What the plugin says to a machine.
//
// Everything else the practice plugin prints is for a person and can be
// reworded. The detonation line cannot: an external clip recorder greps the
// server console for it, so changing its shape breaks a regex in another repo.
public static class PracticeSignalUtility
{
    public const string Prefix = "[utility-practice]";

    // A grenade the plugin emitted has gone off. Deliberately not raised for a
    // grenade a player threw: that one is observable from the demo, from the
    // game events, and from watching the server. A plugin-emitted one is the
    // case nothing outside the plugin can see.
    public const string GhostDetonated = "ghost_detonated";

    private const string Absent = "-";

    // <prefix> ghost_detonated utility=<type> lineup=<client_id> lineup_id=<panel id>
    //          steam=<steamid64> x=<f> y=<f> z=<f>
    //
    // One line, no colour, no punctuation inside a value, invariant decimals,
    // and every field always present with "-" standing in for an absent one, so
    // a reader can key on names rather than positions.
    public static string GhostDetonatedLine(
        string utilityType,
        Vec3 at,
        string? clientId,
        string? lineupId,
        ulong steamId
    )
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{Prefix} {GhostDetonated} utility={Text(utilityType)} lineup={Text(clientId)} lineup_id={Text(lineupId)} steam={steamId} x={at.x:0.00} y={at.y:0.00} z={at.z:0.00}"
        );
    }

    // A value with a space in it would break the key=value reading, and every
    // field that can carry one is an identifier that never does. Replacing
    // rather than quoting keeps the line trivially splittable.
    private static string Text(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Absent : value.Trim().Replace(' ', '_');
    }
}
