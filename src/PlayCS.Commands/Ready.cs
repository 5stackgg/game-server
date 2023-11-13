using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
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

        if (TotalReady() == 10)
        {
            UpdateGameState(eGameState.Knife);
        }

        SendReadyMessage(player);

        SendNotReadyMessage();
    }

    public void SendReadyMessage(CCSPlayerController player)
    {
        if (player.UserId == null)
        {
            return;
        }

        // TODO - get total that should be marked ready 10 is good for now
        Message(
            HudDestination.Chat,
            $"You have been marked {(_readyPlayers[player.UserId.Value] ? $"{ChatColors.Green}ready" : $"{ChatColors.Red}not ready")} {ChatColors.Default}({TotalReady()}/10)",
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
                $" {ChatColors.Red}Players Not Ready: {ChatColors.Default}{string.Join(", ", notReadyPlayers)}"
            );
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
}
