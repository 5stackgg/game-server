using FiveStack.Entities.Practice;
using FiveStack.Enums;

namespace FiveStack.Utilities;

// One player's run: the queue, where they are in it, and what the panel said
// about each throw.
//
// A step is over when a throw has been scored, not when it has been thrown --
// advancing on the throw itself would teleport somebody off a lineup before
// they were told they missed it. Everything that can leave a step unresolved
// has a named way out: the panel not answering, a lineup that cannot be stood
// on, and the player skipping it.
public class PracticeDrillRun
{
    private class Pending
    {
        public required string LineupId;
        public required DateTime Deadline;
    }

    private readonly List<LineupRecord> _queue;
    private readonly List<string> _missed = new List<string>();
    private readonly List<string> _skipped = new List<string>();

    private Pending? _pending;
    private int _index = -1;
    private int _unscoredInARow;
    private int _unloadableInARow;

    public PracticeDrillRun(IReadOnlyList<LineupRecord> queue)
    {
        _queue = queue.ToList();
    }

    public eDrillEnd Ending { get; private set; } = eDrillEnd.Running;

    public LineupRecord? Current { get; private set; }

    public int Length => _queue.Count;

    // One based, so it reads as "3/10" while a run is going.
    public int Position => Math.Min(_index + 1, _queue.Count);

    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int Unscored { get; private set; }
    public int Dropped { get; private set; }
    public int Skipped => _skipped.Count;
    public int Streak { get; private set; }
    public int BestStreak { get; private set; }

    public bool Finished => Ending != eDrillEnd.Running;

    public bool Waiting => _pending != null;

    public int Attempts => Hits + Misses;

    // The next lineup to stand on, or null when there is nothing left. A step
    // that was never resolved is abandoned here rather than carried forward:
    // whatever the player does next belongs to the new step.
    public LineupRecord? Next()
    {
        _pending = null;

        if (Finished)
        {
            return null;
        }

        _index++;

        if (_index >= _queue.Count)
        {
            Current = null;
            Ending = eDrillEnd.Completed;
            return null;
        }

        Current = _queue[_index];

        return Current;
    }

    public void Loaded()
    {
        _unloadableInARow = 0;
    }

    // The current lineup could not be stood on, so it is dropped rather than
    // left as a step nobody can finish. A whole run of them is a broken library
    // or a player who is no longer there, and either way the run is over.
    public void CannotLoad()
    {
        Dropped++;
        _unloadableInARow++;

        if (_unloadableInARow >= DrillUtility.MaxUnloadableInARow)
        {
            Ending = eDrillEnd.Unloadable;
        }
    }

    // A throw only becomes a drill attempt when it is the utility the current
    // lineup asks for: throwing a flash while a smoke is loaded is a different
    // throw, not a missed one. The second throw of a step is ignored as well,
    // so a player who spams two smokes still owes the run one answer.
    public bool Thrown(string utilityType, DateTime now)
    {
        if (Finished || Current == null || _pending != null)
        {
            return false;
        }

        if (
            string.IsNullOrEmpty(Current.id)
            || !string.Equals(Current.utility_type, utilityType, StringComparison.OrdinalIgnoreCase)
        )
        {
            return false;
        }

        _pending = new Pending
        {
            LineupId = Current.id,
            Deadline = now.AddSeconds(DrillUtility.ScoreWaitSeconds),
        };

        return true;
    }

    // The panel has answered. A null result is "not scored", which is not the
    // same as a miss and does not break a streak nobody has disproved.
    public bool Score(string? lineupId, NadePracticeResult? result)
    {
        if (
            _pending == null
            || !string.Equals(_pending.LineupId, lineupId, StringComparison.Ordinal)
        )
        {
            return false;
        }

        _pending = null;

        if (result == null)
        {
            return Unscore();
        }

        _unscoredInARow = 0;

        if (result.success)
        {
            Hits++;
            Streak++;
            BestStreak = Math.Max(BestStreak, Streak);

            return true;
        }

        Misses++;
        Streak = 0;

        if (Current != null)
        {
            _missed.Add(DrillUtility.Name(Current));
        }

        return true;
    }

    // Nothing came back for the throw. The run cannot wait on an answer that
    // may never arrive, so the step resolves unscored.
    public bool Expired(DateTime now)
    {
        if (_pending == null || now < _pending.Deadline)
        {
            return false;
        }

        _pending = null;

        return Unscore();
    }

    // A skipped lineup is not a hit, so it breaks the run's streak; it is not a
    // miss either, so it is counted apart from one.
    public bool Skip()
    {
        if (Finished || Current == null)
        {
            return false;
        }

        _pending = null;
        _skipped.Add(DrillUtility.Name(Current));
        Streak = 0;

        return true;
    }

    public void End(eDrillEnd ending)
    {
        if (!Finished)
        {
            Ending = ending;
        }
    }

    // What the run was for: the number at the end, and the lineups that put it
    // there.
    public List<string> Summary()
    {
        var lines = new List<string>
        {
            $"drill {Headline()} - {Hits}/{Attempts} hit, best streak {BestStreak}",
        };

        if (_missed.Count > 0)
        {
            lines.Add($"missed: {Grouped(_missed)}");
        }

        if (_skipped.Count > 0)
        {
            lines.Add($"skipped: {Grouped(_skipped)}");
        }

        if (Unscored > 0)
        {
            lines.Add(
                $"{Unscored} {(Unscored == 1 ? "throw" : "throws")} could not be scored by the panel"
            );
        }

        if (Dropped > 0)
        {
            lines.Add($"{Dropped} could not be loaded");
        }

        return lines;
    }

    private string Headline()
    {
        switch (Ending)
        {
            case eDrillEnd.Stopped:
                return "stopped";
            case eDrillEnd.Unscorable:
                return "stopped, the panel is not scoring throws right now";
            case eDrillEnd.Unloadable:
                return "stopped, those lineups could not be loaded";
            default:
                return "over";
        }
    }

    private bool Unscore()
    {
        Unscored++;
        _unscoredInARow++;

        if (_unscoredInARow >= DrillUtility.MaxUnscoredInARow)
        {
            Ending = eDrillEnd.Unscorable;
        }

        return true;
    }

    private static string Grouped(IEnumerable<string> names)
    {
        return string.Join(
            ", ",
            names
                .GroupBy(name => name, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Count() > 1 ? $"{group.Key} ({group.Count()})" : group.Key)
        );
    }
}
