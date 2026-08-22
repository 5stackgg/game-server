using System.Globalization;
using FiveStack.Entities.Practice;

namespace FiveStack.Utilities;

// What the plugin says to a machine.
//
// Everything else the practice plugin prints is for a person and can be
// reworded. These two things cannot: an external clip recorder greps the server
// console for the detonation line and issues the toggle below to get itself out
// of frame. Both are contracts. Changing the shape of the line breaks a regex
// in another repo, and reading "off" as "toggle" turns an idempotent command
// into a coin flip.
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

    // "on" / "off" set; anything empty toggles. An external caller must be able
    // to say what it wants rather than ask for the opposite of a state it
    // cannot see.
    public static bool TryParseToggle(string? argument, bool current, out bool value)
    {
        string trimmed = (argument ?? "").Trim().Trim('"');

        if (trimmed.Length == 0)
        {
            value = !current;
            return true;
        }

        switch (trimmed.ToLowerInvariant())
        {
            case "on":
            case "1":
            case "true":
            case "yes":
                value = true;
                return true;
            case "off":
            case "0":
            case "false":
            case "no":
                value = false;
                return true;
            default:
                value = current;
                return false;
        }
    }

    // A value with a space in it would break the key=value reading, and every
    // field that can carry one is an identifier that never does. Replacing
    // rather than quoting keeps the line trivially splittable.
    private static string Text(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? Absent : value.Trim().Replace(' ', '_');
    }
}
