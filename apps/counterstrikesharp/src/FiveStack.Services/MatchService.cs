using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Modules.Cvars;
using FiveStack.Entities;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchService
{
    private MatchManager? _currentMatch;
    private readonly ILogger<MatchService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly EnvironmentService _environmentService;
    private readonly INetworkServerService _networkServerService;

    public MatchService(
        ILogger<MatchService> logger,
        IServiceProvider serviceProvider,
        EnvironmentService environmentService,
        INetworkServerService networkServerService
    )
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _environmentService = environmentService;
        _networkServerService = networkServerService;
    }

    public async Task GetMatchConfigs()
    {
        await new MatchConfigDownloader(_logger).Download(
            Path.Join(Server.GameDirectory + "/csgo/cfg")
        );
    }

    public unsafe string GetWorkshopID()
    {
        return _networkServerService.GetWorkshopID();
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
            HibernationUtility.NextFrame(() =>
            {
                GetMatchFromApi();
            });

            return;
        }

        try
        {
            {
                string matchUri =
                    $"{_environmentService.GetApiUrl()}/matches/current-match/{serverId}";

                _logger.LogInformation($"Fetching Match Info for server : {serverId}");

                var request = new HttpRequestMessage(HttpMethod.Get, matchUri);
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiPassword);

                var httpResponse = await HttpClientProvider.Client.SendAsync(request);
                httpResponse.EnsureSuccessStatusCode();
                string response = await httpResponse.Content.ReadAsStringAsync();

                MatchData? matchData = null;

                if (response.Length > 0)
                {
                    matchData = JsonSerializer.Deserialize<MatchData>(response);

                    if (matchData == null)
                    {
                        _logger.LogError($"Match info for server {serverId} was null");
                        return;
                    }
                }

                HibernationUtility.NextFrame(() =>
                {
                    Guid? previousMatchId = _currentMatch?.GetMatchData()?.id;

                    if (matchData == null)
                    {
                        if (_currentMatch != null)
                        {
                            _currentMatch.Reset();
                        }

                        _currentMatch = null;

                        StayAwake(false);

                        _logger.LogWarning(
                            $"No match assigned — clearing match {previousMatchId?.ToString() ?? "none"}"
                        );
                        return;
                    }

                    StayAwake(true);

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
            _logger.LogError(ex, "HTTP request error");
        }
        catch (JsonException ex)
        {
            _logger.LogCritical(ex, "JSON deserialization error");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred");
        }
    }

    private bool _heldAwake;

    // A hibernating server runs no frames, so timers and the end-of-map work
    // would stall once everyone left. It only hibernates while it has nothing
    // to do: it is held awake from the moment it is given a match until that
    // match is gone.
    public void StayAwake(bool awake)
    {
        ConVar? hibernate = ConVar.Find("sv_hibernate_when_empty");

        if (hibernate == null)
        {
            return;
        }

        if (awake)
        {
            if (hibernate.GetPrimitiveValue<bool>())
            {
                hibernate.SetValue(false);

                // A match server never hibernates. Its match can arrive
                // before server.cfg has turned the engine's default off,
                // and that default is not something to hand it back to.
                _heldAwake = !_environmentService.IsMatchServer();
                _logger.LogInformation("holding the server awake for its match");
            }

            return;
        }

        if (_heldAwake)
        {
            hibernate.SetValue(true);
            _heldAwake = false;
            _logger.LogInformation("no match left, the server may hibernate again");
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
            _logger.LogCritical(ex, "JSON deserialization error for offline match data");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while loading offline match data");
        }
    }

    public MatchManager? GetCurrentMatch()
    {
        return _currentMatch;
    }
}
