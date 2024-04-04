using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace FiveStack;

public partial class FiveStackPlugin
{
    public void ListenForMapChange()
    {
        RegisterListener<Listeners.OnMapStart>(
            async (mapName) =>
            {
                // code smell: we have to wait till server exec's default cfgs
                await Task.Delay(1000 * 5);
                Server.NextFrame(() =>
                {
                    _matchService.GetMatchFromApi();
                });
            }
        );
    }
}
