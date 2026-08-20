using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;

namespace UtilityPractice;

// The css_ prefix is what turns a console command into a chat command, so
// every player-facing verb here carries it and is spoken as ".save", ".load"
// and so on. Replies are always to the caller: a practice server is several
// people working on unrelated things in the same map.
public partial class UtilityPracticePlugin
{
    [ConsoleCommand("css_save", "Saves your last throw as a named lineup")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSave(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        string name = command.ArgString.Trim().Trim('"');

        if (string.IsNullOrEmpty(name))
        {
            command.ReplyToCommand($" {ChatColors.Red}usage: .save <name>");
            return;
        }

        LineupRecord? thrown = _recorder.LastThrow(player.SteamID);

        if (thrown == null)
        {
            command.ReplyToCommand($" {ChatColors.Red}throw something first");
            return;
        }

        if (_library.For(player.SteamID).Count >= _config.MaxSaved)
        {
            command.ReplyToCommand(
                $" {ChatColors.Red}you already have {_config.MaxSaved} saved lineups on this map"
            );
            return;
        }

        thrown.name = name;
        thrown.map = _library.Map;
        thrown.side = player.Team == CsTeam.CounterTerrorist ? "CT" : "TERRORIST";
        thrown.visibility = nameof(eLineupVisibility.Private);
        thrown.plugin_version = ModuleVersion;

        _library.Add(player.SteamID, thrown);

        command.ReplyToCommand($" {ChatColors.Green}saved {ChatColors.Default}{name}");

        ulong steamId = player.SteamID;

        _ = Task.Run(async () =>
        {
            string? id = await _api.Ingest(thrown);

            Server.NextFrame(() =>
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

    [ConsoleCommand("css_load", "Teleports you to a saved lineup")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnLoad(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        string query = command.ArgString.Trim().Trim('"');
        PracticeState state = _system.StateFor(player.SteamID);
        Vec3? near = PracticeSystem.Where(player)?.feet_position;

        LineupRecord? lineup = _library.Resolve(player.SteamID, query, near);

        if (lineup == null)
        {
            command.ReplyToCommand($" {ChatColors.Red}no lineup matches \"{query}\"");
            return;
        }

        state.Results.Clear();
        state.Results.AddRange(
            PracticeLineupUtility.Filter(_library.For(player.SteamID), query, near)
        );
        state.Index = state.Results.FindIndex(match => match.client_id == lineup.client_id);

        Apply(player, lineup);
    }

    [ConsoleCommand("css_list", "Lists your saved lineups on this map")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnList(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        IReadOnlyList<LineupRecord> lineups = _library.For(player.SteamID);

        if (lineups.Count == 0)
        {
            command.ReplyToCommand($" {ChatColors.Grey}no saved lineups on {_library.Map}");
            return;
        }

        command.ReplyToCommand($" {ChatColors.Green}{lineups.Count} lineups on {_library.Map}");

        foreach (LineupRecord lineup in lineups)
        {
            command.ReplyToCommand(
                $" {ChatColors.Default}{lineup.name} {ChatColors.Grey}({lineup.utility_type}, {lineup.technique})"
            );
        }
    }

    [ConsoleCommand("css_next", "Loads the next lineup in your last search")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNext(CCSPlayerController? player, CommandInfo command)
    {
        Step(player, command, 1);
    }

    [ConsoleCommand("css_prev", "Loads the previous lineup in your last search")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPrev(CCSPlayerController? player, CommandInfo command)
    {
        Step(player, command, -1);
    }

    [ConsoleCommand("css_rethrow", "Puts you back on the lineup you last loaded")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRethrow(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        LineupRecord? loaded = _system.StateFor(player.SteamID).Loaded;

        if (loaded == null)
        {
            command.ReplyToCommand($" {ChatColors.Red}nothing loaded");
            return;
        }

        Apply(player, loaded);
    }

    [ConsoleCommand("css_last", "Puts you back on your last throw")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnLast(CCSPlayerController? player, CommandInfo command)
    {
        Back(player, command, 0);
    }

    [ConsoleCommand("css_back", "Puts you back on the throw n before your last")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnBack(CCSPlayerController? player, CommandInfo command)
    {
        if (!int.TryParse(command.ArgString.Trim(), out int back) || back < 0)
        {
            command?.ReplyToCommand($" {ChatColors.Red}usage: .back <n>");
            return;
        }

        Back(player, command, back);
    }

    [ConsoleCommand("css_clear", "Clears your lineup preview")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnClear(CCSPlayerController? player, CommandInfo command)
    {
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

        command.ReplyToCommand($" {ChatColors.Green}cleared");
    }

    [ConsoleCommand("css_bloom", "Outlines where the loaded lineup would bloom")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnBloom(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        LineupRecord? loaded = state.Loaded;

        if (loaded == null)
        {
            command.ReplyToCommand($" {ChatColors.Red}load a lineup first");
            return;
        }

        if (!_config.GhostPreview)
        {
            command.ReplyToCommand($" {ChatColors.Red}previews are disabled on this server");
            return;
        }

        state.Bloom = !state.Bloom;

        if (!state.Bloom)
        {
            _replay.ClearBloom(player.SteamID);
            command.ReplyToCommand($" {ChatColors.Green}bloom off");
            return;
        }

        command.ReplyToCommand($" {ChatColors.Grey}outlining {loaded.name}...");

        ulong steamId = player.SteamID;

        // The same fetch .load already made, and free once it has landed.
        _library.EnsureTrajectory(loaded, fetched => DrawBloom(steamId, fetched));
    }

    [ConsoleCommand("css_playbook", "Runs the loaded execute: .playbook / .playbook stop")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPlaybook(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        string argument = command.ArgString.Trim().Trim('"');

        if (argument.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            if (!_playbook.Stop())
            {
                command.ReplyToCommand($" {ChatColors.Red}nothing is running");
                return;
            }

            Server.PrintToChatAll($" {ChatColors.Green}{player.PlayerName} stopped the execute");
            return;
        }

        StartPlaybook(player, command);
    }

    [ConsoleCommand("css_run", "Runs the loaded execute")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnRun(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        StartPlaybook(player, command);
    }

    [ConsoleCommand("css_drill", "Drills your lineups: .drill [count] [worst], .drill stop")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnDrill(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        DrillRequest request = DrillUtility.Parse(command.ArgString);

        if (!request.Valid)
        {
            command.ReplyToCommand(
                $" {ChatColors.Red}usage: .drill [count] [worst|random] / .drill stop"
            );
            return;
        }

        if (request.Stop)
        {
            if (!_drill.Stop(player.SteamID))
            {
                command.ReplyToCommand($" {ChatColors.Red}you are not drilling");
            }
            return;
        }

        switch (_drill.Start(player.SteamID, request.Order, request.Count))
        {
            case eDrillStart.AlreadyRunning:
                command.ReplyToCommand($" {ChatColors.Red}already drilling; .drill stop first");
                return;
            case eDrillStart.ReplayDisabled:
                command.ReplyToCommand($" {ChatColors.Red}replay is disabled on this server");
                return;
            case eDrillStart.NotConnected:
                command.ReplyToCommand(
                    $" {ChatColors.Red}this server has no panel, so a throw cannot be scored"
                );
                return;
            case eDrillStart.NothingToDrill:
                command.ReplyToCommand(
                    $" {ChatColors.Red}nothing on {_library.Map} to drill; save some lineups or .reload"
                );
                return;
        }
    }

    [ConsoleCommand("css_skip", "Skips the lineup your drill is on")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSkip(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        if (!_drill.Skip(player.SteamID))
        {
            command.ReplyToCommand($" {ChatColors.Red}you are not drilling");
        }
    }

    [ConsoleCommand("css_pos", "Saves and restores positions: .pos save <name>, .pos <name>")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPos(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        string[] args = command
            .ArgString.Trim()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (args.Length == 0)
        {
            if (state.Positions.Count == 0)
            {
                command.ReplyToCommand($" {ChatColors.Grey}no saved positions");
                return;
            }

            command.ReplyToCommand(
                $" {ChatColors.Green}positions: {ChatColors.Default}{string.Join(", ", state.Positions.Keys)}"
            );
            return;
        }

        if (args[0].Equals("save", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
            {
                command.ReplyToCommand($" {ChatColors.Red}usage: .pos save <name>");
                return;
            }

            if (!_system.SavePosition(player, args[1]))
            {
                command.ReplyToCommand($" {ChatColors.Red}unable to save that position");
                return;
            }

            command.ReplyToCommand(
                $" {ChatColors.Green}saved position {ChatColors.Default}{args[1]}"
            );
            return;
        }

        if (!state.Positions.TryGetValue(args[0], out ThrowSnapshot? position))
        {
            command.ReplyToCommand($" {ChatColors.Red}no position named {args[0]}");
            return;
        }

        PracticeSystem.TeleportTo(player, position);
    }

    [ConsoleCommand("css_spawn", "Teleports you to a spawn point")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSpawn(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        List<ThrowSnapshot> spawns = PracticeSystem.SpawnPoints();

        if (spawns.Count == 0)
        {
            command.ReplyToCommand($" {ChatColors.Red}this map has no spawn points");
            return;
        }

        if (!int.TryParse(command.ArgString.Trim(), out int index))
        {
            command.ReplyToCommand($" {ChatColors.Red}usage: .spawn <1-{spawns.Count}>");
            return;
        }

        index = Math.Clamp(index, 1, spawns.Count);

        PracticeSystem.TeleportTo(player, spawns[index - 1]);
        command.ReplyToCommand($" {ChatColors.Green}spawn {index}/{spawns.Count}");
    }

    [ConsoleCommand("css_noclip", "Toggles noclip")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnNoclip(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        state.Noclip = !state.Noclip;

        command.ReplyToCommand($" {ChatColors.Green}noclip {Toggle(state.Noclip)}");
    }

    [ConsoleCommand("css_god", "Toggles god mode")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnGod(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        state.God = !state.God;

        command.ReplyToCommand($" {ChatColors.Green}god {Toggle(state.God)}");
    }

    [ConsoleCommand("css_timer", "Starts and stops a stopwatch")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnTimer(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);

        if (state.TimerStartedAt == null)
        {
            state.TimerStartedAt = DateTime.UtcNow;
            command.ReplyToCommand($" {ChatColors.Green}timer started");
            return;
        }

        double elapsed = (DateTime.UtcNow - state.TimerStartedAt.Value).TotalSeconds;
        state.TimerStartedAt = null;

        command.ReplyToCommand($" {ChatColors.Green}timer stopped at {elapsed:0.00}s");
    }

    [ConsoleCommand("css_solo", "Toggles whether your previews are yours alone")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnSolo(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        state.Solo = !state.Solo;

        command.ReplyToCommand(
            state.Solo
                ? $" {ChatColors.Green}solo on {ChatColors.Grey}(you only see your own previews)"
                : $" {ChatColors.Green}solo off {ChatColors.Grey}(you see everyone's previews)"
        );
    }

    // Sibling of .solo: that one decides whose previews you see, this one
    // decides whether you see any at all. It takes an explicit on/off as well
    // as toggling, because the caller that needs it most is a capture client
    // that cannot read back what state it is in.
    [ConsoleCommand("css_ghosts", "Turns your own preview lines on or off")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnGhosts(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);

        if (!PracticeSignalUtility.TryParseToggle(command.ArgString, state.Ghosts, out bool wanted))
        {
            command.ReplyToCommand($" {ChatColors.Red}usage: .ghosts [on|off]");
            return;
        }

        state.Ghosts = wanted;

        if (!wanted)
        {
            _replay.ClearGhosts(player.SteamID);
        }

        command.ReplyToCommand($" {ChatColors.Green}ghost previews {Toggle(state.Ghosts)}");
    }

    [ConsoleCommand("css_delete", "Deletes the lineup you have loaded")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnDelete(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);
        LineupRecord? loaded = state.Loaded;

        if (loaded == null)
        {
            command.ReplyToCommand($" {ChatColors.Red}load a lineup first");
            return;
        }

        _library.Remove(player.SteamID, loaded);
        state.Results.RemoveAll(match => match.client_id == loaded.client_id);
        state.Loaded = null;
        _replay.ClearGhosts(player.SteamID);

        command.ReplyToCommand($" {ChatColors.Green}deleted {ChatColors.Default}{loaded.name}");

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

            Server.NextFrame(() =>
                Tell(steamId, $" {ChatColors.Red}{loaded.name} is still on the panel; try .reload")
            );
        });
    }

    [ConsoleCommand("css_reload", "Re-fetches your lineups from the panel")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnReload(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        ulong steamId = player.SteamID;

        command.ReplyToCommand($" {ChatColors.Grey}reloading...");

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

    [ConsoleCommand("css_help", "Lists the practice commands")]
    [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
    public void OnPracticeHelp(CCSPlayerController? player, CommandInfo command)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        foreach (string line in HelpLines)
        {
            command.ReplyToCommand(line);
        }
    }

    // Server-only and deliberately unprefixed, like the match plugin's
    // get_match: the panel calls it when the roster or the library changes.
    [ConsoleCommand("utility_practice_refresh", "Re-reads the practice session from the panel")]
    [CommandHelper(whoCanExecute: CommandUsage.SERVER_ONLY)]
    public void OnRefresh(CCSPlayerController? player, CommandInfo command)
    {
        RefreshEverything();
    }

    private const float WelcomeDelaySeconds = 2f;

    // Deliberately short. The full list reads as spam on every join; these are
    // the four that get somebody throwing, and .help is where the rest lives.
    private static readonly string[] WelcomeLines = new[]
    {
        $" {ChatColors.Green}utility practice {ChatColors.Grey}-- infinite utility, buy anywhere",
        $" {ChatColors.Default}.save <name> {ChatColors.Grey}saves the throw you just made",
        $" {ChatColors.Default}.load <query> {ChatColors.Grey}stands you on a saved lineup",
        $" {ChatColors.Default}.rethrow {ChatColors.Grey}back to the loaded lineup",
        $" {ChatColors.Default}.help {ChatColors.Grey}everything else",
    };

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
        $" {ChatColors.Default}.solve {ChatColors.Grey}SwiftlyS2 builds only",
        $" {ChatColors.Default}.drill [count] [worst] / .skip {ChatColors.Grey}drills your book and scores it",
        $" {ChatColors.Default}.playbook / .run / .playbook stop {ChatColors.Grey}the loaded execute",
        $" {ChatColors.Default}.ghosts [on|off] {ChatColors.Grey}draws the preview line, or does not",
        $" {ChatColors.Default}.noclip / .god / .timer / .solo / .clear",
    };

    private void StartPlaybook(CCSPlayerController player, CommandInfo command)
    {
        switch (_playbook.Start(_library.Map))
        {
            case ePlaybookStart.NoPlaybook:
                command.ReplyToCommand($" {ChatColors.Red}no execute is loaded on this session");
                return;
            case ePlaybookStart.NoSteps:
                command.ReplyToCommand($" {ChatColors.Red}that execute has no steps");
                return;
            case ePlaybookStart.WrongMap:
                command.ReplyToCommand($" {ChatColors.Red}that execute is for another map");
                return;
            case ePlaybookStart.AlreadyRunning:
                command.ReplyToCommand($" {ChatColors.Red}already running; .playbook stop first");
                return;
        }

        IReadOnlyList<UtilityPlaybookStep> steps = _playbook.Steps;

        Server.PrintToChatAll(
            $" {ChatColors.Green}{player.PlayerName} started {ChatColors.Default}{_playbook.Loaded?.name} {ChatColors.Grey}({steps.Count} steps)"
        );

        for (int index = 0; index < steps.Count; index++)
        {
            UtilityPlaybookStep step = steps[index];
            string who = PlaybookUtility.IsAssigned(step) ? step.assigned_steam_id! : "anyone";

            command.ReplyToCommand(
                $" {ChatColors.Grey}{index + 1}. {step.offset_ms / 1000f:0.0}s {ChatColors.Default}{step.lineup?.name} {ChatColors.Grey}{who}"
            );
        }
    }

    // A mined lineup's stance and aim are fitted to the flight the demo
    // recorded, which puts them a degree or two out. That is close enough to
    // practise toward and not close enough to trust, so the player is told
    // rather than left reading it as a precise alignment.
    private void WarnIfInexact(CCSPlayerController player, LineupRecord lineup)
    {
        if (!lineup.IsKnownInexact())
        {
            return;
        }

        if (!_system.StateFor(player.SteamID).WarnedInexact.Add(lineup.client_id))
        {
            return;
        }

        player.PrintToChat(
            $" {ChatColors.Yellow}{lineup.name} is {lineup.confidence}, not measured {ChatColors.Grey}- the aim is inferred to a degree or two, so walk it in"
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

        CCSPlayerController? player = Utilities.GetPlayerFromSteamId(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        int beams = _replay.ShowBloom(player, fetched);

        Tell(
            steamId,
            beams == 0
                ? $" {ChatColors.Grey}no measured bloom for {fetched.name}"
                : $" {ChatColors.Green}bloom on {ChatColors.Grey}({beams} lines)"
        );
    }

    private void Step(CCSPlayerController? player, CommandInfo command, int direction)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);

        if (state.Results.Count == 0)
        {
            command.ReplyToCommand($" {ChatColors.Red}load something first");
            return;
        }

        state.Index =
            ((state.Index + direction) % state.Results.Count + state.Results.Count)
            % state.Results.Count;

        Apply(player, state.Results[state.Index]);
    }

    private void Back(CCSPlayerController? player, CommandInfo command, int back)
    {
        if (player == null || !player.IsValid)
        {
            return;
        }

        LineupRecord? thrown = _recorder.LastThrow(player.SteamID, back);

        if (thrown == null)
        {
            command.ReplyToCommand($" {ChatColors.Red}no throw that far back");
            return;
        }

        Apply(player, thrown);
    }

    private void Apply(CCSPlayerController player, LineupRecord lineup)
    {
        if (!_config.ReplayEnabled)
        {
            player.PrintToChat($" {ChatColors.Red}replay is disabled on this server");
            return;
        }

        _system.StateFor(player.SteamID).Loaded = lineup;

        // Standing the player on the lineup needs nothing but the flat fields,
        // so it happens now; the line itself may still be a round trip away.
        _replay.Load(player, lineup);
        _replay.ShowGhost(player, lineup);
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

                CCSPlayerController? still = Utilities.GetPlayerFromSteamId(steamId);

                if (still != null && still.IsValid)
                {
                    _replay.ShowGhost(still, fetched);
                }

                DrawBloom(steamId, fetched);
            }
        );
    }

    private static string Toggle(bool on)
    {
        return on ? "on" : "off";
    }

    private static void Tell(ulong steamId, string message)
    {
        CCSPlayerController? player = Utilities.GetPlayerFromSteamId(steamId);

        if (player == null || !player.IsValid)
        {
            return;
        }

        player.PrintToChat(message);
    }
}
