using System.Text.Json;
using FiveStack.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace FiveStack;

public class MatchService
{
    private MatchManager? _currentMatch;
    private readonly ISwiftlyCore _core;
    private readonly ILogger<MatchService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly EnvironmentService _environmentService;

    public MatchService(
        ISwiftlyCore core,
        ILogger<MatchService> logger,
        IServiceProvider serviceProvider,
        EnvironmentService environmentService
    )
    {
        _core = core;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _environmentService = environmentService;
    }

    public async Task GetMatchConfigs()
    {
        string directoryPath = Path.Join(_core.GameDirectory + "/csgo/cfg");
        foreach (
            var file in new[]
            {
                "5stack.competitive.cfg",
                "5stack.duel.cfg",
                "5stack.knife.cfg",
                "5stack.lan.cfg",
                "5stack.warmup.cfg",
                "5stack.wingman.cfg",
            }
        )
        {
            if (!File.Exists(Path.Join(directoryPath, file)))
            {
                _logger.LogInformation($"Downloading Config: {file}");
                string url =
                    $"https://raw.githubusercontent.com/5stackgg/game-server/main/shared/cfg/{file}";
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError(
                            $"Failed to download config {file}: {response.StatusCode}"
                        );
                        continue;
                    }
                    var content = await response.Content.ReadAsStringAsync();
                    File.WriteAllText(Path.Join(directoryPath, file), content);
                }
            }
        }
    }

    public string GetWorkshopID()
    {
        return _core.Engine.WorkshopId;
    }

    public async void GetMatchFromApi()
    {
        MatchManager? matchManager = _currentMatch;
        if (matchManager != null && matchManager._mapChangeCountdownTimer != null)
        {
            _logger.LogInformation(
                "map change countdown timer is still active, skipping match fetch"
            );
            return;
        }

        if (_environmentService.IsOfflineMode())
        {
            GetMatchFromOffline();
            return;
        }

        string? serverId = _environmentService.GetServerId();
        string? apiPassword = _environmentService.GetServerApiPassword();

        if (serverId == null || apiPassword == null)
        {
            await Task.Delay(1000 * 5);
            _core.Scheduler.NextTick(() =>
            {
                GetMatchFromApi();
            });

            return;
        }

        try
        {
            using (var httpClient = new HttpClient())
            {
                string matchUri =
                    $"{_environmentService.GetApiUrl()}/matches/current-match/{serverId}";

                _logger.LogInformation($"Fetching Match Info for server : {serverId}");

                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiPassword);

                string? response = await httpClient.GetStringAsync(matchUri);

                _core.Scheduler.NextTick(() =>
                {
                    Guid? previousMatchId = _currentMatch?.GetMatchData()?.id;

                    if (response.Length == 0)
                    {
                        if (_currentMatch != null)
                        {
                            _currentMatch.Reset();
                        }

                        _currentMatch = null;

                        _logger.LogWarning(
                            $"No match assigned — clearing match {previousMatchId?.ToString() ?? "none"}"
                        );
                        return;
                    }

                    MatchData? matchData = JsonSerializer.Deserialize<MatchData>(response);

                    if (matchData == null)
                    {
                        return;
                    }

                    if (previousMatchId == matchData.id)
                    {
                        _logger.LogInformation(
                            $"Match {matchData.id} still assigned — re-running setup"
                        );
                        _currentMatch!.SetupMatch(matchData);
                        return;
                    }

                    _logger.LogInformation(
                        $"New match incoming: {previousMatchId?.ToString() ?? "none"} -> {matchData.id}"
                    );

                    if (_currentMatch != null)
                    {
                        _currentMatch.Reset();
                    }

                    _currentMatch =
                        _serviceProvider.GetRequiredService(typeof(MatchManager)) as MatchManager;

                    _currentMatch!.SetupMatch(matchData);
                });
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError($"HTTP request error: {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogCritical($"JSON deserialization error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError($"An unexpected error occurred: {ex.Message}");
        }
    }

    public void GetMatchFromOffline()
    {
        string? offlineMatchData = _environmentService.GetOfflineMatchData();

        if (string.IsNullOrEmpty(offlineMatchData))
        {
            _logger.LogWarning(
                "Offline mode enabled but no match data provided in GAME_SERVER_OFFLINE_MATCH_DATA"
            );
            return;
        }

        try
        {
            _logger.LogInformation("Loading match data from offline mode");

            MatchData? matchData = JsonSerializer.Deserialize<MatchData>(offlineMatchData);

            if (matchData == null)
            {
                _logger.LogError("Failed to deserialize offline match data");
                return;
            }

            _logger.LogInformation($"Loaded match data from offline mode: {matchData.id}");

            if (_currentMatch?.GetMatchData()?.id == matchData.id)
            {
                _currentMatch.SetupMatch(matchData);
                return;
            }

            _currentMatch =
                _serviceProvider.GetRequiredService(typeof(MatchManager)) as MatchManager;

            _currentMatch!.SetupMatch(matchData);
        }
        catch (JsonException ex)
        {
            _logger.LogCritical($"JSON deserialization error for offline match data: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                $"An unexpected error occurred while loading offline match data: {ex.Message}"
            );
        }
    }

    public MatchManager? GetCurrentMatch()
    {
        return _currentMatch;
    }
}
