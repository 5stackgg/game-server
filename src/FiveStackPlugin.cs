using CounterStrikeSharp.API;
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
    private readonly SurrenderSystem _surrender;
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
        SurrenderSystem surrenderSystem,
        GameBackUpRounds backUpManagement,
        EnvironmentService environmentService
    )
    {
        _logger = logger;
        _gameDemos = matchDemos;
        _gameServer = gameServer;
        _matchEvents = matchEvents;
        _matchService = matchService;
        _surrender = surrenderSystem;
        _matchTimeouts = matchTimeoutSystem;
        _matchTimeouts = matchTimeoutSystem;
        _gameBackupRounds = backUpManagement;
        _environmentService = environmentService;

        _pingTimer = new Timer(Ping, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
    }

    private Timer _pingTimer;

    public override void Load(bool hotReload)
    {
        _environmentService.Load();

        _logger.LogInformation($"Server ID: {_environmentService.GetServerId()}");

        RecordEnd.Hook(RecordEndHookResult, HookMode.Post);

        ListenForMapChange();

        _gameServer.Ping();

        AddCommandListener("say", OnPlayerChat, HookMode.Post);

        Server.NextFrame(() =>
        {
            _gameServer.Message(HudDestination.Alert, "5Stack Loaded");
        });
    }

    public override void Unload(bool hotReload)
    {
        _pingTimer?.Dispose();
        RecordEnd.Unhook(RecordEndHookResult, HookMode.Post);

        TimerUtility.Timers.ForEach(
            (timer) =>
            {
                timer.Kill();
            }
        );
    }

    private void Ping(object? state)
    {
        _gameServer.Ping();
    }
}
