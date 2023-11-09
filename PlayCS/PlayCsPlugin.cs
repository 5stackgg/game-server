using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using StackExchange.Redis;

namespace PlayCs;

public class PlayerData
{
    public string? name { get; set; }
    public string? side { get; set; }
    public string? steam_id { get; set; }
}

public class PlayCsPlugin : BasePlugin
{
    public override string ModuleName => "PlayCS Mod";

    public override string ModuleVersion => "0.0.1";

    public override void Load(bool hotReload)
    {
        try
        {
            ConnectionMultiplexer redis = ConnectionMultiplexer.Connect("localhost");
            IDatabase db = redis.GetDatabase();
            
            db.StringSet("mykey", "asdfasdf");
            
            string value = db.StringGet("mykey");
            Console.WriteLine(value); 
        }
        catch
        {
            Console.WriteLine("fail!"); 
        }
        
        Console.WriteLine($"Test Plugin has been loaded, and the hot reload flag was {hotReload}, path is {ModulePath}");    
        
        AddCommandListener("meta", CommandListener_BlockOutput);
        AddCommandListener("css", CommandListener_BlockOutput);
        AddCommandListener("css_plugins", CommandListener_BlockOutput);
        
        Message(HudDestination.Center, "PlayCS Loaded");
    }

    [ConsoleCommand("players", "get players")]
    public void GetPlayers(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null)
        {
            player.PrintToChat(ReplaceColorTags("{GRAY}[ {BLUE}PlayCS{GRAY} ]{LIGHTRED} you do not have access to this command"));
            return;
        }
        var playerDataList = new List<PlayerData>();
        var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");

        foreach (var playerEntity in playerEntities)
        {
            if (!playerEntity.IsBot)
            {
                var playerData = new PlayerData
                {
                    name = playerEntity.PlayerName,
                    // 1 = spectator , 2 = t , 3 = ct
                    side = playerEntity.TeamNum.ToString(),
                    steam_id = playerEntity.SteamID.ToString(),
                };
                
                playerDataList.Add(playerData);
            }
        }
        
        Message(HudDestination.Console, JsonSerializer.Serialize(playerDataList));
    }
    
    [ConsoleCommand("message_notify", "Sends a message to the server")]
    [ConsoleCommand("message_console", "Sends a message to the server")]
    [ConsoleCommand("message_chat", "Sends a message to the server")]
    [ConsoleCommand("message_center", "Sends a message to the server")]
    [ConsoleCommand("message_player", "Sends a message to a player")]
    public void OnCommand(CCSPlayerController? player, CommandInfo command)
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

    [ConsoleCommand("move_player", "Moves a player to a side")]
    public void MovePlayer(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null)
        {
            player.PrintToChat(ReplaceColorTags("{GRAY}[ {BLUE}PlayCS{GRAY} ]{LIGHTRED} you do not have access to this command"));
            return;
        }
        
        var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");

        var foundPlayer = playerEntities.First((ccsPlayerController) => ccsPlayerController.SteamID.ToString() == command.ArgByIndex(1));
                
        switch (command.ArgByIndex(2))
        {
            case  "CT":
                foundPlayer.ChangeTeam(CsTeam.CounterTerrorist);
                break;
            case  "TERRORIST":
                foundPlayer.ChangeTeam(CsTeam.Terrorist);
                break;
        }
    } 
    
    private void Message(HudDestination destination, string message, CCSPlayerController? player = null)
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
    
    private HookResult CommandListener_BlockOutput(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return HookResult.Continue;
        }

        return HookResult.Stop;
    }
}