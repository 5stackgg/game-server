using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin : BasePlugin
{
    private int _currentRound = 0;
    private Redis _redis = new Redis();
    private string _currentMap = Server.MapName;
    private eGameState _currentGameState = eGameState.Unknown;

    private Dictionary<CsTeam, CCSPlayerController?> _captains = new Dictionary<
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
        
        // Env.Load("/opt/PlayCS/.env");

        Console.WriteLine(
            $"Test Plugin has been loaded, and the hot reload flag was {hotReload}, path is {ModulePath}"
        );

        ListenForMapChange();

        Message(HudDestination.Alert, "PlayCS Loaded");

        string? serverId = Environment.GetEnvironmentVariable("SERVER_ID");

        if (serverId != null)
        {
            Console.WriteLine($"Server Connected: {serverId}");
            _redis.PublishServerEvent(
                serverId,
                new Redis.EventData<Dictionary<string, object>>
                {
                    @event = "connected",
                    data = new Dictionary<string, object>()
                }
            );
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

    public void SendCommands(string[] commands)
    {
        foreach (var command in commands)
        {
            Server.ExecuteCommand(command);
        }
    }
}
