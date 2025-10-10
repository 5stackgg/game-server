using System.Runtime.InteropServices;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using FiveStack.Utilities;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace FiveStack;

[MinimumApiVersion(80)]
public partial class FiveStackPlugin : BasePlugin
{
    private readonly GameDemos _gameDemos;
    private readonly GameServer _gameServer;
    private readonly MatchEvents _matchEvents;
    private readonly ReadySystem _readySystem;
    private readonly TimeoutSystem _timeoutSystem;
    private readonly MatchService _matchService;
    private readonly SurrenderSystem _surrenderSystem;
    private readonly IStringLocalizer _localizer;
    private readonly ILogger<FiveStackPlugin> _logger;
    private readonly GameBackUpRounds _gameBackupRounds;
    private readonly EnvironmentService _environmentService;
    private readonly SteamAPI _steamAPI;

    public override string ModuleName => "FiveStack";
    public override string ModuleVersion => "__RELEASE_VERSION__";
    public override string ModuleAuthor => "5Stack.gg";
    public override string ModuleDescription => "5Stack creates and managements custom matches";

    public FiveStackPlugin(
        SteamAPI steamAPI,
        GameDemos matchDemos,
        GameServer gameServer,
        MatchEvents matchEvents,
        MatchService matchService,
        ReadySystem readySystem,
        TimeoutSystem timeoutSystem,
        ILogger<FiveStackPlugin> logger,
        SurrenderSystem surrenderSystem,
        GameBackUpRounds backUpManagement,
        EnvironmentService environmentService,
        IStringLocalizer localizer
    )
    {
        _logger = logger;
        _steamAPI = steamAPI;
        _localizer = localizer;
        _gameDemos = matchDemos;
        _gameServer = gameServer;
        _matchEvents = matchEvents;
        _readySystem = readySystem;
        _matchService = matchService;
        _timeoutSystem = timeoutSystem;
        _surrenderSystem = surrenderSystem;
        _gameBackupRounds = backUpManagement;
        _environmentService = environmentService;

        _pingTimer = new Timer(Ping, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
    }

    private Timer _pingTimer;

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnGameServerSteamAPIActivated>(_steamAPI.OnSteamAPIActivated);
        RegisterListener<Listeners.OnGameServerSteamAPIDeactivated>(
            _steamAPI.OnSteamAPIDeactivated
        );

        if (hotReload)
        {
            _steamAPI.OnSteamAPIActivated();
        }

        _environmentService.Load();

        _logger.LogInformation($"Server ID: {_environmentService.GetServerId()}");

        RecordEnd.Hook(RecordEndHookResult, HookMode.Post);
        ConnectClientFunc.Hook(ConnectClientHook, HookMode.Pre);

        ListenForMapChange();

        _gameServer.Ping(ModuleVersion);

        AddCommandListener("say", OnPlayerChat, HookMode.Pre);

        AddCommandListener("say", GagPlayer, HookMode.Pre);
        AddCommandListener("say_team", GagPlayer, HookMode.Pre);
        AddCommandListener("jointeam", HandleJoinTeam, HookMode.Pre);

        _ = _matchService.GetMatchConfigs();

        if (_environmentService.IsOfflineMode())
        {
            _matchService.GetMatchFromOffline();
        }
    }

    public override void Unload(bool hotReload)
    {
        _pingTimer?.Dispose();
        RecordEnd.Unhook(RecordEndHookResult, HookMode.Post);
        ConnectClientFunc.Unhook(ConnectClientHook, HookMode.Pre);

        Marshal.FreeCoTaskMem(PasswordBuffer);

        TimerUtility.Timers.ForEach(
            (timer) =>
            {
                timer.Kill();
            }
        );

        _ = _matchEvents.Disconnect();
    }

    private void Ping(object? state)
    {
        _gameServer.Ping(ModuleVersion);
    }
}
