using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchManager
{
    private MatchData? _matchData;
    private eMapStatus _currentMapStatus = eMapStatus.Unknown;

    private readonly MatchEvents _gameEvents;
    private readonly GameServer _gameServer;
    private readonly GameDemos _matchDemos;
    private readonly ILogger<MatchManager> _logger;
    private readonly GameBackUpRounds _backUpManagement;

    private readonly EnvironmentService _environmentService;

    public ReadySystem readySystem;

    // public CoachSystem _coachSystem;
    public KnifeSystem knifeSystem;
    public CaptainSystem captainSystem;

    public MatchManager(
        ILogger<MatchManager> logger,
        MatchEvents gameEvents,
        GameServer gameServer,
        GameBackUpRounds backUpManagement,
        GameDemos matchDemos,
        KnifeSystem KnifeSystem,
        ReadySystem ReadySystem,
        CaptainSystem CaptainSystem,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _matchDemos = matchDemos;
        _gameEvents = gameEvents;
        _gameServer = gameServer;
        knifeSystem = KnifeSystem;
        readySystem = ReadySystem;
        captainSystem = CaptainSystem;
        _backUpManagement = backUpManagement;
        _environmentService = environmentService;
    }

    public void Init(MatchData match)
    {
        _matchData = match;
    }

    public MatchData? GetMatchData()
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
        if (_currentMapStatus == eMapStatus.Warmup)
        {
            return true;
        }

        CCSGameRules? rules = CounterStrikeSharp
            .API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .First()
            .GameRules;

        return rules?.WarmupPeriod ?? false;
    }

    public bool IsLive()
    {
        return _currentMapStatus == eMapStatus.Live;
    }

    public bool IsPaused()
    {
        CCSGameRules? rules = CounterStrikeSharp
            .API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .First()
            .GameRules;

        return _currentMapStatus == eMapStatus.Paused || (rules?.GamePaused ?? false);
    }

    public void PauseMatch(string? message = null)
    {
        _gameServer.SendCommands(new[] { "mp_pause_match" });
        UpdateMapStatus(eMapStatus.Paused);

        if (message != null)
        {
            _gameServer.Message(HudDestination.Alert, message);
        }
    }

    public bool isOverTime()
    {
        return GetOverTimeNumber() > 0;
    }

    public int GetOverTimeNumber()
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
        return _currentMapStatus == eMapStatus.Knife;
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
                    return;
                }

                var currentMap = GetCurrentMap();
                if (currentMap == null)
                {
                    break;
                }

                if (currentMap.order == _matchData.best_of)
                {
                    StartKnife();
                }

                break;
            case eMapStatus.Paused:
                if (_backUpManagement.IsResttingRound())
                {
                    break;
                }
                StartLive();
                break;
            case eMapStatus.Live:
                StartLive();
                break;
            default:
                _gameEvents.PublishMapStatus(status);
                break;
        }

        _currentMapStatus = status;
    }

    public void SetupMatch(MatchData match)
    {
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

        for (var i = 1; i <= 10; ++i)
        {
            CCSPlayerController player = new CCSPlayerController(NativeAPI.GetEntityFromIndex(i));

            if (player != null && player.UserId != null && player.IsValid && !player.IsBot)
            {
                EnforceMemberTeam(player);
            }
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

        if (_matchData.type == "Wingman")
        {
            _gameServer.SendCommands(new[] { "game_type 0; game_mode 2" });
        }
        else
        {
            _gameServer.SendCommands(new[] { "game_type 0; game_mode 1" });
        }

        KickBots();

        _gameServer.SendCommands(new[] { "exec warmup" });

        CCSGameRules? rules = CounterStrikeSharp
            .API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .First()
            .GameRules;

        bool isINWarmup = rules?.WarmupPeriod ?? false;

        if (isINWarmup == false)
        {
            Server.NextFrame(() =>
            {
                _gameServer.SendCommands(new[] { "mp_warmup_start" });
            });
        }

        readySystem.Setup();

        _gameEvents.PublishMapStatus(eMapStatus.Warmup);
    }

    private void StartKnife()
    {
        if (_matchData == null || IsKnife())
        {
            return;
        }

        captainSystem.AutoSelectCaptains();

        KickBots();

        _gameServer.SendCommands(new[] { "exec knife" });

        _gameEvents.PublishMapStatus(eMapStatus.Knife);

        Server.NextFrame(() =>
        {
            _gameServer.SendCommands(new[] { "mp_warmup_end" });
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

        KickBots();

        _gameServer.SendCommands(new[] { "exec live" });

        _matchDemos.Start();

        await _backUpManagement.DownloadBackupRounds();

        Server.NextFrame(() =>
        {
            // if we can restore from backup we will prompt the for a vote to restore
            // most likely this happeend because of a server crash
            if (_backUpManagement.CheckForBackupRestore())
            {
                if (IsWarmup())
                {
                    _gameServer.SendCommands(new[] { "mp_warmup_end" });
                }
                _gameEvents.PublishMapStatus(eMapStatus.Live);
                return;
            }

            _backUpManagement.Setup();

            if (IsWarmup())
            {
                _gameServer.SendCommands(new[] { "mp_warmup_end" });
            }

            if (IsWarmup() || IsKnife())
            {
                _gameServer.Message(HudDestination.Alert, "LIVE LIVE LIVE!");
            }

            _gameEvents.PublishMapStatus(eMapStatus.Live);
        });
    }

    public async void EnforceMemberTeam(CCSPlayerController player, CsTeam? currentTeam = null)
    {
        MatchData? matchData = GetMatchData();
        MatchMap? currentMap = GetCurrentMap();

        if (matchData == null || currentMap == null)
        {
            return;
        }

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        if (lineup_id == null)
        {
            return;
        }

        CsTeam startingSide = TeamUtility.TeamStringToCsTeam(
            matchData.lineup_1_id == lineup_id ? currentMap.lineup_1_side : currentMap.lineup_2_side
        );

        if (currentTeam == null)
        {
            currentTeam = player.Team;
        }

        if (currentTeam != startingSide)
        {
            // allow them to click the menu , they jsut get switched really quick
            await Task.Delay(100);

            Server.NextFrame(() =>
            {
                player.ChangeTeam(startingSide);
                _gameServer.Message(
                    HudDestination.Chat,
                    $" You've been assigned to {(startingSide == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{TeamUtility.CSTeamToString(startingSide)}.",
                    player
                );
                captainSystem.IsCaptain(player, startingSide);
            });
            return;
        }

        captainSystem.IsCaptain(player, startingSide);
    }

    private void KickBots() {
        if (_environmentService.AllowBots()) {
            _gameServer.SendCommands(new [] {
                "bot_quota_mode normal",
                "bot_add expert"
            });
            return;
        }

         _gameServer.SendCommands(new[] {
               "bot_quota 0",
               "bot_kick",
               "bot_quota_mode normal"
             });
    }
}
