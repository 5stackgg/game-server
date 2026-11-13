using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class ReadySystem
{
    private Timer? _readyStatusTimer;
    private Timer? _notReadyMessageTimer;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<ReadySystem> _logger;
    private readonly CoachSystem _coachSystem;
    private readonly CaptainSystem _captainSystem;
    private readonly CameraSystem _cameraSystem;

    private Dictionary<int, bool> _readyPlayers = new Dictionary<int, bool>();

    public ReadySystem(
        ILogger<ReadySystem> logger,
        GameServer gameServer,
        MatchService matchService,
        CoachSystem coachSystem,
        CaptainSystem captainSystem,
        CameraSystem cameraSystem
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchService = matchService;
        _coachSystem = coachSystem;
        _captainSystem = captainSystem;
        _cameraSystem = cameraSystem;
    }

    public void Setup()
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        if (matchData == null)
        {
            return;
        }

        _logger.LogInformation("Setting up ready system");
        Reset(true);
        SendReadyStatusMessage();
    }

    public void Reset(bool setupTimer = false)
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        match?.KillPhaseTimer(_readyStatusTimer);
        _readyStatusTimer = null;
        match?.KillPhaseTimer(_notReadyMessageTimer);
        _notReadyMessageTimer = null;

        _readyPlayers.Clear();

        if (setupTimer)
        {
            _readyStatusTimer = match?.PhaseTimer(3, SendReadyStatusMessage, true);
        }

        UpdatePlayerStatus();
    }

    public void UpdatePlayerStatus()
    {
        string readyTag = ReadyTag(true);
        string notReadyTag = ReadyTag(false);

        foreach (var player in MatchUtility.Players())
        {
            if (player.UserId == null)
            {
                continue;
            }

            // only overwrite our own ready tags; leave team, role and camera tags alone
            if (
                !string.IsNullOrEmpty(player.Clan)
                && player.Clan != readyTag
                && player.Clan != notReadyTag
            )
            {
                continue;
            }

            if (_readyStatusTimer == null)
            {
                _matchService.GetCurrentMatch()?.UpdatePlayerName(player, player.PlayerName);
                continue;
            }

            if (
                _readyPlayers.ContainsKey(player.UserId.Value) && _readyPlayers[player.UserId.Value]
            )
            {
                _matchService
                    .GetCurrentMatch()
                    ?.UpdatePlayerName(player, player.PlayerName, readyTag);
                continue;
            }
            _matchService
                .GetCurrentMatch()
                ?.UpdatePlayerName(player, player.PlayerName, notReadyTag);
        }
    }

    // A clan tag is one string the whole server sees on the scoreboard.
    private string ReadyTag(bool ready)
    {
        return _gameServer.Localize(null, ready ? "ready.tag.ready" : "ready.tag.not_ready");
    }

    public bool IsWaitingForReady()
    {
        return _readyStatusTimer != null;
    }

    public void ToggleReady(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        if (_cameraSystem.IsPlayerBlocked(player))
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(player, "camera.cannot_ready"),
                player
            );
            return;
        }

        if (!CanVote(player))
        {
            _gameServer.Message(
                HudDestination.Chat,
                _gameServer.Localize(player, "ready.not_allowed"),
                player
            );
            return;
        }

        int playerId = player.UserId.Value;

        if (!_readyPlayers.ContainsKey(playerId))
        {
            _readyPlayers[playerId] = true;
        }
        else
        {
            _readyPlayers[playerId] = !_readyPlayers[playerId];
        }

        MatchManager? currentMatch = _matchService.GetCurrentMatch();
        (int readyCount, int target) = ReadyCount();

        if (readyCount >= target)
        {
            Reset();
            currentMatch?.UpdateMapStatus(eMapStatus.Knife);
            return;
        }

        SendReadyMessage(player);
        SendReadyStatusMessage();
        SendNotReadyMessage();
        UpdatePlayerStatus();
    }

    public void UnreadyPlayer(CCSPlayerController player)
    {
        if (player.UserId == null || !IsWaitingForReady())
        {
            return;
        }

        int playerId = player.UserId.Value;
        if (_readyPlayers.ContainsKey(playerId))
        {
            _readyPlayers[playerId] = false;
        }

        SendReadyStatusMessage();
        SendNotReadyMessage();
        UpdatePlayerStatus();
    }

    public void SetupReadyMessage(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        (int totalReady, int expectedReady) = ReadyCount();

        int playerId = player.UserId.Value;
        if (_readyPlayers.ContainsKey(playerId) && _readyPlayers[playerId])
        {
            player.PrintToCenter(
                _gameServer.Localize(player, "ready.waiting_for_players", totalReady, expectedReady)
            );
            return;
        }

        if (CanVote(player))
        {
            player.PrintToCenter(
                _gameServer.Localize(player, "ready.type_to_ready", CommandUtility.PublicChatTrigger)
            );
            return;
        }

        switch (GetReadySetting())
        {
            case eReadySettings.Players:
                player.PrintToCenter(
                    _gameServer.Localize(
                        player,
                        "ready.waiting_for_players",
                        totalReady,
                        expectedReady
                    )
                );
                break;
            case eReadySettings.Admin:
                player.PrintToCenter(_gameServer.Localize(player, "ready.admin_will_start"));
                break;
            case eReadySettings.Captains:
                player.PrintToCenter(_gameServer.Localize(player, "ready.waiting_captains"));
                break;
            case eReadySettings.Coach:
                player.PrintToCenter(_gameServer.Localize(player, "ready.waiting_coach"));
                break;
        }
    }

    private (int Ready, int Target) ReadyCount()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null)
        {
            return (0, 10);
        }

        return ReadyUtility.ReadyCount(
            GetReadySetting(),
            matchData,
            MatchUtility
                .Players()
                .Where(player =>
                    player.UserId != null
                    && _readyPlayers.GetValueOrDefault(player.UserId.Value)
                    && CanVote(player)
                )
                .Select(player => (player.SteamID.ToString(), player.PlayerName)),
            match.GetExpectedPlayerCount()
        );
    }

    private (List<string> Absent, List<string> Unlinked) GetEmptySeats()
    {
        MatchManager? match = _matchService.GetCurrentMatch();
        MatchData? matchData = match?.GetMatchData();

        if (match == null || matchData == null || GetReadySetting() != eReadySettings.Players)
        {
            return (new List<string>(), new List<string>());
        }

        List<(string, string)> connected = MatchUtility
            .Players()
            .Select(player => (player.SteamID.ToString(), player.PlayerName))
            .ToList();

        return (
            ReadyUtility.EmptySeats(matchData, connected, match.GetExpectedPlayerCount()),
            ReadyUtility.UnlinkedSeats(matchData, connected, match.GetExpectedPlayerCount())
        );
    }

    public void SendReadyMessage(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        _readyPlayers.TryGetValue(player.UserId.Value, out bool isReady);

        string readyWord = _gameServer.Localize(player, isReady ? "ready.ready" : "ready.not_ready");
        string colored = isReady
            ? $"{ChatColors.Green}{readyWord}"
            : $"{ChatColors.Red}{readyWord}";
        _gameServer.Message(
            HudDestination.Chat,
            _gameServer.Localize(player, "ready.marked", colored),
            player
        );
    }

    public void SendNotReadyMessage()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        match.KillPhaseTimer(_notReadyMessageTimer);
        _notReadyMessageTimer = match.PhaseTimer(
            5,
            () =>
            {
                if (!IsWaitingForReady())
                {
                    return;
                }

                string[] notReadyPlayers = GetNotReadyPlayers();
                (List<string> absent, List<string> unlinked) = GetEmptySeats();
                if (notReadyPlayers.Length == 0 && absent.Count == 0 && unlinked.Count == 0)
                {
                    return;
                }

                _gameServer.MessageEach(
                    HudDestination.Notify,
                    viewer =>
                        _gameServer.Localize(
                            viewer,
                            "ready.players_not_ready",
                            string.Join(
                                ", ",
                                notReadyPlayers
                                    .Concat(
                                        absent.Select(seat =>
                                            _gameServer.Localize(viewer, "ready.not_connected", seat)
                                        )
                                    )
                                    .Concat(
                                        unlinked.Select(seat =>
                                            _gameServer.Localize(viewer, "ready.not_linked", seat)
                                        )
                                    )
                            ),
                            CommandUtility.PublicChatTrigger
                        )
                );
            }
        );
    }

    public void Skip()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        Reset();

        if (match == null || !match.IsWarmup())
        {
            return;
        }

        _gameServer.MessageEach(
            HudDestination.Center,
            viewer => _gameServer.Localize(viewer, "ready.forced_start")
        );

        if (match.IsWarmup())
        {
            match.UpdateMapStatus(eMapStatus.Knife);
            return;
        }

        match.UpdateMapStatus(eMapStatus.Live);
    }

    private string[] GetNotReadyPlayers()
    {
        List<string> notReadyPlayers = new List<string>();

        foreach (var player in MatchUtility.Players())
        {
            if (player.UserId == null || !CanVote(player))
            {
                continue;
            }

            if (
                !_readyPlayers.ContainsKey(player.UserId.Value)
                || _readyPlayers[player.UserId.Value] == false
            )
            {
                notReadyPlayers.Add(player.PlayerName);
            }
        }

        return notReadyPlayers.ToArray();
    }

    private bool CanVote(CCSPlayerController player)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null || player == null || !player.IsValid || player.IsBot)
        {
            return false;
        }

        return ReadyUtility.CanReady(
            GetReadySetting(),
            matchData,
            player.SteamID.ToString(),
            player.PlayerName,
            _coachSystem.IsCoach(player, player.Team),
            _captainSystem.IsCaptain(player, player.Team)
        );
    }

    private eReadySettings GetReadySetting()
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        if (matchData == null)
        {
            return eReadySettings.Admin;
        }

        eReadySettings readySetting = ReadyUtility.ReadySettingStringToEnum(
            matchData.options.ready_setting
        );

        return readySetting;
    }

    private void SendReadyStatusMessage()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        if (match == null)
        {
            return;
        }

        if (!match.IsWarmup())
        {
            Reset();
            return;
        }

        foreach (var player in MatchUtility.Players())
        {
            try
            {
                SetupReadyMessage(player);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending ready status message to player");
            }
        }
    }
}
