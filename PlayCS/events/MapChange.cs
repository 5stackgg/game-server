using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CaptureMapChange()
    {
        // TODO - this should work, but it does not
        RegisterListener<Listeners.OnMapStart>(
            (mapName) =>
            {
                _currentMap = Server.MapName;
                SetupMatch();
            }
        );
    }
}
