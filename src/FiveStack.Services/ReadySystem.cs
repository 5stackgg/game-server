using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using FiveStack.Enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class ReadySystem
{
    private readonly GameServer _gameServer;
    private readonly MatchService _matchService;
    private readonly ILogger<ReadySystem> _logger;

    private Dictionary<int, bool> _readyPlayers = new Dictionary<int, bool>();

    public ReadySystem(
        ILogger<ReadySystem> logger,
        GameServer gameServer,
        MatchService matchService
    )
    {
        _logger = logger;
        _gameServer = gameServer;
        _matchService = matchService;
    }

    public void ToggleReady(CCSPlayerController player)
    {
        if (!_readyPlayers.ContainsKey(player.UserId!.Value))
        {
            _readyPlayers[player.UserId.Value] = true;
        }
        else
        {
            _readyPlayers[player.UserId.Value] = !_readyPlayers[player.UserId.Value];
        }
    }

    public void ShowReady(CCSPlayerController player)
    {
        if (TotalReady() == GetExpectedPlayerCount())
        {
            _matchService.GetCurrentMatch()?.UpdateMapStatus(eMapStatus.Knife);
        }

        SendReadyMessage(player);

        SendNotReadyMessage();
    }

    public void SetupReadyMessage(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        int totalReady = TotalReady();
        int expectedReady = GetExpectedPlayerCount();

        int playerId = player.UserId.Value;
        if (_readyPlayers.ContainsKey(playerId) && _readyPlayers[playerId])
        {
            player.PrintToCenter($"Waiting for players [{totalReady}/{expectedReady}]");
            return;
        }
        player.PrintToCenter($"Type .r to ready up!");
    }

    private int TotalReady()
    {
        return _readyPlayers.Count(pair => pair.Value);
    }

    private int GetExpectedPlayerCount()
    {
        FiveStackMatch? match = _matchService.GetCurrentMatch()?.GetMatchData();

        if (match == null)
        {
            return 10;
        }

        return match.type == "Wingman" ? 4 : 10;
    }

    private void ResetReadyPlayers()
    {
        _readyPlayers = new Dictionary<int, bool>();
    }

    public void SendReadyMessage(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        bool isReady = _readyPlayers[player.UserId.Value];
        player.Clan = isReady ? "[Ready]" : "";

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
                    $" Players {ChatColors.Red}Not Ready: {ChatColors.Default}{string.Join(", ", notReadyPlayers)} type {ChatColors.Green}.ready"
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

        match.UpdateMapStatus(eMapStatus.Knife);
    }

    private string[] GetNotReadyPlayers()
    {
        List<string> notReadyPlayers = new List<string>();

        foreach (var player in CounterStrikeSharp.API.Utilities.GetPlayers())
        {
            if (player.IsBot || !player.IsValid || player.UserId == null)
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
}
