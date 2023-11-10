using PlayCs.entities;
using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public string? matchId;

    public void RegisterAdministrationCommands()
    {
        matchId = Environment.GetEnvironmentVariable("MATCH_ID");

        // this is just for testing....
        matchId = "19887352-5ae8-499c-ad72-ebb9b23319a8";
        
        Console.WriteLine($"MATCH ID {matchId}");
        
        AddCommandListener("meta", CommandListener_BlockOutput);
        AddCommandListener("css", CommandListener_BlockOutput);
        AddCommandListener("css_plugins", CommandListener_BlockOutput);

        AddCommand("players", "gets the list of players currently in the server", RegisterMatchId);
        AddCommand(
            "set_match_id",
            "sets the match id for the current running match",
            RegisterMatchId
        );
    }

    public void RegisterMatchId(CCSPlayerController? player, CommandInfo command)
    {
        Console.WriteLine("YAY");

        if (player == null)
        {
            this.matchId = command.ArgString;
        }
    }

    public void GetPlayers(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null)
        {
            return;
        }
        var playerDataList = new List<PlayerData>();
        var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>(
            "cs_player_controller"
        );

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

    public HookResult CommandListener_BlockOutput(CCSPlayerController? player, CommandInfo info)
    {
        if (player == null)
        {
            return HookResult.Continue;
        }

        return HookResult.Stop;
    }
}
