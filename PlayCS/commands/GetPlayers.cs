using PlayCs.entities;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Core.Attributes.Registration;

namespace PlayCs;

public partial class PlayCsPlugin
{
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
}