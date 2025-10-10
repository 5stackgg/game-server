using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace FiveStack;

public class ReadySystem
{
    private Timer? _readyStatusTimer;
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<ReadySystem> _logger;
    private readonly CoachSystem _coachSystem;
    private readonly CaptainSystem _captainSystem;
    private readonly IStringLocalizer _localizer;

    private Dictionary<int, bool> _readyPlayers = new Dictionary<int, bool>();

    public ReadySystem(
        ILogger<ReadySystem> logger,
        GameServer gameServer,
        MatchService matchService,
        CoachSystem coachSystem,
        CaptainSystem captainSystem,
        IStringLocalizer localizer
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
        ResetReady(true);
        SendReadyStatusMessage();
    }

    public void ResetReady(bool setupTimer = false)
    {
        _readyStatusTimer?.Kill();
        _readyStatusTimer = null;

        _readyPlayers.Clear();

        if (setupTimer)
        {
            _readyStatusTimer = TimerUtility.AddTimer(3, SendReadyStatusMessage, TimerFlags.REPEAT);
        }

        UpdatePlayerStatus();
    }

    public void UpdatePlayerStatus()
    {
        foreach (var player in MatchUtility.Players())
        {
            if (player.UserId == null || !player.IsValid || player.IsBot)
            {
                continue;
            }

            if (
                player.ClanName != ""
                && player.ClanName != "[ready]"
                && player.ClanName != "[not ready]"
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
                    ?.UpdatePlayerName(player, player.PlayerName, "ready");
                continue;
            }
            _matchService
                .GetCurrentMatch()
                ?.UpdatePlayerName(player, player.PlayerName, "not ready");
        }
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

        if (!CanVote(player))
        {
            _gameServer.Message(HudDestination.Chat, _localizer["ready.not_allowed"], player);
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
            ResetReady();
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
        if (player.UserId == null)
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
        if (player.UserId == null || !player.IsValid || player.IsBot)
        {
            return;
        }

        int totalReady = TotalReady();
        int expectedReady = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        int playerId = player.UserId.Value;
        if (_readyPlayers.ContainsKey(playerId) && _readyPlayers[playerId])
        {
            player.PrintToCenter(
                _localizer["ready.waiting_for_players", totalReady, expectedReady]
            );
            return;
        }

        if (CanVote(player))
        {
            player.PrintToCenter(
                _localizer["ready.type_to_ready", CommandUtility.PublicChatTrigger]
            );
            return;
        }

        switch (GetReadySetting())
        {
            case eReadySettings.Admin:
                player.PrintToCenter(_localizer["ready.admin_will_start"]);
                break;
            case eReadySettings.Captains:
                player.PrintToCenter(_localizer["ready.waiting_captains"]);
                break;
            case eReadySettings.Coach:
                player.PrintToCenter(_localizer["ready.waiting_coach"]);
                break;
        }
    }

    private int TotalReady()
    {
        return _readyPlayers.Count(pair => pair.Value);
    }

    public void SendReadyMessage(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        bool isReady = _readyPlayers[player.UserId.Value];

        string readyWord = isReady ? _localizer["ready.ready"] : _localizer["ready.not_ready"];
        string colored = isReady
            ? $"{ChatColors.Green}{readyWord}"
            : $"{ChatColors.Red}{readyWord}";
        _gameServer.Message(HudDestination.Chat, _localizer["ready.marked", colored], player);
    }

    public CancellationTokenSource? _cancelSendNotReadyMessage;

    public async void SendNotReadyMessage()
    {
        _cancelSendNotReadyMessage?.Cancel();

        try
        {
            _cancelSendNotReadyMessage = new CancellationTokenSource();
            await Task.Delay(1000 * 5, _cancelSendNotReadyMessage.Token);

            Server.NextFrame(() =>
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
                    HudDestination.Notify,
                    _localizer["ready.players_not_ready", list, CommandUtility.PublicChatTrigger]
                );
            });
        }
        catch (TaskCanceledException)
        {
            // do nothing
        }
    }

    public void Skip()
    {
        MatchManager? match = _matchService.GetCurrentMatch();

        ResetReady();

        if (match == null || !match.IsWarmup())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, _localizer["ready.forced_start"]);

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
            if (player.UserId == null || !player.IsValid || player.IsBot || !CanVote(player))
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

        bool isCoach = _coachSystem.IsCoach(player, player.Team);
        bool isCaptain = _captainSystem.IsCaptain(player, player.Team);

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

    private bool IsAdminOnly()
    {
        return GetReadySetting() == eReadySettings.Admin;
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
            ResetReady();
            return;
        }

        foreach (var player in MatchUtility.Players())
        {
            SetupReadyMessage(player);
        }
    }
}
