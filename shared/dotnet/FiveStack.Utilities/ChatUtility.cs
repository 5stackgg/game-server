using System.Text;
using System.Text.RegularExpressions;

namespace FiveStack.Utilities
{
    public static class ChatUtility
    {
        public const string OrganizerTag = "[organizer]";

        public const string OrganizerFlag = "1";

        // SwiftlyS2 turns these into colour bytes, case-insensitively, and splits
        // a chat line on [newline]. On either runtime the engine reads bytes
        // below 0x20 as colours.
        private static readonly Regex FormattingTags = new(
            @"\[(?:default|/|white|darkred|lightpurple|green|olive|lime|red|gray|grey|lightyellow|yellow|silver|bluegrey|lightblue|blue|darkblue|purple|magenta|lightred|gold|orange|teamcolor|newline)\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );

        // Only the flag earns the organizer tag, never the line: the line starts
        // with the sender's own name. The flag is the last token because SwiftlyS2
        // also splits on U+200B, so a chatter can push tokens out of the quoted
        // line, just never past the api's own last one. The api still prefixes
        // flagged lines with the tag for plugins that predate the flag.
        public static (string Text, bool Organizer) ParseWebChat(IReadOnlyList<string> args)
        {
            string line = args[0];
            bool organizer = args.Count > 1 && args[^1] == OrganizerFlag;

            if (organizer && line.StartsWith(OrganizerTag))
            {
                line = line[OrganizerTag.Length..];
            }

            return (StripFormatting(line).Trim(), organizer);
        }

        public static string StripFormatting(string text)
        {
            StringBuilder plain = new(text.Length);

            foreach (char character in text)
            {
                if (!char.IsControl(character))
                {
                    plain.Append(character);
                }
            }

            string stripped = plain.ToString();
            string previous;

            do
            {
                previous = stripped;
                stripped = FormattingTags.Replace(stripped, "");
            } while (stripped != previous);

            return stripped;
        }
    }
}
