using FiveStack.Entities.Practice;
using FiveStack.Enums;
using FiveStack.Utilities;

namespace UtilityPractice;

// Turns the library into practice: pick a lineup, stand the player on it, wait
// for the throw to be scored, move on, and say what the run came to.
//
// It owns no timer of its own -- Second is the plugin's shared one second job,
// which is only the watchdog for a throw the panel never answered. Nothing here
// touches a player either: standing somebody on a lineup is what .load already
// does, so it goes back out through the plugin exactly as the playbook's steps
// do. Everything is keyed by steam id, because several people drill in one
// server and none of them are told about each other's runs.
public class PracticeDrill
{
    private readonly UtilityConfig _config;
    private readonly PracticeLibrary _library;

    private readonly Dictionary<ulong, PracticeDrillRun> _runs =
        new Dictionary<ulong, PracticeDrillRun>();

    private readonly DrillProgressBook _progress = new DrillProgressBook();
    private readonly Random _random = new Random();

    public PracticeDrill(UtilityConfig config, PracticeLibrary library)
    {
        _config = config;
        _library = library;
    }

    // Wired by the plugin rather than injected, the same way the playbook's
    // are. Load answers false when the lineup could not be stood on, which is
    // the only thing the runner cannot work out for itself.
    public Func<ulong, LineupRecord, bool>? Load { get; set; }

    // A repeat of the lineup already loaded: hand the grenade back where they
    // stand, and leave it to them to walk to the spot again.
    public Action<ulong, LineupRecord>? Rearm { get; set; }

    // A run that has ended, for the HUD to sum up. Where it is shown, the
    // summary is not also said in chat.
    public Action<ulong, PracticeDrillRun>? Finished { get; set; }
    public Func<ulong, bool>? Shown { get; set; }
    public Action<ulong, string>? Tell { get; set; }
    public Action<ulong, string>? Note { get; set; }
    public Action<ulong, string>? Center { get; set; }

    public eDrillStart Start(ulong steamId, eDrillOrder order, int count)
    {
        if (_runs.ContainsKey(steamId))
        {
            return eDrillStart.AlreadyRunning;
        }

        if (!_config.ReplayEnabled)
        {
            return eDrillStart.ReplayDisabled;
        }

        // Every attempt is scored by the panel, so a server that has none has
        // no drill to offer -- only teleports.
        if (!_config.IsConnected())
        {
            return eDrillStart.NotConnected;
        }

        List<LineupRecord> queue = DrillUtility.Queue(
            _library.For(steamId),
            count,
            order,
            _progress.Lookup(steamId),
            _random
        );

        return Begin(steamId, queue, Ordering(order));
    }

    // A drill over lineups somebody picked rather than a slice of their book --
    // the website sending "drill these" is the same run, chosen differently.
    public eDrillStart StartWith(
        ulong steamId,
        IReadOnlyList<LineupRecord> queue,
        bool endless = false
    )
    {
        if (_runs.ContainsKey(steamId))
        {
            return eDrillStart.AlreadyRunning;
        }

        if (!_config.ReplayEnabled)
        {
            return eDrillStart.ReplayDisabled;
        }

        if (!_config.IsConnected())
        {
            return eDrillStart.NotConnected;
        }

        return Begin(steamId, queue.ToList(), "as sent", endless);
    }

    private eDrillStart Begin(
        ulong steamId,
        List<LineupRecord> queue,
        string ordering,
        bool endless = false
    )
    {
        if (queue.Count == 0)
        {
            return eDrillStart.NothingToDrill;
        }

        var run = new PracticeDrillRun(queue, DrillReps, endless);
        _runs[steamId] = run;

        Tell?.Invoke(
            steamId,
            endless
                ? "drilling until you stop it (.drill again to stop, .next for the next one)"
                : $"drill started - {queue.Count} lineups x{DrillReps}, {ordering}"
                    + " (.skip to pass, .cancel to end)"
        );

        Advance(steamId, run);

        return eDrillStart.Started;
    }

    public bool Stop(ulong steamId)
    {
        if (!_runs.TryGetValue(steamId, out PracticeDrillRun? run))
        {
            return false;
        }

        run.End(eDrillEnd.Stopped);
        Finish(steamId, run);

        return true;
    }

    public bool Skip(ulong steamId)
    {
        if (!_runs.TryGetValue(steamId, out PracticeDrillRun? run) || !run.Skip())
        {
            return false;
        }

        Advance(steamId, run);

        return true;
    }

    // The recorder's release edge. A grenade the plugin emitted never gets
    // here: the recorder drops a projectile it threw itself before it raises
    // anything, so a preview or a solve cannot become somebody's attempt.
    public void OnThrown(ulong steamId, string utilityType)
    {
        if (_runs.TryGetValue(steamId, out PracticeDrillRun? run))
        {
            run.Thrown(utilityType, DateTime.UtcNow);
        }
    }

    // The panel's answer, or the fact that there was not one.
    public void OnScored(ulong steamId, string lineupId, UtilityPracticeResult? result)
    {
        // Recorded whether or not this player is drilling: a worst-first run
        // reads what the panel has already said about a lineup, and a throw
        // made after .load says as much about it as one made in a run.
        _progress.Record(steamId, lineupId, result);

        if (!_runs.TryGetValue(steamId, out PracticeDrillRun? run) || !run.Score(lineupId, result))
        {
            return;
        }

        if (result == null)
        {
            Note?.Invoke(steamId, "that throw was not scored, so it does not count");
        }

        Advance(steamId, run);
    }

    // The shared slow job. A throw whose answer never came is the one way a
    // drill can stop advancing without anybody being told, so it is the one
    // thing this watches for.
    public void Second()
    {
        if (_runs.Count == 0)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        // Finishing a run removes it, so the sweep walks a copy of the keys.
        foreach (ulong steamId in _runs.Keys.ToList())
        {
            if (!_runs.TryGetValue(steamId, out PracticeDrillRun? run) || !run.Expired(now))
            {
                continue;
            }

            Note?.Invoke(steamId, "nothing came back for that throw; the panel may be down");

            Advance(steamId, run);
        }
    }

    // A player who has left cannot be told anything, so their run ends where it
    // stands rather than printing a summary into an empty seat.
    // Three goes at each lineup before moving on. One is a tour of the map,
    // not practice: the point of a drill is throwing the same thing until it
    // stops being luck.
    public const int DrillReps = 3;

    // The lineup this player is being drilled on, or null when no run is going.
    public LineupRecord? Current(ulong steamId)
    {
        return _runs.TryGetValue(steamId, out PracticeDrillRun? run) && !run.Finished
            ? run.Current
            : null;
    }

    // Whether this player owes the run an answer -- they have thrown and the
    // panel has not scored it yet. Used to hold their next grenade back: a
    // drill where you can spam three smokes before the first is judged is not
    // measuring anything.
    /// <summary>
    /// How visible the aim crosshair should still be for this player, 1 down to
    /// 0.
    ///
    /// A drill is reps of the same throw, and a crosshair that is just as loud
    /// on the third as on the first trains you to read the crosshair rather
    /// than the map. It fades across the reps so the last one is thrown off
    /// what you have actually learned -- which is also the only rep that tells
    /// you anything about whether you have it.
    ///
    /// Outside a drill nothing is faded: that is practice, not a test.
    /// </summary>
    public float Assist(ulong steamId)
    {
        return _runs.TryGetValue(steamId, out PracticeDrillRun? run) ? run.Assist : 1f;
    }

    public bool Waiting(ulong steamId)
    {
        return _runs.TryGetValue(steamId, out PracticeDrillRun? run) && run.Waiting;
    }

    // What the panels say while a drill is on. Null when there is no run.
    public string? Progress(ulong steamId)
    {
        if (!_runs.TryGetValue(steamId, out PracticeDrillRun? run) || run.Finished)
        {
            return null;
        }

        string tally = run.Attempts == 0 ? "no throws yet" : Tally(run);

        return run.Endless
            ? $"Drill - {tally}"
            : $"Drill {run.Position}/{run.Length} - rep {run.Rep}/{run.Reps} - {tally}";
    }

    // Landing it this many times is what "drilled" means. The bar is progress
    // towards that, not towards the end of a queue.
    public const int MasteryTarget = 10;

    // Where the panel last said this player stands on a lineup: from the
    // library when it loaded, and from every throw scored since. Null for a
    // lineup they have never thrown at.
    public DrillProgress? ProgressFor(ulong steamId, string? lineupId)
    {
        return _progress.For(steamId, lineupId);
    }

    // Mastered is the panel's word and the panel's rule: this many landed in
    // a row. Five is what it is today; the library says so when it loads, and
    // what it says replaces this.
    public int MasteryStreak { get; private set; } = 5;

    public void Seed(
        ulong steamId,
        IReadOnlyDictionary<string, UtilityPracticeResult> progress,
        int? masteryStreak
    )
    {
        if (masteryStreak is > 0)
        {
            MasteryStreak = masteryStreak.Value;
        }

        foreach ((string lineupId, UtilityPracticeResult result) in progress)
        {
            _progress.Record(steamId, lineupId, result);
        }
    }

    // A run over more than one lineup, as a list for somebody to draw. One
    // lineup thrown until stopped is a drill, not a plan, and has no list.
    public (IReadOnlyList<LineupRecord> Queue, int Index, int Rep, int Reps)? Plan(ulong steamId)
    {
        if (
            !_runs.TryGetValue(steamId, out PracticeDrillRun? run)
            || run.Finished
            || run.Endless
            || run.Length < 2
        )
        {
            return null;
        }

        return (run.Queue, run.Index, run.Rep, run.Reps);
    }

    // Everything on the map not known to be mastered, worst first. Known is
    // the word: a lineup nobody has thrown since the map loaded has no record
    // here, so it is in the plan until one throw at it says otherwise.
    public eDrillStart StartPlan(ulong steamId)
    {
        if (_runs.ContainsKey(steamId))
        {
            return eDrillStart.AlreadyRunning;
        }

        if (!_config.ReplayEnabled)
        {
            return eDrillStart.ReplayDisabled;
        }

        if (!_config.IsConnected())
        {
            return eDrillStart.NotConnected;
        }

        Func<LineupRecord, DrillProgress?> progress = _progress.Lookup(steamId);
        List<LineupRecord> open = DrillUtility
            .Drillable(_library.For(steamId))
            .Where(lineup => progress(lineup)?.Mastered != true)
            .ToList();

        if (open.Count == 0)
        {
            return eDrillStart.NothingToDrill;
        }

        return Begin(
            steamId,
            DrillUtility.Queue(open, open.Count, eDrillOrder.Worst, progress, _random),
            "worst first"
        );
    }

    public void Forget(ulong steamId)
    {
        _runs.Remove(steamId);
        _progress.Forget(steamId);
    }

    // A map change replaces the library every queue was built from.
    public void Reset()
    {
        _runs.Clear();
        _progress.Clear();
    }

    private void Advance(ulong steamId, PracticeDrillRun run)
    {
        while (true)
        {
            LineupRecord? next = run.Next();

            if (next == null)
            {
                Finish(steamId, run);
                return;
            }

            // A later rep is the SAME lineup, already loaded and already
            // marked. Re-loading it would teleport the player back onto the
            // spot, and walking back is part of the throw.
            if (run.Rep > 1)
            {
                Rearm?.Invoke(steamId, next);
                run.Loaded();

                Note?.Invoke(
                    steamId,
                    $"{run.Position}/{run.Length} rep {run.Rep}/{run.Reps} "
                        + $"{DrillUtility.Name(next)} - {Tally(run)}"
                );

                return;
            }

            if (Load?.Invoke(steamId, next) == true)
            {
                run.Loaded();

                Note?.Invoke(
                    steamId,
                    $"{run.Position}/{run.Length} rep {run.Rep}/{run.Reps} "
                        + $"{DrillUtility.Name(next)} - {Tally(run)}"
                );

                return;
            }

            Note?.Invoke(steamId, $"{DrillUtility.Name(next)} could not be loaded; skipping it");

            run.CannotLoad();
        }
    }

    private void Finish(ulong steamId, PracticeDrillRun run)
    {
        _runs.Remove(steamId);

        Finished?.Invoke(steamId, run);

        if (Shown?.Invoke(steamId) == true)
        {
            return;
        }

        foreach (string line in run.Summary())
        {
            Tell?.Invoke(steamId, line);
        }

        Center?.Invoke(steamId, $"drill\n{run.Hits}/{run.Attempts}");
    }

    private static string Tally(PracticeDrillRun run)
    {
        return $"{run.Hits} hit, {run.Misses} miss, streak {run.Streak}";
    }

    private static string Ordering(eDrillOrder order)
    {
        return order == eDrillOrder.Worst ? "worst first" : "shuffled";
    }
}
