using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

// Registered unprefixed, so Swiftly exposes each verb as sw_<name> in the
// console and as ".<name>" in chat. Replies are always to the caller: a
// practice server is several people working on unrelated things in the same
// map.
public partial class UtilityPracticePlugin
{
    [Command("save", registerRaw: false, permission: "")]
    public void OnSave(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        string name = string.Join(" ", context.Args).Trim().Trim('"');

        // No name: ask for one instead of refusing. The next thing they type is
        // captured and never reaches chat, which is as close to a text field as
        // a Panorama panel gets.
        if (string.IsNullOrEmpty(name))
        {
            LineupRecord? unnamed = _recorder.LastThrow(player.SteamID);

            if (unnamed == null)
            {
                Reply(context, $" {ChatColors.Red}throw something first");

                return;
            }

            // The map already knows what this throw is called. Naming it is only
            // a question worth asking when the level has no callouts to answer
            // it with.
            string automatic = LineupNaming.Auto(
                unnamed.utility_type,
                unnamed.release.feet_position,
                unnamed.detonation_position,
                _callouts.Callouts
            );

            if (automatic.Length > 0)
            {
                SaveThrow(player.SteamID, automatic);
                Reply(
                    context,
                    $" {ChatColors.Grey}named by the map -- "
                        + $"{ChatColors.Default}.edit{ChatColors.Grey} to change it"
                );

                return;
            }

            int playerId = player.PlayerID;
            ulong steamId = player.SteamID;

            _prompt.Ask(playerId, steamId, answer => SaveThrow(steamId, answer));

            Reply(
                context,
                $" {ChatColors.Green}type a name for that throw "
                    + $"{ChatColors.Grey}(anything you say next, or {ChatColors.Default}.save <name>{ChatColors.Grey})"
            );

            return;
        }

        SaveThrow(player.SteamID, name);
    }

    // Shared by ".save <name>" and by the prompt, which answers later and has no
    // command context to reply into.
    // What a bare .drill should rep. Focused already answers "which lineup is
    // this player indicating", including the two-on-one-spot case where aim
    // decides, so this is that answer plus the loaded fallback.
    private LineupRecord? DrillTarget(IPlayer player)
    {
        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn != null && pawn.IsValid)
        {
            (LineupRecord? focused, _, _) = Focused(player, pawn);

            if (focused != null)
            {
                return focused;
            }
        }

        return _system.StateFor(player.SteamID).Loaded;
    }

    private void SaveThrow(ulong steamId, string name)
    {
        IPlayer? player = _system.Find(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        LineupRecord? thrown = _recorder.LastThrow(steamId);

        if (thrown == null)
        {
            Tell(steamId, $" {ChatColors.Red}throw something first");

            return;
        }

        if (_library.For(steamId).Count >= _config.MaxSaved)
        {
            Tell(
                steamId,
                $" {ChatColors.Red}you already have {_config.MaxSaved} saved lineups on this map"
            );

            return;
        }

        thrown.name = name;
        thrown.map = _library.Map;
        thrown.side = player.Controller.Team == Team.CT ? "CT" : "TERRORIST";
        thrown.visibility = nameof(eLineupVisibility.Private);
        thrown.plugin_version = ModuleVersion;

        _library.Add(steamId, thrown);

        // A lineup you just saved is the lineup you are working on, so it
        // becomes the loaded one and gets its markers straight away.
        PracticeState saved = _system.StateFor(steamId);

        saved.Cleared = false;
        saved.Loaded = thrown;
        saved.Results.Clear();
        saved.Results.Add(thrown);
        saved.Index = 0;

        // Deliberately not the full .load: the player is already standing on
        // the spot they just threw from, and teleporting them onto it would
        // yank the view for no reason.
        _replay.ShowMarkersFor(player, thrown);

        Tell(steamId, $" {ChatColors.Green}saved {ChatColors.Default}{name}");

        _ = Task.Run(async () =>
        {
            string? id = await _api.Ingest(thrown);

            Core.Scheduler.NextTick(() =>
            {
                if (id != null)
                {
                    thrown.id = id;
                    return;
                }

                Tell(steamId, $" {ChatColors.Red}{name} could not reach the panel; it will retry");
            });
        });
    }

    [Command("load", registerRaw: false, permission: "")]
    public void OnLoad(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        string query = string.Join(" ", context.Args).Trim().Trim('"');
        ulong steamId = player.SteamID;

        // An empty library and a query that matches nothing are different
        // problems, and saying "no lineup matches" for both sends people
        // hunting for a typo when the library never loaded at all.
        if (_library.For(steamId).Count == 0)
        {
            Reply(
                context,
                $" {ChatColors.Red}no lineups loaded for this map. "
                    + $"{ChatColors.Default}fetching..."
            );

            _library.Refresh(
                steamId,
                count =>
                {
                    if (count < 0)
                    {
                        Tell(
                            steamId,
                            $" {ChatColors.Red}could not reach the library (check the server logs)"
                        );
                        return;
                    }

                    if (count == 0)
                    {
                        Tell(
                            steamId,
                            $" {ChatColors.Red}you have no lineups saved for this map"
                        );
                        return;
                    }

                    // Finish the command that started the fetch. It used to
                    // stop here and say "try .load again", which meant the
                    // first .load of a session never loaded anything and
                    // .next answered "load something first" in between.
                    Tell(steamId, $" {ChatColors.Green}loaded {count} lineup(s)");
                    LoadFrom(steamId, query);
                }
            );

            return;
        }

        LoadFrom(steamId, query);
    }

    // Resolve a query against the library and stand the player on the answer.
    // Split out because .load may have to fetch the library first and then has
    // to finish itself once that lands.
    private void LoadFrom(ulong steamId, string query)
    {
        IPlayer? player = _system.Find(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(steamId);
        Vec3? near = PracticeSystem.Where(player)?.feet_position;

        LineupRecord? lineup = _library.Resolve(steamId, query, near);

        if (lineup == null)
        {
            Tell(steamId, $" {ChatColors.Red}no lineup matches \"{query}\"");
            return;
        }

        state.Results.Clear();
        state.Results.AddRange(
            PracticeLineupUtility.Filter(_library.For(steamId), query, near)
        );
        state.Index = state.Results.FindIndex(match => match.client_id == lineup.client_id);

        // Resolve and Filter are different matchers, so what was loaded is not
        // always inside the walk that was just built. An index of -1 left here
        // sends the next .prev two short of the end instead of onto it.
        if (state.Index < 0)
        {
            state.Results.Insert(0, lineup);
            state.Index = 0;
        }

        Apply(player, lineup);
    }

    [Command("list", registerRaw: false, permission: "")]
    public void OnList(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        IReadOnlyList<LineupRecord> lineups = _library.For(player.SteamID);

        if (lineups.Count == 0)
        {
            Reply(context, $" {ChatColors.Grey}no saved lineups on {_library.Map}");
            return;
        }

        Reply(context, $" {ChatColors.Green}{lineups.Count} lineups on {_library.Map}");

        foreach (LineupRecord lineup in lineups)
        {
            Reply(
                context,
                $" {ChatColors.Default}{lineup.name} {ChatColors.Grey}({lineup.utility_type}, {lineup.technique})"
            );
        }
    }

    [Command("next", registerRaw: false, permission: "")]
    public void OnNext(ICommandContext context)
    {
        Step(context, 1);
    }

    [Command("prev", registerRaw: false, permission: "")]
    public void OnPrev(ICommandContext context)
    {
        Step(context, -1);
    }

    // sv_rethrow_last_grenade, which is what the word means everywhere else:
    // the grenade goes again from where it was thrown and the player stays put,
    // so they can stand off to the side and watch the line they cannot see
    // while making it. .load, .last and .back are the commands that move you --
    // this one moving you as well is what left it with nothing of its own to do.
    [Command("rethrow", registerRaw: false, permission: "")]
    public void OnRethrow(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (!_config.ReplayEnabled)
        {
            Reply(context, $" {ChatColors.Red}replay is disabled on this server");
            return;
        }

        ulong steamId = player.SteamID;

        // The loaded lineup first, the player's own last throw second: after a
        // .load the reference throw is the one worth seeing again, and with
        // nothing loaded "rethrow" can only mean the grenade they just threw.
        // Answering that one with "nothing loaded" is most of why this read as
        // a command that did not work.
        LineupRecord? lineup = _system.StateFor(steamId).Loaded ?? _recorder.LastThrow(steamId);

        if (lineup == null)
        {
            Reply(
                context,
                $" {ChatColors.Red}throw something first, or {ChatColors.Default}.load"
                    + $" {ChatColors.Red}a lineup"
            );

            return;
        }

        CCSPlayerPawn? pawn = player.PlayerPawn;

        if (pawn == null || !pawn.IsValid)
        {
            Reply(context, $" {ChatColors.Red}you have to be alive to rethrow");
            return;
        }

        // A lineup fitted to a demo has a path but no seed, so there is nothing
        // to hand the engine. Said out loud rather than logged: a command that
        // silently does nothing is the bug being fixed here.
        if (!lineup.IsExactlyReplayable())
        {
            Reply(
                context,
                $" {ChatColors.Yellow}{DrillUtility.Name(lineup)} {ChatColors.Grey}has no recorded"
                    + $" throw to replay -- {ChatColors.Default}.load{ChatColors.Grey} it and"
                    + $" throw it yourself"
            );

            return;
        }

        _logger.LogInformation("[nade-render] OnRethrow for {steam}", steamId);

        // Forced: np_ghost_projectile decides whether .load and .next throw as
        // they go, but a player who typed the word has already asked.
        _replay.ThrowGhostProjectile(player, lineup, force: true);

        Reply(
            context,
            $" {ChatColors.Green}rethrown {ChatColors.Default}{DrillUtility.Name(lineup)}"
        );
    }

    [Command("last", registerRaw: false, permission: "")]
    public void OnLast(ICommandContext context)
    {
        Back(context, 0);
    }

    [Command("back", registerRaw: false, permission: "")]
    public void OnBack(ICommandContext context)
    {
        if (!int.TryParse(string.Join(" ", context.Args).Trim(), out int back) || back < 0)
        {
            Reply(context, $" {ChatColors.Red}usage: .back <n>");
            return;
        }

        Back(context, back);
    }

    // A read-only dump of what the level says its areas are called. This is the
    // check to run before trusting a map's callouts: compare it against the
    // published extract for the same map, or just against the names you know.
    [Command("callouts", registerRaw: false, permission: "")]
    public void OnCallouts(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        List<MapCalloutPayload> callouts = _callouts.Collect();

        if (callouts.Count == 0)
        {
            Reply(context, $" {ChatColors.Red}this map defines no callouts");

            return;
        }

        Reply(
            context,
            $" {ChatColors.Green}{callouts.Count} {ChatColors.Default}callouts on {_library.Map}"
        );

        foreach (MapCalloutPayload callout in callouts.OrderBy(entry => entry.name))
        {
            MapCalloutBox box = callout.boxes[0];

            Reply(
                context,
                $" {ChatColors.Default}{callout.name} {ChatColors.Grey}"
                    + $"x {box.min[0]:F0}..{box.max[0]:F0} "
                    + $"y {box.min[1]:F0}..{box.max[1]:F0} "
                    + $"z {box.min[2]:F0}..{box.max[2]:F0}"
                    + (callout.boxes.Count > 1 ? $" (+{callout.boxes.Count - 1})" : string.Empty)
            );
        }
    }

    [Command("clear", registerRaw: false, permission: "")]
    public void OnClear(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        state.Loaded = null;
        state.Results.Clear();
        state.Index = -1;
        state.Bloom = false;

        // Held until the next .load, .next, walk-on or save. Sweeping alone
        // left the spot watcher free to redraw the spot under their feet on
        // its next pass -- a quarter of a second, or the first time they
        // glanced at another ring -- which is why .clear read as broken.
        state.Cleared = true;

        _replay.ClearGhosts(player.SteamID);

        // What everybody else's .clear means, and what a player standing in
        // their own smoke is asking for. The ping key does this on its own --
        // this is the same call, for anybody who types it.
        int thrown = _replay.ClearThrownUtility();

        // Swept rather than cleared: ClearMarkers can only despawn what this
        // instance still has a handle to, and anything a previous load left
        // behind is exactly what makes .clear look like it did nothing.
        _replay.SweepMarkers();

        // Safe to drop now the watcher is held off: nothing can match against
        // it, so the first redraw after they ask again is decided by where
        // they are then rather than by where they were when they cleared.
        ForgetSpot(player.SteamID);

        Reply(
            context,
            thrown > 0
                ? $" {ChatColors.Green}cleared {ChatColors.Grey}the preview and the utility"
                    + $" in the world"
                : $" {ChatColors.Green}cleared"
        );
    }

    [Command("bloom", registerRaw: false, permission: "")]
    public void OnBloom(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        LineupRecord? loaded = state.Loaded;

        if (loaded == null)
        {
            Reply(context, $" {ChatColors.Red}load a lineup first");
            return;
        }

        if (!_config.GhostPreview)
        {
            Reply(context, $" {ChatColors.Red}previews are disabled on this server");
            return;
        }

        state.Bloom = !state.Bloom;

        if (!state.Bloom)
        {
            _replay.ClearBloom(player.SteamID);
            Reply(context, $" {ChatColors.Green}bloom off");
            return;
        }

        Reply(context, $" {ChatColors.Grey}outlining {loaded.name}...");

        ulong steamId = player.SteamID;

        // The same fetch .load already made, and free once it has landed.
        _library.EnsureTrajectory(loaded, steamId, fetched => DrawBloom(steamId, fetched));
    }

    [Command("playbook", registerRaw: false, permission: "")]
    public void OnPlaybook(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        string argument = string.Join(" ", context.Args).Trim().Trim('"');

        if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            if (!_playbook.Stop())
            {
                Reply(context, $" {ChatColors.Red}nothing is running");
                return;
            }

            Core.PlayerManager.SendChat(
                $" {ChatColors.Green}{player.Controller.PlayerName} stopped the execute".Colored()
            );
            return;
        }

        StartPlaybook(player, context);
    }

    [Command("run", registerRaw: false, permission: "")]
    public void OnRun(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        StartPlaybook(player, context);
    }

    // The way out that does not need remembering which mode you are in.
    [Command("cancel", registerRaw: false, permission: "")]
    public void OnCancel(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        Reply(
            context,
            _drill.Stop(player.SteamID)
                ? $" {ChatColors.Green}drill stopped"
                : $" {ChatColors.Grey}nothing to cancel"
        );
    }

    [Command("drill", registerRaw: false, permission: "")]
    public void OnDrill(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        // A bare .drill while one is running ends it. Toggling off with the
        // same word you started with is what a player reaches for first, and
        // Stop is a no-op when there is nothing to stop, so this cannot
        // swallow a genuine start.
        if (
            string.IsNullOrWhiteSpace(string.Join(" ", context.Args))
            && _drill.Stop(player.SteamID)
        )
        {
            Reply(context, $" {ChatColors.Green}drill stopped");
            return;
        }

        DrillRequest request = DrillUtility.Parse(string.Join(" ", context.Args));

        if (!request.Valid)
        {
            Reply(
                context,
                $" {ChatColors.Red}usage: .drill [count] [worst|random] / .drill / .cancel"
            );
            return;
        }

        if (request.Stop)
        {
            if (!_drill.Stop(player.SteamID))
            {
                Reply(context, $" {ChatColors.Red}you are not drilling");
            }
            return;
        }

        // A bare .drill takes the lineup the player is indicating: the one they
        // are standing on, or -- when a spot holds more than one -- the one they
        // are pointing at. Failing that, the last one they loaded, which after a
        // drill is the last one they drilled. Only somebody standing nowhere in
        // particular gets their whole book.
        if (string.IsNullOrWhiteSpace(string.Join(" ", context.Args)))
        {
            LineupRecord? here = DrillTarget(player);

            if (here != null)
            {
                if (
                    _drill.StartWith(player.SteamID, new[] { here }, endless: true)
                    == eDrillStart.Started
                )
                {
                    Reply(
                        context,
                        $" {ChatColors.Green}drilling {ChatColors.Default}{here.name}"
                    );

                    return;
                }
            }
        }

        switch (_drill.Start(player.SteamID, request.Order, request.Count))
        {
            case eDrillStart.AlreadyRunning:
                Reply(context, $" {ChatColors.Red}already drilling; .drill stop first");
                return;
            case eDrillStart.ReplayDisabled:
                Reply(context, $" {ChatColors.Red}replay is disabled on this server");
                return;
            case eDrillStart.NotConnected:
                Reply(
                    context,
                    $" {ChatColors.Red}this server has no panel, so a throw cannot be scored"
                );
                return;
            case eDrillStart.NothingToDrill:
                Reply(
                    context,
                    $" {ChatColors.Red}nothing on {_library.Map} to drill; save some lineups or .reload"
                );
                return;
        }
    }

    [Command("skip", registerRaw: false, permission: "")]
    public void OnSkip(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (!_drill.Skip(player.SteamID))
        {
            Reply(context, $" {ChatColors.Red}you are not drilling");
        }
    }

    // The other end of the throw. Loading a lineup puts you where it is thrown
    // FROM; this puts you where it lands, which is the only way to see what the
    // smoke actually covers without throwing it and running.
    [Command("jump", registerRaw: false, permission: "")]
    public void OnJump(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        LineupRecord? lineup = _system.StateFor(player.SteamID).Loaded;

        if (lineup == null)
        {
            Reply(context, $" {ChatColors.Grey}load a lineup first");
            return;
        }

        if (!_replay.JumpToLanding(player, lineup))
        {
            Reply(context, $" {ChatColors.Red}could not move you there");
            return;
        }

        Reply(
            context,
            $" {ChatColors.Green}moved to where {ChatColors.Default}{lineup.name}"
                + $" {ChatColors.Green}lands"
        );
    }

    [Command("pos", registerRaw: false, permission: "")]
    public void OnPos(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        string[] args = context
            .Args.Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0)
            .ToArray();

        if (args.Length == 0)
        {
            if (state.Positions.Count == 0)
            {
                Reply(context, $" {ChatColors.Grey}no saved positions");
                return;
            }

            Reply(
                context,
                $" {ChatColors.Green}positions: {ChatColors.Default}{string.Join(", ", state.Positions.Keys)}"
            );
            return;
        }

        if (args[0].Equals("save", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
            {
                Reply(context, $" {ChatColors.Red}usage: .pos save <name>");
                return;
            }

            if (!_system.SavePosition(player, args[1]))
            {
                Reply(context, $" {ChatColors.Red}unable to save that position");
                return;
            }

            Reply(context, $" {ChatColors.Green}saved position {ChatColors.Default}{args[1]}");
            return;
        }

        if (!state.Positions.TryGetValue(args[0], out ThrowSnapshot? position))
        {
            Reply(context, $" {ChatColors.Red}no position named {args[0]}");
            return;
        }

        PracticeSystem.TeleportTo(player, position);
    }

    [Command("spawn", registerRaw: false, permission: "")]
    public void OnSpawn(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        List<ThrowSnapshot> spawns = _system.SpawnPoints();

        if (spawns.Count == 0)
        {
            Reply(context, $" {ChatColors.Red}this map has no spawn points");
            return;
        }

        string arg = string.Join(" ", context.Args).Trim();
        PracticeState state = _system.StateFor(player.SteamID);
        int index;

        // Walking them is how you find the one you want; a spawn has no name
        // and nobody knows which number they are looking for.
        if (
            arg.Equals("next", StringComparison.OrdinalIgnoreCase)
            || arg.Equals("prev", StringComparison.OrdinalIgnoreCase)
        )
        {
            int direction = arg.Equals("next", StringComparison.OrdinalIgnoreCase) ? 1 : -1;

            state.SpawnIndex =
                state.SpawnIndex < 0
                    ? (direction > 0 ? 0 : spawns.Count - 1)
                    : ((state.SpawnIndex + direction) % spawns.Count + spawns.Count)
                        % spawns.Count;

            index = state.SpawnIndex + 1;
        }
        else if (int.TryParse(arg, out int typed))
        {
            index = Math.Clamp(typed, 1, spawns.Count);
            state.SpawnIndex = index - 1;
        }
        else
        {
            Reply(context, $" {ChatColors.Red}usage: .spawn <1-{spawns.Count} | next | prev>");
            return;
        }

        PracticeSystem.TeleportTo(player, spawns[index - 1]);
        Reply(context, $" {ChatColors.Green}spawn {index}/{spawns.Count}");
    }

    // Off by default: a ring per spawn is a few hundred entities nobody asked
    // for, and the map is quieter without them.
    [Command("spawns", registerRaw: false, permission: "")]
    public void OnSpawns(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (_replay.SpawnsShown)
        {
            _replay.ClearSpawns();
            Reply(context, $" {ChatColors.Grey}spawn rings off");
            return;
        }

        List<ThrowSnapshot> spawns = _system.SpawnPoints();

        // Distinguished from "this map has none": the round has not started, so
        // the game has not chosen its spawns yet.
        if (spawns.Count == 0)
        {
            Reply(
                context,
                $" {ChatColors.Red}no competitive spawns yet "
                    + $"{ChatColors.Grey}-- the round has not set them; try again in a moment"
            );

            return;
        }

        _replay.ShowSpawns(spawns);

        Reply(
            context,
            $" {ChatColors.Green}showing {spawns.Count} spawn(s) "
                + $"{ChatColors.Grey}-- .spawn next walks them"
        );
    }

    // Something to throw at. A smoke tells you where it landed on its own; a
    // flash and an HE only tell you anything if there is somebody standing
    // there to be flashed or hurt.
    [Command("bot", registerRaw: false, permission: "")]
    public void OnBot(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (!AddBot(player))
        {
            Reply(context, $" {ChatColors.Red}unable to place a bot here");
            return;
        }

        Reply(
            context,
            $" {ChatColors.Green}bot placed {ChatColors.Grey}-- "
                + $"{ChatColors.Default}.nobots{ChatColors.Grey} clears them"
        );
    }

    [Command("nobots", registerRaw: false, permission: "")]
    public void OnNoBots(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        int had = ClearBots();

        Reply(
            context,
            had == 0
                ? $" {ChatColors.Grey}no bots to clear"
                : $" {ChatColors.Green}cleared {had} bot(s)"
        );
    }

    // The crosshair is the answer written on the wall. A throw made with it up
    // says nothing about whether you could make it without, which is the only
    // question "have I got this yet" is asking.
    [Command("crosshair", registerRaw: false, permission: "")]
    public void OnCrosshair(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);

        state.Crosshair = !state.Crosshair;

        // Redrawn rather than left until the player steps off the spot and back
        // on: a toggle that appears to do nothing gets pressed again.
        _replay.ClearMarkers();
        ForgetSpot(player.SteamID);

        Reply(
            context,
            state.Crosshair
                ? $" {ChatColors.Green}aim crosshair on"
                : $" {ChatColors.Grey}aim crosshair off {ChatColors.Default}-- throw it blind"
        );
    }

    [Command("colors", registerRaw: false, permission: "")]
    public void OnColors(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);

        state.Colors = !state.Colors;

        Reply(
            context,
            state.Colors
                ? $" {ChatColors.Green}throw colours on {ChatColors.Grey}-- each grenade gets its own"
                : $" {ChatColors.Grey}throw colours off -- smokes come out vanilla"
        );
    }

    [Command("noclip", registerRaw: false, permission: "")]
    public void OnNoclip(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        state.Noclip = !state.Noclip;

        Reply(context, $" {ChatColors.Green}noclip {Toggle(state.Noclip)}");
    }

    [Command("god", registerRaw: false, permission: "")]
    public void OnGod(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        state.God = !state.God;

        Reply(context, $" {ChatColors.Green}god {Toggle(state.God)}");
    }

    [Command("timer", registerRaw: false, permission: "")]
    public void OnTimer(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);

        if (state.TimerStartedAt == null)
        {
            state.TimerStartedAt = DateTime.UtcNow;
            Reply(context, $" {ChatColors.Green}timer started");
            return;
        }

        double elapsed = (DateTime.UtcNow - state.TimerStartedAt.Value).TotalSeconds;
        state.TimerStartedAt = null;

        Reply(context, $" {ChatColors.Green}timer stopped at {elapsed:0.00}s");
    }

    [Command("solo", registerRaw: false, permission: "")]
    public void OnSolo(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        state.Solo = !state.Solo;

        Reply(
            context,
            state.Solo
                ? $" {ChatColors.Green}solo on {ChatColors.Grey}(you only see your own previews)"
                : $" {ChatColors.Green}solo off {ChatColors.Grey}(you see everyone's previews)"
        );
    }

    [Command("delete", registerRaw: false, permission: "")]
    public void OnDelete(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        LineupRecord? loaded = state.Loaded;

        if (loaded == null)
        {
            Reply(context, $" {ChatColors.Red}load a lineup first");
            return;
        }

        _library.Remove(player.SteamID, loaded);
        state.Results.RemoveAll(match => match.client_id == loaded.client_id);
        state.Loaded = null;
        _replay.ClearGhosts(player.SteamID);

        Reply(context, $" {ChatColors.Green}deleted {ChatColors.Default}{loaded.name}");

        if (loaded.id == null)
        {
            return;
        }

        string id = loaded.id;
        ulong steamId = player.SteamID;

        _ = Task.Run(async () =>
        {
            bool deleted = await _api.Delete(id);

            if (deleted)
            {
                return;
            }

            Core.Scheduler.NextTick(() =>
                Tell(steamId, $" {ChatColors.Red}{loaded.name} is still on the panel; try .reload")
            );
        });
    }

    [Command("reload", registerRaw: false, permission: "")]
    public void OnReload(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        ulong steamId = player.SteamID;

        Reply(context, $" {ChatColors.Grey}reloading...");

        // The library about to be replaced is the one these markers came from.
        PracticeState reloading = _system.StateFor(steamId);

        reloading.Loaded = null;
        reloading.Results.Clear();
        reloading.Index = -1;

        _replay.ClearGhosts(steamId);
        _replay.ClearMarkers();
        ForgetSpot(steamId);

        _library.Refresh(
            steamId,
            count =>
            {
                Tell(
                    steamId,
                    count < 0
                        ? $" {ChatColors.Red}the panel did not answer"
                        : $" {ChatColors.Green}{count} lineups on {_library.Map}"
                );
            }
        );
    }

    [Command("help", registerRaw: false, permission: "")]
    public void OnPracticeHelp(ICommandContext context)
    {
        if (context.Sender == null)
        {
            return;
        }

        foreach (string line in HelpLines)
        {
            Reply(context, line);
        }
    }

    // Server-only, like utility_practice_refresh below: the panel sends this
    // over RCON when somebody presses "load me in" on the website, so the
    // command has to name the player rather than being spoken by them.
    [Command("utility_practice_load", registerRaw: true, permission: "")]
    public void OnRemoteLoad(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        string[] args = context.Args.ToArray();

        if (args.Length < 2 || !ulong.TryParse(args[0].Trim(), out ulong steamId))
        {
            Reply(context, "usage: utility_practice_load <steamid64> <lineup_id>");
            return;
        }

        string lineupId = args[1].Trim().Trim('"');

        if (string.IsNullOrEmpty(lineupId))
        {
            Reply(context, "usage: utility_practice_load <steamid64> <lineup_id>");
            return;
        }

        RemoteLoad(steamId, lineupId, refreshed: false);
    }

    // The drill twin of utility_practice_load: the panel names a set of lineups
    // and the run is built from exactly those, in the order they arrived.
    [Command("utility_practice_drill", registerRaw: true, permission: "")]
    public void OnRemoteDrill(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        string[] args = context.Args.ToArray();

        if (args.Length < 2 || !ulong.TryParse(args[0].Trim(), out ulong steamId))
        {
            Reply(context, "usage: utility_practice_drill <steamid64> <id,id,...>");
            return;
        }

        string[] ids = args[1]
            .Trim()
            .Trim('"')
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (ids.Length == 0)
        {
            Reply(context, "usage: utility_practice_drill <steamid64> <id,id,...>");
            return;
        }

        RemoteDrill(steamId, ids, refreshed: false);
    }

    private void RemoteDrill(ulong steamId, string[] ids, bool refreshed)
    {
        IPlayer? player = _system.Find(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        // Same reasoning as RemoteLoad: the panel pushing a drill is itself the
        // signal that the cached library may be behind, so re-read before
        // resolving rather than only when an id turns up missing.
        if (!refreshed)
        {
            _library.Refresh(steamId, _ => RemoteDrill(steamId, ids, refreshed: true));
            return;
        }

        IReadOnlyList<LineupRecord> library = _library.For(steamId);

        List<LineupRecord> queue = ids.Select(id =>
                PracticeLineupUtility.ById(library, id)
            )
            .OfType<LineupRecord>()
            .ToList();

        if (queue.Count == 0)
        {
            Tell(steamId, $" {ChatColors.Red}none of those lineups are on this server");
            return;
        }

        switch (_drill.StartWith(steamId, queue))
        {
            case eDrillStart.AlreadyRunning:
                Tell(steamId, $" {ChatColors.Red}already drilling; .cancel first");
                return;
            case eDrillStart.ReplayDisabled:
                Tell(steamId, $" {ChatColors.Red}replay is disabled on this server");
                return;
            case eDrillStart.NotConnected:
                Tell(steamId, $" {ChatColors.Red}this server has no panel to score throws");
                return;
            case eDrillStart.NothingToDrill:
                Tell(steamId, $" {ChatColors.Red}nothing to drill");
                return;
        }
    }

    private void RemoteLoad(ulong steamId, string lineupId, bool refreshed)
    {
        IPlayer? player = _system.Find(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        // Always re-read before resolving. The panel pushing a load IS the
        // signal that something changed: a draft tested from the website keeps
        // the same client id on purpose so it replaces itself rather than
        // piling up copies, so answering this out of the cache stood the player
        // on the FIRST version of the throw every time afterwards -- move the
        // points, press test again, land in the same place.
        if (!refreshed)
        {
            _library.Refresh(steamId, _ => RemoteLoad(steamId, lineupId, refreshed: true));
            return;
        }

        LineupRecord? lineup = PracticeLineupUtility.ById(_library.For(steamId), lineupId);

        if (lineup != null)
        {
            Apply(player, lineup);
            return;
        }

        // One refresh, then give up: retrying past that would hammer the panel
        // every time somebody sends a lineup that really is gone.
        Tell(steamId, $" {ChatColors.Red}that lineup is not available on this server");
    }

    // Server-only, like the two above. Everything on this server goes through a
    // load screen when it runs, so the countdown before the level change is the
    // only warning anybody gets -- the website never asks first.
    [Command("utility_practice_map", registerRaw: true, permission: "")]
    public void OnRemoteMap(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        if (
            !PracticeMapChangeUtility.TryParse(
                context.Args.ToArray(),
                out PracticeMapChangeRequest request
            )
        )
        {
            Reply(context, "usage: utility_practice_map <map|workshop id> [<steamid64> <id,id,...>]");
            return;
        }

        // Held on the plugin, which a changelevel does NOT reload -- that is
        // the whole mechanism by which somebody arrives standing on the lineup
        // they pressed on the website.
        _pendingMapLoad = PracticeMapChangeUtility.PendingFor(request, DateTime.UtcNow);

        AnnounceMapChange(request.map);

        Core.Scheduler.DelayBySeconds(
            PracticeMapChangeUtility.CountdownSeconds,
            () => Core.Engine.ExecuteCommand(PracticeMapChangeUtility.Command(request.map))
        );
    }

    private void AnnounceMapChange(string map)
    {
        string chat =
            $" {ChatColors.Green}changing map {ChatColors.Grey}to "
            + $"{ChatColors.Default}{map} {ChatColors.Grey}in "
            + $"{PracticeMapChangeUtility.CountdownSeconds}s";

        foreach (IPlayer player in Core.PlayerManager.GetAllPlayers())
        {
            if (player == null || !player.IsValid || player.IsFakeClient)
            {
                continue;
            }

            player.SendChat(chat.Colored());
            player.SendCenter($"changing map to {map}");
        }
    }

    /**
     * Stand whoever asked for this map on what they asked for, once they are
     * actually back in the server.
     *
     * Driven off the second tick rather than a connect event: a changelevel
     * puts every client through its own reconnect, and which hook fires on the
     * far side of that is not something the plugin should be betting a teleport
     * on. Retrying until the player is found costs one dictionary lookup a
     * second and works whatever the engine does.
     */
    private void DrainPendingMapLoad()
    {
        if (_pendingMapLoad == null)
        {
            return;
        }

        if (PracticeMapChangeUtility.IsExpired(_pendingMapLoad, DateTime.UtcNow))
        {
            _pendingMapLoad = null;
            return;
        }

        PracticeMapChangePending pending = _pendingMapLoad;
        IPlayer? player = _system.Find(pending.steam_id);

        // Not back yet. A CS2 client can spend a minute on a map load, and the
        // expiry above is what stops this waiting forever.
        if (player == null || !player.IsValid)
        {
            return;
        }

        _pendingMapLoad = null;

        if (pending.lineup_ids.Count == 1)
        {
            RemoteLoad(pending.steam_id, pending.lineup_ids[0], refreshed: false);
            return;
        }

        RemoteDrill(pending.steam_id, pending.lineup_ids.ToArray(), refreshed: false);
    }

    // Server-only and deliberately unprefixed, like the match plugin's
    // get_match: the panel calls it when the roster or the library changes.
    [Command("utility_practice_refresh", registerRaw: true, permission: "")]
    public void OnRefresh(ICommandContext context)
    {
        if (context.IsSentByPlayer)
        {
            return;
        }

        RefreshEverything();
    }

    private const float WelcomeDelaySeconds = 2f;

    // Deliberately short. The full list is sixteen lines and reads as spam on
    // every join; these are the four that get somebody throwing, and .help is
    // where the rest lives.
    private static readonly string[] WelcomeLines = new[]
    {
        $" {ChatColors.Green}utility practice {ChatColors.Grey}-- infinite utility, buy anywhere",
        $" {ChatColors.Default}.save <name> {ChatColors.Grey}saves the throw you just made",
        $" {ChatColors.Default}.load <query> {ChatColors.Grey}stands you on a saved lineup",
        $" {ChatColors.Default}.rethrow {ChatColors.Grey}throws it again from where it was thrown",
        $" {ChatColors.Grey}your {ChatColors.Default}ping key {ChatColors.Grey}clears smokes and fires",
        $" {ChatColors.Default}.help {ChatColors.Grey}everything else",
    };

    private static readonly string[] HelpLines = new[]
    {
        $" {ChatColors.Green}utility practice",
        $" {ChatColors.Default}.save [name] {ChatColors.Grey}saves your last throw (asks if you skip the name)",
        $" {ChatColors.Default}.load <query> {ChatColors.Grey}teleports you to a lineup",
        $" {ChatColors.Default}.next / .prev {ChatColors.Grey}walk the last search",
        $" {ChatColors.Default}.jump {ChatColors.Grey}stand where the loaded lineup lands",
        $" {ChatColors.Default}.rethrow {ChatColors.Grey}throws the loaded lineup again, without moving you",
        $" {ChatColors.Default}.last / .back <n> {ChatColors.Grey}back to a throw you made",
        $" {ChatColors.Default}.map / .here {ChatColors.Grey}pick off the minimap, or only what you can throw from here",
        $" {ChatColors.Default}.edit {ChatColors.Grey}rename the loaded lineup or change who sees it",
        $" {ChatColors.Default}.menu {ChatColors.Grey}pick a lineup from the on-screen list",
        $" {ChatColors.Default}.list / .reload / .delete {ChatColors.Grey}manage your library",
        $" {ChatColors.Default}.pos save <name> / .pos <name> {ChatColors.Grey}saved positions",
        $" {ChatColors.Default}.spawn <n> {ChatColors.Grey}teleports to a spawn point",
        $" {ChatColors.Default}.bloom {ChatColors.Grey}outlines where the loaded smoke lands",
        $" {ChatColors.Default}.solve [name] {ChatColors.Grey}finds a throw onto the spot you are looking at",
        $" {ChatColors.Default}.drill {ChatColors.Grey}reps the lineup you are on; {ChatColors.Default}.drill [count] [worst] {ChatColors.Grey}drills your book",
        $" {ChatColors.Default}.drill / .cancel {ChatColors.Grey}stops a drill you are in",
        $" {ChatColors.Default}.playbook / .run / .playbook stop {ChatColors.Grey}the loaded execute",
        $" {ChatColors.Default}.hud {ChatColors.Grey}swaps the panel for centre text",
        $" {ChatColors.Default}.bot / .nobots {ChatColors.Grey}something to flash and blow up",
        $" {ChatColors.Default}.colors {ChatColors.Grey}a colour per throw, smoke and trail",
        $" {ChatColors.Default}.crosshair {ChatColors.Grey}hide the aim marker and throw it blind",
        $" {ChatColors.Default}.spawns / .spawn next {ChatColors.Grey}where rounds start from",
        $" {ChatColors.Default}.clear {ChatColors.Grey}or your {ChatColors.Default}ping key"
            + $" {ChatColors.Grey}-- smokes, fires and the preview",
        $" {ChatColors.Default}.noclip / .god / .timer / .solo",
    };

    private void StartPlaybook(IPlayer player, ICommandContext context)
    {
        switch (_playbook.Start(_library.Map))
        {
            case ePlaybookStart.NoPlaybook:
                Reply(context, $" {ChatColors.Red}no execute is loaded on this session");
                return;
            case ePlaybookStart.NoSteps:
                Reply(context, $" {ChatColors.Red}that execute has no steps");
                return;
            case ePlaybookStart.WrongMap:
                Reply(context, $" {ChatColors.Red}that execute is for another map");
                return;
            case ePlaybookStart.AlreadyRunning:
                Reply(context, $" {ChatColors.Red}already running; .playbook stop first");
                return;
        }

        IReadOnlyList<UtilityPlaybookStep> steps = _playbook.Steps;

        Core.PlayerManager.SendChat(
            $" {ChatColors.Green}{player.Controller.PlayerName} started {ChatColors.Default}{_playbook.Loaded?.name} {ChatColors.Grey}({steps.Count} steps)".Colored()
        );

        for (int index = 0; index < steps.Count; index++)
        {
            UtilityPlaybookStep step = steps[index];
            string who = PlaybookUtility.IsAssigned(step) ? step.assigned_steam_id! : "anyone";

            Reply(
                context,
                $" {ChatColors.Grey}{index + 1}. {step.offset_ms / 1000f:0.0}s {ChatColors.Default}{step.lineup?.name} {ChatColors.Grey}{who}"
            );
        }
    }

    // A mined lineup's stance and aim are fitted to the flight the demo
    // recorded, which puts them a degree or two out. That is close enough to
    // practise toward and not close enough to trust, so the player is told
    // rather than left reading it as a precise alignment.
    private void WarnIfInexact(IPlayer player, LineupRecord lineup)
    {
        if (!lineup.IsKnownInexact())
        {
            return;
        }

        if (!_system.StateFor(player.SteamID).WarnedInexact.Add(lineup.client_id))
        {
            return;
        }

        player.SendChat(
            $" {ChatColors.Yellow}{lineup.name} is {lineup.confidence}, not measured {ChatColors.Grey}- the aim is inferred to a degree or two, so walk it in".Colored()
        );
    }

    // The measurement rides along with the flight path, so the outline cannot
    // be drawn until that fetch has landed.
    private void DrawBloom(ulong steamId, LineupRecord fetched)
    {
        PracticeState state = _system.StateFor(steamId);

        if (!state.Bloom || state.Loaded != fetched)
        {
            return;
        }

        IPlayer? player = _system.Find(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        int beams = _replay.ShowBloom(player, fetched);

        // A real smoke is the measurement itself rather than a drawing of it,
        // so Swiftly shows both: the outline is there instantly, the cloud
        // fills it in a second later.
        bool smoke = _replay.ShowBloomSmoke(player, fetched);

        if (beams == 0 && !smoke)
        {
            Tell(steamId, $" {ChatColors.Grey}no measured bloom for {fetched.name}");
            return;
        }

        Tell(steamId, $" {ChatColors.Green}bloom on {ChatColors.Grey}({beams} lines)");
    }

    private void Step(ICommandContext context, int direction)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);

        if (state.Results.Count == 0)
        {
            Reply(context, $" {ChatColors.Red}load something first");
            return;
        }

        // Index is -1 until something has been loaded, which is "before the
        // start" rather than a position. Feeding that through the modulo made
        // the first .prev land on the second-to-last lineup and skip the last
        // one entirely, so the walk was missing an entry until you had gone all
        // the way round.
        state.Index =
            state.Index < 0
                ? (direction > 0 ? 0 : state.Results.Count - 1)
                : ((state.Index + direction) % state.Results.Count + state.Results.Count)
                    % state.Results.Count;

        Apply(player, state.Results[state.Index]);
    }

    private void Back(ICommandContext context, int back)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        LineupRecord? thrown = _recorder.LastThrow(player.SteamID, back);

        if (thrown == null)
        {
            Reply(context, $" {ChatColors.Red}no throw that far back");
            return;
        }

        Apply(player, thrown);
    }

    private void Apply(IPlayer player, LineupRecord lineup)
    {
        if (!_config.ReplayEnabled)
        {
            player.SendChat($" {ChatColors.Red}replay is disabled on this server".Colored());
            return;
        }

        PracticeState applying = _system.StateFor(player.SteamID);

        // Asking for a lineup is asking to see it: whatever .clear held off
        // starts drawing again from here.
        applying.Cleared = false;
        applying.Loaded = lineup;

        // Standing the player on the lineup needs nothing but the flat fields,
        // so it happens now; the line itself may still be a round trip away.
        _replay.Load(player, lineup);
        _replay.ThrowGhostProjectile(player, lineup);
        WarnIfInexact(player, lineup);

        ulong steamId = player.SteamID;

        _library.EnsureTrajectory(
            lineup,
            steamId,
            fetched =>
            {
                // The player may have loaded something else while the path was
                // in flight; drawing it now would replace what they are looking
                // at with the previous lineup.
                if (_system.StateFor(steamId).Loaded != fetched)
                {
                    return;
                }

                DrawBloom(steamId, fetched);
            }
        );
    }

    private static void Reply(ICommandContext context, string message)
    {
        context.Reply(message.Colored());
    }

    private static string Toggle(bool on)
    {
        return on ? "on" : "off";
    }

    private void Tell(ulong steamId, string message)
    {
        IPlayer? player = _system.Find(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        player.SendChat(message.Colored());
    }
}
