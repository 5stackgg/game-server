using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using FiveStack.enums;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public void ListenForMapChange()
    {
        RegisterListener<Listeners.OnMapStart>(
            async (mapName) =>
            {
                _currentRound = 0;
                _onMap = Server.MapName;
                _currentMapStatus = eMapStatus.Unknown;

                // code smell: we have to wait till server exec's default cfgs
                await Task.Delay(1000 * 5);
                Server.NextFrame(() =>
                {
                    SetupMatch();
                });
            }
        );
    }
}
