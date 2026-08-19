using System.Reflection;
using FiveStack.Entities.Practice;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using static SwiftlyS2.Shared.Helper;

namespace NadePractice;

// Standalone plugin, installed through the 5stack game-plugin registry and
// bound to the nade-practice game mode. It is only ever loaded on a practice
// server, so nothing here checks whether practice is "enabled" -- being loaded
// at all is the gate.
[PluginMetadata(
    Id = "NadePractice",
    Version = "__RELEASE_VERSION__",
    Name = "nade-practice",
    Author = "5Stack.gg",
    Description = "Records grenade lineups in game and replays saved lineups back to the thrower"
)]
public partial class NadePracticePlugin : BasePlugin
{
    private ILogger<NadePracticePlugin> _logger = null!;
    private IServiceProvider _serviceProvider = null!;
    private NadesConfig _config = null!;
    private NadesApiClient _api = null!;
    private PracticeSession _session = null!;
    private PracticeRecorder _recorder = null!;
    private PracticeLibrary _library = null!;
    private PracticeReplay _replay = null!;
    private PracticeSystem _system = null!;
    private PracticeScore _score = null!;
    private PracticePlaybook _playbook = null!;
    private PracticeDrill _drill = null!;
    private PracticeSolver _solver = null!;

    private CancellationTokenSource? _secondTimer;
    private CancellationTokenSource? _refillTimer;

    private EventDelegates.OnTick? _tickHandler;
    private EventDelegates.OnEntityCreated? _entityCreatedHandler;
    private EventDelegates.OnMapLoad? _mapLoadHandler;
    private EventDelegates.OnClientDisconnected? _disconnectHandler;
    private EventDelegates.OnClientSteamAuthorize? _authorizeHandler;

    public NadePracticePlugin(ISwiftlyCore core)
        : base(core) { }

    public string ModuleVersion =>
        typeof(NadePracticePlugin).GetCustomAttribute<PluginMetadata>()?.Version ?? "unknown";

    public override void Load(bool hotReload)
    {
        ServiceCollection services = new();
        services
            .AddSwiftly(Core)
            .AddSingleton<NadesConfig>()
            .AddSingleton<NadesApiClient>()
            .AddSingleton<PracticeSession>()
            .AddSingleton<PracticeRecorder>()
            .AddSingleton<PracticeLibrary>()
            .AddSingleton<PracticeReplay>()
            .AddSingleton<PracticeSystem>()
            .AddSingleton<PracticeScore>()
            .AddSingleton<PracticePlaybook>()
            .AddSingleton<PracticeDrill>()
            .AddSingleton<PracticeSolver>();

        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<NadePracticePlugin>>();
        _config = _serviceProvider.GetRequiredService<NadesConfig>();
        _api = _serviceProvider.GetRequiredService<NadesApiClient>();
        _session = _serviceProvider.GetRequiredService<PracticeSession>();
        _recorder = _serviceProvider.GetRequiredService<PracticeRecorder>();
        _library = _serviceProvider.GetRequiredService<PracticeLibrary>();
        _replay = _serviceProvider.GetRequiredService<PracticeReplay>();
        _system = _serviceProvider.GetRequiredService<PracticeSystem>();
        _score = _serviceProvider.GetRequiredService<PracticeScore>();
        _playbook = _serviceProvider.GetRequiredService<PracticePlaybook>();
        _drill = _serviceProvider.GetRequiredService<PracticeDrill>();
        _solver = _serviceProvider.GetRequiredService<PracticeSolver>();

        // addons/swiftlys2/configs is two levels up from
        // addons/swiftlys2/plugins/NadePractice.
        string pluginDirectory =
            Path.GetDirectoryName(typeof(NadePracticePlugin).Assembly.Location) ?? "";
        _config.Load(Path.Join(pluginDirectory, "../../configs"), pluginDirectory);

        _replay.IsSolo = _system.IsSolo;
        _replay.WantsGhosts = _system.WantsGhosts;
        // A solve rains live HE and molotovs on a map people are standing in.
        _system.SolveRunning = () => _solver.IsBusy;
        _session.Refreshed += OnSessionRefreshed;
        _recorder.Thrown += _system.OnThrown;
        _recorder.Finalized += _score.OnFinalized;
        _recorder.Thrown += _drill.OnThrown;
        _score.Scored += _drill.OnScored;

        WirePlaybook();
        WireDrill();

        _tickHandler = OnGameTick;
        Core.Event.OnTick += _tickHandler;

        _entityCreatedHandler = @event => _recorder.OnProjectileCreated(@event.Entity);
        Core.Event.OnEntityCreated += _entityCreatedHandler;

        _mapLoadHandler = @event => OnMapLoad(@event.MapName);
        Core.Event.OnMapLoad += _mapLoadHandler;

        _disconnectHandler = @event => ForPlayer(@event.PlayerId, OnPlayerGone);
        Core.Event.OnClientDisconnected += _disconnectHandler;

        _authorizeHandler = @event =>
            ForPlayer(@event.PlayerId, steamId => _library.Refresh(steamId));
        Core.Event.OnClientSteamAuthorize += _authorizeHandler;

        InitializeConnectClientHook();

        // One repeating job for the whole plugin, not one per player. These
        // deliberately do not get StopOnMapChange: the plugin is not reloaded
        // on a map change, so a timer that stopped there would never come back.
        _secondTimer = Core.Scheduler.RepeatBySeconds(1, OnSecond);
        _refillTimer = Core.Scheduler.RepeatBySeconds(0.1f, OnFastTick);

        _library.SetMap(Core.Engine.GlobalVars.MapName.ToString());
        RefreshEverything();

        _logger.LogInformation(
            "nade practice {version} loaded (connected: {connected})",
            ModuleVersion,
            _config.IsConnected()
        );
    }

    public override void Unload()
    {
        _session.Refreshed -= OnSessionRefreshed;
        _recorder.Thrown -= _system.OnThrown;
        _recorder.Finalized -= _score.OnFinalized;
        _recorder.Thrown -= _drill.OnThrown;
        _score.Scored -= _drill.OnScored;

        if (_tickHandler != null)
        {
            Core.Event.OnTick -= _tickHandler;
        }

        if (_entityCreatedHandler != null)
        {
            Core.Event.OnEntityCreated -= _entityCreatedHandler;
        }

        if (_mapLoadHandler != null)
        {
            Core.Event.OnMapLoad -= _mapLoadHandler;
        }

        if (_disconnectHandler != null)
        {
            Core.Event.OnClientDisconnected -= _disconnectHandler;
        }

        if (_authorizeHandler != null)
        {
            Core.Event.OnClientSteamAuthorize -= _authorizeHandler;
        }

        UninstallConnectClientHook();

        _secondTimer?.Cancel();
        _secondTimer = null;
        _refillTimer?.Cancel();
        _refillTimer = null;

        _playbook.Reset();
        _drill.Reset();
        _system.Reset();
        _solver.Reset();
    }

    // The plugin's one line to a machine. Written straight to the server
    // console rather than through the logger, so the text an external recorder
    // greps for is the text this repo wrote, with no level, category or colour
    // in front of it.
    private void Signal(string? line)
    {
        if (!string.IsNullOrEmpty(line))
        {
            Core.ConsoleOutput.WriteToServerConsole(line + "\n");
        }
    }

    // The recorder and the solver both sample projectiles, and both have to do
    // it on the game tick: a grenade that vanishes between two slower polls
    // takes its landing point with it.
    private void OnGameTick()
    {
        _recorder.OnTick();
        _solver.OnTick();
    }

    private void OnSecond()
    {
        _system.Tick();
        _playbook.Second();
        _drill.Second();
        _solver.RefreshVisibility();
    }

    private void OnFastTick()
    {
        _system.RefillUtility();
        _playbook.Tick();
        _solver.Pump();
    }

    // A step stands a player on its lineup by the same path .load does, so the
    // teleport, the utility and the preview cannot drift apart.
    private void WirePlaybook()
    {
        _playbook.Load = (steamId, lineup) =>
        {
            IPlayer? player = _system.Find(steamId);

            if (player != null && player.IsValid)
            {
                Apply(player, lineup);
            }
        };

        _playbook.Chat = message =>
            Core.PlayerManager.SendChat($" {ChatColors.Green}{message}".Colored());

        _playbook.Tell = (steamId, message) => Tell(steamId, $" {ChatColors.Green}{message}");

        _playbook.Center = (steamId, message) =>
        {
            IPlayer? player = _system.Find(steamId);

            if (player != null && player.IsValid)
            {
                player.SendCenter(message);
            }
        };
    }

    // A drill stands a player on its lineup by the same path .load does, and
    // says so only to them: several people drill in one server.
    private void WireDrill()
    {
        _drill.Load = (steamId, lineup) =>
        {
            IPlayer? player = _system.Find(steamId);

            if (player == null || !player.IsValid || !_config.ReplayEnabled)
            {
                return false;
            }

            Apply(player, lineup);

            return true;
        };

        _drill.Tell = (steamId, message) => Tell(steamId, $" {ChatColors.Green}{message}");

        _drill.Note = (steamId, message) => Tell(steamId, $" {ChatColors.Grey}{message}");

        _drill.Center = (steamId, message) =>
        {
            IPlayer? player = _system.Find(steamId);

            if (player != null && player.IsValid)
            {
                player.SendCenter(message);
            }
        };
    }

    // A run ends with the player who was in it: the map is still standing, but
    // nobody is left to be teleported or told anything.
    private void OnPlayerGone(ulong steamId)
    {
        _drill.Forget(steamId);
        _system.Forget(steamId);
    }

    // Swiftly's client events carry a slot, not a steam id.
    private void ForPlayer(int playerId, Action<ulong> action)
    {
        IPlayer? player = Core.PlayerManager.GetPlayer(playerId);

        if (player == null || !player.IsValid || player.IsFakeClient)
        {
            return;
        }

        action(player.SteamID);
    }

    private void OnMapLoad(string mapName)
    {
        _recorder.Reset();
        _playbook.Reset();
        _drill.Reset();
        _score.Reset();
        _system.Reset();
        // A calibration is a statement about one map's collision mesh, so it
        // does not survive the mesh being replaced.
        _solver.Reset();
        _library.SetMap(mapName);

        RefreshEverything();
    }

    // The panel is the only source of both the roster and the library, so a
    // refresh is one round trip followed by one per connected player.
    private void RefreshEverything()
    {
        _ = Task.Run(async () =>
        {
            await _session.Refresh();
            await _api.Drain();
        });

        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player != null && player.IsValid && !player.IsFakeClient)
            {
                _library.Refresh(player.SteamID);
            }
        }
    }

    private void OnSessionRefreshed(PracticeSessionData session)
    {
        if (!string.IsNullOrEmpty(session.password))
        {
            SetPasswordBuffer(session.password);
        }
    }
}
