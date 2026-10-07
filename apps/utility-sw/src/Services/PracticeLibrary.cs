using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;

namespace UtilityPractice;

// The saved lineups for the map this server is on, one list per player. The
// panel already filters by map and by who is allowed to see what, so nothing
// here re-decides visibility.
public class PracticeLibrary
{
    private readonly ISwiftlyCore _core;
    private readonly UtilityApiClient _api;
    private readonly ILogger<PracticeLibrary> _logger;

    private readonly Dictionary<ulong, List<LineupRecord>> _lineups = new();
    private string _map = "";

    // Everyone who asked for the same player's library while one request was
    // already out, in the order they asked. Two requests in flight for one
    // player landed in whatever order the panel answered, so the older one
    // could overwrite what the newer one had already applied -- a pushed load
    // that teleported someone and then had its markers redrawn a tick later.
    private sealed class PendingFetch
    {
        public DateTime StartedAt;

        // The map it went out asking about. An answer for the map the server
        // has left is no answer at all, so a request made after a changelevel
        // cannot ride one made before it.
        public string Map = "";

        public List<Action<int>> Waiting { get; } = new();
    }

    private readonly Dictionary<ulong, PendingFetch> _fetching = new();

    // What this server changed in a player's library itself -- a .save, a
    // solve, a .delete -- and when, by client id. A fetch that went out before
    // the change, or before the panel had it, answers with the library as it
    // was; applied as-is it dropped the lineup somebody had just saved, or
    // brought back the one they had just deleted. Kept is null for a removal.
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

    public PracticeLibrary(
        ISwiftlyCore core,
        UtilityApiClient api,
        ILogger<PracticeLibrary> logger
    )
    {
        _core = core;
        _api = api;
        _logger = logger;
    }

    public string Map => _map;

    // Set once this instance has been torn down. A fetch still in flight
    // across a hot reload lands on a tick after the teardown, and everything
    // waiting on it -- a pushed load, a redraw -- would teleport players and
    // draw markers on behalf of an instance that no longer owns any of them.
    private volatile bool _closed;

    public void Close()
    {
        _closed = true;
    }

    // Raised on the game thread whenever a fetch replaces a player's library,
    // before anyone waiting on that fetch is answered. Every refresh lands
    // here -- the drain's, .reload's, a load pushed from the website -- so the
    // markers follow the library whichever of them asked.
    public event Action<ulong>? Replaced;

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

            _core.Scheduler.NextTick(() =>
            {
                if (_closed)
                {
                    return;
                }

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
        DateTime now = DateTime.UtcNow;
        string map = _map;
        var fetch = new PendingFetch { StartedAt = now, Map = map };

        if (_fetching.TryGetValue(steamId, out PendingFetch? existing))
        {
            bool expired = PracticeLibraryLoadUtility.IsFetchExpired(existing.StartedAt, now);

            if (!expired && existing.Map == map)
            {
                if (done != null)
                {
                    existing.Waiting.Add(done);
                }

                return;
            }

            if (expired)
            {
                // Presumed dead rather than merely slow. An in-flight marker
                // that only an answer can clear is what pinned a player's
                // library shut for the rest of the process.
                _logger.LogWarning(
                    "library fetch for {steamId} never answered; asking again",
                    steamId
                );
            }

            // Whoever was waiting on it rides the new request rather than
            // being dropped along with the old one.
            fetch.Waiting.AddRange(existing.Waiting);
        }

        if (done != null)
        {
            fetch.Waiting.Add(done);
        }

        _fetching[steamId] = fetch;

        _ = Task.Run(async () =>
        {
            List<LineupRecord>? lineups = null;

            try
            {
                lineups = await _api.Library(map, steamId);
            }
            catch (Exception error)
            {
                // Nothing below this may be skipped: everything waiting on the
                // answer is only ever released by the callback, so a throw that
                // escaped here left the player's library pinned for good.
                _logger.LogError(error, "unable to fetch the lineup library");
            }

            _core.Scheduler.NextTick(() => Landed(steamId, fetch, map, lineups));
        });
    }

    private void Landed(
        ulong steamId,
        PendingFetch fetch,
        string map,
        List<LineupRecord>? lineups
    )
    {
        if (_closed)
        {
            return;
        }

        // An answer to a request that was already given up on. The callers it
        // was carrying moved to the one that replaced it, and its rows are the
        // older of the two.
        if (!_fetching.TryGetValue(steamId, out PendingFetch? current) || current != fetch)
        {
            return;
        }

        _fetching.Remove(steamId);

        int count = -1;

        // The map can change while the request is in flight; dropping the
        // answer beats showing inferno lineups on mirage.
        if (lineups != null && map == _map)
        {
            lineups = MergeLocal(steamId, fetch.StartedAt, lineups);

            _lineups[steamId] = lineups;
            count = lineups.Count;

            try
            {
                Replaced?.Invoke(steamId);
            }
            catch (Exception error)
            {
                // Same reason as the callbacks below: nothing thrown here may
                // reach native code, or keep the waiting callers unanswered.
                _logger.LogError(error, "a library replaced handler threw");
            }
        }

        foreach (Action<int> waiting in fetch.Waiting)
        {
            try
            {
                waiting(count);
            }
            catch (Exception error)
            {
                // One caller throwing is not the rest of them missing their
                // answer, and an exception let back out of here crosses into
                // native SwiftlyS2 and takes the server down.
                _logger.LogError(error, "a library refresh callback threw");
            }
        }
    }
}
