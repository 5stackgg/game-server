using Microsoft.Extensions.Logging;

namespace FiveStack;

public class EnvironmentService
{
    private readonly ILogger<EnvironmentService> _logger;

    public EnvironmentService(ILogger<EnvironmentService> logger)
    {
        _logger = logger;

        if (bool.TryParse(Environment.GetEnvironmentVariable("DEV_SERVER"), out var isDev) && isDev)
        {
            Load();
        }
    }

    public string GetBaseUri()
    {
        // TODO - env variable
        return "https://api.5stack.gg";
    }

    public string? GetServerId()
    {
        return Environment.GetEnvironmentVariable("SERVER_ID");
    }

    public string? GetServerApiPassword()
    {
        return Environment.GetEnvironmentVariable("SERVER_API_PASSWORD");
    }

    private void Load()
    {
        string filePath = "/serverdata/serverfiles/.env";

        _logger.LogInformation($"LOAD FILE PATH {filePath}");
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Unable to read .env file");
            return;
        }

        foreach (var line in File.ReadAllLines(filePath))
        {
            var parts = line.Split('=', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 2)
            {
                continue;
            }

            _logger.LogTrace($"VARIABLE {parts[0]}:{parts[1]}");

            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}
