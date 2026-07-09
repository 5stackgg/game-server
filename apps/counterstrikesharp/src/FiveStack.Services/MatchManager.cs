using System.Text.Json.Serialization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class MatchManager
{
    // Extra seconds added to mp_match_restart_delay on dedicated servers so CS2's
    // auto-restart stays behind the plugin's stop-recording + demo-upload flow.
    private const int DemoUploadRestartBufferSeconds = 300;

    private MatchData? _matchData;
    private eMapStatus _currentMapStatus = eMapStatus.Unknown;
    private Guid? _activeMapId;
    private Timer? _resumeMessageTimer;
    public bool gameEnded = false;

    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly GameDemos _gameDemos;
    private readonly ILogger<MatchManager> _logger;
    private readonly GameBackUpRounds _backUpManagement;
    private readonly SurrenderSystem _surrenderSystem;
    private readonly IStringLocalizer _localizer;

    private readonly EnvironmentService _environmentService;
    private readonly TimeoutSystem _timeoutSystem;
    public ReadySystem readySystem;

    // public CoachSystem _coachSystem;
    public KnifeSystem knifeSystem;
    public CaptainSystem captainSystem;
    public MatchService _matchService;
    private readonly RankSystem _rankSystem;

    private int _remainingMapChangeDelay = 0;
    public Timer? _mapChangeCountdownTimer;

    public MatchManager(
        ILogger<MatchManager> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        GameBackUpRounds backUpManagement,
        GameDemos gameDemos,
        KnifeSystem KnifeSystem,
        ReadySystem ReadySystem,
        CaptainSystem CaptainSystem,
        EnvironmentService environmentService,
        TimeoutSystem timeoutSystem,
        SurrenderSystem surrenderSystem,
        MatchService matchService,
        RankSystem rankSystem,
        IStringLocalizer localizer
    )
    {
        _logger = logger;
        _gameDemos = gameDemos;
        _matchEvents = matchEvents;
        _gameServer = gameServer;
        knifeSystem = KnifeSystem;
        readySystem = ReadySystem;
        captainSystem = CaptainSystem;
        _backUpManagement = backUpManagement;
        _environmentService = environmentService;
        _timeoutSystem = timeoutSystem;
        _surrenderSystem = surrenderSystem;
        _matchService = matchService;
        _rankSystem = rankSystem;
        _localizer = localizer;
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
        var matchData = _matchData;
        if (matchData == null || matchData.current_match_map_id == null)
        {
            return null;
        }

        return matchData.match_maps.FirstOrDefault(match_map =>
        {
            return match_map.id == matchData.current_match_map_id;
        });
    }

    public Guid? GetActiveMapId()
    {
        return _activeMapId;
    }

    public void SyncActiveMapAfterMapStart()
    {
        _activeMapId = GetCurrentMap()?.id;
        gameEnded = false;
    }

    public bool IsMapFinished()
    {
        return _currentMapStatus == eMapStatus.Finished
            || _currentMapStatus == eMapStatus.UploadingDemo
            || _currentMapStatus == eMapStatus.Surrendered;
    }

    public bool IsWarmup()
    {
        if (_currentMapStatus == eMapStatus.Warmup || _currentMapStatus == eMapStatus.Scheduled)
        {
            return true;
        }

        return MatchUtility.Rules()?.WarmupPeriod ?? false;
    }

    public bool IsInPlay()
    {
        if (IsWarmup() == true || IsKnife() == true)
        {
            return false;
        }

        if (gameEnded)
        {
            return false;
        }

        return _currentMapStatus == eMapStatus.Live
            || _currentMapStatus == eMapStatus.Overtime
            || _currentMapStatus == eMapStatus.Paused;
    }

    public bool IsPaused()
    {
        if (_currentMapStatus == eMapStatus.Paused)
        {
            return true;
        }

        _resumeMessageTimer?.Kill();

        return false;
    }

    public bool IsFreezePeriod()
    {
        return MatchUtility.Rules()?.FreezePeriod ?? false;
    }

    public bool isOverTime()
    {
        return GetOverTimeNumber() > 0;
    }

    public bool isSurrendered()
    {
        return _currentMapStatus == eMapStatus.Surrendered;
    }

    public int GetOverTimeNumber()
    {
        return MatchUtility.Rules()?.OvertimePlaying ?? 0;
    }

    public bool IsKnife()
    {
        return _currentMapStatus == eMapStatus.Knife;
    }

    public void PauseMatch(string? message = null, bool skipUpdate = false)
    {
        _gameServer.SendCommands(["mp_pause_match"]);

        if (IsPaused())
        {
            return;
        }

        _logger.LogInformation($"Pausing Match{(message != null ? $": {message}" : "")}");

        if (message != null)
        {
            _gameServer.Message(HudDestination.Alert, message);
        }

        _resumeMessageTimer?.Kill();
        _resumeMessageTimer = TimerUtility.AddTimer(
            3,
            () =>
            {
                if (!IsFreezePeriod() || !IsPaused() || _backUpManagement.IsResettingRound())
                {
                    return;
                }

                _gameServer.Message(
                    HudDestination.Alert,
                    _localizer["match.resume_hint", CommandUtility.PublicChatTrigger]
                );
            },
            TimerFlags.REPEAT
        );

        if (skipUpdate)
        {
            return;
        }

        UpdateMapStatus(eMapStatus.Paused);
    }

    public void ResumeMatch(string? message = null, bool skipUpdate = false)
    {
        if (_timeoutSystem.IsTimeoutActive())
        {
            _logger.LogInformation("Timeout is active, cannot resume match");
            return;
        }

        if (_backUpManagement.IsResettingRound())
        {
            _logger.LogInformation("Resetting round, cannot resume match");
            return;
        }

        _logger.LogInformation($"Resuming Match{(message != null ? $": {message}" : "")}");
        _gameServer.SendCommands(["mp_unpause_match"]);

        _resumeMessageTimer?.Kill();
        if (_timeoutSystem.resumeVote != null)
        {
            _timeoutSystem.resumeVote.CancelVote();
            _timeoutSystem.resumeVote = null;
        }

        if (_backUpManagement.restoreRoundVote != null)
        {
            _backUpManagement.restoreRoundVote.CancelVote();
            _backUpManagement.restoreRoundVote = null;
        }

        _timeoutSystem.ClearPendingTeamResumes();

        if (!IsPaused())
        {
            return;
        }

        if (message != null)
        {
            _gameServer.Message(HudDestination.Center, message);
        }

        if (skipUpdate)
        {
            return;
        }

        UpdateMapStatus(isOverTime() ? eMapStatus.Overtime : eMapStatus.Live);
    }

    public void UpdateMapStatus(eMapStatus status, Guid? winningLineupId = null)
    {
        if (_matchData == null)
        {
            return;
        }

        if (_currentMapStatus == status)
        {
            return;
        }

        if (
            IsMapFinished()
            && (
                status == eMapStatus.Live
                || status == eMapStatus.Overtime
                || status == eMapStatus.Paused
                || status == eMapStatus.Knife
                || status == eMapStatus.Warmup
                || status == eMapStatus.Scheduled
            )
        )
        {
            _logger.LogInformation(
                $"Ignoring map status transition {_currentMapStatus} -> {status} because current map is already terminal"
            );
            return;
        }

        _logger.LogInformation($"Update Map Status {_currentMapStatus} -> {status}");

        if (_currentMapStatus == eMapStatus.Unknown)
        {
            _backUpManagement.CheckForBackupRestore();
        }

        var currentMap = GetCurrentMap();

        // TODO - this should only happen discord matches
        // if (
        //     _currentMapStatus == eMapStatus.Warmup
        //     && status != eMapStatus.Warmup
        //     && currentMap != null
        //     && currentMap.order == 1
        // )
        // {
        //     SendUpdatedMatchLineups();
        // }

        switch (status)
        {
            case eMapStatus.Scheduled:
                UpdateMapStatus(eMapStatus.Warmup);
                return;
            case eMapStatus.Warmup:
                SetupTeams();
                status = eMapStatus.Warmup;
                StartWarmup();
                break;
            case eMapStatus.Knife:
                if (!_matchData.options.knife_round)
                {
                    UpdateMapStatus(eMapStatus.Live);
                    return;
                }

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
                    UpdateMapStatus(eMapStatus.Live);
                    return;
                }

                break;
            case eMapStatus.Paused:
                if (_backUpManagement.IsResettingRound())
                {
                    break;
                }

                if (IsWarmup())
                {
                    _gameServer.SendCommands(["mp_warmup_end"]);
                }

                PauseMatch(null, true);
                break;
            case eMapStatus.Live:
                StartLive();
                break;
            case eMapStatus.WaitingForTV:
                // GameEnd.cs owns the demo lifecycle from here.
                break;
            case eMapStatus.Finished:
            case eMapStatus.Surrendered:
                _surrenderSystem.Reset();
                _matchEvents.ClearPendingRoundResult();
                break;
            case eMapStatus.UploadingDemo:
                _surrenderSystem.Reset();
                if (_currentMapStatus == eMapStatus.Unknown || IsMapFinished())
                {
                    return;
                }
                break;
        }

        if (
            (status == eMapStatus.Knife || status == eMapStatus.Live)
            && MatchUtility.HasPlaceholderMembers(_matchData)
        )
        {
            PublishConnectedPlayers(status);
        }

        _matchEvents.PublishMapStatus(status, winningLineupId);
        _currentMapStatus = status;
    }

    private void PublishConnectedPlayers(eMapStatus status)
    {
        if (_matchData == null)
        {
            return;
        }

        var players = new List<Dictionary<string, object>>();

        foreach (var player in MatchUtility.Players())
        {
            Guid? lineupId = MatchUtility.GetPlayerLineup(_matchData, player);

            players.Add(
                new Dictionary<string, object>
                {
                    { "steam_id", player.SteamID.ToString() },
                    { "player_name", player.PlayerName },
                    { "team", TeamUtility.CSTeamToString(player.Team) },
                    { "lineup_id", lineupId?.ToString() ?? "" },
                }
            );
        }

        _matchEvents.PublishGameEvent(
            "players-connected",
            new Dictionary<string, object>
            {
                { "status", status.ToString() },
                { "players", players },
            }
        );
    }

    public void SetupMatch(MatchData match)
    {
        _matchData = match;

        if (_matchData == null)
        {
            return;
        }

        _logger.LogInformation($"Setup Match {_matchData.id}");

        _rankSystem.OnMatchSetup(_matchData);

        MatchMap? _currentMap = GetCurrentMap();

        if (_currentMap == null)
        {
            _logger.LogWarning("match does not have a current map");
            return;
        }

        _logger.LogInformation(
            $"Game State {_currentMap.status} expected {_currentMap.map.name}, on {Server.MapName}"
        );

        if (_currentMap.map.workshop_map_id is not null)
        {
            string currentWorkshopID = _matchService.GetWorkshopID();
            _logger.LogInformation(
                $"Checking Workshop Map {_currentMap.map.workshop_map_id} / {currentWorkshopID}"
            );

            if (_currentMap.map.workshop_map_id != currentWorkshopID)
            {
                _logger.LogInformation(
                    $"Map switch required: workshop {currentWorkshopID} -> {_currentMap.map.workshop_map_id} (match {_matchData.id})"
                );
                ChangeMap(_currentMap.map);
                return;
            }
        }
        else if (!Server.MapName.ToLower().Contains(_currentMap.map.name.ToLower()))
        {
            _logger.LogInformation(
                $"Map switch required: on {Server.MapName}, need {_currentMap.map.name} (match {_matchData.id})"
            );
            ChangeMap(_currentMap.map);
            return;
        }

        bool wasAlreadySetup = _activeMapId == _currentMap.id;
        _activeMapId = _currentMap.id;

        if (_currentMapStatus == eMapStatus.WaitingForTV)
        {
            // Load-bearing: prevents IsMapFinished safety net below from stopping the demo mid-window.
            return;
        }

        if (_matchData == null || IsMapFinished())
        {
            _gameDemos.Stop();
            _surrenderSystem.Reset();
            return;
        }

        gameEnded = false;

        if (_matchData.options.cfg_overrides != null && _matchData.options.cfg_overrides.Count > 0)
        {
            string configDirectory = Path.Join(Server.GameDirectory, "csgo", "cfg");

            foreach (var overrideEntry in _matchData.options.cfg_overrides)
            {
                if (string.IsNullOrEmpty(overrideEntry.Value))
                {
                    continue;
                }

                string configFileName = $"5stack.{overrideEntry.Key.ToLower()}.cfg";
                string configFilePath = Path.Join(configDirectory, configFileName);

                _logger.LogInformation($"Overriding config file: {configFileName}");
                File.WriteAllText(configFilePath, overrideEntry.Value);
            }
        }

        if (!wasAlreadySetup)
        {
            _gameServer.SendCommands([$"exec 5stack.{_matchData.options.type.ToLower()}.cfg"]);

            if (_matchData.is_lan)
            {
                _gameServer.SendCommands(["exec 5stack.lan.cfg"]);
            }
        }

        ApplyAntiWallhack();
        PublishAntiWallhackStatus();

        Server.NextFrame(() =>
        {
            if (!wasAlreadySetup)
            {
                SetupGameMode();
            }

            FiveStackPlugin.SetPasswordBuffer(_matchData.password);
            ConVar.Find("sv_password")?.SetValue(_matchData.password);
            // On a dedicated server the plugin runs a longer end-of-map flow (wait
            // for GOTV, stop recording, upload the demo) and changes the map itself,
            // so push CS2's auto-restart out past that flow — otherwise the map
            // rotates mid-upload. Game-server nodes skip the upload, so tv_delay is
            // enough there.
            int matchRestartDelay = _matchData.options.tv_delay;
            if (!_environmentService.isOnGameServerNode())
            {
                matchRestartDelay += DemoUploadRestartBufferSeconds;
            }
            ConVar.Find("mp_match_restart_delay")?.SetValue(matchRestartDelay);
            ConVar.Find("hostname")?.SetValue("5Stack.gg");

            if (MatchUtility.MapStatusStringToEnum(_currentMap.status) != _currentMapStatus)
            {
                UpdateMapStatus(MatchUtility.MapStatusStringToEnum(_currentMap.status));
            }

            if (IsWarmup())
            {
                _gameServer.Message(HudDestination.Alert, _localizer["match.received_data"]);
            }

            KickBots();
            SetupBroadcast();
        });
    }

    public void SetupTeams()
    {
        MatchMap? _currentMap = GetCurrentMap();
        if (_matchData == null || _currentMap == null)
        {
            return;
        }

        CsTeam lineup1StartingSide = TeamUtility.TeamStringToCsTeam(_currentMap.lineup_1_side);

        string mp_teamname_1 = ConVar.Find("mp_teamname_1")?.StringValue ?? "";
        string mp_teamname_2 = ConVar.Find("mp_teamname_2")?.StringValue ?? "";

        string expectedTeamname1 =
            lineup1StartingSide == CsTeam.CounterTerrorist
                ? _matchData.lineup_1.name
                : _matchData.lineup_2.name;

        string expectedTeamname2 =
            lineup1StartingSide == CsTeam.CounterTerrorist
                ? _matchData.lineup_2.name
                : _matchData.lineup_1.name;

        ConVar.Find("mp_teamname_1")!.StringValue = expectedTeamname1;
        ConVar.Find("mp_teamname_2")!.StringValue = expectedTeamname2;

        TimerUtility.AddTimer(
            1.0f,
            () =>
            {
                foreach (var player in MatchUtility.Players())
                {
                    EnforceMemberTeam(player);
                }
            }
        );
    }

    public void delayChangeMap(int delay)
    {
        _remainingMapChangeDelay = delay;
        _logger.LogInformation(
            $"Map change in {delay}s then fetching next match (match {_matchData?.id.ToString() ?? "none"})"
        );

        _mapChangeCountdownTimer?.Kill();

        TimerUtility.AddTimer(
            delay,
            () =>
            {
                _gameDemos.StopTV();
                _logger.LogInformation("map change delay complete");
                _mapChangeCountdownTimer?.Kill();
                _mapChangeCountdownTimer = null;
                _matchService.GetMatchFromApi();
            }
        );

        _mapChangeCountdownTimer = TimerUtility.AddTimer(
            1,
            () =>
            {
                if (_remainingMapChangeDelay > 0)
                {
                    _gameServer.Message(
                        HudDestination.Alert,
                        _localizer["match.tv_delay", _remainingMapChangeDelay]
                    );
                    _remainingMapChangeDelay--;
                }
            },
            TimerFlags.REPEAT
        );
    }

    public void ChangeMap(Map map)
    {
        if (_mapChangeCountdownTimer != null)
        {
            return;
        }

        _gameServer.SendCommands(["tv_broadcast 0"]);
        Reset();

        if (map.workshop_map_id == null && Server.IsMapValid(map.name))
        {
            _logger.LogInformation(
                $"Changing Map {map.name} (match {_matchData?.id.ToString() ?? "none"})"
            );
            _gameServer.SendCommands([$"changelevel \"{map.name}\""]);
        }
        else
        {
            _logger.LogInformation(
                $"Changing Map {map.name} / {map.workshop_map_id} (match {_matchData?.id.ToString() ?? "none"})"
            );
            _gameServer.SendCommands([$"host_workshop_map {map.workshop_map_id}"]);
        }
    }

    private void SetupGameMode()
    {
        if (_matchData == null)
        {
            return;
        }

        ConVar.Find("game_type")?.SetValue(0);
        if (_matchData.options.type == "Duel" || _matchData.options.type == "Wingman")
        {
            ConVar.Find("game_mode")?.SetValue(2);
        }
        else
        {
            ConVar.Find("game_mode")?.SetValue(1);
        }

        // game_type/game_mode only take effect after a restart, but restarting an
        // in-progress match (e.g. on a plugin reload) would wrongly reset it. Only
        // restart during warmup, when a fresh game start is expected.
        if (!IsWarmup())
        {
            _logger.LogInformation("SetupGameMode: not in warmup, skipping mp_restartgame");
            return;
        }

        _gameServer.SendCommands(["mp_restartgame 1"]);
    }

    public int GetExpectedPlayerCount()
    {
        if (_matchData == null)
        {
            return 10;
        }

        if (_matchData.options.type == "Wingman")
        {
            return 4;
        }

        if (_matchData.options.type == "Duel")
        {
            return 2;
        }

        return 10;
    }

    private void StartWarmup()
    {
        ConVar.Find("sv_disable_teamselect_menu")?.SetValue(0);
        _gameServer.SendCommands(["exec 5stack.warmup.cfg"]);

        knifeSystem.Reset();

        if (_matchData == null)
        {
            return;
        }

        Server.NextFrame(() =>
        {
            bool isInWarmup = MatchUtility.Rules()?.WarmupPeriod ?? false;

            if (isInWarmup == false)
            {
                _gameServer.SendCommands(["mp_warmup_start"]);
            }

            readySystem.Setup();
        });
    }

    private void StartKnife()
    {
        ConVar.Find("sv_disable_teamselect_menu")?.SetValue(1);

        if (_matchData == null || IsKnife())
        {
            return;
        }

        captainSystem.AutoSelectCaptains();

        knifeSystem.Start();
    }

    private void StartLive()
    {
        knifeSystem.Reset();

        if (_matchData == null)
        {
            return;
        }

        _logger.LogInformation("Starting Live Match");

        ConVar.Find("sv_disable_teamselect_menu")?.SetValue(1);

        ConVar.Find("mp_backup_round_auto")?.SetValue(1);

        ConVar.Find("mp_maxrounds")?.SetValue(_matchData.options.mr * 2);
        ConVar.Find("mp_overtime_enable")?.SetValue(_matchData.options.overtime);

        _gameServer.SendCommands([$"exec 5stack.{_matchData.options.type.ToLower()}.cfg"]);

        ApplyAntiWallhack();
        PublishAntiWallhackStatus();

        Server.NextFrame(() =>
        {
            _gameDemos.Start();
            _backUpManagement.Setup();

            Server.NextFrame(() =>
            {
                ResumeMatch(null, true);

                if (IsWarmup() || IsKnife())
                {
                    _gameServer.SendCommands(["mp_restartgame 1;mp_warmup_end;"]);
                }

                // Sync CS2's initial timeout state to DB after cfg loads
                Server.NextFrame(() =>
                {
                    _timeoutSystem.PublishTimeoutState();
                });
            });
        });
    }

    private void ApplyAntiWallhack()
    {
        if (_matchData == null)
        {
            return;
        }

        if (ConVar.Find("cs2fow_enable") == null)
        {
            _logger.LogInformation("CS2FOW is not loaded; skipping anti-wallhack apply");
            return;
        }

        _gameServer.SendCommands([$"cs2fow_enable {(_matchData.options.anti_wallhack ? 1 : 0)}"]);
    }

    private void PublishAntiWallhackStatus()
    {
        if (_matchData == null)
        {
            return;
        }

        bool pluginLoaded = ConVar.Find("cs2fow_enable") != null;

        string mapsDir = Path.Join(
            Server.GameDirectory,
            "csgo",
            "addons",
            "cs2fow",
            "data",
            "maps"
        );
        string bakePath = Path.Join(mapsDir, $"{Server.MapName}.bvh8");

        if (pluginLoaded && !Directory.Exists(mapsDir))
        {
            _logger.LogWarning(
                $"CS2FOW maps directory missing at {mapsDir}; bake layout may have changed"
            );
        }

        bool active = _matchData.options.anti_wallhack && pluginLoaded && File.Exists(bakePath);

        _matchEvents.PublishGameEvent(
            "antiWallhackStatus",
            new Dictionary<string, object> { { "active", active } }
        );
    }

    public void EnforceMemberTeam(CCSPlayerController player, CsTeam? currentTeam = null)
    {
        CsTeam expectedTeam = GetExpectedTeam(player);

        if (expectedTeam == CsTeam.None)
        {
            return;
        }

        if (currentTeam == null)
        {
            currentTeam = player.Team;
        }

        if (currentTeam != expectedTeam)
        {
            _logger.LogInformation(
                $"Changing Team {player.PlayerName} {currentTeam} -> {expectedTeam}"
            );

            TimerUtility.AddTimer(
                0.1f,
                () =>
                {
                    player.ChangeTeam(expectedTeam);
                }
            );

            _gameServer.Message(
                HudDestination.Chat,
                $" You've been assigned to {(expectedTeam == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{TeamUtility.CSTeamToString(expectedTeam)}.",
                player
            );
        }

        if (
            IsWarmup()
            || (MatchUtility.Rules()?.FreezePeriod == true) && expectedTeam != CsTeam.Spectator
        )
        {
            player.Respawn();
        }

        captainSystem.IsCaptain(player, expectedTeam);
    }

    public CsTeam GetExpectedTeam(CCSPlayerController player)
    {
        MatchData? matchData = GetMatchData();
        MatchMap? currentMap = GetCurrentMap();

        if (matchData == null || currentMap == null)
        {
            return CsTeam.None;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.PlayerName
        );

        if (member == null)
        {
            return CsTeam.Spectator;
        }

        UpdatePlayerName(player, member.name);

        if (member.is_banned)
        {
            player.Disconnect(NetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED);
            return CsTeam.None;
        }

        if (member.is_muted)
        {
            player.VoiceFlags = VoiceFlags.Muted;
        }
        else
        {
            player.VoiceFlags = VoiceFlags.Normal;
        }

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        if (lineup_id == null)
        {
            return CsTeam.None;
        }

        return TeamUtility.GetLineupSide(
            matchData,
            currentMap,
            lineup_id.Value,
            _gameServer.GetTotalRoundsPlayed()
        );
    }

    private void KickBots()
    {
        if (_matchData == null)
        {
            return;
        }

        if (_environmentService.AllowBots())
        {
            _logger.LogInformation("Environment allows bots");
            int expectedPlayers = GetExpectedPlayerCount();

            _gameServer.SendCommands(["bot_quota_mode normal", $"bot_quota {expectedPlayers / 2}"]);

            return;
        }

        _gameServer.SendCommands(["bot_kick"]);
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
            MatchMember? member = MatchUtility.GetMemberFromLineup(
                _matchData,
                player.SteamID.ToString(),
                player.PlayerName
            );
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

    public void UpdatePlayerName(CCSPlayerController player, string name, string? tag = null)
    {
        if (player == null || player.IsBot)
        {
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        if (player.PlayerName != name)
        {
            player.PlayerName = name;
            CounterStrikeSharp.API.Utilities.SetStateChanged(
                player,
                "CBasePlayerController",
                "m_iszPlayerName"
            );
        }

        if (tag != null)
        {
            tag = $"{tag.Trim()} |";
        }
        else
        {
            MatchData? matchData = GetMatchData();
            if (matchData != null)
            {
                tag = MatchUtility.GetPlayerLineupTag(matchData, player);
            }
        }

        if (player.Clan != tag)
        {
            player.Clan = tag ?? "";

            CounterStrikeSharp.API.Utilities.SetStateChanged(
                player,
                "CCSPlayerController",
                "m_szClan"
            );
            CounterStrikeSharp.API.Utilities.SetStateChanged(
                player,
                "CCSPlayerController",
                "m_szClanName"
            );

            var gameRules = CounterStrikeSharp
                .API.Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault();

            if (gameRules is null)
            {
                return;
            }

            gameRules.GameRules!.NextUpdateTeamClanNamesTime = Server.CurrentTime - 0.01f;
            CounterStrikeSharp.API.Utilities.SetStateChanged(
                gameRules,
                "CCSGameRules",
                "m_fNextUpdateTeamClanNamesTime"
            );
        }

        // force the client to update the player name
        new EventNextlevelChanged(false).FireEventToClient(player);
    }

    public void SetupBroadcast()
    {
        if (_matchData == null || IsMapFinished())
        {
            return;
        }

        ConVar.Find("tv_delay")?.SetValue(_matchData.options.tv_delay);
        ConVar.Find("tv_record_immediate")?.SetValue(1);

        if (_matchData.options.use_playcast)
        {
            _logger.LogInformation(
                $"TV Broadcast URL: {_environmentService.GetRelayUrl()}/{_matchData.id}"
            );

            ConVar
                .Find("tv_broadcast_url")
                ?.SetValue($"{_environmentService.GetRelayUrl()}/{_matchData.id}");
            ConVar
                .Find("tv_broadcast_origin_auth")
                ?.SetValue($"{_matchData.id}:{_matchData.password}");

            Server.NextFrame(() =>
            {
                _gameServer.SendCommands(["tv_broadcast 1"]);
                _logger.LogInformation("Sending TV Broadcast command");
            });
        }
        else
        {
            ConVar.Find("sv_deltaticks_enforce")?.SetValue(0);
        }
    }

    public void Reset()
    {
        _logger.LogInformation("resetting match state");
        _resumeMessageTimer?.Kill();
        _mapChangeCountdownTimer?.Kill();

        _currentMapStatus = eMapStatus.Unknown;

        gameEnded = false;

        readySystem.Reset();
        captainSystem.Reset();
        knifeSystem.Reset();
        _surrenderSystem.Reset();
    }
}
