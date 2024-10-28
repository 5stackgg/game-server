using System.Text.Json;
using CounterStrikeSharp.API;
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
        return Environment.GetEnvironmentVariable("API_DOMAIN") ?? "https://api.5stack.gg";
    }

    public string GetDemosUrl()
    {
        return Environment.GetEnvironmentVariable("DEMOS_DOMAIN") ?? "https://demos.5stack.gg";
    }

    public string GetWsUrl()
    {
        return Environment.GetEnvironmentVariable("WS_DOMAIN") ?? "wss://ws.5stack.gg";
    }

    public string? GetServerId()
    {
        return Environment.GetEnvironmentVariable("SERVER_ID");
    }

    public string? GetServerApiPassword()
    {
        return Environment.GetEnvironmentVariable("SERVER_API_PASSWORD");
    }

    public bool AllowBots()
    {
        return Environment.GetEnvironmentVariable("ALLOW_BOTS") == "true";
    }

    public string[] PossibleDirectories =
    [
        "/serverdata/serverfiles",
        $"{Server.GameDirectory}/csgo",
        Directory.GetCurrentDirectory(),
    ];

    public void Load()
    {
        bool isJson = false;
        string? filePath = null;

        foreach (var possibleDirectory in PossibleDirectories)
        {
            var testPath = Path.Combine(possibleDirectory, ".env");
            _logger.LogInformation($"Checking for .env file in {testPath}");
            if (File.Exists(testPath))
            {
                filePath = testPath;
                break;
            }

            var testPathJson = Path.Combine(possibleDirectory, "5stack.json");
            if (File.Exists(testPathJson))
            {
                isJson = true;
                filePath = testPath;
                break;
            }
        }

        if (filePath == null)
        {
            _logger.LogWarning("Unable to find .env file");
            return;
        }

        if (isJson)
        {
            try
            {
                var jsonString = File.ReadAllText(filePath);
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);

                if (config != null)
                {
                    foreach (var kvp in config)
                    {
                        Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogError($"Error parsing JSON config: {ex.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error reading config file: {ex.Message}");
            }
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
