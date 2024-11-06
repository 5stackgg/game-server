using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
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

    public void PauseMatch(string? message = null)
    {
        if (!IsLive())
        {
            _logger.LogWarning("Match is not live, cannot pause");
            return;
        }

        _gameServer.SendCommands(new[] { "mp_pause_match" });
        UpdateMapStatus(eMapStatus.Paused);

        if (message != null)
        {
            _gameServer.Message(HudDestination.Alert, message);
        }
    }

    public void ResumeMatch(string? message = null)
    {
        _gameServer.SendCommands(new[] { "mp_unpause_match" });

        UpdateMapStatus(isOverTime() ? eMapStatus.Overtime : eMapStatus.Live);

        if (message != null)
        {
            _gameServer.Message(HudDestination.Alert, message);
        }
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

        SetupGameMode();

        switch (status)
        {
            case eMapStatus.Scheduled:
            case eMapStatus.Warmup:
                status = eMapStatus.Warmup;
                StartWarmup();
                break;
            case eMapStatus.Knife:
                if (!_matchData.options.knife_round)
                {
                    UpdateMapStatus(eMapStatus.Live);
                    return;
                }

                var currentMap = GetCurrentMap();
                if (currentMap == null)
                {
                    break;
                }

                if (currentMap.order == _matchData.options.best_of)
                {
                    StartKnife();
                }
                else
                {
                    StartLive();
                }

                break;
            case eMapStatus.Paused:
                if (_backUpManagement.IsResettingRound())
                {
                    break;
                }
                StartLive();
                break;
            case eMapStatus.Live:
                StartLive();

                if (
                    _currentMapStatus != eMapStatus.Live
                    && (
                        _currentMapStatus == eMapStatus.Warmup
                        || _currentMapStatus == eMapStatus.Knife
                    )
                )
                {
                    _gameServer.SendCommands(new[] { "mp_restartgame 1" });
                }
                break;
        }

        _matchEvents.PublishMapStatus(status);

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

        MatchMap? _currentMap = GetCurrentMap();

        if (_currentMap == null)
        {
            _logger.LogWarning("match does not have a current map");
            return;
        }

        _logger.LogInformation($"Game State {_currentMap.status} on {_currentMap.map.name}");

        if (_currentMap.map.name != Server.MapName)
        {
            ChangeMap(_currentMap.map);
            return;
        }

        _gameServer.SendCommands(new[] { $"sv_password \"{_matchData.password}\"" });

        SetupTeamNames();

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

        int? gameMode = ConVar.Find("game_mode")?.GetPrimitiveValue<int>();

        if (_matchData.options.type == "Wingman")
        {
            _gameServer.SendCommands(new[] { "game_type 0", "game_mode 2" });

            if (gameMode != 2)
            {
                _gameServer.SendCommands(new[] { "mp_restartgame 1" });
            }
        }
        else
        {
            _gameServer.SendCommands(new[] { "game_type 0", "game_mode 1" });
            if (gameMode != 1)
            {
                _gameServer.SendCommands(new[] { "mp_restartgame 1" });
            }
        }
        KickBots();
    }

    public int GetExpectedPlayerCount()
    {
        if (_matchData == null)
        {
            return 10;
        }

        return _matchData.options.type == "Wingman" ? 4 : 10;
    }

    private void StartWarmup()
    {
        if (_matchData == null)
        {
            return;
        }

        _gameServer.SendCommands(new[] { "exec 5stack.warmup.cfg" });

        Server.NextFrame(() =>
        {
            bool isInWarmup = MatchUtility.Rules()?.WarmupPeriod ?? false;

            if (isInWarmup == false)
            {
                _gameServer.SendCommands(new[] { "mp_warmup_start" });
            }

            readySystem.Setup();
        });
    }

    private void StartKnife()
    {
        if (_matchData == null || IsKnife())
        {
            return;
        }

        captainSystem.AutoSelectCaptains();

        knifeSystem.Start();
    }

    private async void StartLive()
    {
        if (_matchData == null || _matchData == null)
        {
            return;
        }

        if (IsKnife())
        {
            _gameServer.SendCommands(new[] { "mp_unpause_match" });
        }

        _gameServer.SendCommands(
            new[]
            {
                "exec 5stack.live.cfg",
                $"mp_maxrounds {_matchData.options.mr * 2}",
                $"mp_overtime_enable {_matchData.options.overtime}",
            }
        );

        await _backUpManagement.DownloadBackupRounds();

        Server.NextFrame(() =>
        {
            _matchDemos.Start();
            _backUpManagement.Setup();

            if (IsWarmup())
            {
                _gameServer.SendCommands(new[] { "mp_warmup_end" });
            }

            // if we can restore from backup we will prompt the for a vote to restore
            // most likely this happeend because of a server crash
            if (_backUpManagement.CheckForBackupRestore())
            {
                return;
            }
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

        // required because joined team is not set immediately
        await Task.Delay(100);

        Server.NextFrame(() =>
        {
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
                matchData.lineup_1_id == lineup_id
                    ? matchData.lineup_1.name
                    : matchData.lineup_2.name;

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
                player.ChangeTeam(expectedTeam);
                _gameServer.Message(
                    HudDestination.Chat,
                    $" You've been assigned to {(expectedTeam == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{TeamUtility.CSTeamToString(expectedTeam)}.",
                    player
                );
            }

            captainSystem.IsCaptain(player, expectedTeam);
        });
    }

    private void KickBots()
    {
        Server.NextFrame(() =>
        {
            if (this._matchData == null)
            {
                return;
            }

            if (_environmentService.AllowBots())
            {
                int expectedPlayers = GetExpectedPlayerCount();
                int currentPlayers = MatchUtility.Players().Count;

                if (currentPlayers >= expectedPlayers)
                {
                    return;
                }

                _gameServer.SendCommands(
                    new[]
                    {
                        "bot_quota_mode normal",
                        $"bot_quota {Math.Max(0, expectedPlayers - currentPlayers)}",
                    }
                );

                if (currentPlayers < expectedPlayers)
                {
                    _gameServer.SendCommands(new[] { "bot_add expert" });
                }

                return;
            }

            _gameServer.SendCommands(
                new[] { "bot_quota_mode competitive", "bot_quota 0", "bot_kick" }
            );
        });
    }

    private void SendUpdatedMatchLineups()
    {
        bool shouldUpdateApi = false;
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

            MatchMember? member = MatchUtility.GetMemberFromLineup(_matchData, player);
            if (member == null)
            {
                shouldUpdateApi = true;
            }

            if (TeamUtility.TeamStringToCsTeam(_currentMap.lineup_1_side) == player.Team)
            {
                ((List<object>)lineups["lineup_1"]).Add(
                    new
                    {
                        name = player.PlayerName,
                        steam_id = player.SteamID.ToString(),
                        captain = captainSystem.IsCaptain(player, player.Team),
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
                        captain = captainSystem.IsCaptain(player, player.Team),
                    }
                );
            }
        }

        if (shouldUpdateApi)
        {
            _matchEvents.PublishGameEvent(
                "updateLineups",
                new Dictionary<string, object> { { "lineups", lineups } }
            );
        }
    }
}
