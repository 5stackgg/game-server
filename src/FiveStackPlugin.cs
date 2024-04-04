using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;

namespace FiveStack;

[MinimumApiVersion(80)]
public partial class FiveStackPlugin : BasePlugin
{
    private readonly MatchService _matchService;
    private readonly GameServer _gameServer;
    private readonly BackUpManagement _backUpManagement;
    private readonly MatchDemos _matchDemos;
    private readonly MatchTimeoutSystem _matchTimeoutSystem;

    // private readonly GameEvents _gameEvents;

    public override string ModuleName => "FiveStack";
    public override string ModuleVersion => "0.0.1";
    public override string ModuleAuthor => "5Stack.gg";
    public override string ModuleDescription => "5Stack creates and managements custom matches";

    public FiveStackPlugin(
        GameServer gameServer,
        MatchService matchService,
        BackUpManagement backUpManagement,
        MatchTimeoutSystem matchTimeoutSystem,
        MatchDemos matchDemos,
        GameEvents gameEvents
    )
    {
        _matchDemos = matchDemos;
        // _gameEvents = gameEvents;
        _gameServer = gameServer;
        _matchService = matchService;
        _backUpManagement = backUpManagement;
        _matchTimeoutSystem = matchTimeoutSystem;
    }

    public override void Load(bool hotReload)
    {
        _gameServer.Message(HudDestination.Alert, "5Stack Loaded");

        ListenForMapChange();
        ListenForReadyStatus();

        _matchService.GetMatch();
    }
}
