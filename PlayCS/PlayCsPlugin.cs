﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace PlayCs;

public partial class PlayCsPlugin : BasePlugin
{
    public Redis redis = new Redis();
    
    public override string ModuleName => "PlayCS Mod";

    public override string ModuleVersion => "0.0.1";

    public override void Load(bool hotReload)
    {
        Console.WriteLine($"Test Plugin has been loaded, and the hot reload flag was {hotReload}, path is {ModulePath}");    
        
        CapatureChat();
        CaptureRoundEnd();
        CapturePlayerDamage();
        
        BlockServerCommands();
        RegisterMessageCommands();

        Message(HudDestination.Center, "PlayCS Loaded");
    }

    // TODO - this may not be from the server
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
    
}