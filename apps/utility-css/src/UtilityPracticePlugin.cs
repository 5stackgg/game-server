using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Cvars;
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
        // A grenade's thrower and initial velocity are not populated at the
        // moment the entity is created -- read them there and every throw is
        // dropped for having no thrower. One frame later they are set.
        RegisterListener<Listeners.OnEntityCreated>(entity =>
            Server.NextFrame(() =>
            {
                if (entity != null && entity.IsValid)
                {
                    _recorder.OnProjectileCreated(entity);
                }
            })
        );
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorized);
        RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
        RegisterListener<Listeners.CheckTransmit>(OnCheckTransmit);

        ConnectClientFunc.Hook(ConnectClientHook, HookMode.Pre);

        // One repeating job for the whole plugin, not one per player. The
        // execute runner rides the same two rather than starting a third.
        _secondTimer = AddTimer(1f, OnSecond, TimerFlags.REPEAT);
        _refillTimer = AddTimer(0.1f, OnFastTick, TimerFlags.REPEAT);

        // Only on a hot reload -- see the swiftly plugin: a cold boot has no map
        // yet, and OnMapChange does both of these when it arrives.
        if (hotReload)
        {
            _library.SetMap(Server.MapName);
            ApplyPracticeCfg();
            RefreshEverything();
        }

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
        EndWarmup();
        RespawnTheDead();
        KeepEveryoneStocked();
        ReportOccupancy();
        _system.Tick();
        _playbook.Second();
        _drill.Second();
    }

    // Nobody stays dead on a practice server. Rejoining while dead, falling off
    // something, or a stray molotov all leave a player spectating a map they
    // came here to throw on -- and no round ever ends to bring them back.
    private void RespawnTheDead()
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot || player.PawnIsAlive)
            {
                continue;
            }

            if (player.Team is CsTeam.CounterTerrorist or CsTeam.Terrorist)
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

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player != null && player.IsValid && !player.IsBot)
            {
                present.Add(player.SteamID);
            }
        }

        _ = Task.Run(() => _api.Occupancy(present));
    }

    private int _warmupTicks;

    // A practice server is never in warmup. Enforced rather than set once:
    // mp_warmup_end at map load runs before warmup has begun, and the game
    // starts one of its own whenever it feels like it -- on the first connect,
    // on a restart, after a mode cfg lands.
    private void EndWarmup()
    {
        if (--_warmupTicks > 0)
        {
            return;
        }

        CCSGameRules? rules = Utilities
            .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()
            ?.GameRules;

        if (rules == null || !rules.WarmupPeriod)
        {
            return;
        }

        // Not every tick: the command takes a moment to land, and re-issuing it
        // in the meantime achieves nothing.
        _warmupTicks = WarmupRetrySeconds;
        Server.ExecuteCommand("mp_warmup_end");
    }

    private void KeepEveryoneStocked()
    {
        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player == null || !player.IsValid || player.IsBot)
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

        foreach (CCSPlayerController player in Utilities.GetPlayers())
        {
            if (player.IsValid && !player.IsBot)
            {
                _library.Refresh(player.SteamID);
            }
        }
    }

    // The state a practice server has to be in, applied by the plugin rather
    // than a game mode cfg: a practice server may be a third-party dedicated
    // box that no mode was ever selected for, and without this it sits in
    // warmup with no money and no utility.
    private readonly HashSet<ulong> _welcomed = new();

    private const int OccupancySeconds = 15;
    private const int WarmupRetrySeconds = 3;

    private const float CfgReapplySeconds = 3f;

    private static readonly string[] PracticeCfg = new[]
    {
        "sv_cheats 1",
        // Nothing ends the round: a kill or an expired timer would reset
        // everyone mid-lineup.
        "mp_ignore_round_win_conditions 1",
        "mp_warmuptime 1",
        "mp_warmup_pausetimer 0",
        "mp_halftime 0",
        "mp_match_can_clinch 0",
        "mp_team_intro_time 0",
        "mp_round_restart_delay 0",
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
        // Valve's own map-guide editor. Every annotation_* command is client
        // side, so a plugin can never draw one for a player -- but this cvar
        // decides whether they may draw their own, and it ships at view-only.
        // On a practice server there is no reason to withhold the editor.
        "sv_allow_annotations_access_level 2",
        "sv_infinite_ammo 1",
        "ammo_grenade_limit_total 5",
        "sv_full_alltalk 1",
        "tv_enable 0",
    };

    private void ApplyPracticeCfg()
    {
        // Twice, and the second one is the one that usually takes. On a map
        // change the frame after load is before warmup has begun, so
        // mp_warmup_end there ends nothing and the server sits in a countdown.
        Server.NextFrame(() => RunPracticeCfg());
        AddTimer(CfgReapplySeconds, () => RunPracticeCfg());
    }

    private void RunPracticeCfg()
    {
        Server.ExecuteCommand(string.Join(";", PracticeCfg));

        // The map change did not take the session with it, and sv_password is
        // the one thing here that is per-session rather than per-map.
        PracticeSessionData? session = _session.Current;

        if (session == null || string.IsNullOrEmpty(session.password))
        {
            return;
        }

        var password = ConVar.Find("sv_password");
        password?.SetValue(session.password);
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
        var password = ConVar.Find("sv_password");

        if (password is null)
        {
            _logger.LogError(
                "could not find sv_password; assigned players will be rejected"
            );
            return;
        }

        password.SetValue(session.password);

        _logger.LogInformation(
            "practice session {session} password applied to sv_password",
            session.id
        );
    }
}
