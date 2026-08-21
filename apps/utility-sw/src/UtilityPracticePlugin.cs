using System.Reflection;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Events;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.Plugins;
using static SwiftlyS2.Shared.Helper;
using SwiftlyS2.Shared.Natives;
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
    private EventDelegates.OnPrecacheResource? _precacheHandler;
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
        _replay.All = steamId => _library.For(steamId);
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

        // The grenade models floated over each lineup have to be in the map's
        // precache list or they render as ERROR. This fires at map load, which
        // is why a plugin hot-reloaded mid-map cannot show them until the next
        // map change.
        _precacheHandler = @event =>
        {
            foreach (string model in PracticeLineupUtility.AllUtilityModels())
            {
                @event.AddItem(model);
            }
        };
        Core.Event.OnPrecacheResource += _precacheHandler;

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
        // Whatever the last instance drew is still in the world and nothing in
        // this one has a handle to it.
        int swept = _replay.SweepMarkers();

        if (swept > 0)
        {
            _logger.LogInformation("swept {swept} marker(s) left by a previous load", swept);
        }

        if (hotReload)
        {
            _library.SetMap(Core.Engine.GlobalVars.MapName.ToString());
            ApplyPracticeCfg();
            RefreshEverything();
        }

        _logger.LogInformation(
            "utility practice {version} loaded (connected: {connected}) [{switches}]",
            ModuleVersion,
            _config.IsConnected(),
            PracticeReplay.SwitchState()
        );
    }

    public override void Unload()
    {
        // Drawn entities are not the plugin's to leave behind: without this a
        // hot reload orphans every beam, label and model in the world, with no
        // instance left holding a reference to any of them.
        _replay.SweepMarkers();

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

            if (_precacheHandler != null)
            {
                Core.Event.OnPrecacheResource -= _precacheHandler;
            }
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
        AimFeedback();

        // Cheap: it only redraws when the set of lineups under the player's
        // feet actually changes, which is when they step onto or off a spot.
        if (_aimTick % SpotWatchEveryTicks == 0)
        {
            SpotWatch();
        }
    }

    // What LINED UP means is the lineup's own business -- the same number that
    // turns its crosshair green, so the two can never disagree.
    private static float ToleranceFor(LineupRecord lineup)
    {
        return lineup.aim_tolerance > 0f
            ? lineup.aim_tolerance
            : PracticeLineupUtility.DefaultAimTolerance;
    }

    // Centre text has to be re-sent to stay on screen. Four ticks is sixteen
    // updates a second, which is fast enough that a hundredth-of-a-degree
    // readout tracks the mouse instead of lagging behind it.
    private const int AimFeedbackEveryTicks = 4;

    // Walking pace does not need sixty-four checks a second.
    private const int SpotWatchEveryTicks = 16;

    // How near the crosshair has to be to a ring to count as pointing at it.
    // Tight enough that two rings a stride apart are separable at the distance
    // you would stand to look at them.
    private const double RingHoverDegrees = 7.0;

    // Below this a ring is under the player's feet, and the angle to it says
    // nothing about which way they are facing.
    private const float RingHoverMinDistance = 96f;

    // How near the crosshair has to be to a throw's recorded aim to count as
    // meaning that throw. Generous: this is "which of these did you mean", not
    // the tenth-of-a-degree check that says you are on the line.
    private const float AimPickDegrees = 25f;

    private int _aimTick;

    // Lining a crosshair up is the part of a lineup that cannot be shown by
    // standing somewhere, so the moment it IS lined up is the moment to say
    // how to throw -- while they are still looking at the reticle.
    // Which spot each player is standing in, so walking into a stance ring can
    // light up everything throwable from it without redrawing every tick.
    private readonly Dictionary<ulong, string> _standingIn = new();

    // A spot is identified by the set of lineups thrown from it, so stepping
    // between two overlapping spots counts as a change.
    private void SpotWatch()
    {
        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                continue;
            }

            CCSPlayerPawn? pawn = player.PlayerPawn;

            if (pawn == null || !pawn.IsValid)
            {
                continue;
            }

            Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
            var at = new Vec3(origin.X, origin.Y, origin.Z);

            IReadOnlyList<LineupRecord> library = _library.For(player.SteamID);
            List<LineupRecord> here = PracticeReplay.SpotAt(library, at);

            // Which ring the player is LOOKING at. Where two stances overlap,
            // walking in cannot say which throw is meant -- and picking one by
            // arrival order means the aim marker changes depending on which
            // side you stepped in from. Pointing at a ring is unambiguous.
            LineupRecord? aimedAt = LookingAt(pawn, at, library, here);

            // Standing on a spot shows every throw off it -- you cannot choose
            // between options you cannot see. Looking toward one only decides
            // which is drawn heavy. Off a spot, pointing at a ring across the
            // room still shows that one on its own.
            List<LineupRecord> show =
                here.Count > 0
                    ? here
                    : aimedAt != null
                        ? new List<LineupRecord> { aimedAt }
                        : new List<LineupRecord>();

            string key = string.Join(
                ",",
                show.Select(entry => entry.client_id)
                    .OrderBy(id => id)
                    .Append(aimedAt?.client_id ?? "-")
            );

            if (_standingIn.TryGetValue(player.SteamID, out string? was) && was == key)
            {
                continue;
            }

            _standingIn[player.SteamID] = key;

            // The library is already drawn; only this player's selection moves.
            _replay.ShowSelection(player, show, at, aimedAt);

            // Looking at a ring IS choosing it: the crosshair, the name and the
            // angular guidance have to describe one throw, and they read the
            // loaded lineup. Only while standing on a spot, so glancing across
            // the map at a distant ring cannot silently retarget the loaded one.
            if (aimedAt != null && here.Count > 0)
            {
                _system.StateFor(player.SteamID).Loaded = aimedAt;
            }
        }
    }

    // The stance ring the player's crosshair is nearest to, by angle rather
    // than distance so a ring across the room can be picked as easily as one
    // underfoot.
    // Which throw the player means, decided by where they are LOOKING rather
    // than which ring they are nearest.
    //
    // Two candidates, in order:
    //   - the throw whose recorded aim is closest to the player's current view,
    //     which is what "look toward the smoke" means when several throws share
    //     one spot and every ring is under your feet;
    //   - failing that, a ring they are pointing at from a distance, which is
    //     how you pick a spot across the room.
    private static LineupRecord? LookingAt(
        CCSPlayerPawn pawn,
        Vec3 at,
        IReadOnlyList<LineupRecord> library,
        IReadOnlyList<LineupRecord> here
    )
    {
        QAngle eyes = pawn.EyeAngles;

        if (here.Count > 0)
        {
            LineupRecord? bestAim = null;
            float bestOff = AimPickDegrees;

            foreach (LineupRecord lineup in here)
            {
                float off = PracticeLineupUtility.AimError(
                    eyes.Y,
                    eyes.X,
                    lineup.release.yaw,
                    lineup.release.pitch
                );

                if (off < bestOff)
                {
                    bestOff = off;
                    bestAim = lineup;
                }
            }

            if (bestAim != null)
            {
                return bestAim;
            }

            // Standing on a spot but looking nowhere near any of its throws:
            // naming one anyway would be the arbitrary pick this replaced.
            if (here.Count > 1)
            {
                return null;
            }
        }

        double yaw = eyes.Y * Math.PI / 180.0;
        double pitch = eyes.X * Math.PI / 180.0;
        double flat = Math.Cos(pitch);

        var view = new Vec3(
            (float)(Math.Cos(yaw) * flat),
            (float)(Math.Sin(yaw) * flat),
            (float)(-Math.Sin(pitch))
        );

        var eye = new Vec3(at.x, at.y, at.z + 64f);

        LineupRecord? best = null;
        double bestAngle = RingHoverDegrees;

        foreach (LineupRecord lineup in library)
        {
            Vec3 feet = lineup.release.feet_position;

            // The floating grenade is the spot's face, so pointing at it has
            // to count the same as pointing at the ground ring under it --
            // with no ground text left, the model is what a player actually
            // aims at to ask "what is this one called".
            foreach (
                float lift in new[] { 0f, PracticeReplay.UtilityModelHeight }
            )
            {
                var toRing = new Vec3(
                    feet.x - eye.x,
                    feet.y - eye.y,
                    feet.z + lift - eye.z
                );
                float length = toRing.Length();

                // A ring you are standing on is straight down from the eye,
                // which is never what "looking at" means.
                if (length < RingHoverMinDistance)
                {
                    continue;
                }

                double dot =
                    (view.x * toRing.x + view.y * toRing.y + view.z * toRing.z)
                    / length;

                double angle =
                    Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * 180.0 / Math.PI;

                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = lineup;
                }
            }
        }

        return best;
    }

    private void AimFeedback()
    {
        if (++_aimTick % AimFeedbackEveryTicks != 0)
        {
            return;
        }

        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                continue;
            }

            CCSPlayerPawn? pawn = player.PlayerPawn;

            if (pawn == null || !pawn.IsValid)
            {
                continue;
            }

            QAngle eyes = pawn.EyeAngles;

            Vector standing = pawn.AbsOrigin ?? new Vector(0, 0, 0);

            _replay.TintAim(
                player,
                eyes.Y,
                eyes.X,
                new Vec3(standing.X, standing.Y, standing.Z)
            );

            string? message = CentreText(player, pawn);

            if (message != null)
            {
                player.SendCenter(message);
            }
        }
    }

    // First match wins. The order is the order a player needs the answer in:
    // how to throw it once they are on the angle, otherwise what they are
    // pointing at, otherwise what they are standing on.
    private string? CentreText(IPlayer player, CCSPlayerPawn pawn)
    {
        LineupRecord? lineup = _system.StateFor(player.SteamID).Loaded;

        if (lineup != null)
        {
            QAngle eyes = pawn.EyeAngles;
            Vector standing = pawn.AbsOrigin ?? new Vector(0, 0, 0);
            Vec3 spot = lineup.release.feet_position;

            bool onAngle =
                PracticeLineupUtility.AimError(
                    eyes.Y,
                    eyes.X,
                    lineup.release.yaw,
                    lineup.release.pitch
                ) <= ToleranceFor(lineup);

            // Both halves, not just the angle. Saying LINED UP to somebody
            // holding the right angle from the wrong place is worse than saying
            // nothing -- they throw it, it misses, and the lineup gets blamed.
            bool onSpot =
                PracticeLineupUtility.StanceMiss(
                    new Vec3(spot.x - standing.X, spot.y - standing.Y, 0f).LengthXY()
                ) == 0f;

            return Describe(lineup, onSpot, onAngle);
        }

        Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
        var at = new Vec3(origin.X, origin.Y, origin.Z);

        IReadOnlyList<LineupRecord> library = _library.For(player.SteamID);
        List<LineupRecord> here = PracticeReplay.SpotAt(library, at);
        LineupRecord? aimedAt = LookingAt(pawn, at, library, here);

        if (aimedAt != null)
        {
            return Describe(aimedAt, here.Contains(aimedAt));
        }

        if (here.Count == 1)
        {
            return Describe(here[0], true);
        }

        // Several throws off one spot: name them all rather than a count, so the
        // player knows what is on offer before choosing one.
        if (here.Count > 1)
        {
            return string.Join("\n", here.Select(entry => entry.name));
        }

        return null;
    }

    // Three rows, always the same three, so the window never reflows under the
    // player's eyes: WHAT it is called, HOW it is thrown, WHAT TO DO NEXT.
    // The name stays put throughout -- losing it the moment you lined up was
    // the thing that made this window feel like it was flickering at you.
    private static string Describe(
        LineupRecord lineup,
        bool onSpot = false,
        bool onAngle = false
    )
    {
        string next =
            !onSpot ? PracticeReplay.Tracked("stand in the circle")
            : !onAngle ? PracticeReplay.Tracked("match the crosshair")
            : string.IsNullOrWhiteSpace(lineup.description)
                ? PracticeReplay.Tracked("throw it")
                : PracticeReplay.Tracked("write-up on the web");

        return $"{lineup.name}\n{PracticeReplay.ThrowHint(lineup)}\n{next}";
    }

    // Shortest way round the circle, so 359 and 1 are two degrees apart.

    private void OnSecond()
    {
        EndWarmup();
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

        CCSGameRules? rules = Core
            .EntitySystem.GetAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
            .FirstOrDefault()
            ?.GameRules;

        if (rules == null || !rules.WarmupPeriod)
        {
            return;
        }

        // Not every tick: the command takes a moment to land, and re-issuing it
        // in the meantime achieves nothing.
        _warmupTicks = WarmupRetrySeconds;
        Core.Engine.ExecuteCommand("mp_warmup_end");
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

        // Their selection is theirs: leaving it behind leaks the entities and
        // leaves standing transmit blocks pointing at indices the engine will
        // hand to something else.
        _replay.ClearSelectionFor(steamId);
        _standingIn.Remove(steamId);
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

        // Anything that did survive the map change is despawned outright, and
        // the handles are dropped either way -- a stale handle can be recycled
        // into a NEW entity, which makes despawning it later actively harmful.
        _replay.SweepMarkers();

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
        // The trail is how you see WHERE it went wrong rather than just that it
        // did. Ten seconds outlives the throw and the walk back to the spot.
        "sv_grenade_trajectory_prac_trailtime 10",
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
