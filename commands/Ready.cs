using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    [ConsoleCommand("css_r", "Marks the player as ready")]
    [ConsoleCommand("css_ready", "Marks the player as ready")]
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
            _readyPlayers[player.UserId.Value] = true;
        }

        if (TotalReady() == 10)
        {
            UpdatePhase(ePhase.Knife);
        }

        SendReadyMessage(player);
    }

    [ConsoleCommand("css_nr", "Marks the player as ready")]
    [ConsoleCommand("css_not-ready", "Marks the player as ready")]
    public void OnNotReady(CCSPlayerController? player, CommandInfo? command)
    {
        if (!IsWarmup() || player == null)
        {
            return;
        }

        if (!_readyPlayers.ContainsKey(player.UserId!.Value))
        {
            _readyPlayers[player.UserId.Value] = false;
        }
        else
        {
            _readyPlayers[player.UserId.Value] = false;
        }

        SendReadyMessage(player);
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
            $"You have been marked {(_readyPlayers[player.UserId.Value] ? $"{ChatColors.Red}ready" : $"{ChatColors.Red}not ready")} {ChatColors.Default}({TotalReady()}/10)",
            player
        );
    }
}
