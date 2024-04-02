using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.entities;
using FiveStack.enums;
using Microsoft.Extensions.Logging;

namespace FiveStack;

// TODO - DI
public partial class FiveStackPlugin : BasePlugin
{
    private Match? _matchData;
    private Guid? _currentMatchId;
    private MatchMap? _currentMap;
    private int _currentRound = 0;
    private Redis _redis = new Redis();
    private string _onMap = Server.MapName;
    public CsTeam? KnifeWinningTeam;
    private eMapStatus _currentMapStatus = eMapStatus.Unknown;
    private Dictionary<int, bool> _readyPlayers = new Dictionary<int, bool>();
    private string? _resetRound;
    private Dictionary<int, bool> _restoreRoundVote = new Dictionary<int, bool>();

    private Dictionary<CsTeam, CCSPlayerController?> _captains = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null }
    };

    private Dictionary<CsTeam, CCSPlayerController?> _coaches = new Dictionary<
        CsTeam,
        CCSPlayerController?
    >
    {
        { CsTeam.Terrorist, null },
        { CsTeam.CounterTerrorist, null }
    };

    public override string ModuleName => "5Stack Mod";

    public override string ModuleVersion => "0.0.3";

    public override void Load(bool hotReload)
    {
        if (bool.TryParse(Environment.GetEnvironmentVariable("DEV_SERVER"), out var isDev) && isDev)
        {
            DotEnv.Load("/serverdata/serverfiles/.env");
        }

        string? serverId = Environment.GetEnvironmentVariable("SERVER_ID");
        string? apiPassword = Environment.GetEnvironmentVariable("SERVER_API_PASSWORD");

        ListenForMapChange();
        ListenForReadyStatus();

        Message(HudDestination.Alert, "5Stack Loaded");
        GetMatch();
    }

    public override void Unload(bool hotReload)
    {
        Logger.LogInformation("Hello World! We are unloading!");
    }
}
