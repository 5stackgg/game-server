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
// string pointing at somebody else's server. Clearing the stale value is the
// panel's job; reporting the real one is this.
//
// Deliberately NOT the match plugin's /game-server-node/ping endpoint: that one
// also writes plugin_version and plugin_runtime, which decide how the panel
// talks to a server, and a practice pod reporting the utility plugin's version
// there would tell the panel the wrong thing about the box.
public class PracticeRelay
{
    private readonly ISwiftlyCore _core;
    private readonly ILogger<PracticeRelay> _logger;
    private readonly bool _onRelay;

    public PracticeRelay(ISwiftlyCore core, ILogger<PracticeRelay> logger)
    {
        _core = core;
        _logger = logger;
        _onRelay = _core.ConVar.Find<bool>("net_p2p_listen_dedicated")?.Value ?? false;
    }

    /// <summary>
    /// The relay account id, or null when this server is not on the relay or
    /// has not finished registering with it yet. Null means "say nothing" --
    /// never "clear it": a pod that is still connecting would otherwise flap
    /// the address out from under anybody mid-join.
    /// </summary>
    public string? AccountId()
    {
        if (!_onRelay)
        {
            return null;
        }

        try
        {
            ulong steamId = SteamGameServer.GetSteamID().GetSteamID64();

            if (steamId == 0)
            {
                return null;
            }

            return SteamIdUtility.ConvertSteamID64ToSteamID(steamId);
        }
        catch (Exception error)
        {
            _logger.LogWarning(error, "steam game server is not ready for a relay lookup");
            return null;
        }
    }
}
