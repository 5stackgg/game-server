using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void RegisterMessageCommands()
    {
        AddCommand("message_notify", "Prints Message to Server", MessageCommand);
        AddCommand("message_console", "Prints Message to Server", MessageCommand);
        AddCommand("message_chat", "Prints Message to Server", MessageCommand);
        AddCommand("message_center", "Prints Message to Server", MessageCommand);
        AddCommand("message_player", "Prints Message to Server", MessageCommand);
    }

    private void MessageCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null)
        {
            return;
        }

        switch (command.ArgByIndex(0))
        {
            case "message_notify":
                Message(HudDestination.Notify, command.ArgString);
                break;
            case "message_console":
                Message(HudDestination.Console, command.ArgString);
                break;
            case "message_chat":
                Message(HudDestination.Chat, command.ArgString);
                break;
            case "message_center":
                Message(HudDestination.Center, command.ArgString);
                break;
            case "message_player":
                var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>(
                    "cs_player_controller"
                );

                Message(
                    HudDestination.Center,
                    command.ArgByIndex(2),
                    playerEntities.First(
                        (player) => player.SteamID.ToString() == command.ArgByIndex(1)
                    )
                );
                break;
        }
    }

    public void Message(
        HudDestination destination,
        string message,
        CCSPlayerController? player = null
    )
    {
        if (player != null)
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                player.PrintToChat($"{part}");
            }
        }
        else if (destination == HudDestination.Console)
        {
            Server.PrintToConsole(message);
        }
        else if (destination == HudDestination.Alert || destination == HudDestination.Center)
        {
            VirtualFunctions.ClientPrintAll(destination, $" {message}", 0, 0, 0, 0);
        }
        else
        {
            var parts = message.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            foreach (var part in parts)
            {
                Server.PrintToChatAll($"{part}");
            }
        }
    }
}
