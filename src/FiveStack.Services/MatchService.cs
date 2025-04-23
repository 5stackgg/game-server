using System.Text.Json;
using CounterStrikeSharp.API;
using FiveStack.Entities;
using FiveStack.Enums;
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

    public async Task GetMatchConfigs()
    {
        string directoryPath = Path.Join(Server.GameDirectory + "/csgo/cfg");
        foreach (
            var file in new[]
            {
                "5stack.base.cfg",
                "5stack.knife.cfg",
                "5stack.live.cfg",
                "5stack.warmup.cfg",
                "5stack.duel.cfg",
                "5stack.wingman.cfg",
                "5stack.competitive.cfg",
            }
        )
        {
            if (!File.Exists(Path.Join(directoryPath, file)))
            {
                _logger.LogInformation($"Downloading Config: {file}");
                string url =
                    $"https://raw.githubusercontent.com/5stackgg/game-server/main/cfg/{file}";
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(url);
                    var content = await response.Content.ReadAsStringAsync();
                    File.WriteAllText(Path.Join(directoryPath, file), content);
                }
            }
        }
    }

    public async void GetMatchFromApi(eMapStatus? forceState = null)
    {
        HttpClient httpClient = new HttpClient();

        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        if (serverId == null || apiPassword == null)
        {
            await Task.Delay(1000 * 5);
            Server.NextFrame(() =>
            {
                GetMatchFromApi();
            });

            return;
        }

        try
        {
            string matchUri = $"{_environmentService.GetApiUrl()}/matches/current-match/{serverId}";

            _logger.LogInformation($"Fetching Match Info {matchUri}");

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

                MatchData? matchData = JsonSerializer.Deserialize<MatchData>(response);

                if (matchData == null)
                {
                    return;
                }

                if (_currentMatch?.GetMatchData()?.id == matchData.id)
                {
                    _currentMatch.SetupMatch(matchData, forceState);
                    return;
                }

                _currentMatch =
                    _serviceProvider.GetRequiredService(typeof(MatchManager)) as MatchManager;

                _currentMatch!.SetupMatch(matchData, forceState);
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
