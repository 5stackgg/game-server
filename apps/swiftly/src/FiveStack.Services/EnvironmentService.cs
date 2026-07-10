using FiveStack.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SwiftlyS2.Shared;

namespace FiveStack;

public class EnvironmentService
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger<EnvironmentService> _logger;
    private readonly IOptions<FiveStackConfig> _config;

    public EnvironmentService(
        ISwiftlyCore core,
        ILogger<EnvironmentService> logger,
        IOptions<FiveStackConfig> config
    )
    {
        _core = core;
        _logger = logger;
        _config = config;
    }

    public string GetApiUrl()
    {
        return Environment.GetEnvironmentVariable("API_DOMAIN") ?? GetConfig().API_DOMAIN;
    }

    public string GetRelayUrl()
    {
        return Environment.GetEnvironmentVariable("RELAY_DOMAIN") ?? GetConfig().RELAY_DOMAIN;
    }

    public string GetDemosUrl()
    {
        return Environment.GetEnvironmentVariable("DEMOS_DOMAIN") ?? GetConfig().DEMOS_DOMAIN;
    }

    public string GetWsUrl()
    {
        return Environment.GetEnvironmentVariable("WS_DOMAIN") ?? GetConfig().WS_DOMAIN;
    }

    public string? GetServerId()
    {
        return Environment.GetEnvironmentVariable("SERVER_ID") ?? GetConfig().SERVER_ID;
    }

    public string? GetServerApiPassword()
    {
        return Environment.GetEnvironmentVariable("SERVER_API_PASSWORD")
            ?? GetConfig().SERVER_API_PASSWORD;
    }

    public string GetPublicChatTrigger()
    {
        return Environment.GetEnvironmentVariable("PUBLIC_CHAT_TRIGGER")
            ?? GetConfig().PUBLIC_CHAT_TRIGGER;
    }

    public string GetSilentChatTrigger()
    {
        return Environment.GetEnvironmentVariable("SILENT_CHAT_TRIGGER")
            ?? GetConfig().SILENT_CHAT_TRIGGER;
    }

    public bool AllowBots()
    {
        return Environment.GetEnvironmentVariable("ALLOW_BOTS") == "true";
    }

    public bool isOnGameServerNode()
    {
        return Environment.GetEnvironmentVariable("GAME_NODE_SERVER") == "true";
    }

    public bool IsOfflineMode()
    {
        return Environment.GetEnvironmentVariable("OFFLINE_MODE") == "true";
    }

    public string? GetOfflineMatchData()
    {
        return Environment.GetEnvironmentVariable("GAME_SERVER_OFFLINE_MATCH_DATA");
    }

    public string[] PossibleDirectories()
    {
        return
        [
            "/serverdata/serverfiles",
            $"{_core.GameDirectory}/csgo",
            Directory.GetCurrentDirectory(),
        ];
    }

    public FiveStackConfig GetConfig()
    {
        return _config.Value;
    }

    public void Load()
    {
        string? filePath = null;

        foreach (var possibleDirectory in PossibleDirectories())
        {
            var testPath = Path.Combine(possibleDirectory, ".env");
            if (File.Exists(testPath))
            {
                filePath = testPath;
                break;
            }
        }

        if (filePath == null)
        {
            return;
        }

        foreach (var line in File.ReadAllLines(filePath))
        {
            // Skip blanks and comments.
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
            {
                continue;
            }

            // Split on the FIRST '=' only. The previous Split('=', RemoveEmptyEntries)
            // dropped any value that itself contained '=' (base64 secrets, tokens,
            // connection strings all do) and any empty value, silently losing them.
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line.Substring(0, separatorIndex).Trim();
            var value = line.Substring(separatorIndex + 1);

            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
