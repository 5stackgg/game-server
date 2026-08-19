using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities.Practice;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace UtilityPractice;

// Standalone plugin, installed through the 5stack game-plugin registry and
// bound to the utility-practice game mode. It is only ever loaded on a practice
// server, so nothing here checks whether practice is "enabled" -- being loaded
// at all is the gate.
[MinimumApiVersion(80)]
public partial class UtilityPracticePlugin : BasePlugin
{
    private readonly UtilityConfig _config;
    private readonly UtilityApiClient _api;
    private readonly PracticeSession _session;
    private readonly PracticeRecorder _recorder;
    private readonly PracticeLibrary _library;
    private readonly PracticeReplay _replay;
    private readonly PracticeSystem _system;
    private readonly PracticeScore _score;
    private readonly PracticePlaybook _playbook;
    private readonly PracticeDrill _drill;
    private readonly ILogger<UtilityPracticePlugin> _logger;

    private Timer? _secondTimer;
    private Timer? _refillTimer;

    public override string ModuleName => "UtilityPractice";
    public override string ModuleVersion => "__RELEASE_VERSION__";
    public override string ModuleAuthor => "5Stack.gg";
    public override string ModuleDescription =>
        "Records grenade lineups in game and replays saved lineups back to the thrower";

    public UtilityPracticePlugin(
        UtilityConfig config,
        UtilityApiClient api,
        PracticeSession session,
        PracticeRecorder recorder,
        PracticeLibrary library,
        PracticeReplay replay,
        PracticeSystem system,
        PracticeScore score,
        PracticePlaybook playbook,
        PracticeDrill drill,
        ILogger<UtilityPracticePlugin> logger
    )
    {
        _config = config;
        _api = api;
        _session = session;
        _recorder = recorder;
        _library = library;
        _replay = replay;
        _system = system;
        _score = score;
        _playbook = playbook;
        _drill = drill;
        _logger = logger;
    }

    public override void Load(bool hotReload)
    {
        _config.Load(Path.Join(ModuleDirectory, "../../configs"), ModuleDirectory);

        _session.Refreshed += OnSessionRefreshed;
        _recorder.Thrown += _system.OnThrown;
        _recorder.Finalized += _score.OnFinalized;
        _recorder.Thrown += _drill.OnThrown;
        _score.Scored += _drill.OnScored;
        _replay.WantsGhosts = _system.WantsGhosts;

        WirePlaybook();
        WireDrill();

        RegisterListener<Listeners.OnTick>(_recorder.OnTick);
        RegisterListener<Listeners.OnEntityCreated>(_recorder.OnProjectileCreated);
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);

        ConnectClientFunc.Hook(ConnectClientHook, HookMode.Pre);

        // One repeating job for the whole plugin, not one per player. The
        // execute runner rides the same two rather than starting a third.
        _secondTimer = AddTimer(1f, OnSecond, TimerFlags.REPEAT);
        _refillTimer = AddTimer(0.1f, OnFastTick, TimerFlags.REPEAT);

        _library.SetMap(Server.MapName);
        RefreshEverything();

        _logger.LogInformation(
            "utility practice {version} loaded (connected: {connected})",
            ModuleVersion,
            _config.IsConnected()
        );
    }

    public override void Unload(bool hotReload)
    {
        _session.Refreshed -= OnSessionRefreshed;
        _recorder.Thrown -= _system.OnThrown;
        _recorder.Finalized -= _score.OnFinalized;
        _recorder.Thrown -= _drill.OnThrown;
        _score.Scored -= _drill.OnScored;

        ConnectClientFunc.Unhook(ConnectClientHook, HookMode.Pre);

        _secondTimer?.Kill();
        _secondTimer = null;
        _refillTimer?.Kill();
        _refillTimer = null;

        _playbook.Reset();
        _drill.Reset();
        _system.Reset();
    }

    private void OnSecond()
    {
        _system.Tick();
        _playbook.Second();
        _drill.Second();
    }

    private void OnFastTick()
    {
        _system.RefillUtility();
        _playbook.Tick();
    }

    // A step stands a player on its lineup by the same path .load does, so the
    // teleport, the utility and the preview cannot drift apart.
    private void WirePlaybook()
    {
        _playbook.Load = (steamId, lineup) =>
        {
            CCSPlayerController? player = Utilities.GetPlayerFromSteamId(steamId);

            if (player != null && player.IsValid)
            {
                Apply(player, lineup);
            }
        };

        _playbook.Chat = message => Server.PrintToChatAll($" {ChatColors.Green}{message}");

        _playbook.Tell = (steamId, message) => Tell(steamId, $" {ChatColors.Green}{message}");

        _playbook.Center = (steamId, message) =>
        {
            CCSPlayerController? player = Utilities.GetPlayerFromSteamId(steamId);

            if (player != null && player.IsValid)
            {
                player.PrintToCenter(message);
            }
        };
    }

    // A drill stands a player on its lineup by the same path .load does, and
    // says so only to them: several people drill in one server.
    private void WireDrill()
    {
        _drill.Load = (steamId, lineup) =>
        {
            CCSPlayerController? player = Utilities.GetPlayerFromSteamId(steamId);

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
            CCSPlayerController? player = Utilities.GetPlayerFromSteamId(steamId);

            if (player != null && player.IsValid)
            {
                player.PrintToCenter(message);
            }
        };
    }

    private void OnMapStart(string mapName)
    {
        _recorder.Reset();
        _playbook.Reset();
        _drill.Reset();
        _score.Reset();
        _system.Reset();
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

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player.IsValid && !player.IsBot)
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
