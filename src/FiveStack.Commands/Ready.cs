using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    [ConsoleCommand("css_r", "Toggles the player as ready")]
    [ConsoleCommand("css_ready", "Toggles the player as ready")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReady(CCSPlayerController? player, CommandInfo? command)
    {
        if (!IsWarmup() || player == null)
        {
            return;
        }

        if (!_readyPlayers.ContainsKey(player.UserId!.Value))
        {
            _readyPlayers[player.UserId.Value] = true;
        }
        else
        {
            _readyPlayers[player.UserId.Value] = !_readyPlayers[player.UserId.Value];
        }

        if (TotalReady() == GetExpectedPlayerCount())
        {
            UpdateMapStatus(eMapStatus.Knife);
        }

        SendReadyMessage(player);

        SendNotReadyMessage();
    }

    [ConsoleCommand("force_ready", "Forces the match to start")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnForceStart(CCSPlayerController? player, CommandInfo? command)
    {
        if (!IsWarmup())
        {
            return;
        }

        Message(HudDestination.Center, $"Game has been forced to start.", player);

        UpdateMapStatus(eMapStatus.Knife);
    }

    public void SendReadyMessage(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        bool isReady = _readyPlayers[player.UserId.Value];
        player.Clan = isReady ? "[Ready]" : "";

        Message(
            HudDestination.Chat,
            $"You have been marked {(isReady ? $"{ChatColors.Green}ready" : $"{ChatColors.Red}not ready")} {ChatColors.Default}({TotalReady()}/{GetExpectedPlayerCount()})",
            player
        );
    }

    private CancellationTokenSource? _cancelSendNotReadyMessage;

    private async void SendNotReadyMessage()
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

                string[] notReadyPlayers = _getNotReadyPlayers();
                if (notReadyPlayers.Length == 0)
                {
                    return;
                }

                Message(
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

    private string[] _getNotReadyPlayers()
    {
        List<string> notReadyPlayers = new List<string>();

        foreach (var player in Utilities.GetPlayers())
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

    private int GetExpectedPlayerCount()
    {
        if (_matchData == null)
        {
            return 10;
        }
        return _matchData.type == "Wingman" ? 4 : 10;
    }
}
