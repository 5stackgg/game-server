using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace FiveStack;

[MinimumApiVersion(80)]
public partial class FiveStackPlugin : BasePlugin
{
    private readonly GameDemos _gameDemos;
    private readonly GameServer _gameServer;
    private readonly Timeouts _matchTimeouts;
    private readonly MatchEvents _matchEvents;
    private readonly MatchService _matchService;
    private readonly ILogger<FiveStackPlugin> _logger;
    private readonly GameBackUpRounds _gameBackupRounds;
    private readonly EnvironmentService _environmentService;

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
        ILogger<FiveStackPlugin> logger,
        GameBackUpRounds backUpManagement,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _gameDemos = matchDemos;
        _gameServer = gameServer;
        _matchEvents = matchEvents;
        _matchService = matchService;
        _matchTimeouts = matchTimeoutSystem;
        _matchTimeouts = matchTimeoutSystem;
        _gameBackupRounds = backUpManagement;
        _environmentService = environmentService;
    }

    public override void Load(bool hotReload)
    {
        _environmentService.Load();

        ListenForMapChange();

        _gameServer.Message(HudDestination.Alert, "5Stack Loaded");

        _watchSpectatorChanges();
    }

    public override void Unload(bool hotReload)
    {
        TimerUtility.Timers.ForEach(
            (timer) =>
            {
                timer.Kill();
            }
        );
        SpectatorChanged.Unhook(SpectatorChangedHook, HookMode.Post);
    }
}
