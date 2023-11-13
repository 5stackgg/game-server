using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using PlayCS.enums;

namespace PlayCs;

public partial class PlayCsPlugin
{
    public void ListenForMapChange()
    {
        RegisterListener<Listeners.OnMapStart>(
            async (mapName) =>
            {
                _currentRound = 0;
                _currentMap = Server.MapName;
                _currentGameState = eGameState.Unknown;

                // code smell: we have to wait till server exec's default cfgs
                await Task.Delay(1000 * 5);

                SetupMatch();
            }
        );
    }
}
