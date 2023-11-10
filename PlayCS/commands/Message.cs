using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Commands;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void RegisterMessageCommands()
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
            player.PrintToChat(ReplaceColorTags("{GRAY}[ {BLUE}PlayCS{GRAY} ]{LIGHTRED} you do not have access to this command"));
            return;
        }
        
        switch (command.ArgByIndex(0))
        {
            case  "message_notify":
                Message(HudDestination.Notify, command.ArgString);
                break;
            case  "message_console":
                Message(HudDestination.Console, command.ArgString);
                break;
            case  "message_chat":
                Message(HudDestination.Chat, command.ArgString);
                break;
            case  "message_center":
                Message(HudDestination.Center, command.ArgString);
                break;
            case  "message_player":
                var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
                
                Message(HudDestination.Center, command.ArgByIndex(2), playerEntities.First((player) => player.SteamID.ToString() == command.ArgByIndex(1)));
                break;
        }
    }
    
    public void Message(HudDestination destination, string message, CCSPlayerController? player = null)
    {
        message = ReplaceColorTags(message);

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

        else if (destination == HudDestination.Center)
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
    
    private string ReplaceColorTags(string input)
    {
        string[] colorPatterns =
        {
            "{DEFAULT}", "{RED}", "{LIGHTPURPLE}", "{GREEN}", "{LIME}", "{LIGHTGREEN}", "{LIGHTRED}", "{GRAY}",
            "{LIGHTOLIVE}", "{OLIVE}", "{LIGHTBLUE}", "{BLUE}", "{PURPLE}", "{GRAYBLUE}"
        };
        string[] colorReplacements =
        {
            "\x01", "\x02", "\x03", "\x04", "\x05", "\x06", "\x07", "\x08", "\x09", "\x10", "\x0B", "\x0C", "\x0E",
            "\x0A"
        };

        for (var i = 0; i < colorPatterns.Length; i++)
        {
            input = input.Replace(colorPatterns[i], colorReplacements[i]);
        }

        return input;
    }
}