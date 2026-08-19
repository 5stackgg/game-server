using FiveStack.Entities.Practice;
using FiveStack.Utilities;

namespace NadePractice;

public enum ePlaybookStart
{
    Started,
    NoPlaybook,
    NoSteps,
    WrongMap,
    AlreadyRunning,
}

// Runs the execute the panel loaded onto this session: a countdown, then each
// step at its own offset.
//
// It owns no timer of its own. Tick is the plugin's shared fast job and Second
// is the shared slow one, because ten people practising must not mean ten
// clocks. Everything that touches a player goes back out through the plugin, so
// a step puts somebody on a lineup by exactly the same path .load does.
public class PracticePlaybook
{
    public const int CountdownSeconds = 5;

    private enum Phase
    {
        Idle,
        Countdown,
        Running,
    }

    private readonly PracticeSession _session;
    private readonly PracticeSystem _system;

    private Phase _phase = Phase.Idle;
    private string _name = "";
    private List<NadePlaybookStep> _steps = new List<NadePlaybookStep>();

    // When t=0 is, which is the end of the countdown rather than the moment
    // .playbook was typed.
    private DateTime _startsAt = DateTime.MinValue;

    // The last elapsed time already fired. Negative so a step at offset zero is
    // still in the first window.
    private int _elapsedMs = -1;
    private int _announced = -1;

    // A step names a lineup, and the same lineup can appear in several steps
    // and several runs. Keeping one record per id means one trajectory fetch
    // per id, and the identity check .load relies on keeps working.
    private readonly Dictionary<string, LineupRecord> _lineups =
        new Dictionary<string, LineupRecord>();

    public PracticePlaybook(PracticeSession session, PracticeSystem system)
    {
        _session = session;
        _system = system;
    }

    // Wired by the plugin rather than injected: standing a player on a lineup is
    // what .load already does, and a second implementation of it would be a
    // second answer to the same question.
    public Action<ulong, LineupRecord>? Load { get; set; }
    public Action<string>? Chat { get; set; }
    public Action<ulong, string>? Tell { get; set; }
    public Action<ulong, string>? Center { get; set; }

    public bool Running => _phase != Phase.Idle;

    public NadePlaybook? Loaded => _session.Current?.playbook;

    public IReadOnlyList<NadePlaybookStep> Steps => PlaybookUtility.Ordered(Loaded);

    public ePlaybookStart Start(string map)
    {
        if (Running)
        {
            return ePlaybookStart.AlreadyRunning;
        }

        NadePlaybook? playbook = Loaded;

        if (playbook == null)
        {
            return ePlaybookStart.NoPlaybook;
        }

        if (
            !string.IsNullOrEmpty(playbook.map_name)
            && !string.IsNullOrEmpty(map)
            && !string.Equals(playbook.map_name, map, StringComparison.OrdinalIgnoreCase)
        )
        {
            return ePlaybookStart.WrongMap;
        }

        List<NadePlaybookStep> steps = PlaybookUtility.Ordered(playbook);

        if (steps.Count == 0)
        {
            return ePlaybookStart.NoSteps;
        }

        _steps = steps;
        _name = string.IsNullOrEmpty(playbook.name) ? "execute" : playbook.name!;
        _phase = Phase.Countdown;
        _startsAt = DateTime.UtcNow.AddSeconds(CountdownSeconds);
        _elapsedMs = -1;
        _announced = -1;

        return ePlaybookStart.Started;
    }

    public bool Stop()
    {
        if (!Running)
        {
            return false;
        }

        _phase = Phase.Idle;
        _steps = new List<NadePlaybookStep>();

        return true;
    }

    // A map change takes the geometry the steps refer to with it.
    public void Reset()
    {
        _phase = Phase.Idle;
        _steps = new List<NadePlaybookStep>();
        _lineups.Clear();
    }

    // The shared fast job. Sub-second offsets are the whole point of an execute,
    // which is why the step clock lives here and not on the one second job.
    public void Tick()
    {
        if (_phase == Phase.Idle)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;

        if (_phase == Phase.Countdown)
        {
            if (now < _startsAt)
            {
                return;
            }

            _phase = Phase.Running;
            Chat?.Invoke($"{_name} go");
        }

        int elapsed = (int)(now - _startsAt).TotalMilliseconds;

        foreach (NadePlaybookStep step in PlaybookUtility.Due(_steps, _elapsedMs, elapsed))
        {
            Fire(step);
        }

        _elapsedMs = elapsed;

        if (elapsed > PlaybookUtility.DurationMs(_steps) + PlaybookUtility.TailMs)
        {
            _phase = Phase.Idle;
            Chat?.Invoke($"{_name} complete");
        }
    }

    // The shared slow job, which is only the countdown: a number that changes
    // once a second does not need a finer clock than that.
    public void Second()
    {
        if (_phase != Phase.Countdown)
        {
            return;
        }

        int remaining = (int)Math.Ceiling((_startsAt - DateTime.UtcNow).TotalSeconds);

        if (remaining <= 0 || remaining == _announced)
        {
            return;
        }

        _announced = remaining;

        foreach (ulong steamId in _system.ConnectedSteamIds())
        {
            Center?.Invoke(steamId, $"{_name}\n{remaining}");
        }
    }

    private void Fire(NadePlaybookStep step)
    {
        LineupRecord? lineup = LineupFor(step);

        if (lineup == null)
        {
            return;
        }

        int order = _steps.IndexOf(step) + 1;
        string name = string.IsNullOrEmpty(lineup.name) ? lineup.utility_type : lineup.name;
        string note = string.IsNullOrWhiteSpace(step.note) ? "" : $" - {step.note}";

        var targets = _system
            .ConnectedSteamIds()
            .Where(steamId => PlaybookUtility.IsFor(step, steamId))
            .ToList();

        // An assigned step whose player is not on the server is announced and
        // skipped: silently handing their smoke to everybody would rehearse an
        // execute nobody is going to run.
        if (targets.Count == 0)
        {
            Chat?.Invoke(
                PlaybookUtility.IsAssigned(step)
                    ? $"{order}/{_steps.Count} {name}{note} - {step.assigned_steam_id} is not here"
                    : $"{order}/{_steps.Count} {name}{note} - nobody to throw it"
            );
            return;
        }

        foreach (ulong steamId in targets)
        {
            Load?.Invoke(steamId, lineup);
            Tell?.Invoke(steamId, $"{order}/{_steps.Count} {name}{note}");
        }
    }

    private LineupRecord? LineupFor(NadePlaybookStep step)
    {
        string id = step.nade_lineup_id ?? "";

        if (!string.IsNullOrEmpty(id) && _lineups.TryGetValue(id, out LineupRecord? cached))
        {
            return cached;
        }

        LineupRecord? lineup = step.ToLineup();

        if (lineup != null && !string.IsNullOrEmpty(id))
        {
            _lineups[id] = lineup;
        }

        return lineup;
    }
}
