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

    // What this server changed in a player's library itself -- a .save, a
    // .delete -- and when, by client id. A fetch that went out before the
    // change, or before the panel had it, answers with the library as it was;
    // applied as-is it dropped the lineup somebody had just saved, or brought
    // back the one they had just deleted. Kept is null for a removal.
    private sealed class LocalChange
    {
        public DateTime At;
        public LineupRecord? Kept;
    }

    private readonly Dictionary<ulong, Dictionary<string, LocalChange>> _local = new();

    // How long a local change outranks the panel's answer once a fetch has
    // started after it. A save is one round trip away from being on the panel;
    // past this, a lineup missing from the answer is one the website removed.
    private static readonly TimeSpan LocalHold = TimeSpan.FromMinutes(2);

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
        _local.Clear();
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
        Touch(steamId, lineup.client_id, lineup);
    }

    public void Remove(ulong steamId, LineupRecord lineup)
    {
        if (_lineups.TryGetValue(steamId, out List<LineupRecord>? lineups))
        {
            lineups.RemoveAll(existing => existing.client_id == lineup.client_id);
        }

        Touch(steamId, lineup.client_id, null);
    }

    private void Touch(ulong steamId, string clientId, LineupRecord? kept)
    {
        if (!_local.TryGetValue(steamId, out Dictionary<string, LocalChange>? changes))
        {
            changes = new Dictionary<string, LocalChange>();
            _local[steamId] = changes;
        }

        changes[clientId] = new LocalChange { At = DateTime.UtcNow, Kept = kept };
    }

    // The panel's answer, with this server's own changes laid back over it
    // while the answer cannot have seen them yet. A change is forgotten once
    // an answer agrees with it, or once it is old enough that disagreeing
    // means somebody else changed it since.
    private List<LineupRecord> MergeLocal(ulong steamId, DateTime startedAt, List<LineupRecord> answer)
    {
        if (!_local.TryGetValue(steamId, out Dictionary<string, LocalChange>? changes))
        {
            return answer;
        }

        DateTime now = DateTime.UtcNow;
        var merged = new List<LineupRecord>(answer);

        foreach ((string clientId, LocalChange change) in changes.ToList())
        {
            int index = merged.FindIndex(row => row.client_id == clientId);

            // Made after the request left: the answer cannot know about it.
            bool newer = change.At >= startedAt;
            bool holding = newer || now - change.At < LocalHold;

            if (change.Kept == null)
            {
                if (index < 0)
                {
                    if (!newer)
                    {
                        changes.Remove(clientId);
                    }
                }
                else if (holding)
                {
                    merged.RemoveAt(index);
                }
                else
                {
                    changes.Remove(clientId);
                }

                continue;
            }

            if (index >= 0)
            {
                if (newer)
                {
                    merged[index] = change.Kept;
                }
                else
                {
                    changes.Remove(clientId);
                }

                continue;
            }

            // Not on the panel yet. One with no id never reached it at all --
            // it is waiting in the retry queue -- so it stays for as long as it
            // stays in this server's hands.
            if (holding || string.IsNullOrEmpty(change.Kept.id))
            {
                merged.Add(change.Kept);
            }
            else
            {
                changes.Remove(clientId);
            }
        }

        return merged;
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
        DateTime startedAt = DateTime.UtcNow;

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

                lineups = MergeLocal(steamId, startedAt, lineups);

                _lineups[steamId] = lineups;
                done?.Invoke(lineups.Count);
            });
        });
    }
}
