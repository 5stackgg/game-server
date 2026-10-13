using FiveStack.Utilities;
using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared.SteamAPI;

namespace PlayerManagement;

// What this server says about Steam's relay rides on the sanction sync. The
// rule is SteamRelayUtility's, the same as every other plugin's; only reading
// the cvar and the Steam ID is this runtime's.
public partial class PlayerManagementPlugin
{
    private bool _relayListening;
    private bool _relayExpected;

    private void LoadSteamRelay()
    {
        _relayListening = Core.ConVar.Find<bool>(SteamRelayUtility.ListenConVar)?.Value ?? false;
        _relayExpected = SteamRelayUtility.Expected();

        if (_relayExpected && !_relayListening)
        {
            _logger.LogError(SteamRelayUtility.NotListening);
        }
    }

    // Called from the sync's own timer, never the game thread.
    private SteamRelayReport SteamRelay()
    {
        return SteamRelayUtility.Report(_relayListening, _relayExpected, SteamRelayAddress);
    }

    private static string? SteamRelayAddress()
    {
        try
        {
            return SteamIdUtility.ConvertSteamID64ToSteamID(
                SteamGameServer.GetSteamID().GetSteamID64()
            );
        }
        catch (Exception)
        {
            return null;
        }
    }
}
