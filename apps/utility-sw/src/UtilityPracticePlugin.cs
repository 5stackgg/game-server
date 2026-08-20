using System.Reflection;
using FiveStack.Entities.Practice;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using static SwiftlyS2.Shared.Helper;
using SwiftlyS2.Shared.SchemaDefinitions;

namespace UtilityPractice;

// Standalone plugin, installed through the 5stack game-plugin registry and
// bound to the utility-practice game mode. It is only ever loaded on a practice
// server, so nothing here checks whether practice is "enabled" -- being loaded
// at all is the gate.
[PluginMetadata(
    Id = "UtilityPractice",
    Version = "__RELEASE_VERSION__",
    Name = "utility-practice",
    Author = "5Stack.gg",
    Description = "Records grenade lineups in game and replays saved lineups back to the thrower"
)]
public partial class UtilityPracticePlugin : BasePlugin
{
    private ILogger<UtilityPracticePlugin> _logger = null!;
    private IServiceProvider _serviceProvider = null!;
    private UtilityConfig _config = null!;
    private UtilityApiClient _api = null!;
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
    private readonly HashSet<ulong> _welcomed = new();
    private EventDelegates.OnMapLoad? _mapLoadHandler;
    private EventDelegates.OnClientDisconnected? _disconnectHandler;
    private EventDelegates.OnClientSteamAuthorize? _authorizeHandler;

    public UtilityPracticePlugin(ISwiftlyCore core)
        : base(core) { }

    public string ModuleVersion =>
        typeof(UtilityPracticePlugin).GetCustomAttribute<PluginMetadata>()?.Version ?? "unknown";

    public override void Load(bool hotReload)
    {
        ServiceCollection services = new();
        services
            .AddSwiftly(Core)
            .AddSingleton<UtilityConfig>()
            .AddSingleton<UtilityApiClient>()
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
        _logger = _serviceProvider.GetRequiredService<ILogger<UtilityPracticePlugin>>();
        _config = _serviceProvider.GetRequiredService<UtilityConfig>();
        _api = _serviceProvider.GetRequiredService<UtilityApiClient>();
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
        // addons/swiftlys2/plugins/UtilityPractice.
        string pluginDirectory =
            Path.GetDirectoryName(typeof(UtilityPracticePlugin).Assembly.Location) ?? "";
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

        // A grenade's thrower and initial velocity are not populated at the
        // moment the entity is created -- read them there and every throw is
        // dropped for having no thrower. One tick later they are set.
        _entityCreatedHandler = @event =>
        {
            CEntityInstance entity = @event.Entity;
            Core.Scheduler.NextTick(() =>
            {
                if (entity.IsValid)
                {
                    _recorder.OnProjectileCreated(entity);
                }
            });
        };
        Core.Event.OnEntityCreated += _entityCreatedHandler;

        _mapLoadHandler = @event => OnMapLoad(@event.MapName);
        Core.Event.OnMapLoad += _mapLoadHandler;

        _disconnectHandler = @event =>
            ForPlayer(
                @event.PlayerId,
                steamId =>
                {
                    _welcomed.Remove(steamId);
                    OnPlayerGone(steamId);
                }
            );
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

        // Only on a hot reload. A cold boot has no engine globals yet -- asking
        // for the map here is what stopped the plugin loading at all -- and the
        // map arrives moments later with OnMapLoad, which does both of these.
        // A hot reload has already missed that event, so this is its only chance.
        if (hotReload)
        {
            _library.SetMap(Core.Engine.GlobalVars.MapName.ToString());
            ApplyPracticeCfg();
            RefreshEverything();
        }

        _logger.LogInformation(
            "utility practice {version} loaded (connected: {connected})",
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
        RespawnTheDead();
        KeepEveryoneStocked();
        ReportOccupancy();
        _system.Tick();
        _playbook.Second();
        _drill.Second();
        _solver.RefreshVisibility();
    }

    // Nobody stays dead on a practice server. Rejoining while dead, falling off
    // something, or a stray molotov all leave a player spectating a map they
    // came here to throw on -- and no round ever ends to bring them back.
    private void RespawnTheDead()
    {
        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient || player.IsAlive)
            {
                continue;
            }

            if (player.Controller.Team is Team.CT or Team.T)
            {
                player.Respawn();
            }
        }
    }

    // Every second rather than only on spawn: a respawn, a team switch and a
    // round reset all hand a player an empty bag, and the cost of checking is
    // one loop over the weapons they already have.
    private int _occupancyTicks;

    // Every few seconds, not every one: the panel only needs to know somebody
    // is here, and the reaper's clocks are measured in minutes.
    private void ReportOccupancy()
    {
        if (++_occupancyTicks < OccupancySeconds)
        {
            return;
        }

        _occupancyTicks = 0;

        var present = new List<ulong>();

        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player != null && player.IsValid && !player.IsFakeClient)
            {
                present.Add(player.SteamID);
            }
        }

        _ = Task.Run(() => _api.Occupancy(present));
    }

    private void KeepEveryoneStocked()
    {
        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                continue;
            }

            _system.GiveUtility(player);
        }
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

        ApplyPracticeCfg();

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
        if (string.IsNullOrEmpty(session.password))
        {
            _logger.LogWarning(
                "practice session {session} carries no password; the connect hook has nothing to present",
                session.id
            );
            return;
        }

        SetPasswordBuffer(session.password);

        // The buffer only substitutes this password into the connect call --
        // the server still has to be the one asking for it. Without this the
        // hook hands over a password sv_password never heard of, and every
        // assigned player is turned away with "bad password".
        if (!TrySetConVar("sv_password", session.password))
        {
            _logger.LogError(
                "could not set sv_password; assigned players will be rejected"
            );
            return;
        }

        _logger.LogInformation(
            "practice session {session} password applied to sv_password",
            session.id
        );
    }

    // The state a practice server has to be in, applied by the plugin rather
    // than a game mode cfg: a practice server may be a third-party dedicated
    // box that no mode was ever selected for, and without this it sits in
    // warmup with no money and no utility.
    private const int OccupancySeconds = 15;

    private const float CfgReapplySeconds = 3f;

    private static readonly string[] PracticeCfg = new[]
    {
        "sv_cheats 1",
        // Nothing ends the round: a kill or an expired timer would reset
        // everyone mid-lineup.
        "mp_ignore_round_win_conditions 1",
        "mp_warmup_end",
        "mp_freezetime 0",
        "mp_roundtime 60",
        "mp_roundtime_defuse 60",
        "mp_respawn_immunitytime 0",
        "mp_buy_anywhere 1",
        "mp_buytime 60000",
        "mp_maxmoney 65535",
        "mp_startmoney 65535",
        "mp_afterroundmoney 65535",
        "mp_death_drop_gun 0",
        "mp_death_drop_grenade 0",
        "mp_solid_teammates 0",
        "mp_teammates_are_enemies 0",
        "sv_grenade_trajectory_prac_pipreview 1",
        "sv_infinite_ammo 1",
        "ammo_grenade_limit_total 5",
        "sv_full_alltalk 1",
        "tv_enable 0",
    };

    private void ApplyPracticeCfg()
    {
        // Twice, and the second one is the one that usually takes. On a map
        // change the tick after load is before warmup has begun, so
        // mp_warmup_end there ends nothing and the server sits in a countdown.
        Core.Scheduler.NextTick(() => RunPracticeCfg());
        Core.Scheduler.DelayBySeconds(CfgReapplySeconds, () => RunPracticeCfg());
    }

    private void RunPracticeCfg()
    {
        Core.Engine.ExecuteCommand(string.Join(";", PracticeCfg));

        // The map change did not take the session with it, and sv_password is
        // the one thing here that is per-session rather than per-map.
        PracticeSessionData? session = _session.Current;

        if (session != null && !string.IsNullOrEmpty(session.password))
        {
            TrySetConVar("sv_password", session.password);
        }
    }

    private bool TrySetConVar(string name, string value)
    {
        try
        {
            var conVar = Core.ConVar.Find<string>(name);

            if (conVar == null)
            {
                return false;
            }

            conVar.Value = value;
            return true;
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "failed setting convar {Name}", name);
            return false;
        }
    }
}
