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
        _replay.All = steamId => _library.For(steamId);
        // A solve rains live HE and molotovs on a map people are standing in.
        _system.SolveRunning = () => _solver.IsBusy;
        _session.Refreshed += OnSessionRefreshed;
        _recorder.Thrown += _system.OnThrown;
        _recorder.Finalized += _score.OnFinalized;
        _recorder.Thrown += _drill.OnThrown;
        _system.HoldUtility = _drill.Waiting;
        _score.Scored += _drill.OnScored;
        _score.Scored += OnScoredHint;

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

        // Refresh FETCHES; it does not draw. Somebody who joins and runs no
        // command should still see every lineup on the map.
        _authorizeHandler = @event =>
            ForPlayer(@event.PlayerId, steamId => RefreshAndShow(steamId));
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
        _score.Scored -= OnScoredHint;

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

            // A drill asks for ONE throw, so it draws one. Showing the other
            // lineups off the same spot leaves the player picking between
            // crosshairs when the whole point is that the run has already
            // chosen for them.
            LineupRecord? drilling = _drill.Current(player.SteamID);

            if (drilling != null)
            {
                show = new List<LineupRecord> { drilling };
                aimedAt = drilling;
            }

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

            Panels(player, pawn);
        }
    }

    // First match wins. The order is the order a player needs the answer in:
    // how to throw it once they are on the angle, otherwise what they are
    // pointing at, otherwise what they are standing on.
    // Two panels, deliberately: the card (centre HTML) answers "what is this
    // and how do I throw it", the steps line (centre text) answers "what have
    // I not done yet". One channel had to keep swapping between the two, so
    // reading the instructions meant losing the guidance and back again.
    // Chat rather than a HUD panel, precisely BECAUSE chat stacks: a line that
    // scrolls away with the rest of the log is the right home for a tip. On a
    // panel it would either sit there forever or fight the three that are
    // already earning their place.
    private readonly Dictionary<ulong, int> _hintedAt = new();

    // Long enough that nobody reads it twice in a practice run they are
    // concentrating on.
    private const int HintCooldownTicks = 180 * 64;

    // A landed throw is the moment .next actually means something, so the tip
    // gets a much shorter gap there -- but not none, or a player working one
    // spot hard would be told the same thing after every smoke.
    private const int HintAfterHitTicks = 30 * 64;

    private void Hint(IPlayer player, int cooldown)
    {
        if (
            _hintedAt.TryGetValue(player.SteamID, out int last)
            && _aimTick - last < cooldown
        )
        {
            return;
        }

        _hintedAt[player.SteamID] = _aimTick;

        Tell(
            player.SteamID,
            $" {ChatColors.Grey}tip: {ChatColors.Default}.next{ChatColors.Grey} and "
                + $"{ChatColors.Default}.prev{ChatColors.Grey} walk through the lineups"
        );
    }

    // Landing one is the natural point to move on, so that is where the nudge
    // to do so belongs.
    private void OnScoredHint(ulong steamId, string lineupId, UtilityPracticeResult? result)
    {
        if (result?.success != true)
        {
            return;
        }

        IPlayer? player = _system.Find(steamId);

        if (player != null && player.IsValid)
        {
            Hint(player, HintAfterHitTicks);
        }
    }

    private void Panels(IPlayer player, CCSPlayerPawn pawn)
    {

        (LineupRecord? lineup, bool onSpot, bool onAngle) = Focused(player, pawn);

        // Null, not "": an empty string is CONTENT to Send, and the title would
        // never clear.
        Send(
            player,
            PanelKind.Title,
            lineup == null ? null : PracticeLineupUtility.TitleCase(lineup.name)
        );
        if (lineup != null)
        {
            Hint(player, HintCooldownTicks);
        }

        Send(
            player,
            PanelKind.Card,
            lineup == null ? null : Card(lineup, _drill.Progress(player.SteamID))
        );
        Send(
            player,
            PanelKind.Steps,
            lineup == null ? null : Headline(lineup, onSpot, onAngle)
        );
    }

    private enum PanelKind
    {
        Title,
        Card,
        Steps,
    }

    // What each panel is currently showing, so an unchanged panel is left
    // alone. Re-sending centre HTML restarts its fade-in, which at sixteen
    // times a second is a strobe rather than a message -- the panel has to be
    // written only when what it says actually changes.
    private readonly Dictionary<(ulong, PanelKind), string> _showing = new();

    // The HTML panel holds for as long as it is told to, so its keepalive can
    // be rare -- and it needs to be, because every write restarts its fade-in.
    private const int PanelHoldMilliseconds = 60000;

    // Half the hold, in ticks, so the two can never drift into a gap.
    private const int StepsKeepAliveTicks = (PanelHoldMilliseconds / 1000 / 2) * 64;

    // Centre text expires on the game's own short schedule and takes no
    // duration, so the card has to be re-sent often to stay up at all. This is
    // only safe because that channel does not animate on write: the same rate
    // on the HTML panel is exactly the strobe this arrangement was made to fix.
    private const int CardKeepAliveTicks = 64;

    private void Send(IPlayer player, PanelKind kind, string? content)
    {
        (ulong, PanelKind) key = (player.SteamID, kind);
        bool had = _showing.TryGetValue(key, out string? showing);

        if (content == null)
        {
            // Cleared the moment it stops being true, rather than left to time
            // out: a stale instruction is worse than no instruction.
            if (had)
            {
                _showing.Remove(key);
                Clear(player, kind);
            }

            return;
        }

        int keepAlive = kind == PanelKind.Steps ? StepsKeepAliveTicks : CardKeepAliveTicks;

        if (had && showing == content && _aimTick % keepAlive != 0)
        {
            return;
        }

        _showing[key] = content;
        Write(player, kind, content);
    }

    private static void Write(IPlayer player, PanelKind kind, string content)
    {
        // Steps take the animating HTML panel, the card takes the quiet one.
        // The panel that flashes on every write is the one whose message is
        // supposed to be changing, and the panel that can be killed in a
        // millisecond is the one that has to vanish the instant it comes true.
        if (kind == PanelKind.Steps)
        {
            player.SendCenterHTML(content, PanelHoldMilliseconds);

            return;
        }

        // Alert is the third place on screen that holds still. It is why the
        // name, the throw details and the outstanding step can sit apart from
        // each other rather than stacking into one block.
        if (kind == PanelKind.Title)
        {
            player.SendAlert(content);

            return;
        }

        player.SendCenter(content);
    }

    // Clearing is a WRITE with the shortest possible life, not a write with the
    // panel's usual hold: sending blank content on the sixty-second hold left
    // an empty panel sitting on screen for a minute.
    private const int PanelClearMilliseconds = 1;

    private void Clear(IPlayer player, PanelKind kind)
    {
        if (kind == PanelKind.Steps)
        {
            player.SendCenterHTML("", PanelClearMilliseconds);

            return;
        }

        // Neither the card nor the title is written blank. Those channels take
        // no duration, so a blank write just starts another full-length message
        // that happens to be empty -- slower to clear than the line it
        // replaced. Writing nothing lets them lapse on the game's schedule.
    }

    private (LineupRecord? lineup, bool onSpot, bool onAngle) Focused(
        IPlayer player,
        CCSPlayerPawn pawn
    )
    {
        Vector origin = pawn.AbsOrigin ?? new Vector(0, 0, 0);
        var at = new Vec3(origin.X, origin.Y, origin.Z);

        IReadOnlyList<LineupRecord> library = _library.For(player.SteamID);
        List<LineupRecord> here = PracticeReplay.SpotAt(library, at);
        LineupRecord? aimedAt = LookingAt(pawn, at, library, here);
        LineupRecord? loaded = _system.StateFor(player.SteamID).Loaded;

        // A drill has already decided what the player is working on, and it
        // stays decided even when they walk off the spot -- the panels are how
        // they find their way back to it, so dropping them there would be
        // exactly backwards.
        LineupRecord? drilling = _drill.Current(player.SteamID);

        if (drilling != null)
        {
            loaded = drilling;
        }

        // Otherwise a loaded lineup only owns the panels while the player is
        // actually at its spot or looking at it. Walk away and the panels go:
        // instructions for a throw you are nowhere near are just something
        // stuck to the screen, and .load is not a commitment to read about it
        // forever.
        if (loaded != null && (drilling != null || here.Contains(loaded) || aimedAt == loaded))
        {
            QAngle eyes = pawn.EyeAngles;
            Vec3 spot = loaded.release.feet_position;

            bool onAngle =
                PracticeLineupUtility.AimError(
                    eyes.Y,
                    eyes.X,
                    loaded.release.yaw,
                    loaded.release.pitch
                ) <= ToleranceFor(loaded);

            // Both halves, not just the angle. Saying you are lined up while
            // you stand in the wrong place is worse than saying nothing -- the
            // throw misses and the lineup gets blamed.
            bool onSpot =
                PracticeLineupUtility.StanceMiss(
                    new Vec3(spot.x - origin.X, spot.y - origin.Y, 0f).LengthXY()
                ) == 0f;

            return (loaded, onSpot, onAngle);
        }

        // Nothing loaded here, so nothing is done yet: the panels describe what
        // the player is pointing at or standing on, and the steps line gives
        // them the first thing to do about it.
        if (aimedAt != null)
        {
            return (aimedAt, here.Contains(aimedAt), false);
        }

        if (here.Count == 1)
        {
            return (here[0], true, false);
        }

        return (null, false, false);
    }

    // The reference card: what this throw is and how it is thrown. Stays up
    // the whole time a lineup is in focus, because it is the thing a player
    // reads once and glances back at -- never the thing that nags.
    // Plain text, because the card sits on the channel that does not animate.
    // No escaping needed here for the same reason -- a lineup name is user text
    // and this channel renders it literally, which is exactly what we want.
    // How the throw is made, and whether there is more to read about it. The
    // name is not here -- it has its own channel, which is the whole reason
    // these ended up in three places instead of one.
    private static string Card(LineupRecord lineup, string? drill)
    {
        string details = string.IsNullOrWhiteSpace(lineup.description)
            ? ""
            : "\nWrite-up on the web";

        // While a drill is running, where you are in it belongs next to the
        // throw you are being asked to make -- not in chat, where it scrolls
        // away between attempts.
        string progress = drill == null ? "" : $"\n{drill}";

        return $"{PracticeLineupUtility.TitleCase(PracticeReplay.ThrowHint(lineup))}"
            + $"{details}{progress}";
    }

    // The one step that is not done yet, on the animating channel -- where a
    // flash on each change reads as the instruction CHANGING rather than as the
    // reference card blinking at you. It closes the moment the player is lined
    // up: silence, alongside the crosshair fading out, IS the success signal.
    // CS2 offers exactly two persistent on-screen text channels: centre text
    // and centre HTML. The third (CUserMessageHudMsg, the positioned game_text
    // element) compiles and sends but never renders, so the name takes its own
    // line at the top of the HTML panel rather than a channel of its own.
    // Throw details stay on the other channel entirely, which was the point.
    // How to throw it, and what is left to do. Both belong on the animating
    // panel: they are the lines that CHANGE, so a flash on write reads as the
    // instruction moving on rather than as the title blinking.
    // The step and nothing else. This panel exists to nag and then get out of
    // the way, so it carries only what is still undone.
    private static string? Headline(LineupRecord lineup, bool onSpot, bool onAngle)
    {
        return Steps(onSpot, onAngle);
    }

    private static string? Steps(bool onSpot, bool onAngle)
    {
        if (!onSpot)
        {
            return Instruction("stand in the circle", "#f99e2f");
        }

        if (!onAngle)
        {
            return Instruction("match the crosshair", "#f99e2f");
        }

        return null;
    }

    private static string Instruction(string text, string color)
    {
        return $"<font class='fontSize-m' color='{color}'>"
            + $"{PracticeLineupUtility.TrackedHtml(text)}</font>";
    }

    // Shortest way round the circle, so 359 and 1 are two degrees apart.

    private void OnSecond()
    {
        // Until the session is known, the door policy is "nobody" -- so this
        // comes before everything else that assumes people can get in.
        _session.RetryIfMissing(TimeSpan.FromSeconds(15));
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

        _drill.Rearm = (steamId, lineup) =>
        {
            IPlayer? player = _system.Find(steamId);

            if (player != null && player.IsValid)
            {
                _replay.GiveUtility(player, lineup.utility_type);
            }
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
        _hintedAt.Remove(steamId);
        _showing.Remove((steamId, PanelKind.Title));
        _showing.Remove((steamId, PanelKind.Card));
        _showing.Remove((steamId, PanelKind.Steps));
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
    private void RefreshAndShow(ulong steamId)
    {
        _library.Refresh(
            steamId,
            count =>
            {
                if (count <= 0)
                {
                    return;
                }

                IReadOnlyList<LineupRecord> library = _library.For(steamId);

                _replay.ShowLibrary(library);

                // .next and .prev walk state.Results, and a refresh never filled
                // it -- so every lineup on the map was drawn and none of them
                // could be stepped through until the player ran a search. If
                // they can SEE them, they can walk them. Any earlier search is
                // discarded on purpose: this only runs on join, map change and
                // an explicit refresh, and a search from before any of those is
                // describing a map state that no longer exists.
                PracticeState state = _system.StateFor(steamId);

                state.Results.Clear();
                state.Results.AddRange(library);
                state.Index = -1;
            }
        );
    }

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
                RefreshAndShow(player.SteamID);
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
        // A render (and a lineup drill) respawns the player on a loop and
        // teleports them onto spots -- both of which cs2 counts as suicides in
        // competitive rules and then kicks "for suiciding too many times".
        // mp_autokick governs the idle/teamkill/suicide auto-kick; off is what
        // the duel cfg uses for the same continuous-respawn reason.
        "mp_autokick 0",
        "mp_disconnect_kills_players 0",
        // Nobody is here but the thrower. Bots add competitive round noise and
        // a team-select screen the render has to sit through.
        "bot_quota 0",
        "bot_kick",
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
