using System.Runtime.InteropServices;
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
        string directoryPath = Path.Join(Server.GameDirectory + "/csgo/cfg");
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

    public unsafe string GetWorkshopID()
    {
        IntPtr networkGameServer = _networkServerService.GetIGameServerHandle();
        IntPtr vtablePtr = Marshal.ReadIntPtr(networkGameServer);
        IntPtr functionPtr = Marshal.ReadIntPtr(vtablePtr + (25 * IntPtr.Size));
        var getAddonName = Marshal.GetDelegateForFunctionPointer<GetAddonNameDelegate>(functionPtr);
        IntPtr result = getAddonName(networkGameServer);
        return Marshal.PtrToStringAnsi(result)!.Split(',')[0];
    }

    public async void GetMatchFromApi()
    {
        if (_environmentService.IsOfflineMode())
        {
            GetMatchFromOffline();
            return;
        }

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

            _logger.LogInformation($"Fetching Match Info for server : {serverId}");

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
                    _currentMatch.SetupMatch(matchData);
                    return;
                }

                _currentMatch =
                    _serviceProvider.GetRequiredService(typeof(MatchManager)) as MatchManager;

                _currentMatch!.SetupMatch(matchData);
            });
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
