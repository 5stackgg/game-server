using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Translation;
using static SwiftlyS2.Shared.Helper;

namespace FiveStack;

public class ReadySystem
{
    private CancellationTokenSource? _readyStatusTimer;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<ReadySystem> _logger;
    private readonly CoachSystem _coachSystem;
    private readonly CaptainSystem _captainSystem;
    private readonly ILocalizer _localizer;

    private Dictionary<int, bool> _readyPlayers = new Dictionary<int, bool>();

    public ReadySystem(
        ILogger<ReadySystem> logger,
        GameServer gameServer,
        MatchService matchService,
        CoachSystem coachSystem,
        CaptainSystem captainSystem,
        ILocalizer localizer
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchService = matchService;
        _coachSystem = coachSystem;
        _captainSystem = captainSystem;
        _localizer = localizer;
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
        TimerUtility.Kill(_readyStatusTimer);
        _readyStatusTimer = null;

        _readyPlayers.Clear();

        if (setupTimer)
        {
            _readyStatusTimer = TimerUtility.Repeat(3, SendReadyStatusMessage);
        }

        UpdatePlayerStatus();
    }

    public void UpdatePlayerStatus()
    {
        foreach (var player in MatchUtility.Players())
        {
            int userId = player.UserID;

            if (
                player.Controller.Clan != ""
                && !player.Controller.Clan.EndsWith(" |")
            )
            {
                continue;
            }

            if (_readyStatusTimer == null)
            {
                _matchService.GetCurrentMatch()?.UpdatePlayerName(player, player.Name);
                continue;
            }

            if (_readyPlayers.ContainsKey(userId) && _readyPlayers[userId])
            {
                _matchService
                    .GetCurrentMatch()
                    ?.UpdatePlayerName(player, player.Name, "ready");
                continue;
            }
            _matchService
                .GetCurrentMatch()
                ?.UpdatePlayerName(player, player.Name, "not ready");
        }
    }

    public bool IsWaitingForReady()
    {
        return _readyStatusTimer != null;
    }

    public void ToggleReady(IPlayer player)
    {
        if (!CanVote(player))
        {
            _gameServer.Message(MessageType.Chat, _localizer["ready.not_allowed"], player);
            return;
        }

        int playerId = player.UserID;

        if (!_readyPlayers.ContainsKey(playerId))
        {
            _readyPlayers[playerId] = true;
        }
        else
        {
            _readyPlayers[playerId] = !_readyPlayers[playerId];
        }

        MatchManager? currentMatch = _matchService.GetCurrentMatch();

        int expectedCount = currentMatch?.GetExpectedPlayerCount() ?? 10;

        switch (GetReadySetting())
        {
            case eReadySettings.Admin:
                expectedCount = 1;
                break;
            case eReadySettings.Captains:
                expectedCount = 2;
                break;
            case eReadySettings.Coach:
                expectedCount = 2;
                break;
        }

        if (TotalReady() == expectedCount)
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

    public void UnreadyPlayer(IPlayer player)
    {
        int playerId = player.UserID;
        if (_readyPlayers.ContainsKey(playerId))
        {
            _readyPlayers[playerId] = false;
        }

        SendReadyStatusMessage();
        SendNotReadyMessage();
        UpdatePlayerStatus();
    }

    public void SetupReadyMessage(IPlayer player)
    {
        int totalReady = TotalReady();
        int expectedReady = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        int playerId = player.UserID;
        if (_readyPlayers.ContainsKey(playerId) && _readyPlayers[playerId])
        {
            player.SendCenter(_localizer["ready.waiting_for_players", totalReady, expectedReady]);
            return;
        }

        if (CanVote(player))
        {
            player.SendCenter(_localizer["ready.type_to_ready", CommandUtility.PublicChatTrigger]);
            return;
        }

        switch (GetReadySetting())
        {
            case eReadySettings.Admin:
                player.SendCenter(_localizer["ready.admin_will_start"]);
                break;
            case eReadySettings.Captains:
                player.SendCenter(_localizer["ready.waiting_captains"]);
                break;
            case eReadySettings.Coach:
                player.SendCenter(_localizer["ready.waiting_coach"]);
                break;
        }
    }

    private int TotalReady()
    {
        return _readyPlayers.Count(pair => pair.Value);
    }

    public void SendReadyMessage(IPlayer player)
    {
        _readyPlayers.TryGetValue(player.UserID, out bool isReady);

        string readyWord = isReady ? _localizer["ready.ready"] : _localizer["ready.not_ready"];
        string colored = isReady
            ? $"{ChatColors.Green}{readyWord}"
            : $"{ChatColors.Red}{readyWord}";
        _gameServer.Message(MessageType.Chat, _localizer["ready.marked", colored], player);
    }

    public CancellationTokenSource? _cancelSendNotReadyMessage;

    public async void SendNotReadyMessage()
    {
        _cancelSendNotReadyMessage?.Cancel();

        try
        {
            _cancelSendNotReadyMessage = new CancellationTokenSource();
            await Task.Delay(1000 * 5, _cancelSendNotReadyMessage.Token);

            MatchUtility.Core.Scheduler.NextTick(() =>
            {
                if (_cancelSendNotReadyMessage.IsCancellationRequested)
                {
                    return;
                }

                string[] notReadyPlayers = GetNotReadyPlayers();
                if (notReadyPlayers.Length == 0)
                {
                    return;
                }

                string list = string.Join(", ", notReadyPlayers);
                _gameServer.Message(
                    MessageType.Notify,
                    _localizer["ready.players_not_ready", list, CommandUtility.PublicChatTrigger]
                );
            });
        }
        catch (TaskCanceledException)
        {
        }
    }

    public void Skip()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        Reset();

        if (match == null || !match.IsWarmup())
        {
            return;
        }

        _gameServer.Message(MessageType.Center, _localizer["ready.forced_start"]);

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
            if (!CanVote(player))
            {
                continue;
            }

            if (
                !_readyPlayers.ContainsKey(player.UserID)
                || _readyPlayers[player.UserID] == false
            )
            {
                notReadyPlayers.Add(player.Name);
            }
        }

        return notReadyPlayers.ToArray();
    }

    private bool CanVote(IPlayer player)
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();

        if (matchData == null || player == null || !player.IsValid || player.IsFakeClient)
        {
            return false;
        }

        bool isCoach = _coachSystem.IsCoach(player, player.Controller.Team);
        bool isCaptain = _captainSystem.IsCaptain(player, player.Controller.Team);

        switch (GetReadySetting())
        {
            case eReadySettings.Coach:
                if (!isCoach)
                {
                    return false;
                }
                break;
            case eReadySettings.Captains:
                if (!isCaptain)
                {
                    return false;
                }
                break;
            case eReadySettings.Admin:
                return false;
        }

        return true;
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
                _logger.LogError($"Error sending ready status message to player: {ex.Message}");
            }
        }
    }
}
