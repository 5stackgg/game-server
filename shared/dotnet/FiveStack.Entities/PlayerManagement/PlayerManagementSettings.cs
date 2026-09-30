using System.Text.RegularExpressions;

namespace FiveStack.Entities.PlayerManagement;

public class PlayerManagementSettings
{
    public string API_DOMAIN { get; set; } = "https://api.5stack.gg";
    public string SERVER_ID { get; set; } = "";
    public string SERVER_API_PASSWORD { get; set; } = "";

    // A 5stack node hands every pod these three as env, so a node server needs
    // no config file at all; the file is for servers run outside a node.
    public PlayerManagementSettings Resolve(Func<string, string?> environment)
    {
        string apiDomain = Pick(environment("API_DOMAIN"), API_DOMAIN).TrimEnd('/');

        // A doubled scheme dials a host literally named "https" and dies quietly
        // on DNS, which is invisible from outside the server.
        apiDomain = Regex.Replace(apiDomain, "^(https?://)+", "$1");

        return new PlayerManagementSettings
        {
            API_DOMAIN = apiDomain,
            SERVER_ID = Pick(environment("SERVER_ID"), SERVER_ID).Trim(),
            SERVER_API_PASSWORD = Pick(environment("SERVER_API_PASSWORD"), SERVER_API_PASSWORD)
                .Trim(),
        };
    }

    public bool IsConnected()
    {
        return !string.IsNullOrEmpty(API_DOMAIN)
            && Guid.TryParse(SERVER_ID, out _)
            && !string.IsNullOrEmpty(SERVER_API_PASSWORD);
    }

    public string SyncUrl()
    {
        return $"{API_DOMAIN}/sanctions/server/{SERVER_ID}";
    }

    private static string Pick(string? preferred, string fallback)
    {
        return string.IsNullOrWhiteSpace(preferred) ? fallback ?? "" : preferred;
    }
}
