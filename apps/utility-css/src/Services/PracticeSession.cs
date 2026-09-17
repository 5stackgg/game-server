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

    // The level this server actually came up on, set alongside the library's.
    // The panel cannot tell a map change that has finished from one still
    // loading any other way -- every clock it could use is a guess at how long
    // a changelevel takes.
    public string? Map { get; set; }

    // When the roster the connect hook is deciding on was fetched.
    public DateTime? LoadedAt { get; private set; }

    public async Task Refresh()
    {
        PracticeSessionData? session = await _api.Session(Map);

        // A failed fetch must not empty the roster: everyone already connected
        // stays connected, and the door keeps the policy it had.
        if (session == null)
        {
            _logger.LogWarning(
                "unable to refresh the practice session; keeping the last roster ({roster})",
                _session == null
                    ? "none loaded"
                    : $"{_session.allowed_steam_ids.Count} players, loaded {LoadedAt:u}"
            );
            return;
        }

        List<string> previous = _session?.allowed_steam_ids ?? new List<string>();

        _session = session;
        LoadedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "practice session {id} match {match} ({players} players allowed: {roster} | added: {added} | removed: {removed})",
            session.id,
            session.match_id,
            session.allowed_steam_ids.Count,
            Ids(session.allowed_steam_ids),
            Ids(session.allowed_steam_ids.Except(previous)),
            Ids(previous.Except(session.allowed_steam_ids))
        );

        Refreshed?.Invoke(session);
    }

    private static string Ids(IEnumerable<string> ids)
    {
        string joined = string.Join(", ", ids);

        return joined.Length == 0 ? "none" : joined;
    }

    public bool IsAllowed(ulong steamId)
    {
        return _session != null && PracticeConnectUtility.IsOnRoster(_session, steamId);
    }
}
