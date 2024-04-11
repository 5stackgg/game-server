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

    private readonly MatchEvents _matchEvents;
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
        MatchEvents matchEvents,
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
        _matchEvents = matchEvents;
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

        return MatchUtility.Rules()?.WarmupPeriod ?? false;
    }

    public bool IsLive()
    {
        return _currentMapStatus == eMapStatus.Live || _currentMapStatus == eMapStatus.Overtime;
    }

    public bool IsPaused()
    {
        return _currentMapStatus == eMapStatus.Paused
            || (MatchUtility.Rules()?.GamePaused ?? false);
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
        return MatchUtility.Rules()?.OvertimePlaying ?? 0;
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

        if (_currentMapStatus == eMapStatus.Warmup && status != eMapStatus.Warmup)
        {
            SendUpdatedMatchLineups();
        }

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
                else
                {
                    StartLive();
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
                _matchEvents.PublishMapStatus(status);
                break;
        }

        _currentMapStatus = status;
    }

    public void SetupMatch(MatchData match)
    {
        _matchData = match;

        if (_matchData == null)
        {
            _logger.LogInformation("Missing Match Data");
            return;
        }
        _logger.LogInformation($"Setup Match {_matchData.id}");

        _gameServer.UpdateCurrentRound();

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

        _logger.LogInformation(
            $"Expected Game State {_currentMap.status} on {_currentMap.map.name}"
        );

        if (MatchUtility.MapStatusStringToEnum(_currentMap.status) != _currentMapStatus)
        {
            UpdateMapStatus(MatchUtility.MapStatusStringToEnum(_currentMap.status));
        }

        foreach (var player in MatchUtility.Players())
        {
            if (player.IsBot)
            {
                continue;
            }
            EnforceMemberTeam(player);
        }

        if (IsWarmup())
        {
            _gameServer.Message(HudDestination.Alert, "Received Match Data");
        }
    }

    private void SetupTeamNames()
    {
        MatchMap? _currentMap = GetCurrentMap();
        if (_matchData == null || _currentMap == null)
        {
            return;
        }

        CsTeam lineup1StartingSide = TeamUtility.TeamStringToCsTeam(_currentMap.lineup_1_side);

        string lineup1Side = "mp_teamname_1";
        string lineup2Side = "mp_teamname_2";

        if (lineup1StartingSide == CsTeam.Terrorist)
        {
            lineup1Side = "mp_teamname_2";
            lineup2Side = "mp_teamname_1";
        }

        _gameServer.SendCommands(new[] { $"{lineup1Side} {_matchData.lineup_1.name}" });
        _gameServer.SendCommands(new[] { $"{lineup2Side} {_matchData.lineup_2.name}" });
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

    private void SetupGameMode()
    {
        if (_matchData == null)
        {
            return;
        }

        if (_matchData.type == "Wingman")
        {
            _gameServer.SendCommands(new[] { "game_type 0", "game_mode 2" });
        }
        else
        {
            _gameServer.SendCommands(new[] { "game_type 0", "game_mode 1" });
        }
    }

    private void StartWarmup()
    {
        if (_matchData == null)
        {
            return;
        }

        SetupGameMode();

        _gameServer.SendCommands(new[] { "exec warmup" });

        Server.NextFrame(() =>
        {
            KickBots();

            bool isInWarmup = MatchUtility.Rules()?.WarmupPeriod ?? false;

            if (isInWarmup == false)
            {
                _gameServer.SendCommands(new[] { "mp_warmup_start" });
            }

            readySystem.Setup();

            _matchEvents.PublishMapStatus(eMapStatus.Warmup);
        });
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

        _matchEvents.PublishMapStatus(eMapStatus.Knife);

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

        SetupGameMode();

        _gameServer.SendCommands(new[] { "exec live" });

        await _backUpManagement.DownloadBackupRounds();

        Server.NextFrame(() =>
        {
            KickBots();

            _matchDemos.Start();

            // if we can restore from backup we will prompt the for a vote to restore
            // most likely this happeend because of a server crash
            if (_backUpManagement.CheckForBackupRestore())
            {
                if (IsWarmup())
                {
                    _gameServer.SendCommands(new[] { "mp_warmup_end" });
                }
                _matchEvents.PublishMapStatus(eMapStatus.Live);
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

            _matchEvents.PublishMapStatus(eMapStatus.Live);
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

        if (currentTeam == null)
        {
            currentTeam = player.Team;
        }

        CsTeam expectedTeam = CsTeam.None;

        string lineupName =
            matchData.lineup_1_id == lineup_id ? matchData.lineup_1.name : matchData.lineup_2.name;

        foreach (var team in MatchUtility.Teams())
        {
            if (team.ClanTeamname == lineupName)
            {
                expectedTeam = TeamUtility.TeamNumToCSTeam(team.TeamNum);
            }
        }

        if (expectedTeam == CsTeam.None)
        {
            _logger.LogWarning("Unable to get expected team");
            return;
        }

        if (currentTeam != expectedTeam)
        {
            // allow them to click the menu, they just get switched really quick
            await Task.Delay(100);

            Server.NextFrame(() =>
            {
                player.ChangeTeam(expectedTeam);
                _gameServer.Message(
                    HudDestination.Chat,
                    $" You've been assigned to {(expectedTeam == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{TeamUtility.CSTeamToString(expectedTeam)}.",
                    player
                );
                captainSystem.IsCaptain(player, expectedTeam);
            });
            return;
        }

        captainSystem.IsCaptain(player, expectedTeam);
    }

    private void KickBots()
    {
        if (_environmentService.AllowBots())
        {
            _gameServer.SendCommands(
                new[] { "bot_quota 3", "bot_quota_mode fill", "bot_add expert" }
            );
            return;
        }

        _gameServer.SendCommands(new[] { "bot_quota 0", "bot_kick", "bot_quota_mode competitive" });
    }

    private void SendUpdatedMatchLineups()
    {
        MatchMap? _currentMap = GetCurrentMap();

        if (_matchData == null || _currentMap == null)
        {
            return;
        }

        var lineups = new Dictionary<string, List<object>>
        {
            { "lineup_1", new List<object>() },
            { "lineup_2", new List<object>() },
        };

        foreach (var player in MatchUtility.Players())
        {
            if (player.PlayerName == "SourceTV")
            {
                continue;
            }

            if (TeamUtility.TeamStringToCsTeam(_currentMap.lineup_1_side) == player.Team)
            {
                ((List<object>)lineups["lineup_1"]).Add(
                    new
                    {
                        name = player.PlayerName,
                        steam_id = player.SteamID.ToString(),
                        captain = captainSystem.IsCaptain(player, player.Team)
                    }
                );
            }
            else
            {
                ((List<object>)lineups["lineup_2"]).Add(
                    new
                    {
                        name = player.PlayerName,
                        steam_id = player.SteamID.ToString(),
                        captain = captainSystem.IsCaptain(player, player.Team)
                    }
                );
            }
        }

        var data = new Dictionary<string, object> { { "lineups", lineups } };

        _matchEvents.PublishGameEvent("updateLineups", data);
    }
}
