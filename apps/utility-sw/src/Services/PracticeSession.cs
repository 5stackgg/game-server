using FiveStack.Entities.Practice;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace UtilityPractice;

// A practice server never loads the match plugin, so this is where it learns
// who it is hosting for. The roster is the door policy; the connect hook reads
// nothing else.
public class PracticeSession
{
    private readonly UtilityApiClient _api;
    private readonly ILogger<PracticeSession> _logger;

    private PracticeSessionData? _session;

    public PracticeSession(UtilityApiClient api, ILogger<PracticeSession> logger)
    {
        _api = api;
        _logger = logger;
    }

    public event Action<PracticeSessionData>? Refreshed;

    public PracticeSessionData? Current => _session;

    private int _refreshing;
    private DateTime _lastAttempt = DateTime.MinValue;

    // The map-load fetch is one shot, and an unattended server has nobody on it
    // to cause another -- a render pod cannot even connect until the roster
    // exists. One transient failure of that single call used to brick the
    // server silently. This is the safety net: keep asking, spaced out, only
    // while we still know nothing.
    public void RetryIfMissing(TimeSpan minInterval)
    {
        if (_session != null)
        {
            return;
        }

        if (DateTime.UtcNow - _lastAttempt < minInterval)
        {
            return;
        }

        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
        {
            return;
        }

        _lastAttempt = DateTime.UtcNow;

        _ = Task.Run(async () =>
        {
            try
            {
                await Refresh();
            }
            finally
            {
                Interlocked.Exchange(ref _refreshing, 0);
            }
        });
    }

    public async Task Refresh()
    {
        PracticeSessionData? session = await _api.Session();

        // A failed fetch must not empty the roster: everyone already connected
        // stays connected, and the door keeps the policy it had.
        if (session == null)
        {
            _logger.LogWarning("unable to refresh the practice session; keeping the last roster");
            return;
        }

        _session = session;

        _logger.LogInformation(
            "practice session {id} ({players} players allowed)",
            session.id,
            session.allowed_steam_ids.Count
        );

        Refreshed?.Invoke(session);
    }

    public bool IsAllowed(ulong steamId)
    {
        return _session != null && PracticeConnectUtility.IsOnRoster(_session, steamId);
    }
}
