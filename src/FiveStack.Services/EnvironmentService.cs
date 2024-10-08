using Microsoft.Extensions.Logging;

namespace FiveStack;

public class EnvironmentService
{
    private readonly ILogger<EnvironmentService> _logger;

    public EnvironmentService(ILogger<EnvironmentService> logger)
    {
        _logger = logger;
    }

    public string GetBaseApiUri()
    {
        return "https://" + (Environment.GetEnvironmentVariable("API_DOMAIN") ?? "api.5stack.gg");
    }

    public string GetDemosBaseUri()
    {
        return "https://"
            + (Environment.GetEnvironmentVariable("DEMOS_DOMAIN") ?? "demos.5stack.gg");
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
        string filePath = "/serverdata/serverfiles/.env";

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

            Environment.SetEnvironmentVariable(parts[0], parts[1]);
        }
    }
}
