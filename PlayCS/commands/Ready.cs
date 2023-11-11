using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

/**
 * TODO : show who is not ready
 */
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

        if (!ReadyPlayers.ContainsKey(player.UserId!.Value))
        {
            ReadyPlayers[player.UserId.Value] = true;
        }
        else
        {
            ReadyPlayers[player.UserId.Value] = true;
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

        if (!ReadyPlayers.ContainsKey(player.UserId!.Value))
        {
            ReadyPlayers[player.UserId.Value] = false;
        }
        else
        {
            ReadyPlayers[player.UserId.Value] = false;
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
            $"You have been marked {(ReadyPlayers[player.UserId.Value] ? $"{ChatColors.Red}ready" : $"{ChatColors.Red}not ready")} {ChatColors.Default}({TotalReady()}/10)",
            player
        );
    }
}
