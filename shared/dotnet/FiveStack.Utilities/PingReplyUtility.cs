using System.Text.Json;

namespace FiveStack.Utilities;

// A hibernating server does not answer RCON, so the panel cannot push
// `get_match` to it. It answers the server's own ping instead.
public static class PingReplyUtility
{
    public static bool WantsMatch(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        try
        {
            using JsonDocument reply = JsonDocument.Parse(body);

            return reply.RootElement.ValueKind == JsonValueKind.Object
                && reply.RootElement.TryGetProperty("get_match", out JsonElement wants)
                && wants.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
