using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin : BasePlugin
{
    private int _currentRound = 0;
    private Redis _redis = new Redis();
    private ePhase _currentPhase = ePhase.Unknown;

    public readonly string CurrentMap = Server.MapName;

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

        CaptureChat();
        CaptureRoundEnd();

        CaptureAssists();
        CapturePlayerKills();
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
            _redis.PublishServerEvent(
                serverId,
                new Redis.EventData<Dictionary<string, object>>
                {
                    @event = "connected",
                    data = new Dictionary<string, object> { { "server_id", serverId }, }
                }
            );
        }

        RegisterListener<Listeners.OnMapStart>(name =>
        {
            SetupMatch();
        });
    }

    public void SendCommands(string[] commands)
    {
        foreach (var command in commands)
        {
            Server.ExecuteCommand(command);
        }
    }
}
