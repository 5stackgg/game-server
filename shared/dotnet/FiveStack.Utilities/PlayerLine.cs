namespace FiveStack.Utilities;

// A line players read in their own language: a translation key and its
// arguments, any of which can be a line of its own. English is the same line for
// the api and the logs, which read it in English whatever the server's language.
public record PlayerLine(string Key, object[] Args, string English)
{
    public static object MatchType(string type)
    {
        return type is "Competitive" or "Wingman" or "Duel" or "Rush"
            ? new PlayerLine($"rules.match_type.{type.ToLowerInvariant()}", [], type)
            : type;
    }
}
