using System.Runtime.InteropServices;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

[UnmanagedFunctionPointer(CallingConvention.ThisCall)]
delegate IntPtr GetAddonNameDelegate(IntPtr self);

public class MatchManager
{
    private MatchData? _matchData;
    private eMapStatus _currentMapStatus = eMapStatus.Unknown;

    private readonly MatchEvents _matchEvents;
    private readonly GameServer _gameServer;
    private readonly GameDemos _matchDemos;
    private readonly ILogger<MatchManager> _logger;
    private readonly GameBackUpRounds _backUpManagement;
    private readonly SurrenderSystem _surrenderSystem;

    private readonly EnvironmentService _environmentService;
    private readonly TimeoutSystem _timeoutSystem;
    public ReadySystem readySystem;

    // public CoachSystem _coachSystem;
    public KnifeSystem knifeSystem;
    public CaptainSystem captainSystem;
    public INetworkServerService _networkServerService;

    public MatchManager(
        ILogger<MatchManager> logger,
        MatchEvents matchEvents,
        GameServer gameServer,
        GameBackUpRounds backUpManagement,
        GameDemos matchDemos,
        KnifeSystem KnifeSystem,
        ReadySystem ReadySystem,
        CaptainSystem CaptainSystem,
        EnvironmentService environmentService,
        INetworkServerService networkServerService,
        TimeoutSystem timeoutSystem,
        SurrenderSystem surrenderSystem
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
        _networkServerService = networkServerService;
        _timeoutSystem = timeoutSystem;
        _surrenderSystem = surrenderSystem;
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
        return _currentMapStatus == eMapStatus.Paused;
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

    public void PauseMatch(string? message = null)
    {
        _logger.LogInformation($"Pausing Match: {message}");
        UpdateMapStatus(eMapStatus.Paused);

        if (message != null)
        {
            _gameServer.Message(HudDestination.Center, message);
        }
    }

    public void ResumeMatch(string? message = null)
    {
        if (_timeoutSystem.IsTimeoutActive())
        {
            _logger.LogInformation("Timeout is active, cannot resume match");
            return;
        }

        _logger.LogInformation($"Resuming Match: {message}");
        _gameServer.SendCommands(new[] { "mp_unpause_match" });

        _timeoutSystem.resumeVote?.CancelVote();
        _backUpManagement.restoreRoundVote?.CancelVote();

        if (!IsPaused())
        {
            return;
        }

        UpdateMapStatus(isOverTime() ? eMapStatus.Overtime : eMapStatus.Live);

        if (message != null)
        {
            _gameServer.Message(HudDestination.Center, message);
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

        var currentMap = GetCurrentMap();

        if (
            _currentMapStatus == eMapStatus.Warmup
            && status != eMapStatus.Warmup
            && currentMap != null
            && currentMap.order == 1
        )
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

                _gameServer.SendCommands(new[] { "mp_pause_match" });
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
                    _gameServer.SendCommands(new[] { "mp_restartgame 3" });
                }
                break;
            case eMapStatus.Surrendered:
            case eMapStatus.UploadingDemo:
                _matchDemos.Stop();
                _surrenderSystem.ResetSurrender();
                break;
            case eMapStatus.Finished:
                _surrenderSystem.ResetSurrender();
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

        if (_matchData.is_lan)
        {
            _gameServer.SendCommands(new[] { "exec 5stack.lan.cfg" });
        }

        MatchMap? _currentMap = GetCurrentMap();

        if (_currentMap == null)
        {
            _logger.LogWarning("match does not have a current map");
            return;
        }

        if (_currentMapStatus == eMapStatus.Unknown)
        {
            SetupTeams();
        }

        _logger.LogInformation(
            $"Game State {_currentMap.status} on ({_currentMap.map.name}) / {Server.MapName}"
        );

        if (_currentMap.map.workshop_map_id is not null)
        {
            string currentWorkshopID = GetWorkshopID();
            _logger.LogInformation(
                $"Checking Workshop Map {_currentMap.map.workshop_map_id} / {currentWorkshopID}"
            );

            if (_currentMap.map.workshop_map_id != currentWorkshopID)
            {
                ChangeMap(_currentMap.map);
                return;
            }
        }
        else if (!Server.MapName.ToLower().Contains(_currentMap.map.name.ToLower()))
        {
            ChangeMap(_currentMap.map);
            return;
        }

        FiveStackPlugin.SetPasswordBuffer(_matchData.password);
        _gameServer.SendCommands(new[] { $"sv_password \"{_matchData.password}\"" });

        if (MatchUtility.MapStatusStringToEnum(_currentMap.status) != _currentMapStatus)
        {
            UpdateMapStatus(MatchUtility.MapStatusStringToEnum(_currentMap.status));
        }

        if (IsWarmup())
        {
            _gameServer.Message(HudDestination.Alert, "Received Match Data");
        }
    }

    private unsafe string GetWorkshopID()
    {
        IntPtr networkGameServer = _networkServerService.GetIGameServerHandle();
        IntPtr vtablePtr = Marshal.ReadIntPtr(networkGameServer);
        IntPtr functionPtr = Marshal.ReadIntPtr(vtablePtr + (25 * IntPtr.Size));
        var getAddonName = Marshal.GetDelegateForFunctionPointer<GetAddonNameDelegate>(functionPtr);
        IntPtr result = getAddonName(networkGameServer);
        return Marshal.PtrToStringAnsi(result)!.Split(',')[0];
    }

    public void SetupTeams()
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

        _gameServer.SendCommands(
            new[]
            {
                $"{lineup1Side} {_matchData.lineup_1.name}",
                $"{lineup2Side} {_matchData.lineup_2.name}",
            }
        );

        foreach (var player in MatchUtility.Players())
        {
            EnforceMemberTeam(player);
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

    private void SetupGameMode()
    {
        if (_matchData == null)
        {
            return;
        }

        int? gameMode = ConVar.Find("game_mode")?.GetPrimitiveValue<int>();

        if (_matchData.options.type == "Wingman" || _matchData.options.type == "Duel")
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
        knifeSystem.ResetKnifeRound();

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
        knifeSystem.ResetKnifeRound();

        if (_matchData == null)
        {
            return;
        }

        _logger.LogInformation("Starting Live Match");
        _gameServer.SendCommands(new[] { "mp_unpause_match" });

        List<string> commands = new List<string> { "exec 5stack.live.cfg" };

        if (_matchData.options.cfg_override != "")
        {
            // Split the cfg_override string by newlines and add each line as a separate command
            string[] cfgLines = _matchData.options.cfg_override.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries
            );
            commands.AddRange(cfgLines);
        }
        else
        {
            commands.Add($"exec 5stack.{_matchData.options.type.ToLower()}.cfg");
        }

        commands.Add($"mp_maxrounds {_matchData.options.mr * 2}");
        commands.Add($"mp_overtime_enable {_matchData.options.overtime}");

        _gameServer.SendCommands(commands.ToArray());

        await _backUpManagement.DownloadBackupRounds();

        Server.NextFrame(() =>
        {
            _matchDemos.Start();
            _backUpManagement.Setup();

            if (IsWarmup())
            {
                _gameServer.SendCommands(new[] { "mp_warmup_end" });

                // if we can restore from backup we will prompt the for a vote to restore
                // most likely this happeend because of a server crash
                _backUpManagement.CheckForBackupRestore();
            }
        });
    }

    public void EnforceMemberTeam(CCSPlayerController player, CsTeam? currentTeam = null)
    {
        CsTeam expectedTeam = GetExpectedTeam(player);

        if (expectedTeam == CsTeam.None)
        {
            _logger.LogInformation($"No expected team for {player.PlayerName}");
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

            // allow them to click the menu, they just get switched really quick
            player.ChangeTeam(expectedTeam);
            _gameServer.Message(
                HudDestination.Chat,
                $" You've been assigned to {(expectedTeam == CsTeam.Terrorist ? ChatColors.Gold : ChatColors.Blue)}{TeamUtility.CSTeamToString(expectedTeam)}.",
                player
            );
        }

        if (IsWarmup() || (MatchUtility.Rules()?.FreezePeriod == true))
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
            player.ChangeTeam(CsTeam.Spectator);
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

        string lineupName =
            matchData.lineup_1_id == lineup_id ? matchData.lineup_1.name : matchData.lineup_2.name;

        foreach (var team in MatchUtility.Teams())
        {
            if (team.ClanTeamname == lineupName)
            {
                return TeamUtility.TeamNumToCSTeam(team.TeamNum);
            }
        }

        return CsTeam.None;
    }

    private void KickBots()
    {
        if (this._matchData == null)
        {
            return;
        }

        if (_environmentService.AllowBots())
        {
            int expectedPlayers = GetExpectedPlayerCount();
            // we want to count the bots in this case
            int currentPlayers = CounterStrikeSharp.API.Utilities.GetPlayers().Count;

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

        _gameServer.SendCommands(new[] { "bot_quota_mode competitive", "bot_quota 0", "bot_kick" });
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
            tag = $"[{tag.Trim()}]";
        }

        if (tag != null && player.Clan != tag)
        {
            player.Clan = tag;
            player.ClanName = tag;

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
}
