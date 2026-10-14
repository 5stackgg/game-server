using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using SwiftlyS2.Shared.Players;

namespace UtilityPractice;

// A stack is a few throws that go again together: one press, and the server
// throws every one of them from the seed it was recorded with. It is how one
// person practises an execute that takes four -- three smokes arrive on
// their own and the fourth is theirs to make.
public partial class UtilityPracticePlugin
{
    private class StackRun
    {
        public required int Zero;
        public required IReadOnlyList<LineupRecord> Throws;
        public required IReadOnlyList<int> Offsets;
        public required int Mine;
        public required bool[] Fired;
    }

    // A throw on the stack, and when it leaves if the stack came from an
    // execute that says so. A throw added by hand has no such time: its gap is
    // read off when it was thrown.
    private record StackEntry(LineupRecord Lineup, int? OffsetTicks = null);

    private readonly Dictionary<ulong, List<StackEntry>> _stacks = new();
    private readonly Dictionary<ulong, StackRun> _stackRuns = new();

    // The execute a stack was loaded from, for the card's title.
    private readonly Dictionary<ulong, string> _stackNames = new();

    // Whose stack is thrown at the gaps it was thrown at, rather than all at
    // once; and which throw each player has kept for themselves.
    private readonly HashSet<ulong> _stackAsThrown = new();
    private readonly Dictionary<ulong, int> _stackMine = new();

    // The card that lists it has this many rows.
    private const int StackMost = HudSlots.PlanRows;

    // Time to get to the spot and pull the pin, when one of them is yours.
    private const int StackCountdownTicks = 3 * 64;

    // The gap between two throws as they were made is mostly the walk between
    // the two spots. Past this it says nothing about timing.
    private const int StackGapMostTicks = 5 * 64;
    private const int StackGapUnknownTicks = 64;

    private List<StackEntry> StackOf(ulong steamId)
    {
        if (!_stacks.TryGetValue(steamId, out List<StackEntry>? stack))
        {
            stack = new List<StackEntry>();
            _stacks[steamId] = stack;
        }

        return stack;
    }

    private int StackCount(ulong steamId)
    {
        return _stacks.TryGetValue(steamId, out List<StackEntry>? stack) ? stack.Count : 0;
    }

    // The throw the player keeps, as an index into the stack, or -1.
    private int StackMine(ulong steamId)
    {
        return
            _stackMine.TryGetValue(steamId, out int mine) && mine < StackCount(steamId)
            ? mine
            : -1;
    }

    private void StackAdd(IPlayer player)
    {
        ulong steamId = player.SteamID;
        LineupRecord? lineup = RethrowTarget(steamId);

        if (lineup == null)
        {
            Toast(player, "Throw something first");

            return;
        }

        if (!lineup.IsExactlyReplayable())
        {
            Toast(player, $"{DrillUtility.Name(lineup)} has no recorded throw to stack");

            return;
        }

        List<StackEntry> stack = StackOf(steamId);

        if (stack.Any(entry => entry.Lineup.client_id == lineup.client_id))
        {
            Toast(player, "Already on the stack");

            return;
        }

        if (stack.Count >= StackMost)
        {
            Toast(player, $"The stack holds {StackMost}");

            return;
        }

        stack.Add(new StackEntry(lineup));
        Toast(player, $"Stacked · {stack.Count} · R throws them all");
    }

    private void StackRemoveLast(IPlayer player)
    {
        List<StackEntry> stack = StackOf(player.SteamID);

        if (stack.Count == 0)
        {
            Toast(player, "The stack is empty");

            return;
        }

        stack.RemoveAt(stack.Count - 1);
        _stackRuns.Remove(player.SteamID);
        Toast(player, stack.Count == 0 ? "Stack emptied" : $"Removed · {stack.Count} left");
    }

    private void StackEmpty(IPlayer player)
    {
        _stacks.Remove(player.SteamID);
        _stackRuns.Remove(player.SteamID);
        _stackMine.Remove(player.SteamID);
        _stackNames.Remove(player.SteamID);
        Toast(player, "Stack emptied");
    }

    private void StackCycleMine(IPlayer player)
    {
        ulong steamId = player.SteamID;
        int count = StackCount(steamId);

        if (count == 0)
        {
            Toast(player, "The stack is empty");

            return;
        }

        int next = StackMine(steamId) + 1;

        if (next >= count)
        {
            _stackMine.Remove(steamId);
            Toast(player, "The server throws all of them");

            return;
        }

        _stackMine[steamId] = next;
        Toast(player, $"You throw {next + 1} · {DrillUtility.Name(StackOf(steamId)[next].Lineup)}");
    }

    // When each throw leaves, in ticks after the first.
    private List<int> StackOffsets(ulong steamId, IReadOnlyList<StackEntry> stack)
    {
        var offsets = new List<int>();
        bool asThrown = _stackAsThrown.Contains(steamId);

        for (int index = 0; index < stack.Count; index++)
        {
            if (!asThrown)
            {
                offsets.Add(0);

                continue;
            }

            if (stack[index].OffsetTicks is int written)
            {
                offsets.Add(written);

                continue;
            }

            if (index == 0)
            {
                offsets.Add(0);

                continue;
            }

            int gap = stack[index].Lineup.release.tick - stack[index - 1].Lineup.release.tick;

            offsets.Add(
                offsets[index - 1]
                    + (gap > 0 ? Math.Min(gap, StackGapMostTicks) : StackGapUnknownTicks)
            );
        }

        return offsets;
    }

    private void RethrowStack(IPlayer player)
    {
        ulong steamId = player.SteamID;

        if (!_config.ReplayEnabled)
        {
            Toast(player, "Replay is off on this server");

            return;
        }

        if (_stackRuns.ContainsKey(steamId))
        {
            return;
        }

        List<StackEntry> entries = StackOf(steamId).ToList();
        List<LineupRecord> stack = entries.Select(entry => entry.Lineup).ToList();
        int mine = StackMine(steamId);

        _stackRuns[steamId] = new StackRun
        {
            Zero = _aimTick + (mine >= 0 ? StackCountdownTicks : 0),
            Throws = stack,
            Offsets = StackOffsets(steamId, entries),
            Mine = mine,
            Fired = new bool[stack.Count],
        };

        Toast(
            player,
            mine >= 0
                ? $"Execute in 3 · you throw {DrillUtility.Name(stack[mine])}"
                : $"Rethrown · all {stack.Count}"
        );
    }

    // Every game tick: a throw a quarter of a second late is a different
    // execute.
    private void StackTick()
    {
        if (_stackRuns.Count == 0)
        {
            return;
        }

        foreach ((ulong steamId, StackRun run) in _stackRuns.ToList())
        {
            IPlayer? player = _system.Find(steamId);

            if (player == null || !player.IsValid)
            {
                _stackRuns.Remove(steamId);

                continue;
            }

            int elapsed = _aimTick - run.Zero;

            for (int index = 0; index < run.Throws.Count; index++)
            {
                if (run.Fired[index] || elapsed < run.Offsets[index])
                {
                    continue;
                }

                run.Fired[index] = true;

                if (index == run.Mine)
                {
                    Toast(player, $"Throw yours now · {DrillUtility.Name(run.Throws[index])}");

                    continue;
                }

                _replay.ThrowGhostProjectile(player, run.Throws[index], force: true);
            }

            if (run.Fired.All(fired => fired))
            {
                _stackRuns.Remove(steamId);
            }
        }
    }

    private NadeBarTemplate.Card? StackCard(ulong steamId)
    {
        if (!_stacks.TryGetValue(steamId, out List<StackEntry>? stack) || stack.Count == 0)
        {
            return null;
        }

        _stackRuns.TryGetValue(steamId, out StackRun? run);

        IReadOnlyList<LineupRecord> throws =
            run?.Throws ?? stack.Select(entry => entry.Lineup).ToList();
        IReadOnlyList<int> offsets = run?.Offsets ?? StackOffsets(steamId, stack);
        int mine = run?.Mine ?? StackMine(steamId);
        int elapsed = run == null ? 0 : _aimTick - run.Zero;
        int next = run == null ? -1 : Array.IndexOf(run.Fired, false);
        var rows = new List<NadeBarTemplate.CardRow>();

        for (int index = 0; index < throws.Count && index < HudSlots.PlanRows; index++)
        {
            string when = $"+{offsets[index] / 64f:0.0}s";

            rows.Add(
                new NadeBarTemplate.CardRow(
                    $"{index + 1}  {PracticeLineupUtility.TitleCase(DrillUtility.Name(throws[index]))}",
                    index == mine ? $"you · {when}" : when,
                    Now: index == next,
                    Done: run != null && run.Fired[index]
                )
            );
        }

        string foot =
            run == null ? (throws.Count > 1 ? $"R throws all {throws.Count}" : "Add another to throw them together")
            : elapsed < 0 ? $"In {(-elapsed + 63) / 64}"
            : $"+{elapsed / 64f:0.0}s";

        return new NadeBarTemplate.Card(
            _stackNames.TryGetValue(steamId, out string? execute) ? execute : "Stack",
            _stackAsThrown.Contains(steamId) ? "on its timings" : "together",
            foot,
            rows
        );
    }

    private NadeBarTemplate.Card? PlanCard(ulong steamId)
    {
        (IReadOnlyList<LineupRecord> Queue, int Index, int Rep, int Reps)? plan = _drill.Plan(steamId);

        if (plan == null)
        {
            return null;
        }

        (IReadOnlyList<LineupRecord> queue, int at, int rep, int reps) = plan.Value;
        int bar = _drill.MasteryStreak;
        int first = Math.Clamp(at - 1, 0, Math.Max(0, queue.Count - HudSlots.PlanRows));
        var rows = new List<NadeBarTemplate.CardRow>();

        for (int index = first; index < queue.Count && rows.Count < HudSlots.PlanRows; index++)
        {
            DrillProgress? progress = _drill.ProgressFor(steamId, queue[index].id);

            rows.Add(
                new NadeBarTemplate.CardRow(
                    PracticeLineupUtility.TitleCase(DrillUtility.Name(queue[index])),
                    progress == null ? "–"
                        : progress.Mastered ? "mastered"
                        : $"{Math.Min(progress.CurrentStreak, bar)} / {bar}",
                    Now: index == at,
                    Done: index < at || progress?.Mastered == true
                )
            );
        }

        return new NadeBarTemplate.Card(
            "Unmastered",
            $"{at + 1} of {queue.Count} · worst first",
            $"Rep {rep} of {reps}",
            rows
        );
    }

    // What the meter on the throw bar says about a lineup: how far along
    // the run of landed throws that the panel calls mastered. It empties on a
    // miss, because the run does. Only a lineup the panel knows is ever
    // scored, so only one of those has a count.
    private NadeHudTemplate.Meter? MasteryOf(ulong steamId, LineupRecord lineup)
    {
        if (string.IsNullOrEmpty(lineup.id) || !_config.IsConnected())
        {
            return null;
        }

        int bar = _drill.MasteryStreak;
        DrillProgress? progress = _drill.ProgressFor(steamId, lineup.id);

        if (progress == null)
        {
            return new NadeHudTemplate.Meter("Mastery", $"0 / {bar} in a row · never thrown", 0);
        }

        string landed = $"{progress.Successes} of {progress.Attempts} landed";

        if (progress.Mastered)
        {
            return new NadeHudTemplate.Meter(
                "Mastered",
                $"best run {progress.BestStreak} · {landed}",
                HudSlots.MeterSteps - 1,
                Done: true
            );
        }

        int run = Math.Min(progress.CurrentStreak, bar);

        return new NadeHudTemplate.Meter(
            "Mastery",
            $"{run} / {bar} in a row · {landed}",
            run * (HudSlots.MeterSteps - 1) / bar
        );
    }

    private void ForgetStacks()
    {
        _stacks.Clear();
        _stackRuns.Clear();
        _stackMine.Clear();
        _stackNames.Clear();
        _executes.Clear();
    }

    // ── executes from the website ────────────────────────────────────────

    private class ExecuteList
    {
        public required int Asked;
        public List<UtilityPlaybook>? Books;
    }

    private readonly Dictionary<ulong, ExecuteList> _executes = new();

    // The list is asked for again once it is this old: somebody building an
    // execute on the website with the game open wants to see it turn up.
    private const int ExecutesFreshTicks = 20 * 64;

    // Null while the panel has not answered yet.
    private List<UtilityPlaybook>? ExecutesFor(IPlayer player)
    {
        ulong steamId = player.SteamID;

        if (
            _executes.TryGetValue(steamId, out ExecuteList? list)
            && _aimTick - list.Asked < ExecutesFreshTicks
        )
        {
            return list.Books;
        }

        var asking = new ExecuteList { Asked = _aimTick, Books = list?.Books };

        _executes[steamId] = asking;

        _ = Task.Run(async () =>
        {
            List<UtilityPlaybook> books = await _api.Playbooks(steamId) ?? new List<UtilityPlaybook>();

            Core.Scheduler.NextTick(() =>
            {
                asking.Books = books;

                IPlayer? still = _system.Find(steamId);

                // Redrawn only for somebody still looking at the list.
                if (
                    still != null
                    && still.IsValid
                    && _hud != null
                    && _hud.IsOpen(still, HudSlots.Wheel)
                    && WheelPath(steamId).LastOrDefault() == "executes"
                )
                {
                    RenderWheel(still);
                }
            });
        });

        return asking.Books;
    }

    // The execute becomes the stack: every step the server can throw, at the
    // time the execute gives it, with the step that is this player's kept for
    // them. A step whose lineup was never thrown by a plugin has no seed to
    // throw it from and is left out.
    private void LoadExecute(IPlayer player, int index)
    {
        ulong steamId = player.SteamID;

        if (
            !_executes.TryGetValue(steamId, out ExecuteList? list)
            || list.Books == null
            || index < 0
            || index >= list.Books.Count
        )
        {
            return;
        }

        UtilityPlaybook book = list.Books[index];
        var stack = new List<StackEntry>();
        int mine = -1;
        int skipped = 0;

        foreach (UtilityPlaybookStep step in PlaybookUtility.Ordered(book))
        {
            LineupRecord? lineup = step.ToLineup();

            if (lineup == null || !lineup.IsExactlyReplayable())
            {
                skipped++;

                continue;
            }

            if (stack.Count >= StackMost)
            {
                skipped++;

                continue;
            }

            if (mine < 0 && step.assigned_steam_id == steamId.ToString())
            {
                mine = stack.Count;
            }

            stack.Add(new StackEntry(lineup, step.offset_ms * 64 / 1000));
        }

        if (stack.Count == 0)
        {
            Toast(player, $"{book.name} has no throw the server can make");

            return;
        }

        _stacks[steamId] = stack;
        _stackRuns.Remove(steamId);
        _stackNames[steamId] = book.name ?? "Execute";
        _stackAsThrown.Add(steamId);

        if (mine >= 0)
        {
            _stackMine[steamId] = mine;
        }
        else
        {
            _stackMine.Remove(steamId);
        }

        Toast(
            player,
            skipped > 0
                ? $"{book.name} · {stack.Count} throws, {skipped} left out · R throws them"
                : $"{book.name} · {stack.Count} throws · R throws them"
        );
    }
}
