using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchService
{
    private FiveStackMatch? _matchData;
    private eMapStatus _currentMapStatus = eMapStatus.Unknown;

    private readonly GameEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly MatchDemos _matchDemos;
    private readonly BackUpManagement _backUpManagement;
    private readonly ILogger<MatchService> _logger;

    private readonly EnvironmentService _environmentService;

    private readonly IServiceProvider _serviceProvider;

    public MatchReadySystem? readySystem;
    public MatchCoachSystem? coachSystem;
    public MatchCaptainSystem? captainSystem;
    public KnifeSystem? knifeSystem;

    public MatchService(
        ILogger<MatchService> logger,
        GameEvents gameEvents,
        GameServer gameServer,
        BackUpManagement backUpManagement,
        MatchDemos matchDemos,
        EnvironmentService environmentService,
        IServiceProvider serviceProvider
    )
    {
        _logger = logger;
        _matchDemos = matchDemos;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
        _backUpManagement = backUpManagement;
        _environmentService = environmentService;
        _serviceProvider = serviceProvider;
    }

    public async void GetMatch()
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
                GetMatch();
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

                var previousMatchId = _matchData?.id;

                FiveStackMatch? matchData = JsonSerializer.Deserialize<FiveStackMatch>(response);

                if (matchData == null)
                {
                    return;
                }

                SetupMatch(matchData);
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

    public FiveStackMatch? GetCurrentMatchData()
    {
        return _matchData;
    }

    public MatchMap? GetCurrentMap()
    {
        if (_matchData == null || _matchData.current_match_map_id == null)
        {
            return null;
        }

        return _matchData?.match_maps.FirstOrDefault(match_map =>
        {
            return match_map.id == _matchData.current_match_map_id;
        });
    }

    public bool IsWarmup()
    {
        MatchMap? _currentMap = GetCurrentMap();
        if (_currentMap == null)
        {
            return false;
        }
        return MatchUtility.MapStatusStringToEnum(_currentMap.status) == eMapStatus.Warmup;
    }

    public bool IsLive()
    {
        MatchMap? _currentMap = GetCurrentMap();
        if (_currentMap == null)
        {
            return false;
        }
        return MatchUtility.MapStatusStringToEnum(_currentMap.status) == eMapStatus.Live;
    }

    public bool isOverTime()
    {
        return GetOverTimeNumber() > 0;
    }

    private int GetOverTimeNumber()
    {
        CCSGameRules? rules = CounterStrikeSharp
            .API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .First()
            .GameRules;

        if (rules == null)
        {
            return 0;
        }

        return rules.OvertimePlaying;
    }

    public bool IsKnife()
    {
        MatchMap? _currentMap = GetCurrentMap();
        if (_currentMap == null)
        {
            return false;
        }

        knifeSystem = _serviceProvider.GetRequiredService(typeof(KnifeSystem)) as KnifeSystem;

        knifeSystem!.Setup(_matchData!);

        return MatchUtility.MapStatusStringToEnum(_currentMap.status) == eMapStatus.Knife;
    }

    public void UpdateMapStatus(eMapStatus status)
    {
        if (_matchData == null)
        {
            _logger.LogInformation("missing event data");
            return;
        }

        _logger.LogInformation($"Update Map Status {_currentMapStatus} -> {status}");

        switch (status)
        {
            case eMapStatus.Scheduled:
            case eMapStatus.Warmup:
                status = eMapStatus.Warmup;
                StartWarmup();
                break;
            case eMapStatus.Knife:
                if (!_matchData.knife_round)
                {
                    UpdateMapStatus(eMapStatus.Live);
                    break;
                }

                var currentMap = GetCurrentMap();
                if (currentMap == null)
                {
                    break;
                }

                if (currentMap.order == _matchData.best_of && _matchData.knife_round)
                {
                    StartKnife();
                }

                break;
            case eMapStatus.Live:
                StartLive();
                break;
            default:
                PublishMapStatus(status);
                break;
        }

        _currentMapStatus = status;
    }

    public void PublishMapStatus(eMapStatus status)
    {
        if (_matchData == null)
        {
            _logger.LogWarning("match data missing");
            return;
        }

        _gameEvents.PublishGameEvent(
            _matchData.id,
            "mapStatus",
            new Dictionary<string, object> { { "status", status.ToString() }, }
        );
    }

    private void SetupMatch(FiveStackMatch match)
    {
        if (_matchData == null || match.id != _matchData.id)
        {
            coachSystem =
                _serviceProvider.GetRequiredService(typeof(MatchCoachSystem)) as MatchCoachSystem;
            captainSystem =
                _serviceProvider.GetRequiredService(typeof(MatchCaptainSystem))
                as MatchCaptainSystem;

            captainSystem!.Setup(match);
        }

        _matchData = match;

        if (IsWarmup())
        {
            _gameServer.Message(HudDestination.Alert, "Received Match Data");
        }

        if (_matchData == null)
        {
            _logger.LogInformation("Missing Match Data");
            return;
        }
        _logger.LogInformation($"Setup Match {_matchData.id}");

        MatchMap? _currentMap = GetCurrentMap();

        if (_currentMap == null)
        {
            _logger.LogWarning("match does not have a current map");
            return;
        }

        if (_currentMap.map.name != Server.MapName)
        {
            ChangeMap(_currentMap.map);
            return;
        }

        _gameServer.SendCommands(new[] { $"sv_password \"{_matchData.password}\"" });

        SetupTeamNames();

        _logger.LogInformation($"Current Game State {_currentMap.status}:{_currentMap.map.name}");

        if (MatchUtility.MapStatusStringToEnum(_currentMap.status) != _currentMapStatus)
        {
            UpdateMapStatus(MatchUtility.MapStatusStringToEnum(_currentMap.status));
        }
    }

    private void SetupTeamNames()
    {
        if (_matchData == null)
        {
            return;
        }

        if (_matchData.lineup_1.name != null)
        {
            _gameServer.SendCommands(new[] { $"mp_teamname_1 {_matchData.lineup_1.name}" });
        }

        if (_matchData.lineup_2.name != null)
        {
            _gameServer.SendCommands(new[] { $"mp_teamname_2 {_matchData.lineup_2.name}" });
        }
    }

    private void ChangeMap(Map map)
    {
        _logger.LogInformation($"Changing Map {map.name}");

        if (map.workshop_map_id == null && Server.IsMapValid(map.name))
        {
            _gameServer.SendCommands(new[] { $"changelevel \"{map.name}\"" });
        }
        else
        {
            _gameServer.SendCommands(new[] { $"host_workshop_map {map.workshop_map_id}" });
        }
    }

    private void StartWarmup()
    {
        if (_matchData == null)
        {
            return;
        }

        readySystem =
            _serviceProvider.GetRequiredService(typeof(MatchReadySystem)) as MatchReadySystem;

        readySystem!.Setup(_matchData);

        if (_matchData.type == "Wingman")
        {
            _gameServer.SendCommands(new[] { "game_type 0; game_mode 2" });
        }
        else
        {
            _gameServer.SendCommands(new[] { "game_type 0; game_mode 1" });
        }

        _gameServer.SendCommands(new[] { "exec warmup", "mp_warmup_start" });

        PublishMapStatus(eMapStatus.Warmup);
    }

    private async void StartKnife()
    {
        if (_matchData == null || IsKnife())
        {
            return;
        }

        captainSystem!.AutoSelectCaptains(_matchData);

        _gameServer.SendCommands(new[] { "exec knife" });

        PublishMapStatus(eMapStatus.Knife);

        _gameServer.SendCommands(new[] { "mp_warmup_end", "mp_restartgame 0" });

        await Task.Delay(1000);
        Server.NextFrame(() =>
        {
            _gameServer.Message(HudDestination.Alert, "KNIFE KNIFE KNIFE!");
        });
    }

    private async void StartLive()
    {
        if (_matchData == null || _matchData == null)
        {
            return;
        }

        if (_matchData.type == "Wingman")
        {
            _gameServer.SendCommands(new[] { "game_type 0; game_mode 2" });
        }
        else
        {
            _gameServer.SendCommands(new[] { "game_type 0; game_mode 1" });
        }

        _gameServer.SendCommands(new[] { "mp_warmup_end", "exec live" });

        _backUpManagement.Setup(_matchData);

        _matchDemos.Start(_matchData);

        // if we can restore from backup we will prompt the for a vote to restore
        // most likely this happeend because of a server crash
        if (_backUpManagement.CheckForBackupRestore(_matchData))
        {
            return;
        }

        _gameServer.SendCommands(new[] { "mp_restartgame" });

        PublishMapStatus(eMapStatus.Live);

        await Task.Delay(1000);
        Server.NextFrame(() =>
        {
            _gameServer.Message(HudDestination.Alert, "LIVE LIVE LIVE!");
        });
    }
}
