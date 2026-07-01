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
    private readonly CaptainSystem _captainSystem;
    private readonly RankSystem _rankSystem;
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
        GameDemos gameDemos,
        GameServer gameServer,
        MatchEvents matchEvents,
        MatchService matchService,
        CaptainSystem captainSystem,
        RankSystem rankSystem,
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
        _gameDemos = gameDemos;
        _gameServer = gameServer;
        _matchEvents = matchEvents;
        _readySystem = readySystem;
        _matchService = matchService;
        _captainSystem = captainSystem;
        _rankSystem = rankSystem;
        _timeoutSystem = timeoutSystem;
        _surrenderSystem = surrenderSystem;
        _gameBackupRounds = backUpManagement;
        _environmentService = environmentService;

        _pingTimer = new Timer(Ping, null, TimeSpan.Zero, TimeSpan.FromSeconds(15));
    }

    private Timer _pingTimer;
    private readonly System.Threading.CancellationTokenSource _shutdownCts = new();

    public override void Load(bool hotReload)
    {
        RegisterListener<Listeners.OnGameServerSteamAPIActivated>(_steamAPI.OnSteamAPIActivated);
        RegisterListener<Listeners.OnGameServerSteamAPIDeactivated>(
            _steamAPI.OnSteamAPIDeactivated
        );

        RegisterListener<Listeners.OnTick>(_rankSystem.OnTick);

        if (hotReload)
        {
            _steamAPI.OnSteamAPIActivated();
        }

        _environmentService.Load();

        _logger.LogInformation($"Server ID: {_environmentService.GetServerId()}");

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

        // On a fresh start (not a hot reload, where a recording may still be
        // live) any recording lock left on disk is stale from a crash.
        if (!hotReload)
        {
            _gameDemos.ClearStaleRecordingLocks();
        }

        // Recover demos left on disk by a previous crash/restart, once env/API have settled.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(15), _shutdownCts.Token);
                await _gameDemos.UploadOrphanedDemos(_shutdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                // plugin unloaded before/while recovering
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to recover orphaned demos: {ex.Message}");
            }
        });
    }

    public override void Unload(bool hotReload)
    {
        _shutdownCts.Cancel();
        _pingTimer?.Dispose();
        ConnectClientFunc.Unhook(ConnectClientHook, HookMode.Pre);

        Marshal.FreeCoTaskMem(PasswordBuffer);

        TimerUtility.Timers.ForEach(
            (timer) =>
            {
                timer.Kill();
            }
        );

        _matchService.GetCurrentMatch()?.Reset();

        _ = _matchEvents.Disconnect();
    }

    private void Ping(object? state)
    {
        _gameServer.Ping(ModuleVersion);
    }
}
