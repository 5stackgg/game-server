using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

/**
 * TODO : show who is not ready
 * TODO - do .nr and .not-ready isntaed
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

        if (!readyPlayers.ContainsKey(player.UserId!.Value))
        {
            readyPlayers[player.UserId.Value] = true;
        }
        else
        {
            readyPlayers[player.UserId.Value] = true;
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

        if (!readyPlayers.ContainsKey(player.UserId!.Value))
        {
            readyPlayers[player.UserId.Value] = false;
        }
        else
        {
            readyPlayers[player.UserId.Value] = false;
        }

        SendReadyMessage(player);
    }

    public void SendReadyMessage(CCSPlayerController player)
    {
        // TODO - get total that should be marked ready
        Message(
            HudDestination.Chat,
            $"You have been marked {(readyPlayers[player.UserId.Value] ? "{GREEN}ready" : "{RED}not ready")} {{DEFAULT}}({TotalReady()}/10)",
            player
        );
    }
}
