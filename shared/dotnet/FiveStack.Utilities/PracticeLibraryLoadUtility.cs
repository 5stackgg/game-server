namespace FiveStack.Utilities;

// When a player's saved lineups are worth (re)fetching, kept out of the plugin
// for the same reason the map change is: the drain runs off the second tick
// against a panel that can be slow, wrong or absent, and every one of those
// cases is a rule rather than a game object.
public static class PracticeLibraryLoadUtility
{
    // A fetch is one HTTP call with a ten second timeout, so anything still
    // outstanding after this is not coming back -- the answer was dropped
    // somewhere between the request and the tick meant to apply it. Presuming
    // it dead is the only thing that unpins the player: an in-flight marker
    // that is only ever cleared by an answer stops the library loading again
    // for the rest of the process.
    public const int FetchTimeoutSeconds = 30;

    // A panel that is down stays down for minutes, not milliseconds. Doubling
    // from two seconds keeps a blip invisible, and the cap turns an outage into
    // one request per player per minute instead of sixty.
    public const int MinRetrySeconds = 2;
    public const int MaxRetrySeconds = 60;

    public static TimeSpan RetryDelay(int failures)
    {
        if (failures <= 1)
        {
            return TimeSpan.FromSeconds(MinRetrySeconds);
        }

        // Clamped before the shift as well as after it: a server left running
        // against a dead panel reaches a failure count that overflows one.
        int doublings = Math.Min(failures - 1, 8);
        int seconds = Math.Min(MinRetrySeconds << doublings, MaxRetrySeconds);

        return TimeSpan.FromSeconds(seconds);
    }

    public static bool IsFetchExpired(DateTime startedAt, DateTime now)
    {
        return now - startedAt >= TimeSpan.FromSeconds(FetchTimeoutSeconds);
    }

    /// <summary>
    /// Whether the drain should ask the panel for this player's library.
    /// </summary>
    /// <param name="map">The map the server is on; empty before one is known.</param>
    /// <param name="loadedFor">The map the library in hand is for, if any.</param>
    /// <param name="fetchingSince">When the outstanding fetch was issued, if any.</param>
    /// <param name="retryAt">When the next attempt is allowed after a failure.</param>
    public static bool ShouldFetch(
        string map,
        string? loadedFor,
        DateTime? fetchingSince,
        DateTime? retryAt,
        DateTime now
    )
    {
        // A cold boot has no map until OnMapLoad, and a library for "" is a
        // wasted round trip that would then be recorded as loaded.
        if (string.IsNullOrEmpty(map))
        {
            return false;
        }

        if (fetchingSince != null && !IsFetchExpired(fetchingSince.Value, now))
        {
            return false;
        }

        if (string.Equals(loadedFor, map, StringComparison.Ordinal))
        {
            return false;
        }

        return retryAt == null || now >= retryAt.Value;
    }
}
