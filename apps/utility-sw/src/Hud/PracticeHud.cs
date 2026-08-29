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

    private bool OpenMap(IPlayer player)
    {
        if (!UseHud(player.SteamID) || !_hud!.Available(HudSlots.Map))
        {
            return false;
        }

        return RenderMap(player);
    }

    private void CloseMap(IPlayer player)
    {
        _hud?.Hide(player, HudSlots.Map);
    }

    private bool RenderMap(IPlayer player)
    {
        RadarCalibration? calibration = Calibration();

        if (calibration == null || _hud == null)
        {
            return false;
        }

        List<UtilityTarget> targets = Targets(player.SteamID);
        LineupRecord? loaded = _system.StateFor(player.SteamID).Loaded;
        string? selected = _target.GetValueOrDefault(player.SteamID);

        // What the player can actually throw from where they are standing. This
        // is the search: walk somewhere and the map shows what is reachable.
        Vec3? at = PracticeSystem.Where(player)?.feet_position;
        HashSet<string> reachable = at == null
            ? new HashSet<string>()
            : PracticeReplay
                .SpotAt(_library.For(player.SteamID), at.Value)
                .Select(UtilityTargetCluster.Key)
                .ToHashSet(StringComparer.Ordinal);

        string map = RadarProjection.NormalizeMapName(_library.Map);

        var template = new NadeMapTemplate
        {
            MapClass = map,
            Title = PracticeLineupUtility.TitleCase(map.Replace("de_", "")),
            Tag = $"{targets.Count} target{(targets.Count == 1 ? "" : "s")}",
            Focus = selected == null ? "click a marker" : "",
            Selected = Detail(targets, selected),
        };

        foreach (UtilityTarget target in targets)
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
                new NadeMapTemplate.Marker(
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

    private NadeMapTemplate.Detail? Detail(List<UtilityTarget> targets, string? selected)
    {
        UtilityTarget? target = targets.FirstOrDefault(candidate => candidate.Id == selected);

        if (target == null)
        {
            return null;
        }

        string place = _callouts.Label(target.Landing);

        // The second button only earns its place when there is more than one way
        // to hit the spot.
        return new NadeMapTemplate.Detail(
            place.Length > 0 ? place : PracticeLineupUtility.TitleCase(target.Name),
            $"{target.UtilityType} · {target.Count} lineup{(target.Count == 1 ? "" : "s")}",
            "Load",
            target.Count > 1 ? $"All {target.Count}" : null
        );
    }



    // Twice a second. Fast enough that the reachable ring keeps up with a
    // player walking onto a spot, slow enough to be free.
    private const int MapRefreshEveryTicks = 32;

    // Where the panel currently believes the player is, which lags the raw test
    // on purpose. About a third of a second at 64 tick: long enough that
    // shuffling on the edge of the circle does not throw the panel across the
    // screen, short enough that a deliberate step onto it feels immediate.
    private const int SpotSettleTicks = 22;

    private readonly Dictionary<ulong, HudSteady> _onSpotSteady = new();

    private bool SteadyOnSpot(ulong steamId, bool onSpot)
    {
        HudSteady steady = _onSpotSteady.TryGetValue(steamId, out HudSteady found)
            ? found
            : HudSteady.Start(onSpot);

        steady = steady.Read(onSpot, _aimTick, SpotSettleTicks);
        _onSpotSteady[steamId] = steady;

        return steady.Committed;
    }

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

        if (lineup == null)
        {
            _hud!.Hide(player, HudSlots.Hud);

            return true;
        }

        // Walking pace, not aiming pace. The surface diffs writes, so a redraw
        // that changes nothing costs nothing.
        if (_aimTick % MapRefreshEveryTicks == 0 && _hud!.IsOpen(player, HudSlots.Map))
        {
            RenderMap(player);
        }

        // Every other tick of the aim job: the clock reads in tenths, so it has
        // to keep up, but nothing else on it moves fast.
        if (_aimTick % 8 == 0)
        {
            HudRun(player);
        }

        QAngle eyes = pawn.EyeAngles;
        (string Label, int Tenths)? drill = _drill.HudProgress(player.SteamID);

        // ThrowColor encodes "step beats cycle"; re-deriving it here is how that
        // rule drifts. Off means off -- .colors hides it everywhere, not just in
        // the world.
        string? colour = _system.StateFor(player.SteamID).Colors
            ? ThrowColor(player.SteamID).Name.ToUpperInvariant()
            : null;

        PracticeState walking = _system.StateFor(player.SteamID);
        string position =
            walking.Index >= 0 && walking.Results.Count > 1
                ? $"{walking.Index + 1} / {walking.Results.Count}"
                : "";

        string lands = _callouts.Label(lineup.detonation_position);

        return _hud!.Show(
            player,
            NadeHudTemplate.For(
                lineup,
                eyes.Y,
                eyes.X,
                ToleranceFor(lineup),
                colour,
                onSpot,
                // Docked the moment they commit. A drill never leaves the right
                // hand side, and a load arrives there rather than sliding to it.
                _drill.Current(player.SteamID) != null
                    || _system.StateFor(player.SteamID).Loaded != null
                    || SteadyOnSpot(player.SteamID, onSpot),
                onAngle,
                drill?.Label,
                drill?.Tenths ?? 0,
                position,
                lands
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

        if (layout == HudSlots.NadeMap)
        {
            OnMapClicked(player, button);

            return;
        }

        if (layout != HudSlots.NadeList)
        {
            return;
        }

        MenuState menu = MenuFor(player.SteamID);

        if (button == "close")
        {
            CloseMenu(player);

            return;
        }

        if (button.StartsWith("side_", StringComparison.Ordinal))
        {
            menu.Side = button["side_".Length..];
            menu.Page = 0;
            RenderMenu(player);

            return;
        }

        if (button.StartsWith("scope_", StringComparison.Ordinal))
        {
            menu.Scope = button["scope_".Length..];
            menu.Page = 0;
            RenderMenu(player);

            return;
        }

        if (button.StartsWith("type_", StringComparison.Ordinal))
        {
            menu.Type = button["type_".Length..];
            menu.Page = 0;
            RenderMenu(player);

            return;
        }


        if (button is "next" or "prev")
        {
            List<LineupRecord> rows = MenuRows(player.SteamID);
            int pages = Math.Max(1, (rows.Count + HudSlots.ListRows - 1) / HudSlots.ListRows);

            menu.Page = ((menu.Page + (button == "next" ? 1 : -1)) % pages + pages) % pages;
            RenderMenu(player);

            return;
        }

        if (button.StartsWith("lineup:", StringComparison.Ordinal))
        {
            string key = button["lineup:".Length..];
            LineupRecord? chosen = _library
                .For(player.SteamID)
                .FirstOrDefault(lineup => Key(lineup) == key);

            if (chosen == null)
            {
                Tell(player.SteamID, $" {ChatColors.Red}that lineup is no longer in your library");

                return;
            }

            PracticeState state = _system.StateFor(player.SteamID);
            List<LineupRecord> rows = MenuRows(player.SteamID);

            // The menu the player was reading IS the result set .next and .prev
            // should walk, so it replaces whatever an earlier query left behind.
            state.Results.Clear();
            state.Results.AddRange(rows);
            state.Index = rows.FindIndex(row => Key(row) == key);

            CloseMenu(player);
            Apply(player, chosen);
        }
    }

    private void OnMapClicked(IPlayer player, string button)
    {
        if (button == "close")
        {
            CloseMap(player);

            return;
        }

        if (button is "load" or "list")
        {
            OnDetailClicked(player, button);

            return;
        }

        if (!button.StartsWith("target:", StringComparison.Ordinal))
        {
            return;
        }

        _target[player.SteamID] = button["target:".Length..];
        MenuFor(player.SteamID).Page = 0;

        // Answers in place. Opening the list on every marker click made picking
        // a spot feel like leaving the map.
        RenderMap(player);
    }

    private void OnDetailClicked(IPlayer player, string button)
    {
        if (!_target.TryGetValue(player.SteamID, out string? id))
        {
            return;
        }

        UtilityTarget? target = Targets(player.SteamID).FirstOrDefault(t => t.Id == id);

        if (target == null)
        {
            return;
        }

        if (button == "list")
        {
            RenderMenu(player);

            return;
        }

        // Nearest throw spot, so LOAD on a target with six lineups gives the one
        // they can walk to rather than the first one recorded.
        Vec3? at = PracticeSystem.Where(player)?.feet_position;
        LineupRecord chosen = at == null
            ? target.Lineups[0]
            : target
                .Lineups.OrderBy(l => (l.release.feet_position - at.Value).Length())
                .First();

        PracticeState state = _system.StateFor(player.SteamID);

        state.Results.Clear();
        state.Results.AddRange(target.Lineups);
        state.Index = target.Lineups.FindIndex(l => Key(l) == Key(chosen));

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

    private List<LineupRecord> MenuRows(ulong steamId)
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

        IPlayer? player = _system.Find(steamId);
        Vec3? standing = player == null ? null : PracticeSystem.Where(player)?.feet_position;

        if (_reachableOnly.Contains(steamId) && standing != null)
        {
            var here = PracticeReplay
                .SpotAt(_library.For(steamId), standing.Value)
                .Select(UtilityTargetCluster.Key)
                .ToHashSet(StringComparer.Ordinal);

            lineups = lineups.Where(l => here.Contains(UtilityTargetCluster.Key(l)));
        }

        List<LineupRecord> matched = lineups
            .Where(lineup =>
                menu.Type == "all"
                || string.Equals(
                    lineup.utility_type,
                    TypeFor[menu.Type],
                    StringComparison.OrdinalIgnoreCase
                )
            )
            .ToList();

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

    // Opened straight from .menu rather than off the map, so it shows the whole
    // library again instead of whatever target was last picked.
    private bool OpenMenu(IPlayer player)
    {
        if (!UseHud(player.SteamID) || !_hud!.Available(HudSlots.List))
        {
            return false;
        }

        ClearFilters(player.SteamID);
        MenuFor(player.SteamID).Page = 0;

        return RenderMenu(player);
    }

    private void CloseMenu(IPlayer player)
    {
        _hud?.Hide(player, HudSlots.List);
    }

    private bool RenderMenu(IPlayer player)
    {
        MenuState menu = MenuFor(player.SteamID);
        List<LineupRecord> rows = MenuRows(player.SteamID);
        int pages = Math.Max(1, (rows.Count + HudSlots.ListRows - 1) / HudSlots.ListRows);

        menu.Page = Math.Clamp(menu.Page, 0, pages - 1);

        var template = new NadeListTemplate
        {
            Title = Narrowing(player.SteamID) ?? "Load a lineup",
            Tag = Tag(player.SteamID, rows.Count),
            Page = $"page {menu.Page + 1} / {pages}",
            Side = menu.Side,
            Type = menu.Type,
            Scope = menu.Scope,
            HasPager = pages > 1,
        };

        LineupRecord? loaded = _system.StateFor(player.SteamID).Loaded;
        Vec3? at = PracticeSystem.Where(player)?.feet_position;

        foreach (LineupRecord lineup in rows.Skip(menu.Page * HudSlots.ListRows).Take(HudSlots.ListRows))
        {
            template.Add(
                new NadeListTemplate.Row(
                    $"lineup:{Key(lineup)}",
                    PracticeLineupUtility.TitleCase(lineup.name),
                    RowValue(lineup, at),
                    loaded != null && Key(loaded) == Key(lineup)
                )
            );
        }

        // Explicit rather than null-forgiving: an NRE here crosses back into
        // native SwiftlyS2 and takes the server down instead of throwing.
        return _hud != null && _hud.Show(player, template);
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

        return _reachableOnly.Contains(steamId) ? "Throwable from here" : null;
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
        _edits.Remove(steamId);
        _onSpotSteady.Remove(steamId);
        ClearFilters(steamId);
        _hud?.Forget(playerId);
    }

    [Command("menu", registerRaw: false, permission: "")]
    public void OnMenu(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (_hud != null && _hud.IsOpen(player, HudSlots.List))
        {
            CloseMenu(player);

            return;
        }

        if (OpenMenu(player))
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
            $" {ChatColors.Red}the lineup menu needs the 5stack HUD addon. "
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

        if (!UseHud(player.SteamID) || !RenderMenu(player))
        {
            Reply(
                context,
                on
                    ? $" {ChatColors.Green}{matches} lineup{(matches == 1 ? "" : "s")} from this spot"
                    : $" {ChatColors.Grey}showing every lineup again"
            );
        }
    }

    [Command("map", registerRaw: false, permission: "")]
    public void OnMap(ICommandContext context)
    {
        IPlayer? player = context.Sender;

        if (player == null || !player.IsValid)
        {
            return;
        }

        if (_hud != null && _hud.IsOpen(player, HudSlots.Map))
        {
            CloseMap(player);

            return;
        }

        if (OpenMap(player))
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

        if (!RadarMaps.Has(_library.Map))
        {
            Reply(context, $" {ChatColors.Red}no radar for {_library.Map}");

            return;
        }

        // Distinguished rather than lumped together: the last thing that spoke
        // gets blamed, and "needs the addon" sent someone hunting a mount that
        // was fine when the real fault was a missing file server-side.
        if (Calibration() == null)
        {
            _logger.LogWarning(
                "no radar calibration for {Map}; radars/metadata.json is missing beside the plugin",
                _library.Map
            );

            Reply(
                context,
                $" {ChatColors.Red}the server has no radar calibration for {_library.Map} "
                    + $"{ChatColors.Grey}(server problem, not yours)"
            );

            return;
        }

        Reply(
            context,
            $" {ChatColors.Red}the map needs the 5stack HUD addon. "
                + $"{ChatColors.Grey}use {ChatColors.Default}.menu{ChatColors.Grey} instead"
        );
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
            _hud?.Hide(player, HudSlots.Hud);
            CloseMenu(player);
            CloseMap(player);
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
