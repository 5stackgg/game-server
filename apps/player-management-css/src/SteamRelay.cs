using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Logging;
using CounterStrikeSharp.API.Modules.Cvars;
using FiveStack;
using FiveStack.Utilities;
using Microsoft.Extensions.Logging;

namespace PlayerManagement;

// What this server says about Steam's relay rides on the sanction sync. The
// rule is SteamRelayUtility's, the same as every other plugin's; only reading
// the cvar and the Steam ID is this runtime's, through the match plugin's own
// Steam binding.
public partial class PlayerManagementPlugin
{
    private SteamAPI? _steamAPI;
    private bool _relayListening;
    private bool _relayExpected;

    private void LoadSteamRelay(bool hotReload)
    {
        _steamAPI = new SteamAPI(CoreLogging.Factory.CreateLogger<SteamAPI>());

        RegisterListener<Listeners.OnGameServerSteamAPIActivated>(_steamAPI.OnSteamAPIActivated);
        RegisterListener<Listeners.OnGameServerSteamAPIDeactivated>(
            _steamAPI.OnSteamAPIDeactivated
        );

        if (hotReload)
        {
            _steamAPI.OnSteamAPIActivated();
        }

        _relayListening =
            ConVar.Find(SteamRelayUtility.ListenConVar)?.GetPrimitiveValue<bool>() ?? false;
        _relayExpected = SteamRelayUtility.Expected();

        if (_relayExpected && !_relayListening)
        {
            Logger.LogError(SteamRelayUtility.NotListening);
        }
    }

    // Called from the sync's own timer, never the game thread.
    private SteamRelayReport SteamRelay()
    {
        return SteamRelayUtility.Report(
            _relayListening,
            _relayExpected,
            () => _steamAPI?.GetServerSteamIDFormatted()
        );
    }
}
