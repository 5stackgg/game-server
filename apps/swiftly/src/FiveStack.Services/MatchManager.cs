using System.Text.RegularExpressions;
using System.Threading;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.GameEventDefinitions;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.ProtobufDefinitions;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Translation;

namespace FiveStack;

public class MatchManager
{
    // Extra seconds added to mp_match_restart_delay on dedicated servers so CS2's
    // auto-restart stays behind the plugin's stop-recording + demo-upload flow.
    private const int DemoUploadRestartBufferSeconds = 300;

    private MatchData? _matchData;
    private eMapStatus _currentMapStatus = eMapStatus.Unknown;
    private Guid? _activeMapId;

    // Distinct from _activeMapId, which is stamped at map start for event
    // attribution: this only moves once the server-side setup (cfg exec +
    // game mode) has actually run for that map. Gating setup on _activeMapId
    // instead means every map the plugin itself loads is already "active"
    // before setup runs, so maps 2+ silently skip their cfg.
    private Guid? _configuredMapId;
    private CancellationTokenSource? _resumeMessageTimer;
    private CancellationTokenSource? _playcastWindowTimer;
    public bool gameEnded = false;

    private readonly ISwiftlyCore _core;
    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly GameDemos _gameDemos;
    private readonly ILogger<MatchManager> _logger;
    private readonly GameBackUpRounds _backUpManagement;
    private readonly SurrenderSystem _surrenderSystem;
    private readonly ILocalizer _localizer;

    private readonly EnvironmentService _environmentService;
    private readonly TimeoutSystem _timeoutSystem;
    public TimeoutSystem timeoutSystem => _timeoutSystem;
    public ReadySystem readySystem;

    public KnifeSystem knifeSystem;
    public CaptainSystem captainSystem;
    public MatchService _matchService;
    private readonly RankSystem _rankSystem;

    private int _remainingMapChangeDelay = 0;
    public CancellationTokenSource? _mapChangeCountdownTimer;

    public MatchManager(
        ILogger<MatchManager> logger,
        ISwiftlyCore core,
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
        ILocalizer localizer
    )
    {
        _logger = logger;
        _core = core;
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

    public eMapStatus GetCurrentMapStatus()
    {
        return _currentMapStatus;
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

        TimerUtility.Kill(_resumeMessageTimer);

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

    public void PauseMatch(string? message = null, bool skipUpdate = false, int retriesLeft = 20)
    {
        // CS2 will not hold mp_pause_match while its own tactical timeout is
        // running -- the command is accepted and silently does nothing, so the
        // match resumes as soon as the timeout ends. Wait it out instead.
        // ResumeMatch already refuses in the same situation.
        if (_timeoutSystem.IsTimeoutActive())
        {
            if (retriesLeft <= 0)
            {
                _logger.LogWarning(
                    "Timeout still active, giving up on pausing the match"
                );
                return;
            }

            _logger.LogInformation(
                $"Timeout is active, retrying pause in 1s ({retriesLeft} attempts left)"
            );

            TimerUtility.AddTimer(
                1,
                () => PauseMatch(message, skipUpdate, retriesLeft - 1)
            );
            return;
        }

        _gameServer.SendCommands(["mp_pause_match"]);

        if (IsPaused())
        {
            return;
        }

        _logger.LogInformation($"Pausing Match{(message != null ? $": {message}" : "")}");

        if (message != null)
        {
            _gameServer.Message(MessageType.Alert, message);
        }

        TimerUtility.Kill(_resumeMessageTimer);
        _resumeMessageTimer = TimerUtility.Repeat(
            3,
            () =>
            {
                if (!IsFreezePeriod() || !IsPaused() || _backUpManagement.IsResettingRound())
                {
                    return;
                }

                _gameServer.Message(
                    MessageType.Alert,
                    _localizer["match.resume_hint", CommandUtility.PublicChatTrigger]
                );
            }
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

        TimerUtility.Kill(_resumeMessageTimer);
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
            _gameServer.Message(MessageType.Center, message);
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
                    { "player_name", player.Name },
                    { "team", TeamUtility.TeamToString(player.Controller.Team) },
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
            $"Game State {_currentMap.status} expected {_currentMap.map.name}, on {_core.Engine.GlobalVars.MapName}"
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
        else if (!_core.Engine.GlobalVars.MapName.ToString().ToLower().Contains(_currentMap.map.name.ToLower()))
        {
            _logger.LogInformation(
                $"Map switch required: on {_core.Engine.GlobalVars.MapName}, need {_currentMap.map.name} (match {_matchData.id})"
            );
            ChangeMap(_currentMap.map);
            return;
        }

        bool wasAlreadySetup = _configuredMapId == _currentMap.id;
        _activeMapId = _currentMap.id;

        if (_currentMapStatus == eMapStatus.WaitingForTV)
        {
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
            string configDirectory = Path.Join(_core.GameDirectory, "csgo", "cfg");

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
            _configuredMapId = _currentMap.id;

            _gameServer.SendCommands([$"exec 5stack.{_matchData.options.type.ToLower()}.cfg"]);

            if (_matchData.is_lan)
            {
                _gameServer.SendCommands(["exec 5stack.lan.cfg"]);
            }

            ApplyWorkshopBlockedCvars();
        }

        _core.Scheduler.NextTick(() =>
        {
            if (!wasAlreadySetup)
            {
                SetupGameMode();
            }

            FiveStackPlugin.SetPasswordBuffer(_matchData.password);
            SetConVar("sv_password", _matchData.password);

            // mp_match_restart_delay is when CS2 auto-restarts the map after game
            // over. On a dedicated server the plugin runs a longer end-of-map flow
            // (wait for GOTV, stop recording, upload the demo) and changes the map
            // itself, so push CS2's auto-restart out past that flow — otherwise the
            // map rotates mid-upload. Game-server nodes skip the upload, so tv_delay
            // is enough there.
            int matchRestartDelay = _matchData.options.tv_delay;
            if (!_environmentService.isOnGameServerNode())
            {
                matchRestartDelay += DemoUploadRestartBufferSeconds;
            }
            SetConVar("mp_match_restart_delay", matchRestartDelay);

            SetConVar("hostname", "5Stack.gg");

            // Ensure round backups are configured on every setup (not only when
            // going live) — a plugin reload of a live/paused match never hits
            // StartLive, so the backup convars would otherwise never be set.
            _backUpManagement.Setup();

            if (MatchUtility.MapStatusStringToEnum(_currentMap.status) != _currentMapStatus)
            {
                UpdateMapStatus(MatchUtility.MapStatusStringToEnum(_currentMap.status));
            }

            if (IsWarmup())
            {
                _gameServer.Message(MessageType.Alert, _localizer["match.received_data"]);
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

        Team lineup1StartingSide = TeamUtility.TeamStringToTeam(_currentMap.lineup_1_side);

        string mp_teamname_1 = _core.ConVar.FindAsString("mp_teamname_1")?.ValueAsString ?? "";
        string mp_teamname_2 = _core.ConVar.FindAsString("mp_teamname_2")?.ValueAsString ?? "";

        string expectedTeamname1 =
            lineup1StartingSide == Team.CT
                ? _matchData.lineup_1.name
                : _matchData.lineup_2.name;

        string expectedTeamname2 =
            lineup1StartingSide == Team.CT
                ? _matchData.lineup_2.name
                : _matchData.lineup_1.name;

        SetConVar("mp_teamname_1", expectedTeamname1);
        SetConVar("mp_teamname_2", expectedTeamname2);

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

    private const int PlaycastHeartbeatIntervalSeconds = 15;

    // The playcast window is the only stretch of a match where the plugin goes
    // silent for minutes with the map's result still unwritten. Without a
    // heartbeat, a timer that never fired and a process that was killed leave
    // the exact same trace: nothing.
    public void StartPlaycastWindowHeartbeat(int tvDelay)
    {
        StopPlaycastWindowHeartbeat();

        // Anchored to a clock, not a tick count: CSS timers first fire after the
        // interval and Swiftly's fire immediately, so counting down would report
        // a different number on each runtime.
        DateTime startedAt = DateTime.UtcNow;

        _playcastWindowTimer = TimerUtility.Repeat(
            PlaycastHeartbeatIntervalSeconds,
            () =>
            {
                int remaining = tvDelay - (int)(DateTime.UtcNow - startedAt).TotalSeconds;

                if (remaining <= 0)
                {
                    StopPlaycastWindowHeartbeat();
                    return;
                }

                _logger.LogInformation(
                    "playcast window: {Remaining}s until HandleEndOfMap (match={MatchId} map={MapId} status={Status} gameEnded={GameEnded} players={Players})",
                    remaining,
                    _matchData?.id,
                    _activeMapId,
                    _currentMapStatus,
                    gameEnded,
                    MatchUtility.Players().Count
                );
            }
        );
    }

    public void StopPlaycastWindowHeartbeat()
    {
        TimerUtility.Kill(_playcastWindowTimer);
        _playcastWindowTimer = null;
    }

    public void delayChangeMap(int delay)
    {
        _remainingMapChangeDelay = delay;
        _logger.LogInformation(
            $"Map change in {delay}s then fetching next match (match {_matchData?.id.ToString() ?? "none"})"
        );

        TimerUtility.Kill(_mapChangeCountdownTimer);

        TimerUtility.AddTimer(
            delay,
            () =>
            {
                _gameDemos.StopTV();
                _logger.LogInformation("map change delay complete");
                TimerUtility.Kill(_mapChangeCountdownTimer);
                _mapChangeCountdownTimer = null;
                _matchService.GetMatchFromApi();
            }
        );

        _mapChangeCountdownTimer = TimerUtility.Repeat(
            1,
            () =>
            {
                if (_remainingMapChangeDelay > 0)
                {
                    _gameServer.Message(
                        MessageType.Alert,
                        _localizer["match.tv_delay", _remainingMapChangeDelay]
                    );
                    _remainingMapChangeDelay--;
                }
            }
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

        // The mode decides which layout the map loads with (Wingman gets the
        // 2v2 version), so it ships in the same batch as the level change and
        // ahead of it — Valve's documented form is "game_type 0; game_mode 2;
        // map <name>". Setting it after the load leaves the map on whatever
        // the previous map's mode was.
        string[] gameModeCommands = GetGameModeCommands();

        if (map.workshop_map_id == null && _core.Engine.IsMapValid(map.name))
        {
            _logger.LogInformation(
                $"Changing Map {map.name} (match {_matchData?.id.ToString() ?? "none"})"
            );
            _gameServer.SendCommands([.. gameModeCommands, $"changelevel \"{map.name}\""]);
        }
        else
        {
            _logger.LogInformation(
                $"Changing Map {map.name} / {map.workshop_map_id} (match {_matchData?.id.ToString() ?? "none"})"
            );
            _gameServer.SendCommands(
                [.. gameModeCommands, $"host_workshop_map {map.workshop_map_id}"]
            );
        }
    }

    // game_type/game_mode are integers, not mode names: game_type 0 (Classic)
    // with game_mode 1 = Competitive, game_mode 2 = Wingman/2v2. Sent as console
    // commands rather than typed convar writes — a convar lookup that misses or
    // comes back differently typed would silently leave the server in the wrong
    // mode.
    private string[] GetGameModeCommands()
    {
        if (_matchData == null)
        {
            return [];
        }

        int gameMode =
            _matchData.options.type == "Duel" || _matchData.options.type == "Wingman" ? 2 : 1;

        return ["game_type 0", $"game_mode {gameMode}"];
    }

    private void SetupGameMode()
    {
        if (_matchData == null)
        {
            return;
        }

        string[] gameModeCommands = GetGameModeCommands();

        // game_type/game_mode only take effect after a restart, but restarting an
        // in-progress match (e.g. on a plugin reload) would wrongly reset it. Only
        // restart during warmup, when a fresh game start is expected.
        if (!IsWarmup())
        {
            _gameServer.SendCommands(gameModeCommands);
            _logger.LogInformation("SetupGameMode: not in warmup, skipping mp_restartgame");
            return;
        }

        _gameServer.SendCommands([.. gameModeCommands, "mp_restartgame 1"]);
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
        SetConVar("sv_disable_teamselect_menu", false);
        _gameServer.SendCommands(["exec 5stack.warmup.cfg"]);

        knifeSystem.Reset();

        if (_matchData == null)
        {
            return;
        }

        _core.Scheduler.NextTick(() =>
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
        SetConVar("sv_disable_teamselect_menu", true);

        if (_matchData == null || IsKnife())
        {
            return;
        }

        captainSystem.AutoSelectCaptains();

        knifeSystem.Start();
    }

    // CS2 treats a cfg exec'd on a workshop map as an untrusted "workshop
    // config" and silently drops a set of convars from it, logging
    // "DISALLOWED WORKSHOP CONVAR: <name>". The block only applies to values
    // coming from a cfg file -- setting the same convar directly is fine --
    // so anything on that list has to be re-asserted here or it never takes
    // effect on a workshop map, leaving CS2's built-in defaults in place.
    //
    // Values mirror shared/cfg/5stack.{competitive,duel,wingman}.cfg, which
    // all agree on these. Keep them in step; an admin cfg override is read
    // back out of the override text below so it still wins.
    private static readonly Dictionary<string, string> WorkshopBlockedCvars =
        new()
        {
            { "mp_team_timeout_time", "31" },
            { "mp_team_timeout_max", "3" },
            { "mp_overtime_limit", "0" },
            { "mp_halftime_pausematch", "0" },
            { "mp_competitive_endofmatch_extra_time", "155" },
            { "sv_pausable", "1" },
        };

    private void ApplyWorkshopBlockedCvars()
    {
        if (_matchData == null)
        {
            return;
        }

        // An override replaces the whole cfg file, so its values are the ones
        // that should win -- but it is exec'd the same way and therefore just
        // as blocked. Read the convar back out of the override text.
        string? overrideCfg = null;
        _matchData.options.cfg_overrides?.TryGetValue(
            _matchData.options.type.ToLower(),
            out overrideCfg
        );

        foreach (var cvar in WorkshopBlockedCvars)
        {
            string value = cvar.Value;

            if (!string.IsNullOrEmpty(overrideCfg))
            {
                var match = Regex.Match(
                    overrideCfg,
                    $@"^\s*{Regex.Escape(cvar.Key)}\s+(\S+)",
                    RegexOptions.Multiline
                );

                if (match.Success)
                {
                    value = match.Groups[1].Value;
                }
            }

            _gameServer.SendCommands([$"{cvar.Key} {value}"]);
        }
    }

    private void StartLive()
    {
        knifeSystem.Reset();

        if (_matchData == null)
        {
            return;
        }

        _logger.LogInformation("Starting Live Match");

        SetConVar("sv_disable_teamselect_menu", true);

        // mp_backup_round_auto is enabled in _backUpManagement.Setup().

        SetConVar("mp_maxrounds", _matchData.options.mr * 2);
        SetConVar("mp_overtime_enable", _matchData.options.overtime);
        SetConVar("mp_halftime_pausematch", _matchData.options.halftime_pausematch);

        if (_matchData.options.round_restart_delay.HasValue)
        {
            SetConVar("mp_round_restart_delay", _matchData.options.round_restart_delay.Value);
        }

        _gameServer.SendCommands([$"exec 5stack.{_matchData.options.type.ToLower()}.cfg"]);

        ApplyWorkshopBlockedCvars();

        _core.Scheduler.NextTick(() =>
        {
            _gameDemos.Start();
            _backUpManagement.Setup();

            _core.Scheduler.NextTick(() =>
            {
                ResumeMatch(null, true);

                if (IsWarmup() || IsKnife())
                {
                    _gameServer.SendCommands(["mp_restartgame 1;mp_warmup_end;"]);
                }

                _core.Scheduler.NextTick(() =>
                {
                    _timeoutSystem.PublishTimeoutState();
                });
            });
        });
    }

    public void EnforceMemberTeam(IPlayer player, Team? currentTeam = null)
    {
        Team expectedTeam = GetExpectedTeam(player);

        if (expectedTeam == Team.None)
        {
            return;
        }

        if (currentTeam == null)
        {
            currentTeam = player.Controller.Team;
        }

        bool shouldRespawn =
            IsWarmup()
            || (MatchUtility.Rules()?.FreezePeriod == true) && expectedTeam != Team.Spectator;

        if (currentTeam != expectedTeam)
        {
            _logger.LogInformation(
                $"[team] Changing Team {player.Name} ({player.SteamID}) {currentTeam} -> {expectedTeam} (respawn: {shouldRespawn})"
            );

            TimerUtility.AddTimer(
                0.1f,
                () =>
                {
                    if (!player.IsValid)
                    {
                        return;
                    }

                    player.ChangeTeam(expectedTeam);

                    _logger.LogInformation(
                        $"[team] ChangeTeam applied {player.Name} ({player.SteamID}) -> {expectedTeam}"
                    );

                    // Respawn only once the team change has landed. Respawning
                    // first spawns the player while still unassigned, and the
                    // weapons a spawn creates are what the inventory plugin
                    // skins — a spawn on the wrong team wastes that one shot.
                    if (shouldRespawn)
                    {
                        _core.Scheduler.NextTick(() =>
                        {
                            if (!player.IsValid)
                            {
                                return;
                            }

                            _logger.LogInformation(
                                $"[team] Respawning {player.Name} ({player.SteamID}) after team change -> {expectedTeam}"
                            );
                            player.Respawn();
                        });
                    }
                }
            );

            _gameServer.Message(
                MessageType.Chat,
                $" You've been assigned to {(expectedTeam == Team.T ? "[gold]" : "[blue]")}{TeamUtility.TeamToString(expectedTeam)}.",
                player
            );
        }
        else if (shouldRespawn)
        {
            _logger.LogInformation(
                $"[team] Respawning {player.Name} ({player.SteamID}) (already on {expectedTeam})"
            );
            player.Respawn();
        }

        captainSystem.IsCaptain(player, expectedTeam);
    }

    public Team GetExpectedTeam(IPlayer player)
    {
        MatchData? matchData = GetMatchData();
        MatchMap? currentMap = GetCurrentMap();

        if (matchData == null || currentMap == null)
        {
            return Team.None;
        }

        MatchMember? member = MatchUtility.GetMemberFromLineup(
            matchData,
            player.SteamID.ToString(),
            player.Name
        );

        if (member == null)
        {
            return Team.Spectator;
        }

        UpdatePlayerName(player, member.name);

        if (member.is_banned)
        {
            player.Kick(
                "banned",
                ENetworkDisconnectionReason.NETWORK_DISCONNECT_BANADDED
            );
            return Team.None;
        }

        if (member.is_muted)
        {
            player.VoiceFlags = VoiceFlagValue.Muted;
        }
        else
        {
            player.VoiceFlags = VoiceFlagValue.Normal;
        }

        Guid? lineup_id = MatchUtility.GetPlayerLineup(matchData, player);

        if (lineup_id == null)
        {
            return Team.None;
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
                player.Name
            );
            if (member == null)
            {
                shouldUpdateApi = true;
            }

            if (TeamUtility.TeamStringToTeam(_currentMap.lineup_1_side) == player.Controller.Team)
            {
                ((List<object>)lineups["lineup_1"]).Add(
                    new
                    {
                        name = player.Name,
                        steam_id = player.SteamID.ToString(),
                        captain = captainSystem.IsCaptain(player, player.Controller.Team),
                    }
                );
            }
            else
            {
                ((List<object>)lineups["lineup_2"]).Add(
                    new
                    {
                        name = player.Name,
                        steam_id = player.SteamID.ToString(),
                        captain = captainSystem.IsCaptain(player, player.Controller.Team),
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

    public void UpdatePlayerName(IPlayer player, string name, string? tag = null)
    {
        if (player == null)
        {
            return;
        }

        if (player.IsFakeClient)
        {
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        bool changed = false;

        if (player.Name != name)
        {
            player.Controller.PlayerName = name;
            player.Controller.PlayerNameUpdated();
            changed = true;
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

        if (player.Controller.Clan != tag)
        {
            player.Controller.Clan = tag ?? "";
            player.Controller.ClanUpdated();
            changed = true;

            var gameRules = _core
                .EntitySystem.GetAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault();

            if (gameRules?.GameRules != null)
            {
                gameRules.GameRules.NextUpdateTeamClanNamesTime =
                    _core.Engine.GlobalVars.CurrentTime - 0.01f;
                gameRules.GameRulesUpdated();
            }
        }

        // force the client to update the player name; firing this unconditionally
        // rebuilds every scoreboard on each call, which strobes under repeating timers
        if (changed)
        {
            _core.GameEvent.FireToPlayer<EventNextlevelChanged>(player.Slot);
        }
    }

    public void SetupBroadcast()
    {
        if (_matchData == null || IsMapFinished())
        {
            return;
        }

        SetConVar("tv_delay", _matchData.options.tv_delay);
        SetConVar("tv_record_immediate", 1);

        if (_matchData.options.use_playcast)
        {
            _logger.LogInformation(
                $"TV Broadcast URL: {_environmentService.GetRelayUrl()}/{_matchData.id}"
            );

            SetConVar("tv_broadcast_url", $"{_environmentService.GetRelayUrl()}/{_matchData.id}");
            SetConVar("tv_broadcast_origin_auth", $"{_matchData.id}:{_matchData.password}");

            _core.Scheduler.NextTick(() =>
            {
                _gameServer.SendCommands(["tv_broadcast 1"]);
                _logger.LogInformation("Sending TV Broadcast command");
            });
        }
        else
        {
            SetConVar("sv_deltaticks_enforce", 0);
        }
    }

    public void Reset()
    {
        _logger.LogInformation("resetting match state");
        TimerUtility.Kill(_resumeMessageTimer);
        TimerUtility.Kill(_mapChangeCountdownTimer);
        StopPlaycastWindowHeartbeat();

        _currentMapStatus = eMapStatus.Unknown;
        _configuredMapId = null;

        gameEnded = false;

        readySystem.Reset();
        captainSystem.Reset();
        knifeSystem.Reset();
        _surrenderSystem.Reset();
    }

    private void SetConVar(string name, string value)
    {
        // String convar: the string accessor is the correct one.
        if (!TrySetConVar<string>(name, value))
        {
            _logger.LogWarning("SetConVar: could not set {Name}={Value}", name, value);
        }
    }

    private void SetConVar(string name, int value)
    {
        // The caller passes an int, but the convar may actually be int/float/bool.
        // SwiftlyS2's Find<T> throws on a type mismatch, so try each numeric type
        // and use the matching typed accessor (which does a reliable native write).
        if (
            TrySetConVar(name, value)
            || TrySetConVar(name, (float)value)
            || TrySetConVar(name, value != 0)
        )
        {
            return;
        }

        _logger.LogWarning("SetConVar: could not set {Name}={Value}", name, value);
    }

    private void SetConVar(string name, bool value)
    {
        if (TrySetConVar(name, value) || TrySetConVar(name, value ? 1 : 0))
        {
            return;
        }

        _logger.LogWarning("SetConVar: could not set {Name}={Value}", name, value);
    }

    // Sets a convar via the typed SwiftlyS2 accessor. Returns false (instead of
    // throwing) when the convar doesn't exist or isn't of type T, so callers can
    // fall through to the correct type.
    private bool TrySetConVar<T>(string name, T value)
    {
        try
        {
            var conVar = _core.ConVar.Find<T>(name);
            if (conVar == null)
            {
                return false;
            }

            conVar.Value = value;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
