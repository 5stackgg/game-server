namespace FiveStack.Utilities;

// The panel serves its api at api.<web domain>, so the scoreboard's website
// button follows the panel the server belongs to.
public static class ServerWebsiteUtility
{
    public const string Default = "https://5stack.gg";

    public static string FromApiDomain(string? apiDomain)
    {
        if (
            !Uri.TryCreate(apiDomain, UriKind.Absolute, out Uri? api)
            || api.Scheme != Uri.UriSchemeHttps
        )
        {
            return Default;
        }

        string host = api.IdnHost;
        if (!host.StartsWith("api.", StringComparison.OrdinalIgnoreCase) || host.Length <= 4)
        {
            return Default;
        }

        string port = api.IsDefaultPort ? "" : $":{api.Port}";
        return $"https://{host[4..]}{port}";
    }
}
