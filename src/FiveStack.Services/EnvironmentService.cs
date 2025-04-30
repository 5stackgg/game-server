using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Config;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class EnvironmentService
{
    private readonly ILogger<EnvironmentService> _logger;

    public EnvironmentService(ILogger<EnvironmentService> logger)
    {
        _logger = logger;
    }

    public string GetApiUrl()
    {
        return Environment.GetEnvironmentVariable("API_DOMAIN") ?? GetConfig().API_DOMAIN;
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

    public bool AllowBots()
    {
        return Environment.GetEnvironmentVariable("ALLOW_BOTS") == "true";
    }

    public bool isOnGameServerNode()
    {
        return Environment.GetEnvironmentVariable("GAME_NODE_SERVER") == "true";
    }

    public string[] PossibleDirectories =
    [
        "/serverdata/serverfiles",
        $"{Server.GameDirectory}/csgo",
        Directory.GetCurrentDirectory(),
    ];

    public FiveStackConfig GetConfig()
    {
        return ConfigManager.Load<FiveStackConfig>("FiveStack");
    }

    public void Load()
    {
        string? filePath = null;

        foreach (var possibleDirectory in PossibleDirectories)
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
            var parts = line.Split('=', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                continue;
            }

            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}
