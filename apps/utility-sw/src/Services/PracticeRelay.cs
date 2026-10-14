using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.SteamAPI;

namespace UtilityPractice;

// Where this server can be reached over Steam's relay network, if it is on it.
//
// The panel prefers a relay address over host:port whenever it has one, but the
// only thing that ever reported one was the match plugin's ping -- and a
// practice pod does not run the match plugin. So the row kept whatever relay
// the last tenant of that server registered, and players were handed a connect
// string pointing at somebody else's server. So this server says both things
// itself: whether it is on the relay at all, and where. What it says is worked
// out in SteamRelayUtility, the same as every other plugin's.
//
// Deliberately NOT the match plugin's /game-server-node/ping endpoint: that one
// also writes plugin_version and plugin_runtime, which decide how the panel
// talks to a server, and a practice pod reporting the utility plugin's version
// there would tell the panel the wrong thing about the box.
public class PracticeRelay
{
    private readonly ILogger<PracticeRelay> _logger;
    private readonly bool _listening;
    private readonly bool _expected;

    public PracticeRelay(ISwiftlyCore core, ILogger<PracticeRelay> logger)
    {
        _logger = logger;
        _listening = core.ConVar.Find<bool>(SteamRelayUtility.ListenConVar)?.Value ?? false;
        _expected = SteamRelayUtility.Expected();

        if (_expected && !_listening)
        {
            _logger.LogError(SteamRelayUtility.NotListening);
        }
    }

    public SteamRelayReport Report()
    {
        return SteamRelayUtility.Report(_listening, _expected, Address);
    }

    private string? Address()
    {
        try
        {
            return SteamIdUtility.ConvertSteamID64ToSteamID(
                SteamGameServer.GetSteamID().GetSteamID64()
            );
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "steam game server is not ready for a relay lookup");
            return null;
        }
    }
}
