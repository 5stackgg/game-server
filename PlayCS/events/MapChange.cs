using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace PlayCs;

public partial class PlayCsPlugin
{
    private void CaptureMapChange()
    {
        RegisterListener<Listeners.OnMapStart>(
            (mapName) =>
            {
                _currentMap = Server.MapName;

                SetupMatch();
            }
        );
    }
}
