using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
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
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<ReadySystem> _logger;
    private readonly CoachSystem _coachSystem;
    private readonly CaptainSystem _captainSystem;

    private Dictionary<int, bool> _readyPlayers = new Dictionary<int, bool>();

    public ReadySystem(
        ILogger<ReadySystem> logger,
        GameServer gameServer,
        MatchService matchService,
        CoachSystem coachSystem,
        CaptainSystem captainSystem
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchService = matchService;
        _coachSystem = coachSystem;
        _captainSystem = captainSystem;
    }

    public void Setup()
    {
        MatchData? matchData = _matchService.GetCurrentMatch()?.GetMatchData();
        if (matchData == null)
        {
            return;
        }

        _logger.LogInformation("Setting up ready system");
        ResetReady();
        SendReadyStatusMessage();
        if (_readyStatusTimer == null)
        {
            _readyStatusTimer = TimerUtility.AddTimer(3, SendReadyStatusMessage, TimerFlags.REPEAT);
        }
    }

    public void ResetReady()
    {
        _readyPlayers.Clear();
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
            _gameServer.Message(HudDestination.Chat, $"You are not allowed to ready up", player);
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

        int expectedCount = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;
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
            _matchService.GetCurrentMatch()?.UpdateMapStatus(eMapStatus.Knife);
            return;
        }

        SendReadyMessage(player);
        SendReadyStatusMessage();
        SendNotReadyMessage();
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
    }

    public void SetupReadyMessage(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        int totalReady = TotalReady();
        int expectedReady = _matchService.GetCurrentMatch()?.GetExpectedPlayerCount() ?? 10;

        int playerId = player.UserId.Value;
        if (_readyPlayers.ContainsKey(playerId) && _readyPlayers[playerId])
        {
            player.PrintToCenter($"Waiting for players [{totalReady}/{expectedReady}]");
            return;
        }

        if (CanVote(player))
        {
            player.PrintToCenter($"Type {CommandUtility.PublicChatTrigger}r to ready up!");
            return;
        }

        switch (GetReadySetting())
        {
            case eReadySettings.Admin:
                player.PrintToCenter($"The Admin will start the match when everyone is ready");
                break;
            case eReadySettings.Captains:
                player.PrintToCenter($"Waiting for the Captins to ready up");
                break;
            case eReadySettings.Coach:
                player.PrintToCenter($"Waiting for the coach to ready up");
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

        _gameServer.Message(
            HudDestination.Chat,
            $"You have been marked {(isReady ? $"{ChatColors.Green}ready" : $"{ChatColors.Red}not ready")}",
            player
        );
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

                _gameServer.Message(
                    HudDestination.Notify,
                    $" Players {ChatColors.Red}Not Ready: {ChatColors.Default}{string.Join(", ", notReadyPlayers)} type {ChatColors.Green}{CommandUtility.PublicChatTrigger}r"
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

        if (match == null || !match.IsWarmup())
        {
            return;
        }

        _gameServer.Message(HudDestination.Center, $"Game has been forced to start.");

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

        if (matchData == null || player == null)
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
            _readyStatusTimer?.Kill();
            _readyStatusTimer = null;
            return;
        }

        foreach (var player in MatchUtility.Players())
        {
            SetupReadyMessage(player);
        }
    }
}
