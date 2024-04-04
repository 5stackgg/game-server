using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace FiveStack;

[MinimumApiVersion(80)]
public partial class FiveStackPlugin : BasePlugin
{
    private readonly GameDemos _gameDemos;
    private readonly GameServer _gameServer;
    private readonly MatchEvents _matchEvents;
    private readonly Timeouts _matchTimeouts;
    private readonly MatchService _matchService;
    private readonly GameBackUpRounds _gameBackupRounds;

    public override string ModuleName => "FiveStack";
    public override string ModuleVersion => "0.0.1";
    public override string ModuleAuthor => "5Stack.gg";
    public override string ModuleDescription => "5Stack creates and managements custom matches";

    public FiveStackPlugin(
        GameDemos matchDemos,
        GameServer gameServer,
        MatchEvents matchEvents,
        MatchService matchService,
        Timeouts matchTimeoutSystem,
        GameBackUpRounds backUpManagement
    )
    {
        _gameDemos = matchDemos;
        _gameServer = gameServer;
        _matchEvents = matchEvents;
        _matchService = matchService;
        _gameBackupRounds = backUpManagement;
        _matchTimeouts = matchTimeoutSystem;
    }

    public override void Load(bool hotReload)
    {
        _gameServer.Message(HudDestination.Alert, "5Stack Loaded");

        ListenForMapChange();
        ListenForReadyStatus();

        _matchService.GetMatchFromApi();
    }

    public MatchManager? CurrentMatch()
    {
        return _matchService.GetCurrentMatch();
    }

    public override void Unload(bool hotReload)
    {
        // _matchEvents.Disconnect();
    }
}
