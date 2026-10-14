using FiveStack.Entities.Practice;
using Microsoft.Extensions.Logging;
using FiveStack.Enums;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Commands;
using SwiftlyS2.Shared.Natives;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

public partial class UtilityPracticePlugin
{
    // Null when the runtime predates custom_hud_layout. Constructing HudKit
    // resolves CCSCustomHudLayout, which does not exist before
    // SwiftlyS2 1.4.6-beta.9 -- and an unguarded resolve there took the whole
    // plugin down rather than just the panels.
    private HudKit? _hud;
    private HudPrompt _prompt = null!;

    // Opt-out rather than opt-in: a player who cannot see the panel sees the
    // centre text the plugin has always sent, and one who can gets the panel
    // without asking for it.
    private readonly HashSet<ulong> _hudOff = new();

    private class MenuState
    {
        public string Side { get; set; } = "all";
        public string Type { get; set; } = "all";
        public string Scope { get; set; } = "all";
        public int Page { get; set; }
    }

    private readonly Dictionary<ulong, MenuState> _menus = new();

    // Loaded once from hud/radars/metadata.json, mirrored from the web panel.
    private Dictionary<string, RadarCalibration>? _radars;

    // The target a player picked off the map, so the list knows what to filter
    // to and the map knows what to light up.
    private readonly Dictionary<ulong, string> _target = new();


    // Off by default and only offered once standing somewhere useful.
    private readonly HashSet<ulong> _reachableOnly = new();

    private Dictionary<string, RadarCalibration> Radars()
    {
        // Core.PluginPath, not Assembly.Location: SwiftlyS2 loads plugins from
        // bytes so hot reload can replace the file, which leaves Location empty
        // and every lookup here resolving against the server's working directory.
        return _radars ??= RadarMaps.Load(Path.Join(Core.PluginPath, "radars", "metadata.json"));
    }

    private RadarCalibration? Calibration()
    {
        return Radars().TryGetValue(
            RadarProjection.NormalizeMapName(_library.Map),
            out RadarCalibration? found
        )
            ? found
            : null;
    }

    private List<UtilityTarget> Targets(ulong steamId)
    {
        return UtilityTargetCluster.Top(
            UtilityTargetCluster.Build(_library.For(steamId)),
            HudSlots.MapMarkers
        );
    }

    // Twice a second. Fast enough that the reachable ring keeps up with a
    // player walking onto a spot, slow enough to be free.
    private const int MapRefreshEveryTicks = 32;

    // The execute follows the run rather than a command: it appears when one
    // starts and goes when it ends, because there is no moment during an execute
    // when you would want to summon it by typing.
    private void HudRun(IPlayer player)
    {
        if (!UseHud(player.SteamID) || !_playbook.Running || !_hud!.Available(HudSlots.Run))
        {
            _hud?.Hide(player, HudSlots.Run);

            return;
        }

        IReadOnlyList<UtilityPlaybookStep> steps = _playbook.Steps;
        double elapsed = _playbook.Elapsed.TotalSeconds;

        var template = new NadeRunTemplate
        {
            Kicker = "Execute",
            Title = PracticeLineupUtility.TitleCase(_playbook.Loaded?.name ?? "Execute"),
            Clock = elapsed < 0 ? $"{Math.Ceiling(-elapsed):0}" : $"t+{elapsed:0.0}",
        };

        string me = player.SteamID.ToString();
        int index = 0;

        foreach (UtilityPlaybookStep step in steps.Take(HudSlots.RunSteps))
        {
            double at = step.offset_ms / 1000.0;

            // Unassigned steps are everyone's, so they read as yours rather than
            // as somebody else's problem.
            bool mine =
                string.IsNullOrEmpty(step.assigned_steam_id) || step.assigned_steam_id == me;

            template.Add(
                new NadeRunTemplate.Step(
                    $"t+{at:0.0}",
                    PracticeLineupUtility.TitleCase(step.lineup?.name ?? "unknown"),
                    mine ? "you" : Short(step.assigned_steam_id),
                    _system.StateFor(player.SteamID).Colors
                        ? PracticeStepColors.For(index).Name
                        : null,
                    mine,
                    elapsed > at,
                    elapsed >= at - 1.0 && elapsed <= at + 1.0
                )
            );

            index++;
        }

        _hud.Show(player, template);
    }

    // A steam id is not a name. Without the roster this is the best that can be
    // said, and it is still enough to tell two teammates apart on the list.
    private string Short(string? steamId)
    {
        IPlayer? who = ulong.TryParse(steamId, out ulong parsed) ? _system.Find(parsed) : null;

        return who?.Name ?? "team";
    }

    private bool UseHud(ulong steamId)
    {
        return _hud != null && _config.HudEnabled && !_hudOff.Contains(steamId);
    }

    // Returns false when the centre-text panels still have to do the work, so
    // the two paths never both write.
    private bool HudPanels(
        IPlayer player,
        CCSPlayerPawn pawn,
        LineupRecord? lineup,
        bool onSpot,
        bool onAngle
    )
    {
        if (!UseHud(player.SteamID) || !_hud!.Available(HudSlots.Hud))
        {
            return false;
        }

        // Walking pace, not aiming pace. The surface diffs writes, so a redraw
        // that changes nothing costs nothing.
        if (_aimTick % MapRefreshEveryTicks == 0 && _hud!.IsOpen(player, HudSlots.Pick))
        {
            RenderPicker(player);
        }

        if (lineup == null)
        {
            _hud!.Hide(player, HudSlots.Hud);

            return true;
        }

        // Every other tick of the aim job: the clock reads in tenths, so it has
        // to keep up, but nothing else on it moves fast.
        if (_aimTick % 8 == 0)
        {
            HudRun(player);
        }

        QAngle eyes = pawn.EyeAngles;

        // ThrowColor encodes "step beats cycle"; re-deriving it here is how that
        // rule drifts. Off means off -- .colors hides it everywhere, not just in
        // the world.
        string? colour =
            _system.StateFor(player.SteamID).Colors && lineup.utility_type == nameof(eUtilityType.Smoke)
                ? ThrowColor(player.SteamID).Name
                : null;

        PracticeState walking = _system.StateFor(player.SteamID);
        string position =
            walking.Index >= 0 && walking.Results.Count > 1
                ? $"{walking.Index + 1} / {walking.Results.Count}"
                : "";

        Vector feet = pawn.AbsOrigin ?? new Vector(0, 0, 0);

        return _hud!.Show(
            player,
            NadeHudTemplate.For(
                lineup,
                eyes.Y,
                eyes.X,
                colour,
                onSpot,
                Flat(lineup.release.feet_position, new Vec3(feet.X, feet.Y, feet.Z)),
                onAngle,
                MasteryOf(player.SteamID, lineup),
                position,
                _replay.AimAnglesFrom(lineup, PracticeReplay.EyeOf(pawn)),
                LastFor(player.SteamID, lineup)
            )
        );
    }

    private void OnHudClicked(IPlayer player, string layout, string button)
    {
        if (layout == HudSlots.NadeEdit)
        {
            OnEditClicked(player, button);

            return;
        }

        if (layout == HudSlots.NadeWheel)
        {
            OnWheelClicked(player, button);

            return;
        }

        if (layout == HudSlots.NadePick)
        {
            OnPickClicked(player, button);
        }
    }

    private void OnPickClicked(IPlayer player, string button)
    {
        ulong steamId = player.SteamID;
        MenuState menu = MenuFor(steamId);

        if (button == "close")
        {
            ClosePicker(player);

            return;
        }

        if (button == "here")
        {
            if (!_reachableOnly.Add(steamId))
            {
                _reachableOnly.Remove(steamId);
            }

            menu.Page = 0;
            RenderPicker(player);

            return;
        }

        if (button == "reset")
        {
            ResetFilters(player);
            RenderPicker(player);

            return;
        }

        if (button.StartsWith("side_", StringComparison.Ordinal))
        {
            menu.Side = button["side_".Length..];
            menu.Page = 0;
            FiltersChanged(player);
            RenderPicker(player);

            return;
        }

        if (button.StartsWith("scope_", StringComparison.Ordinal))
        {
            menu.Scope = button["scope_".Length..];
            menu.Page = 0;
            FiltersChanged(player);
            RenderPicker(player);

            return;
        }

        if (button.StartsWith("type_", StringComparison.Ordinal))
        {
            menu.Type = button["type_".Length..];
            menu.Page = 0;
            FiltersChanged(player);
            RenderPicker(player);

            return;
        }

        if (button is "next" or "prev")
        {
            List<LineupRecord> paged = MenuRows(steamId);
            int pages = Math.Max(1, (paged.Count + HudSlots.ListRows - 1) / HudSlots.ListRows);

            menu.Page = ((menu.Page + (button == "next" ? 1 : -1)) % pages + pages) % pages;
            RenderPicker(player);

            return;
        }

        if (button.StartsWith("target:", StringComparison.Ordinal))
        {
            PickTarget(player, button["target:".Length..]);

            return;
        }

        if (!button.StartsWith("lineup:", StringComparison.Ordinal))
        {
            return;
        }

        string key = button["lineup:".Length..];
        List<LineupRecord> rows = MenuRows(steamId);
        LineupRecord? chosen = rows.FirstOrDefault(lineup => Key(lineup) == key);

        if (chosen == null)
        {
            Toast(player, "That lineup is no longer in your library");
            RenderPicker(player);

            return;
        }

        LoadPicked(player, rows, chosen);
    }

    // A spot on the radar narrows the list beside it to the throws that land
    // there; the same spot again lets go of it. One way to hit it is not a
    // choice, so that one is loaded outright.
    private void PickTarget(IPlayer player, string id)
    {
        ulong steamId = player.SteamID;

        if (_target.TryGetValue(steamId, out string? was) && was == id)
        {
            _target.Remove(steamId);
        }
        else
        {
            _target[steamId] = id;
        }

        MenuFor(steamId).Page = 0;

        List<LineupRecord> rows = MenuRows(steamId);

        if (_target.ContainsKey(steamId) && rows.Count == 1)
        {
            // Not left behind as a filter: nobody chose to narrow the world to
            // one throw, they chose the throw.
            _target.Remove(steamId);
            FiltersChanged(player);
            LoadPicked(player, rows, rows[0]);

            return;
        }

        FiltersChanged(player);
        RenderPicker(player);
    }

    // The list the player was reading IS the result set .next and .prev should
    // walk, so it replaces whatever an earlier query left behind. Picking is
    // the end of choosing: every menu goes.
    private void LoadPicked(IPlayer player, List<LineupRecord> rows, LineupRecord chosen)
    {
        PracticeState state = _system.StateFor(player.SteamID);

        state.Results.Clear();
        state.Results.AddRange(rows);
        state.Index = rows.FindIndex(row => Key(row) == Key(chosen));

        CloseCursorPanels(player);
        Apply(player, chosen);
    }

    private MenuState MenuFor(ulong steamId)
    {
        if (!_menus.TryGetValue(steamId, out MenuState? menu))
        {
            menu = new MenuState();
            _menus[steamId] = menu;
        }

        return menu;
    }

    private static readonly Dictionary<string, string> TypeFor = new()
    {
        ["smoke"] = nameof(eUtilityType.Smoke),
        ["flash"] = nameof(eUtilityType.Flash),
        ["molly"] = nameof(eUtilityType.Molotov),
        ["he"] = nameof(eUtilityType.HighExplosive),
    };

    // Everything a player's filters let through, in library order. The world
    // is drawn from this and the list is built on it, so a chip can never mean
    // one thing on the panel and another on the map.
    private List<LineupRecord> Narrowed(ulong steamId)
    {
        MenuState menu = MenuFor(steamId);
        IEnumerable<LineupRecord> lineups = _library.For(steamId);

        // Whose lineups. The library already only contains what this player is
        // allowed to see, so these narrow rather than reveal.
        if (menu.Scope != "all")
        {
            string me = steamId.ToString();

            lineups = menu.Scope switch
            {
                "mine" => lineups.Where(l =>
                    string.Equals(l.author_steam_id, me, StringComparison.Ordinal)
                ),
                "team" => lineups.Where(l =>
                    string.Equals(l.visibility, "Team", StringComparison.OrdinalIgnoreCase)
                ),
                _ => lineups.Where(l =>
                    string.Equals(l.visibility, "Public", StringComparison.OrdinalIgnoreCase)
                ),
            };
        }

        if (menu.Side != "all")
        {
            string side = menu.Side == "ct" ? "CT" : "TERRORIST";

            lineups = lineups.Where(l =>
                string.Equals(l.side, side, StringComparison.OrdinalIgnoreCase)
            );
        }

        // A target picked off the map narrows the list to the lineups that hit
        // it, which is what makes a library of hundreds usable at all.
        if (_target.TryGetValue(steamId, out string? target))
        {
            UtilityTarget? chosen = Targets(steamId).FirstOrDefault(t => t.Id == target);

            if (chosen != null)
            {
                lineups = chosen.Lineups;
            }
        }

        return lineups
            .Where(lineup =>
                menu.Type == "all"
                || string.Equals(
                    lineup.utility_type,
                    TypeFor[menu.Type],
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();
    }

    private List<LineupRecord> MenuRows(ulong steamId)
    {
        List<LineupRecord> matched = Narrowed(steamId);
        Vec3? standing = Standing(steamId);

        if (_reachableOnly.Contains(steamId) && standing != null)
        {
            var here = PracticeReplay
                .SpotAt(_library.For(steamId), standing.Value)
                .Select(UtilityTargetCluster.Key)
                .ToHashSet(StringComparer.Ordinal);

            matched = matched.Where(l => here.Contains(UtilityTargetCluster.Key(l))).ToList();
        }

        if (standing == null)
        {
            return matched;
        }

        // Nearest throw spot first. At four hundred lineups the ones you can
        // reach without crossing the map are the only ones worth reading.
        return matched
            .OrderBy(lineup => (lineup.release.feet_position - standing.Value).Length())
            .ToList();
    }

    private static string Key(LineupRecord lineup)
    {
        return string.IsNullOrEmpty(lineup.id) ? lineup.client_id : lineup.id!;
    }

    // The filters are left as they were. They are what the world is drawn
    // from, and opening the picker is not asking for that to change -- the
    // reset chip is there for when it is.
    private bool OpenPicker(IPlayer player)
    {
        if (!UseHud(player.SteamID) || !_hud!.Available(HudSlots.Pick))
        {
            return false;
        }

        MenuFor(player.SteamID).Page = 0;

        return RenderPicker(player);
    }

    private void ClosePicker(IPlayer player)
    {
        _hud?.Hide(player, HudSlots.Pick);
    }

    private bool RenderPicker(IPlayer player)
    {
        // Explicit rather than null-forgiving: an NRE here crosses back into
        // native SwiftlyS2 and takes the server down instead of throwing.
        if (_hud == null)
        {
            return false;
        }

        ulong steamId = player.SteamID;
        MenuState menu = MenuFor(steamId);
        List<LineupRecord> rows = MenuRows(steamId);
        int pages = Math.Max(1, (rows.Count + HudSlots.ListRows - 1) / HudSlots.ListRows);

        menu.Page = Math.Clamp(menu.Page, 0, pages - 1);

        RadarCalibration? calibration = Calibration();
        string? selected = _target.GetValueOrDefault(steamId);
        LineupRecord? loaded = _system.StateFor(steamId).Loaded;
        Vec3? at = PracticeSystem.Where(player)?.feet_position;

        var template = new NadePickTemplate
        {
            Title = Narrowing(steamId) ?? "Pick a lineup",
            Tag = Tag(steamId, rows.Count),
            Page = $"page {menu.Page + 1} / {pages}",
            Side = menu.Side,
            Type = menu.Type,
            Scope = menu.Scope,
            HasPager = pages > 1,
            Here = _reachableOnly.Contains(steamId),
            Filtering = Filtering(steamId) || _reachableOnly.Contains(steamId),
            MapClass = calibration == null
                ? null
                : RadarProjection.NormalizeMapName(_library.Map),
            Focus = selected == null
                ? "click a spot for the throws that land there"
                : "click it again to let go of it",
        };

        foreach (LineupRecord lineup in rows.Skip(menu.Page * HudSlots.ListRows).Take(HudSlots.ListRows))
        {
            template.Add(
                new NadePickTemplate.Row(
                    $"lineup:{Key(lineup)}",
                    PracticeLineupUtility.TitleCase(lineup.name),
                    RowValue(lineup, at),
                    loaded != null && Key(loaded) == Key(lineup)
                )
            );
        }

        if (calibration == null)
        {
            return _hud.Show(player, template);
        }

        // What the player can actually throw from where they are standing. This
        // is the search: walk somewhere and the map shows what is reachable.
        HashSet<string> reachable = at == null
            ? new HashSet<string>()
            : PracticeReplay
                .SpotAt(_library.For(steamId), at.Value)
                .Select(UtilityTargetCluster.Key)
                .ToHashSet(StringComparer.Ordinal);

        foreach (UtilityTarget target in Targets(steamId))
        {
            (int column, int row) = RadarProjection.Cell(
                target.Landing,
                calibration,
                HudSlots.MapGrid,
                HudSlots.MapGrid
            );

            // The place, not somebody's name for one throw onto it. Falls back
            // to the lineup name where the level defines no callout there.
            string label = _callouts.Label(target.Landing);

            template.Add(
                new NadePickTemplate.Marker(
                    target.Id,
                    label.Length > 0 ? label : PracticeLineupUtility.TitleCase(target.Name),
                    column,
                    row,
                    RadarMaps.TypeClass(target.UtilityType),
                    target.Count,
                    target.Lineups.Any(l => reachable.Contains(UtilityTargetCluster.Key(l))),
                    loaded != null && target.Lineups.Any(l => Key(l) == Key(loaded)),
                    target.Id == selected
                )
            );
        }

        return _hud.Show(player, template);
    }

    // Names whatever is currently narrowing the list. An empty list with no
    // explanation reads as a broken panel, which is the failure mode this whole
    // feature set keeps running into.
    private string? Narrowing(ulong steamId)
    {
        if (_target.TryGetValue(steamId, out string? target))
        {
            UtilityTarget? chosen = Targets(steamId).FirstOrDefault(t => t.Id == target);

            if (chosen != null)
            {
                return PracticeLineupUtility.TitleCase(chosen.Name);
            }
        }

        // Only while it is actually narrowing anything. A player who is dead or
        // between pawns has nowhere to be throwing from, so the filter stands
        // down -- and a full list under a heading that says otherwise reads as
        // broken just as surely as an empty one with no heading at all.
        return _reachableOnly.Contains(steamId) && Standing(steamId) != null
            ? "Throwable from here"
            : null;
    }

    private Vec3? Standing(ulong steamId)
    {
        IPlayer? player = _system.Find(steamId);

        return player == null ? null : PracticeSystem.Where(player)?.feet_position;
    }

    private string Tag(ulong steamId, int shown)
    {
        int total = _library.For(steamId).Count;
        string scope = shown == total ? $"{total}" : $"{shown} of {total}";

        return $"{_library.Map} · {scope} lineup{(total == 1 ? "" : "s")}";
    }

    private void ClearFilters(ulong steamId)
    {
        _target.Remove(steamId);
        _reachableOnly.Remove(steamId);
    }

    // Distance to the THROW spot, not the landing spot: once a destination is
    // chosen the question becomes which of these you can get to, which is the
    // "from" half of the pair.
    // Once a destination is chosen every row lands in the same place, so what
    // separates them is where they are thrown FROM -- the other half of the
    // destination x origin pair the panel names lineups by.
    private string RowValue(LineupRecord lineup, Vec3? at)
    {
        string from = _callouts.Label(lineup.release.feet_position);

        if (at == null)
        {
            return from.Length > 0 ? from : lineup.utility_type;
        }

        float away = (lineup.release.feet_position - at.Value).Length();
        string distance = away < 120f ? "HERE" : PracticeLineupUtility.Metres(away);

        return from.Length > 0 ? $"{from} · {distance}" : distance;
    }

    private void ForgetHud(ulong steamId, int playerId)
    {
        _hudOff.Remove(steamId);
        _menus.Remove(steamId);
        _shown.Remove(steamId);
        _edits.Remove(steamId);
        ForgetBar(steamId);
        _wheelPath.Remove(steamId);
        _wheelReturn.Remove(steamId);
        _mapChoices.Remove(steamId);
        ForgetVerdicts(steamId);
        ClearFilters(steamId);
        _hud?.Forget(playerId);
    }

    [Command("menu", registerRaw: false, permission: "")]
    public void OnMenu(ICommandContext context)
    {
        TogglePicker(context);
    }

    // The map and the list are one panel now, so both words open it.
    [Command("map", registerRaw: false, permission: "")]
    public void OnMap(ICommandContext context)
    {
        TogglePicker(context);
    }

    private void TogglePicker(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (_hud != null && _hud.IsOpen(player, HudSlots.Pick))
        {
            ClosePicker(player);

            return;
        }

        if (OpenPicker(player))
        {
            return;
        }

        if (_hudOff.Contains(player.SteamID))
        {
            Reply(
                context,
                $" {ChatColors.Red}you turned the panel off. "
                    + $"{ChatColors.Default}.hud{ChatColors.Grey} turns it back on"
            );

            return;
        }

        Reply(
            context,
            $" {ChatColors.Red}the lineup picker needs the 5stack HUD addon. "
                + $"{ChatColors.Grey}use {ChatColors.Default}.list{ChatColors.Grey} instead"
        );
    }

    [Command("here", registerRaw: false, permission: "")]
    public void OnHere(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        bool on = _reachableOnly.Add(player.SteamID);

        if (!on)
        {
            _reachableOnly.Remove(player.SteamID);
        }

        MenuFor(player.SteamID).Page = 0;

        int matches = MenuRows(player.SteamID).Count;

        if (!UseHud(player.SteamID) || !RenderPicker(player))
        {
            Reply(
                context,
                on
                    ? $" {ChatColors.Green}{matches} lineup{(matches == 1 ? "" : "s")} from this spot"
                    : $" {ChatColors.Grey}showing every lineup again"
            );
        }
    }

    [Command("hud", registerRaw: false, permission: "")]
    public void OnHud(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (_hud == null)
        {
            Reply(
                context,
                $" {ChatColors.Grey}this server's SwiftlyS2 has no custom hud support; "
                    + "panels are on centre text"
            );

            return;
        }

        bool off = !_hudOff.Add(player.SteamID);

        if (off)
        {
            _hudOff.Remove(player.SteamID);
        }
        else
        {
            HudOff(player);
        }

        Reply(
            context,
            off
                ? $" {ChatColors.Green}hud panel ON {ChatColors.Grey}(.hud again to turn it off)"
                : $" {ChatColors.Grey}hud panel OFF, back to centre text "
                    + $"({ChatColors.Default}.hud{ChatColors.Grey} again to turn it on)"
        );
    }
}
