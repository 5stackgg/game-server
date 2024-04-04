using System.Text.Json;
using CounterStrikeSharp.API;
using FiveStack.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchService
{
    private MatchManager? _currentMatch;
    private readonly ILogger<MatchService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly EnvironmentService _environmentService;

    public MatchService(
        ILogger<MatchService> logger,
        IServiceProvider serviceProvider,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _environmentService = environmentService;
    }

    public async void GetMatchFromApi()
    {
        HttpClient httpClient = new HttpClient();

        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        _logger.LogInformation($"Server ID: {serverId}");

        if (serverId == null || apiPassword == null)
        {
            _logger.LogWarning("Missing Server ID / API Password");
            await Task.Delay(1000 * 5);
            Server.NextFrame(() =>
            {
                GetMatchFromApi();
            });

            return;
        }

        try
        {
            string matchUri = $"{_environmentService.GetBaseUri()}/server/{serverId}/current-match";

            _logger.LogInformation($"Fetching Match Info: {matchUri}");

            httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiPassword);

            string? response = await httpClient.GetStringAsync(matchUri);

            Server.NextFrame(() =>
            {
                if (response.Length == 0)
                {
                    _logger.LogWarning("currenlty no match assigned to server");
                    return;
                }

                FiveStackMatch? matchData = JsonSerializer.Deserialize<FiveStackMatch>(response);

                if (matchData == null)
                {
                    return;
                }

                if (_currentMatch?.GetMatchData()?.id == matchData.id)
                {
                    return;
                }

                _currentMatch =
                    _serviceProvider.GetRequiredService(typeof(MatchManager)) as MatchManager;
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogInformation($"HTTP request error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogInformation($"JSON deserialization error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogInformation($"An unexpected error occurred: {ex.Message}");
        }
    }

    public MatchManager? GetCurrentMatch()
    {
        return _currentMatch;
    }
}
