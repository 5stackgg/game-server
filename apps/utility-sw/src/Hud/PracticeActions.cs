using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.Players;
using SwiftlyS2.Shared.SchemaDefinitions;
using static SwiftlyS2.Shared.Helper;

namespace UtilityPractice;

// Everything that used to be a chat command, as something to click. The
// commands still work; these call the same services and answer on the panel
// instead of in chat.
//
// The menu is one wheel of eight wedges. A wedge either does something, and
// the menu is gone, or it opens another wheel in the same place.
public partial class UtilityPracticePlugin
{
    private record WheelItem(
        string Id,
        string Label,
        string Sub = "",
        string? Opens = null,
        bool On = false,
        bool Picked = false,
        bool Disabled = false
    )
    {
        // Up to two things worth one click without opening what is behind the
        // wedge, drawn in the outer ring over it. Clockwise, so on the bottom
        // half of the wheel the first is the one on the right.
        public IReadOnlyList<WheelItem?> Quick { get; init; } = Array.Empty<WheelItem?>();
    }

    private record WheelPage(string Title, IReadOnlyList<WheelItem?> Items);

    // Which wheel each player is on, as the trail of wheels opened to get
    // there. Empty is the first one.
    private readonly Dictionary<ulong, List<string>> _wheelPath = new();

    // Practice and the execute wheel are where a session is spent, so the menu
    // opens back onto them. Anything else -- a confirm, a list to pick from --
    // was a step on the way somewhere, and starts again from the top.
    private static readonly HashSet<string> WheelReturnPages = new() { "practice", "execute" };
    private readonly Dictionary<ulong, List<string>> _wheelReturn = new();

    private static string Switch(bool on)
    {
        return on ? "On" : "Off";
    }

    private static WheelItem Toggle(string id, string label, bool on, bool disabled = false)
    {
        return new WheelItem(id, label, Switch(on), On: on, Disabled: disabled);
    }

    private string Passing(ulong steamId)
    {
        int total = _library.For(steamId).Count;

        return Filtering(steamId) ? $"{Shown(steamId).Count} of {total}" : $"{total}";
    }

    private WheelPage WheelFor(IPlayer player, string page)
    {
        ulong steamId = player.SteamID;
        PracticeState state = _system.StateFor(steamId);
        MenuState menu = MenuFor(steamId);

        switch (page)
        {
            case "filters":
                return new WheelPage(
                    "Filters",
                    new WheelItem?[]
                    {
                        new WheelItem("type", "Type", menu.Type, Opens: "type", Picked: menu.Type != "all"),
                        new WheelItem("side", "Side", menu.Side, Opens: "side", Picked: menu.Side != "all"),
                        new WheelItem("who", "Whose", menu.Scope, Opens: "who", Picked: menu.Scope != "all"),
                        new WheelItem(
                            "reset",
                            "Reset Filters",
                            Filtering(steamId) ? "back to all" : "nothing set",
                            Disabled: !Filtering(steamId)
                        ),
                        new WheelItem(
                            "next",
                            "Next Lineup",
                            state.Results.Count > 1 && state.Index >= 0
                                ? $"{state.Index + 1} / {state.Results.Count}"
                                : "",
                            Disabled: state.Results.Count == 0
                        ),
                        new WheelItem("prev", "Previous", Disabled: state.Results.Count == 0),
                    }
                );

            case "type":
                return new WheelPage(
                    "Type",
                    new WheelItem?[]
                    {
                        new WheelItem("type:all", "All", Picked: menu.Type == "all"),
                        new WheelItem("type:smoke", "Smoke", Picked: menu.Type == "smoke"),
                        new WheelItem("type:flash", "Flash", Picked: menu.Type == "flash"),
                        new WheelItem("type:molly", "Molotov", Picked: menu.Type == "molly"),
                        new WheelItem("type:he", "HE", Picked: menu.Type == "he"),
                    }
                );

            case "side":
                return new WheelPage(
                    "Side",
                    new WheelItem?[]
                    {
                        new WheelItem("side:all", "Both", Picked: menu.Side == "all"),
                        new WheelItem("side:t", "T Side", Picked: menu.Side == "t"),
                        new WheelItem("side:ct", "CT Side", Picked: menu.Side == "ct"),
                    }
                );

            case "who":
                return new WheelPage(
                    "Whose",
                    new WheelItem?[]
                    {
                        new WheelItem("who:all", "Everyone's", Picked: menu.Scope == "all"),
                        new WheelItem("who:mine", "Mine", Picked: menu.Scope == "mine"),
                        new WheelItem("who:team", "My Team's", Picked: menu.Scope == "team"),
                        new WheelItem("who:public", "Public", Picked: menu.Scope == "public"),
                    }
                );

            case "practice":
                bool drilling = _drill.Current(steamId) != null;
                bool planning = _drill.Plan(steamId) != null;
                int stacked = StackCount(steamId);
                LineupRecord? repeatable = drilling ? null : DrillTarget(player);

                return new WheelPage(
                    "Practice",
                    new WheelItem?[]
                    {
                        new WheelItem(
                            "drill",
                            drilling && !planning ? "Stop Repeating" : "Repeat Lineup",
                            drilling && !planning ? "running"
                                : repeatable != null
                                    ? PracticeLineupUtility.TitleCase(DrillUtility.Name(repeatable))
                                : "stand on one first",
                            On: drilling && !planning,
                            Disabled: planning || (!drilling && repeatable == null)
                        ),
                        new WheelItem(
                            "plan",
                            planning ? "Stop Drilling" : "Drill Unmastered",
                            planning ? "running" : "worst first",
                            On: planning,
                            Disabled: drilling && !planning
                        )
                        {
                            Quick = new WheelItem?[]
                            {
                                new WheelItem("skip", "Skip This One", Disabled: !drilling),
                            },
                        },
                        new WheelItem(
                            "execute",
                            "Execute",
                            stacked == 0 ? "stack is empty" : $"{stacked} stacked",
                            Opens: "execute"
                        ),
                        Toggle("bloom", "Smoke Outline", state.Bloom, state.Loaded == null || !_config.GhostPreview),
                        new WheelItem("maps", "Change Map", MapLabel(_library.Map), Opens: "maps"),
                        new WheelItem(
                            "bot",
                            "Place A Bot",
                            _bots.Count == 0 ? "where I stand" : $"{_bots.Count} of {MaxBots}",
                            Disabled: BotSlotsLeft() == 0
                        ),
                        new WheelItem(
                            "nobots",
                            "Remove Bots",
                            _bots.Count == 0 ? "none placed" : $"{_bots.Count}",
                            Disabled: _bots.Count == 0
                        ),
                        Toggle("spawns", "Spawn Points", _replay.SpawnsShown) with
                        {
                            Quick = new WheelItem?[]
                            {
                                new WheelItem("spawnprev", "Prev Spawn"),
                                new WheelItem("spawnnext", "Next Spawn"),
                            },
                        },
                    }
                );

            case "delete":
                LineupRecord? doomed = state.Loaded;

                return new WheelPage(
                    "Delete",
                    new WheelItem?[]
                    {
                        new WheelItem(
                            "delete!",
                            "Delete It",
                            doomed == null ? "nothing loaded" : DrillUtility.Name(doomed),
                            Disabled: doomed == null
                        ),
                    }
                );

            case "execute":
                int inStack = StackCount(steamId);
                int mine = StackMine(steamId);

                return new WheelPage(
                    "Execute",
                    new WheelItem?[]
                    {
                        new WheelItem(
                            "stackall",
                            "Rethrow All",
                            inStack == 0 ? "stack is empty" : $"{inStack} throws",
                            Disabled: inStack == 0 || !_config.ReplayEnabled
                        ),
                        new WheelItem(
                            "stack",
                            "Add To Stack",
                            "what R would throw",
                            Disabled: RethrowTarget(steamId) == null
                        ),
                        new WheelItem(
                            "timing",
                            "Timing",
                            _stackAsThrown.Contains(steamId) ? "as thrown" : "together"
                        ),
                        new WheelItem(
                            "mine",
                            "I Throw",
                            mine < 0 ? "none" : $"number {mine + 1}",
                            On: mine >= 0,
                            Disabled: inStack == 0
                        ),
                        new WheelItem("unstack", "Remove Last", Disabled: inStack == 0),
                        new WheelItem("nostack", "Empty Stack", Disabled: inStack == 0),
                        new WheelItem(
                            "playbook",
                            _playbook.Running ? "Stop Execute" : "Run Execute",
                            _playbook.Loaded?.name ?? "none sent from the website",
                            On: _playbook.Running,
                            Disabled: _playbook.Loaded == null
                        ),
                        new WheelItem("executes", "Load Execute", "from the website", Opens: "executes"),
                    }
                );

            case "maps":
                return MapsPage(player);

            case "executes":
                List<UtilityPlaybook>? books = ExecutesFor(player);

                if (books == null)
                {
                    return new WheelPage(
                        "Executes",
                        new WheelItem?[] { new WheelItem("none", "Loading", "asking the website", Disabled: true) }
                    );
                }

                if (books.Count == 0)
                {
                    return new WheelPage(
                        "Executes",
                        new WheelItem?[]
                        {
                            new WheelItem("none", "No Executes", "build one on the website", Disabled: true),
                        }
                    );
                }

                return new WheelPage(
                    "Executes",
                    books
                        .Take(HudSlots.WheelSlots)
                        .Select(
                            (book, at) =>
                                (WheelItem?)
                                    new WheelItem(
                                        $"exec:{at}",
                                        book.name ?? "Execute",
                                        $"{book.steps.Count} throws"
                                    )
                        )
                        .ToArray()
                );

            case "settings":
                bool hidden = _replay.IsHidden(steamId);

                return new WheelPage(
                    "Settings",
                    new WheelItem?[]
                    {
                        Toggle("god", "No Damage", state.God),
                        new WheelItem("markers", "World Markers", hidden ? "cleared" : "shown", On: !hidden),
                        Toggle("crosshair", "Aim Marker", state.Crosshair),
                        Toggle("colors", "Throw Colours", state.Colors),
                        Toggle("compass", "Compass", !_compassOff.Contains(steamId)) with
                        {
                            Quick = new WheelItem?[]
                            {
                                new WheelItem("out", "Utility Out", On: !_outOff.Contains(steamId)),
                                new WheelItem("flashcard", "Flash Card", On: !_flashCardOff.Contains(steamId)),
                            },
                        },
                        Toggle("legend", "Key Legend", !_legendOff.Contains(steamId)) with
                        {
                            Quick = new WheelItem?[]
                            {
                                new WheelItem("sounds", "Sounds", On: !_soundsOff.Contains(steamId)),
                            },
                        },
                        Toggle("solo", "Private Previews", state.Solo),
                        new WheelItem("reload", "Reload Library", $"{_library.For(steamId).Count} lineups"),
                    }
                );
        }

        LineupRecord? again = RethrowTarget(steamId);
        bool thrown = _recorder.LastThrow(steamId) != null;

        return new WheelPage(
            "Menu",
            new WheelItem?[]
            {
                new WheelItem(
                    "rethrow",
                    StackCount(steamId) > 1 ? "Rethrow All" : "Rethrow",
                    StackCount(steamId) > 1 ? $"R · {StackCount(steamId)} stacked" : "R",
                    Disabled: again == null || !again.IsExactlyReplayable() || !_config.ReplayEnabled
                )
                {
                    Quick = new WheelItem?[]
                    {
                        new WheelItem("stack", "Add To Stack", Disabled: again == null),
                        new WheelItem("rethrow1", "Last Only", Disabled: StackCount(steamId) < 2),
                    },
                },
                new WheelItem("sweep", "Clear Utility", "smoke + fire")
                {
                    Quick = new WheelItem?[]
                    {
                        new WheelItem("nobots", "Clear Bots", Disabled: _bots.Count == 0),
                        new WheelItem("markers", "Markers", On: !_replay.IsHidden(steamId)),
                    },
                },
                new WheelItem("last", "Last Throw", "back to its spot", Disabled: !thrown)
                {
                    Quick = new WheelItem?[]
                    {
                        new WheelItem("earlier", "Earlier", Disabled: !HasEarlier(steamId)),
                        new WheelItem("landing", "Landing", Disabled: state.Loaded == null),
                    },
                },
                new WheelItem("save", "Save Throw", "keep the last one", Disabled: !thrown)
                {
                    Quick = new WheelItem?[]
                    {
                        new WheelItem("edit", "Edit", Disabled: state.Loaded == null),
                        new WheelItem("delete", "Delete", Opens: "delete", Disabled: state.Loaded == null),
                    },
                },
                new WheelItem("lineups", "Lineups", Passing(steamId))
                {
                    Quick = new WheelItem?[]
                    {
                        new WheelItem("next", "Next", Disabled: state.Results.Count == 0),
                        new WheelItem("prev", "Prev", Disabled: state.Results.Count == 0),
                    },
                },
                new WheelItem(
                    "filters",
                    "Filters",
                    Filtering(steamId) ? "set" : "none",
                    Opens: "filters",
                    Picked: Filtering(steamId)
                )
                {
                    Quick = new WheelItem?[]
                    {
                        new WheelItem("reset", "Reset", Disabled: !Filtering(steamId)),
                    },
                },
                new WheelItem(
                    "practice",
                    "Practice",
                    _drill.Current(steamId) != null ? "drilling" : "drill · bots",
                    Opens: "practice"
                )
                {
                    Quick = new WheelItem?[]
                    {
                        new WheelItem("execute", "Execute", Opens: "execute", On: StackCount(steamId) > 0),
                        new WheelItem(
                            "plan",
                            _drill.Plan(steamId) != null ? "Stop Drilling" : "Unmastered",
                            On: _drill.Plan(steamId) != null
                        ),
                    },
                },
                new WheelItem("settings", "Settings", "view · cheats", Opens: "settings")
                {
                    Quick = new WheelItem?[]
                    {
                        new WheelItem("spawns", "Spawns", On: _replay.SpawnsShown),
                        new WheelItem("god", "No Damage", On: state.God),
                    },
                },
            }
        );
    }

    private List<string> WheelPath(ulong steamId)
    {
        if (!_wheelPath.TryGetValue(steamId, out List<string>? path))
        {
            path = new List<string>();
            _wheelPath[steamId] = path;
        }

        return path;
    }

    private bool OpenWheel(IPlayer player)
    {
        if (!UseHud(player.SteamID) || !_hud!.Available(HudSlots.Wheel))
        {
            return false;
        }

        List<string> path = WheelPath(player.SteamID);

        path.Clear();

        if (_wheelReturn.Remove(player.SteamID, out List<string>? back))
        {
            path.AddRange(back);
        }

        return RenderWheel(player);
    }

    private void CloseWheel(IPlayer player)
    {
        ulong steamId = player.SteamID;

        if (_wheelPath.Remove(steamId, out List<string>? path))
        {
            if (path.Count > 0 && WheelReturnPages.Contains(path[^1]))
            {
                _wheelReturn[steamId] = path;
            }
            else
            {
                _wheelReturn.Remove(steamId);
            }
        }

        _hud?.Hide(player, HudSlots.Wheel);
    }

    private bool RenderWheel(IPlayer player)
    {
        if (_hud == null)
        {
            return false;
        }

        List<string> path = WheelPath(player.SteamID);
        WheelPage page = WheelFor(player, path.Count == 0 ? "root" : path[^1]);

        return _hud.Show(
            player,
            new NadeWheelTemplate
            {
                Title = page.Title,
                Hint = path.Count == 0 ? "Close" : "‹ Back",
                Wedges = page
                    .Items.Select(item =>
                        item == null
                            ? null
                            : new NadeWheelTemplate.Wedge(
                                item.Label,
                                item.Opens == null ? item.Sub : $"{item.Sub} ›".TrimStart(),
                                item.Opens != null,
                                item.On,
                                item.Picked,
                                item.Disabled
                            )
                    )
                    .ToList(),
                Quick = Enumerable
                    .Range(0, HudSlots.WheelQuicks)
                    .Select(index => Drawn(QuickAt(page, index)))
                    .ToList(),
                Wide = Enumerable
                    .Range(0, HudSlots.WheelSlots)
                    .Select(slot => Drawn(WideAt(page, slot)))
                    .ToList(),
            }
        );
    }

    private static NadeWheelTemplate.Wedge? Drawn(WheelItem? quick)
    {
        return quick == null
            ? null
            : new NadeWheelTemplate.Wedge(quick.Label, On: quick.On, Disabled: quick.Disabled);
    }

    private static IReadOnlyList<WheelItem> QuicksOf(WheelPage page, int slot)
    {
        if (slot < 0 || slot >= page.Items.Count)
        {
            return Array.Empty<WheelItem>();
        }

        return page.Items[slot]?.Quick.OfType<WheelItem>().ToList() ?? new List<WheelItem>();
    }

    // Two share the wedge between them. One on its own is not left sitting
    // in half of it: see WideAt.
    private static WheelItem? QuickAt(WheelPage page, int index)
    {
        IReadOnlyList<WheelItem> quick = QuicksOf(page, index / 2);

        return quick.Count > 1 && index % 2 < quick.Count ? quick[index % 2] : null;
    }

    private static WheelItem? WideAt(WheelPage page, int slot)
    {
        IReadOnlyList<WheelItem> quick = QuicksOf(page, slot);

        return quick.Count == 1 ? quick[0] : null;
    }

    private void OnWheelClicked(IPlayer player, string button)
    {
        ulong steamId = player.SteamID;

        if (button == "close")
        {
            CloseWheel(player);

            return;
        }

        List<string> path = WheelPath(steamId);

        // The middle: back one wheel, or out of the first.
        if (button == "hub")
        {
            if (path.Count == 0)
            {
                CloseWheel(player);

                return;
            }

            path.RemoveAt(path.Count - 1);
            RenderWheel(player);

            return;
        }

        string current = path.Count == 0 ? "root" : path[^1];
        WheelPage page = WheelFor(player, current);

        WheelItem? quick = null;
        bool outer = false;

        if (
            button.StartsWith("quick:", StringComparison.Ordinal)
            && int.TryParse(button.AsSpan("quick:".Length), out int index)
        )
        {
            quick = QuickAt(page, index);
            outer = true;
        }
        else if (
            button.StartsWith("wide:", StringComparison.Ordinal)
            && int.TryParse(button.AsSpan("wide:".Length), out int over)
        )
        {
            quick = WideAt(page, over);
            outer = true;
        }

        if (outer)
        {
            if (quick == null || quick.Disabled)
            {
                return;
            }

            if (quick.Opens != null)
            {
                path.Add(quick.Opens);
                RenderWheel(player);

                return;
            }

            PickFrom(player, current, quick.Id);

            return;
        }

        if (
            !button.StartsWith("slot:", StringComparison.Ordinal)
            || !int.TryParse(button.AsSpan("slot:".Length), out int slot)
        )
        {
            return;
        }

        WheelItem? item = slot >= 0 && slot < page.Items.Count ? page.Items[slot] : null;

        if (item == null || item.Disabled)
        {
            return;
        }

        if (item.Opens != null)
        {
            path.Add(item.Opens);
            RenderWheel(player);

            return;
        }

        PickFrom(player, current, item.Id);
    }

    // Settings are flipped several at a time and a filter is set a few ways
    // before looking, so those pages stay up until TAB or the middle. Anything
    // else is gone before the action runs: picking is the end of the menu, and
    // some of these open a panel of their own that wants the cursor.
    private void PickFrom(IPlayer player, string page, string id)
    {
        bool staysOpen = page switch
        {
            "settings" or "type" or "side" or "who" => true,
            "filters" => id == "reset",
            _ => false,
        };

        if (!staysOpen)
        {
            CloseWheel(player);
            Pick(player, id);

            return;
        }

        Pick(player, id);
        RenderWheel(player);
    }

    private void Pick(IPlayer player, string id)
    {
        ulong steamId = player.SteamID;
        MenuState menu = MenuFor(steamId);

        if (id == "lineups")
        {
            if (!OpenPicker(player))
            {
                Toast(player, "The lineup picker needs the 5stack HUD addon");
            }

            return;
        }

        if (id == "reset")
        {
            ResetFilters(player);
            Toast(player, $"All {_library.For(steamId).Count} lineups");

            return;
        }

        int split = id.IndexOf(':');

        if (split < 0)
        {
            Do(player, id);

            return;
        }

        string value = id[(split + 1)..];

        if (id[..split] == "map")
        {
            ChangeMapTo(player, value);

            return;
        }

        if (id[..split] == "exec")
        {
            if (int.TryParse(value, out int execute))
            {
                LoadExecute(player, execute);
            }

            return;
        }

        switch (id[..split])
        {
            case "type":
                menu.Type = value;

                break;
            case "side":
                menu.Side = value;

                break;
            case "who":
                menu.Scope = value;

                break;
            default:
                return;
        }

        menu.Page = 0;
        FiltersChanged(player);
        Toast(player, $"{Passing(steamId)} lineups");
    }

    private static void Flip(HashSet<ulong> off, ulong steamId)
    {
        if (!off.Add(steamId))
        {
            off.Remove(steamId);
        }
    }

    private void Do(IPlayer player, string action)
    {
        ulong steamId = player.SteamID;
        PracticeState state = _system.StateFor(steamId);

        switch (action)
        {
            case "rethrow":
                Rethrow(player);

                break;
            case "cycle":
                CCSPlayerPawn? pawn = player.PlayerPawn;

                if (pawn == null || !pawn.IsValid || !NextThrowHere(player, pawn))
                {
                    Toast(player, "No other throw from this spot");
                }

                break;
            case "rethrow1":
                RethrowOne(player);

                break;
            case "stack":
                StackAdd(player);

                break;
            case "stackall":
                if (StackCount(steamId) > 0)
                {
                    RethrowStack(player);
                }

                break;
            case "timing":
                Flip(_stackAsThrown, steamId);
                Toast(player, _stackAsThrown.Contains(steamId) ? "Timing · as thrown" : "Timing · together");

                break;
            case "mine":
                StackCycleMine(player);

                break;
            case "unstack":
                StackRemoveLast(player);

                break;
            case "nostack":
                StackEmpty(player);

                break;
            case "plan":
                TogglePlan(player);

                break;
            case "out":
                Flip(_outOff, steamId);

                break;
            case "flashcard":
                Flip(_flashCardOff, steamId);

                break;
            case "sweep":
                int cleared = ClearUtility();

                Toast(player, cleared > 0 ? "Smoke and fire cleared" : "Nothing in the world to clear");

                break;
            case "last":
                _earlier.Remove(steamId);
                BackTo(player, 0);

                break;
            case "earlier":
                int back = _earlier.TryGetValue(steamId, out int stepped) ? stepped + 1 : 1;

                if (BackTo(player, back))
                {
                    _earlier[steamId] = back;
                }

                break;
            case "landing":
                GoToLanding(player);

                break;
            case "edit":
                EditLoaded(player);

                break;
            case "delete!":
                DeleteLoaded(player);

                break;
            case "spawnnext":
                WalkSpawns(player, 1);

                break;
            case "spawnprev":
                WalkSpawns(player, -1);

                break;
            case "playbook":
                TogglePlaybook(player);

                break;
            case "save":
                SaveLast(player);

                break;
            case "next":
            case "prev":
                Walk(player, action == "next" ? 1 : -1);

                break;
            case "drill":
                ToggleDrill(player);

                break;
            case "skip":
                _drill.Skip(steamId);

                break;
            case "bloom":
                ToggleBloom(player);

                break;
            case "noclip":
                state.Noclip = !state.Noclip;
                _system.ApplyNow(player);

                break;
            case "god":
                state.God = !state.God;
                _system.ApplyNow(player);

                break;
            case "bot":
                if (BotSlotsLeft() == 0)
                {
                    Toast(player, _bots.Count >= MaxBots ? $"{MaxBots} bots at most" : "The server has no room for another bot");

                    break;
                }

                Toast(player, AddBot(player) ? "Bot placed" : "No room for a bot here");

                break;
            case "nobots":
                ClearBots();
                Toast(player, "Bots removed");

                break;
            case "spawns":
                ToggleSpawns(player);

                break;
            case "reload":
                Reload(player);

                break;
            case "markers":
                if (_replay.IsHidden(steamId))
                {
                    ShowMarkers(player);
                }
                else
                {
                    HideMarkers(player);
                }

                break;
            case "crosshair":
                state.Crosshair = !state.Crosshair;

                break;
            case "colors":
                state.Colors = !state.Colors;

                break;
            case "solo":
                state.Solo = !state.Solo;

                break;
            case "compass":
                Flip(_compassOff, steamId);

                break;
            case "legend":
                Flip(_legendOff, steamId);

                break;
            case "sounds":
                Flip(_soundsOff, steamId);

                break;
        }
    }

    private void HudOff(IPlayer player)
    {
        _hudOff.Add(player.SteamID);
        _hud?.Hide(player, HudSlots.Hud);
        _hud?.Hide(player, HudSlots.Bar);
        CloseWheel(player);
        ClosePicker(player);
    }

    private void Walk(IPlayer player, int direction)
    {
        PracticeState state = _system.StateFor(player.SteamID);

        if (state.Results.Count == 0)
        {
            Toast(player, "Load a lineup first");

            return;
        }

        // Before the start is not a position: stepping back from it lands on
        // the last entry, not the one before the last.
        state.Index =
            state.Index < 0
                ? (direction > 0 ? 0 : state.Results.Count - 1)
                : ((state.Index + direction) % state.Results.Count + state.Results.Count)
                    % state.Results.Count;

        Apply(player, state.Results[state.Index]);
    }

    private void SaveLast(IPlayer player)
    {
        ulong steamId = player.SteamID;
        LineupRecord? thrown = _recorder.LastThrow(steamId);

        if (thrown == null)
        {
            Toast(player, "Throw something first");

            return;
        }

        string automatic = LineupNaming.Auto(
            thrown.utility_type,
            thrown.release.feet_position,
            thrown.detonation_position,
            _callouts.Callouts
        );

        // Saved under the plainest name there is when the map has nothing to
        // call the place: stopping to ask for one in chat is the one thing a
        // menu is there to avoid, and Edit renames it afterwards.
        SaveThrow(steamId, automatic.Length > 0 ? automatic : thrown.utility_type);
    }

    private void ToggleDrill(IPlayer player)
    {
        ulong steamId = player.SteamID;

        if (_drill.Stop(steamId))
        {
            Toast(player, "Stopped repeating");

            return;
        }

        LineupRecord? here = DrillTarget(player);

        if (here == null)
        {
            Toast(player, "Stand on a lineup, or load one, to repeat it");

            return;
        }

        Toast(
            player,
            _drill.StartWith(steamId, new[] { here }, endless: true) switch
            {
                eDrillStart.Started =>
                    $"Repeating {PracticeLineupUtility.TitleCase(DrillUtility.Name(here))} · the aim ring fades as you land it",
                eDrillStart.ReplayDisabled => "Replay is off on this server",
                eDrillStart.NotConnected => "No panel, so a throw cannot be scored",
                eDrillStart.AlreadyRunning => "Already drilling",
                _ => "Nothing here to drill",
            }
        );
    }

    // How many throws back "Earlier" has walked for each player. Last Throw
    // puts it back to the most recent.
    private readonly Dictionary<ulong, int> _earlier = new();

    private bool HasEarlier(ulong steamId)
    {
        int back = _earlier.TryGetValue(steamId, out int stepped) ? stepped + 1 : 1;

        return _recorder.LastThrow(steamId, back) != null;
    }

    private bool BackTo(IPlayer player, int back)
    {
        LineupRecord? thrown = _recorder.LastThrow(player.SteamID, back);

        if (thrown == null)
        {
            Toast(player, back == 0 ? "Throw something first" : "No throw that far back");

            return false;
        }

        Apply(player, thrown);

        if (back > 0)
        {
            Toast(player, $"{back + 1} throws ago · {DrillUtility.Name(thrown)}");
        }

        return true;
    }

    private void GoToLanding(IPlayer player)
    {
        LineupRecord? lineup = _system.StateFor(player.SteamID).Loaded;

        if (lineup == null)
        {
            Toast(player, "Load a lineup first");

            return;
        }

        Toast(
            player,
            _replay.JumpToLanding(player, lineup)
                ? $"Where {DrillUtility.Name(lineup)} lands"
                : "Could not move you there"
        );
    }

    private void EditLoaded(IPlayer player)
    {
        LineupRecord? loaded = _system.StateFor(player.SteamID).Loaded;

        if (loaded == null)
        {
            Toast(player, "Load a lineup first");

            return;
        }

        if (!OpenEdit(player, loaded))
        {
            Toast(player, "Editing needs the 5stack HUD");
        }
    }

    private void WalkSpawns(IPlayer player, int direction)
    {
        List<ThrowSnapshot> spawns = _system.SpawnPoints();

        if (spawns.Count == 0)
        {
            Toast(player, "This map has no spawn points");

            return;
        }

        PracticeState state = _system.StateFor(player.SteamID);

        state.SpawnIndex =
            state.SpawnIndex < 0
                ? (direction > 0 ? 0 : spawns.Count - 1)
                : ((state.SpawnIndex + direction) % spawns.Count + spawns.Count) % spawns.Count;

        PracticeSystem.TeleportTo(player, spawns[state.SpawnIndex]);
        Toast(player, $"Spawn {state.SpawnIndex + 1} of {spawns.Count}");
    }

    private void TogglePlaybook(IPlayer player)
    {
        if (_playbook.Running)
        {
            _playbook.Stop();
            Toast(player, "Execute stopped");

            return;
        }

        Toast(
            player,
            _playbook.Start(_library.Map) switch
            {
                ePlaybookStart.NoPlaybook => "No execute has been sent from the website",
                ePlaybookStart.NoSteps => "That execute has no steps",
                ePlaybookStart.WrongMap => "That execute is for another map",
                ePlaybookStart.AlreadyRunning => "Already running",
                _ => $"Started {_playbook.Loaded?.name}",
            }
        );
    }

    private void TogglePlan(IPlayer player)
    {
        ulong steamId = player.SteamID;

        if (_drill.Stop(steamId))
        {
            Toast(player, "Stopped drilling");

            return;
        }

        Toast(
            player,
            _drill.StartPlan(steamId) switch
            {
                eDrillStart.Started => "Drilling unmastered · the aim ring fades as you land each one",
                eDrillStart.ReplayDisabled => "Replay is off on this server",
                eDrillStart.NotConnected => "No panel, so a throw cannot be scored",
                eDrillStart.AlreadyRunning => "Already drilling",
                _ => "Nothing left to master on this map",
            }
        );
    }

    // What was thrown, and the list of it: nothing reports a smoke taken out
    // of the world by hand, so the list is told here.
    private int ClearUtility()
    {
        ForgetLive();

        return _replay.ClearThrownUtility();
    }

    private void ToggleBloom(IPlayer player)
    {
        ulong steamId = player.SteamID;
        PracticeState state = _system.StateFor(steamId);
        LineupRecord? loaded = state.Loaded;

        if (loaded == null || !_config.GhostPreview)
        {
            Toast(player, loaded == null ? "Load a lineup first" : "Previews are off on this server");

            return;
        }

        state.Bloom = !state.Bloom;

        if (!state.Bloom)
        {
            _replay.ClearBloom(steamId);

            return;
        }

        _library.EnsureTrajectory(loaded, steamId, fetched => DrawBloom(steamId, fetched));
    }

    private void ToggleSpawns(IPlayer player)
    {
        if (_replay.SpawnsShown)
        {
            _spawnsWanted = false;
            _replay.ClearSpawns();

            return;
        }

        _spawnsWanted = true;

        List<ThrowSnapshot> spawns = _system.SpawnPoints();

        if (spawns.Count == 0)
        {
            Toast(player, "The round has not set its spawns yet");

            return;
        }

        _replay.ShowSpawns(spawns);
    }

    // On until somebody turns them off: where a round starts you is half of
    // whether a lineup is one you can actually reach in time.
    private bool _spawnsWanted = true;

    // The round has not chosen its spawns when the map loads, so this asks
    // again each second until it has.
    private void KeepSpawnsShown()
    {
        if (!_spawnsWanted || _replay.SpawnsShown)
        {
            return;
        }

        List<ThrowSnapshot> spawns = _system.SpawnPoints();

        if (spawns.Count > 0)
        {
            _replay.ShowSpawns(spawns);
        }
    }

    // Nothing to buy: every grenade is handed back as it is thrown. With the
    // zones gone the buy key has nowhere it works, so it opens nothing.
    private void RemoveBuyZones()
    {
        try
        {
            foreach (
                CBaseEntity zone in Core
                    .EntitySystem.GetAllEntitiesByDesignerName<CBaseEntity>("func_buyzone")
                    .ToList()
            )
            {
                if (zone.IsValid)
                {
                    zone.Despawn();
                }
            }
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "unable to remove the buy zones");
        }
    }

    private void Reload(IPlayer player)
    {
        ulong steamId = player.SteamID;
        PracticeState state = _system.StateFor(steamId);

        // The library about to be replaced is the one these came from.
        state.Loaded = null;
        state.Results.Clear();
        state.Index = -1;
        _replay.ClearGhosts(steamId);

        Toast(player, "Reloading...");

        _library.Refresh(
            steamId,
            count =>
            {
                IPlayer? still = _system.Find(steamId);

                if (still == null || !still.IsValid)
                {
                    return;
                }

                Toast(
                    still,
                    count < 0 ? "The panel did not answer" : $"{count} lineups on {_library.Map}"
                );

                if (_hud != null && _hud.IsOpen(still, HudSlots.Pick))
                {
                    RenderPicker(still);
                }
            }
        );
    }
}
