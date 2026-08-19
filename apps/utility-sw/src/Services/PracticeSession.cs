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
