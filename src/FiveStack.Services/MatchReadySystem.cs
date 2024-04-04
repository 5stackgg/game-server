using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

public class MatchReadySystem
{
    private FiveStackMatch? _match;
    private readonly GameServer _gameServer;
    private readonly ILogger<MatchReadySystem> _logger;

    private Dictionary<int, bool> _readyPlayers = new Dictionary<int, bool>();

    public MatchReadySystem(ILogger<MatchReadySystem> logger, GameServer gameServer)
    {
        _logger = logger;
        _gameServer = gameServer;
    }

    public void Setup(FiveStackMatch match)
    {
        _match = match;
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
            // TODO - not sure how to trigger this without really bad DI loop
            // UpdateMapStatus(eMapStatus.Knife);
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
        return _match?.type == "Wingman" ? 4 : 10;
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
