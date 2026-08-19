using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Players;
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

        if (string.IsNullOrEmpty(name))
        {
            Reply(context, $" {ChatColors.Red}usage: .save <name>");
            return;
        }

        LineupRecord? thrown = _recorder.LastThrow(player.SteamID);

        if (thrown == null)
        {
            Reply(context, $" {ChatColors.Red}throw something first");
            return;
        }

        if (_library.For(player.SteamID).Count >= _config.MaxSaved)
        {
            Reply(
                context,
                $" {ChatColors.Red}you already have {_config.MaxSaved} saved lineups on this map"
            );
            return;
        }

        thrown.name = name;
        thrown.map = _library.Map;
        thrown.side = player.Controller.Team == Team.CT ? "CT" : "TERRORIST";
        thrown.visibility = nameof(eLineupVisibility.Private);
        thrown.plugin_version = ModuleVersion;

        _library.Add(player.SteamID, thrown);

        Reply(context, $" {ChatColors.Green}saved {ChatColors.Default}{name}");

        ulong steamId = player.SteamID;

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
        PracticeState state = _system.StateFor(player.SteamID);
        Vec3? near = PracticeSystem.Where(player)?.feet_position;

        LineupRecord? lineup = _library.Resolve(player.SteamID, query, near);

        if (lineup == null)
        {
            Reply(context, $" {ChatColors.Red}no lineup matches \"{query}\"");
            return;
        }

        state.Results.Clear();
        state.Results.AddRange(
            PracticeLineupUtility.Filter(_library.For(player.SteamID), query, near)
        );
        state.Index = state.Results.FindIndex(match => match.client_id == lineup.client_id);

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

    [Command("rethrow", registerRaw: false, permission: "")]
    public void OnRethrow(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        LineupRecord? loaded = _system.StateFor(player.SteamID).Loaded;

        if (loaded == null)
        {
            Reply(context, $" {ChatColors.Red}nothing loaded");
            return;
        }

        Apply(player, loaded);
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

        _replay.ClearGhosts(player.SteamID);

        Reply(context, $" {ChatColors.Green}cleared");
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
        _library.EnsureTrajectory(loaded, fetched => DrawBloom(steamId, fetched));
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

    [Command("drill", registerRaw: false, permission: "")]
    public void OnDrill(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        DrillRequest request = DrillUtility.Parse(string.Join(" ", context.Args));

        if (!request.Valid)
        {
            Reply(context, $" {ChatColors.Red}usage: .drill [count] [worst|random] / .drill stop");
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

        if (!int.TryParse(string.Join(" ", context.Args).Trim(), out int index))
        {
            Reply(context, $" {ChatColors.Red}usage: .spawn <1-{spawns.Count}>");
            return;
        }

        index = Math.Clamp(index, 1, spawns.Count);

        PracticeSystem.TeleportTo(player, spawns[index - 1]);
        Reply(context, $" {ChatColors.Green}spawn {index}/{spawns.Count}");
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

        _replay.ApplyVisibility();

        Reply(
            context,
            state.Solo
                ? $" {ChatColors.Green}solo on {ChatColors.Grey}(you only see your own previews)"
                : $" {ChatColors.Green}solo off {ChatColors.Grey}(you see everyone's previews)"
        );
    }

    // Sibling of .solo: that one decides whose previews you see, this one
    // decides whether you see any at all. It takes an explicit on/off as well
    // as toggling, because the caller that needs it most is a capture client
    // that cannot read back what state it is in.
    [Command("ghosts", registerRaw: false, permission: "")]
    public void OnGhosts(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        string argument = string.Join(" ", context.Args).Trim();

        if (!PracticeSignalUtility.TryParseToggle(argument, state.Ghosts, out bool wanted))
        {
            Reply(context, $" {ChatColors.Red}usage: .ghosts [on|off]");
            return;
        }

        state.Ghosts = wanted;

        if (!wanted)
        {
            _replay.ClearGhosts(player.SteamID);
        }

        _replay.ApplyVisibility();

        Reply(context, $" {ChatColors.Green}ghost previews {Toggle(state.Ghosts)}");
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

    private static readonly string[] HelpLines = new[]
    {
        $" {ChatColors.Green}utility practice",
        $" {ChatColors.Default}.save <name> {ChatColors.Grey}saves your last throw",
        $" {ChatColors.Default}.load <query> {ChatColors.Grey}teleports you to a lineup",
        $" {ChatColors.Default}.next / .prev {ChatColors.Grey}walk the last search",
        $" {ChatColors.Default}.rethrow {ChatColors.Grey}back to the loaded lineup",
        $" {ChatColors.Default}.last / .back <n> {ChatColors.Grey}back to a throw you made",
        $" {ChatColors.Default}.list / .reload / .delete {ChatColors.Grey}manage your library",
        $" {ChatColors.Default}.pos save <name> / .pos <name> {ChatColors.Grey}saved positions",
        $" {ChatColors.Default}.spawn <n> {ChatColors.Grey}teleports to a spawn point",
        $" {ChatColors.Default}.bloom {ChatColors.Grey}outlines where the loaded smoke lands",
        $" {ChatColors.Default}.solve [name] {ChatColors.Grey}finds a throw onto the spot you are looking at",
        $" {ChatColors.Default}.drill [count] [worst] / .skip {ChatColors.Grey}drills your book and scores it",
        $" {ChatColors.Default}.playbook / .run / .playbook stop {ChatColors.Grey}the loaded execute",
        $" {ChatColors.Default}.ghosts [on|off] {ChatColors.Grey}draws the preview line, or does not",
        $" {ChatColors.Default}.noclip / .god / .timer / .solo / .clear",
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

        state.Index =
            ((state.Index + direction) % state.Results.Count + state.Results.Count)
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

        _system.StateFor(player.SteamID).Loaded = lineup;

        // Standing the player on the lineup needs nothing but the flat fields,
        // so it happens now; the line itself may still be a round trip away.
        _replay.Load(player, lineup);
        _replay.ShowGhost(player, lineup);
        _replay.ThrowGhostProjectile(player, lineup);
        WarnIfInexact(player, lineup);

        ulong steamId = player.SteamID;

        _library.EnsureTrajectory(
            lineup,
            fetched =>
            {
                // The player may have loaded something else while the path was
                // in flight; drawing it now would replace what they are looking
                // at with the previous lineup.
                if (_system.StateFor(steamId).Loaded != fetched)
                {
                    return;
                }

                IPlayer? still = _system.Find(steamId);

                if (still != null && still.IsValid)
                {
                    _replay.ShowGhost(still, fetched);
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
