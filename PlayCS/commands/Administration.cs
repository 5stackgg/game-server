using System.Text.Json;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCs.entities;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private MatchData? matchData;
    private ConVar password = ConVar.Find("sv_password");

    // RegisterListener<Listeners.OnMapStart>((mapName) =>
    // {
    //     Task.Run(() => SendMessage($"> Changed map to: {mapName}"));
    // });

    private void RegisterAdministrationCommands()
    {
        AddCommandListener("meta", CommandListener_BlockOutput);
        AddCommandListener("css", CommandListener_BlockOutput);
        AddCommandListener("css_plugins", CommandListener_BlockOutput);

        AddCommand("players", "gets the list of players currently in the server", GetPlayers);
        AddCommand(
            "match_details",
            "gets the list of players currently in the server",
            SetMatchDetails
        );
    }

    public void SetMatchDetails(CCSPlayerController? player, CommandInfo command)
    {
        matchData = JsonSerializer.Deserialize<MatchData>(command.ArgString);

        if (matchData == null)
        {
            return;
        }

        Console.WriteLine($"SET PASSSWORD {matchData.password}");
        // we cant detect it has changed, its encrypted
        password.StringValue = matchData.password;

        if (matchData.map != CurrentMap)
        {
            Console.WriteLine($"Change Level {matchData.map}");
            ChangeMap(matchData.map);
        }

        // "mp_backup_round_file playcs_1",

        switch (PhaseStringToEnum(matchData.status))
        {
            case ePhase.Scheduled:
                Console.WriteLine("Scheduled phase");
                break;
            case ePhase.Warmup:
                startWarmup();
                break;
            case ePhase.Knife:
                Console.WriteLine("Knife phase");
                break;
            case ePhase.Live:
                Console.WriteLine("Live phase");
                break;
            case ePhase.Overtime:
                Console.WriteLine("Overtime phase");
                break;
            case ePhase.Paused:
                Console.WriteLine("Paused phase");
                break;
            case ePhase.TechTimeout:
                Console.WriteLine("TechTimeout phase");
                break;
            case ePhase.Finished:
                Console.WriteLine("Finished phase");
                break;
        }
    }

    public void UpdatePhase(ePhase ePhase)
    {
        Console.WriteLine($"UPDATING PHASE {ePhase}");
        Eventing.PublishMatchEvent(
            matchData.id,
            new Eventing.EventData<Dictionary<string, object>>
            {
                @event = "status",
                data = new Dictionary<string, object> { { "status", ePhase.ToString() }, }
            }
        );

        Phase = ePhase;
    }

    public void ChangeMap(string Map)
    {
        if (Server.IsMapValid(Map))
        {
            SendCommands(new[] { $"changelevel \"{Map}\"" });
            return;
        }

        // TODO - check if i exist in subscribed map list
        SendCommands(new[] { $"host_workshop_map \"{Map}\"" });
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
