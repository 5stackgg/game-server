using System.Text.Json;

namespace FiveStack.Utilities
{
    public static class CommandUtility
    {
        public static string PublicChatTrigger = ".";
        public static string SilentChatTrigger = "/";

        public static void Initialize(string publicChatTrigger, string silentChatTrigger)
        {
            PublicChatTrigger = publicChatTrigger;
            SilentChatTrigger = silentChatTrigger;
        }

        // SwiftlyS2 answers its built-in default when the key is missing or core.jsonc fails to parse.
        public static string ResolveTrigger(
            string configured,
            string? coreConfig,
            string key,
            string runtimeDefault
        )
        {
            if (coreConfig == null)
            {
                return configured;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(
                    coreConfig,
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }
                );

                if (
                    document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty(key, out JsonElement prefixes)
                    || prefixes.ValueKind != JsonValueKind.Array
                )
                {
                    return runtimeDefault;
                }

                List<string> listed = prefixes
                    .EnumerateArray()
                    .Where(prefix => prefix.ValueKind == JsonValueKind.String)
                    .Select(prefix => prefix.GetString()!)
                    .ToList();

                if (listed.Contains(configured))
                {
                    return configured;
                }

                return listed.FirstOrDefault() ?? runtimeDefault;
            }
            catch (JsonException)
            {
                return runtimeDefault;
            }
        }
    }
}
