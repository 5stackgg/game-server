using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;

namespace PlayCs;

public partial class PlayCsPlugin : BasePlugin
{
    private Eventing Eventing = new Eventing();
    private ePhase CurrentPhase = ePhase.Unknown;

    public string CurrentMap = Server.MapName;

    private Dictionary<CsTeam, CCSPlayerController?> Captains = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null }
    };

    public override string ModuleName => "PlayCS Mod";

    public override string ModuleVersion => "0.0.1";

    public override void Load(bool hotReload)
    {
        // TODO - figure out how to load it later
        // Env.Load("/opt/playcs/PlayCS/.env");

        Console.WriteLine(
            $"Test Plugin has been loaded, and the hot reload flag was {hotReload}, path is {ModulePath}"
        );

        CapatureChat();
        CaptureRoundEnd();
        CapturePlayerDamage();
        CapturePlayerConnected();
        CapturePlayerDisconnected();
        CapturePlayerSwitchedSides();

        RegisterMessageCommands();
        RegisterAdministrationCommands();

        Message(HudDestination.Alert, "PlayCS Loaded");

        string serverId = "82c90c4f-ab44-432b-9025-29332461bfe2";
        // string? serverId = Environment.GetEnvironmentVariable("SERVER_ID");

        if (serverId != null)
        {
            Eventing.PublishServerEvent(
                serverId,
                new Eventing.EventData<Dictionary<string, object>>
                {
                    @event = "connected",
                    data = new Dictionary<string, object> { { "server_id", serverId }, }
                }
            );
        }
    }

    public void SendCommands(string[] commands)
    {
        foreach (var command in commands)
        {
            Server.ExecuteCommand(command);
        }
    }

    // TODO - this may not be from the server
    [ConsoleCommand("move_player", "Moves a player to a side")]
    private void MovePlayer(CCSPlayerController? player, CommandInfo command)
    {
        if (player != null)
        {
            return;
        }

        var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>(
            "cs_player_controller"
        );

        var foundPlayer = playerEntities.First(
            (ccsPlayerController) => ccsPlayerController.SteamID.ToString() == command.ArgByIndex(1)
        );

        switch (command.ArgByIndex(2))
        {
            case "CT":
                foundPlayer.ChangeTeam(CsTeam.CounterTerrorist);
                break;
            case "TERRORIST":
                foundPlayer.ChangeTeam(CsTeam.Terrorist);
                break;
        }
    }
}
