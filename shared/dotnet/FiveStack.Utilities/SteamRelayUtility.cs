namespace FiveStack.Utilities;

// What a server says about Steam's relay network. Every plugin that reports to
// the panel works it out here, so no two of them can disagree: the match plugin
// on its ping, the practice plugin on its session and occupancy, and player
// management on its sync. The panel believes it and decides nothing itself.
public readonly record struct SteamRelayReport(bool OnRelay, string? Address)
{
    // On the relay the address is the only way in. Until it exists the server
    // is not up, and nothing may tell the panel that it is: the panel would
    // hand out a host and port that reaches nobody.
    public bool Pending => OnRelay && Address == null;
}

public static class SteamRelayUtility
{
    public const string ListenConVar = "net_p2p_listen_dedicated";

    public const string NotListening =
        "STEAM_RELAY is set but the engine is not listening on the relay; this server will never report itself up";

    // Settled before the server starts: the launcher's STEAM_RELAY is what
    // makes the engine listen on the relay at all.
    public static bool Expected()
    {
        return Expected(Environment.GetEnvironmentVariable("STEAM_RELAY"));
    }

    public static bool Expected(string? launcher)
    {
        return string.Equals(launcher?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
    }

    // listening is the engine's own word, the cvar: a server set up by hand has
    // only that. One the launcher asked the relay of that is not listening is
    // still on it -- it has no address to give, so it stays pending rather than
    // being handed out on a host and port.
    public static SteamRelayReport Report(bool listening, bool expected, Func<string?> address)
    {
        return new SteamRelayReport(listening || expected, listening ? address() : null);
    }
}
