using CounterStrikeSharp.API;
using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace UtilityPractice;

// The saved lineups for the map this server is on, one list per player. The
// panel already filters by map and by who is allowed to see what, so nothing
// here re-decides visibility.
public class PracticeLibrary
{
    private readonly UtilityApiClient _api;
    private readonly ILogger<PracticeLibrary> _logger;

    private readonly Dictionary<ulong, List<LineupRecord>> _lineups = new();
    private string _map = "";

    public PracticeLibrary(UtilityApiClient api, ILogger<PracticeLibrary> logger)
    {
        _api = api;
        _logger = logger;
    }

    public string Map => _map;

    public void SetMap(string map)
    {
        if (_map == map)
        {
            return;
        }

        _map = map;
        _lineups.Clear();
    }

    public IReadOnlyList<LineupRecord> For(ulong steamId)
    {
        return _lineups.TryGetValue(steamId, out List<LineupRecord>? lineups)
            ? lineups
            : new List<LineupRecord>();
    }

    public LineupRecord? Resolve(ulong steamId, string query, Vec3? near = null)
    {
        return PracticeLineupUtility.Resolve(For(steamId), query, near);
    }

    public void Add(ulong steamId, LineupRecord lineup)
    {
        if (!_lineups.TryGetValue(steamId, out List<LineupRecord>? lineups))
        {
            lineups = new List<LineupRecord>();
            _lineups[steamId] = lineups;
        }

        lineups.RemoveAll(existing => existing.client_id == lineup.client_id);
        lineups.Add(lineup);
    }

    public void Remove(ulong steamId, LineupRecord lineup)
    {
        if (_lineups.TryGetValue(steamId, out List<LineupRecord>? lineups))
        {
            lineups.RemoveAll(existing => existing.client_id == lineup.client_id);
        }
    }

    // A library row carries no flight path and no measured bloom, so neither
    // can be drawn until they have been fetched. Everything else about a lineup
    // -- where to stand, where to look, what to hold -- is already in hand,
    // which is why .load teleports first and only then waits on this.
    public void EnsureTrajectory(LineupRecord lineup, ulong steamId, Action<LineupRecord> ready)
    {
        if (lineup.trajectory.Count > 0 || string.IsNullOrEmpty(lineup.id))
        {
            ready(lineup);
            return;
        }

        string id = lineup.id;

        _ = Task.Run(async () =>
        {
            UtilityTrajectoryArtifact? artifact = await _api.Trajectory(id, steamId);

            Server.NextFrame(() =>
            {
                if (artifact != null)
                {
                    lineup.trajectory = artifact.path;
                    lineup.smoke_volume = artifact.smoke_volume;
                }

                ready(lineup);
            });
        });
    }

    // Fetches off the game thread and applies on it, so a slow panel cannot
    // stall a tick and the dictionary is only ever touched from one thread.
    public void Refresh(ulong steamId, Action<int>? done = null)
    {
        string map = _map;

        _ = Task.Run(async () =>
        {
            List<LineupRecord>? lineups = await _api.Library(map, steamId);

            Server.NextFrame(() =>
            {
                if (lineups == null)
                {
                    done?.Invoke(-1);
                    return;
                }

                // The map can change while the request is in flight; dropping
                // the answer beats showing inferno lineups on mirage.
                if (map != _map)
                {
                    done?.Invoke(-1);
                    return;
                }

                _lineups[steamId] = lineups;
                done?.Invoke(lineups.Count);
            });
        });
    }
}
