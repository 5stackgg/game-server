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

    public void Load()
    {
        string currentDirectory = Directory.GetCurrentDirectory();
        _logger.LogInformation($"Current directory: {currentDirectory}");

        string filePath = Path.Combine(currentDirectory, ".env");
        if (!File.Exists(filePath))
        {
            filePath = "/serverdata/serverfiles/.env";
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Unable to read .env file");
                return;
            }   
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
